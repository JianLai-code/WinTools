using System.Text;

namespace WinTools.Tools.Files;

/// <summary>一种可选的文本编码：Id 稳定不变，用于配置与测试断言。</summary>
public sealed record EncodingOption(string Id, string Display, Encoding Encoding);

/// <summary>编码检测结果：识别到的编码、是否带 BOM、是否为二进制（应拒绝转换）。</summary>
public sealed record EncodingDetection(Encoding? Encoding, bool HasBom, bool IsBinary, string Reason);

/// <summary>编码转换结果，含转换后的字节与是否发生了替换字符（可能有信息丢失）。</summary>
public sealed record ConversionResult(bool Success, byte[]? Bytes, bool HasReplacement, string? ErrorMessage)
{
    public static ConversionResult Ok(byte[] bytes, bool replaced = false) => new(true, bytes, replaced, null);
    public static ConversionResult Fail(string msg) => new(false, null, false, msg);
}

/// <summary>
/// 文本编码识别与转换：UTF-8 / UTF-8 BOM / GBK / GB18030 / UTF-16 互转。
/// 纯函数，不读写文件；二进制检测在前，避免把图片当文本转换。
/// </summary>
public static class TextEncodingConverter
{
    static TextEncodingConverter() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // 直接向 provider 实例取编码，不依赖进程级 RegisterProvider 是否生效：
    // 工具 DLL 被 Shell / 测试宿主反射加载时，全局注册未必在静态构造前完成。
    private static Encoding Gbk => CodePagesEncodingProvider.Instance.GetEncoding(936)
        ?? throw new NotSupportedException("当前运行时不支持 GBK 编码页");
    private static Encoding Gb18030 => CodePagesEncodingProvider.Instance.GetEncoding(54936)
        ?? throw new NotSupportedException("当前运行时不支持 GB18030 编码页");

    /// <summary>界面下拉的候选编码。BOM 与否作为独立选项，语义明确。</summary>
    public static readonly IReadOnlyList<EncodingOption> Options =
    [
        new("utf-8", "UTF-8（无 BOM）", new UTF8Encoding(false)),
        new("utf-8-bom", "UTF-8（带 BOM）", new UTF8Encoding(true)),
        new("gbk", "GBK / GB2312", Gbk),
        new("gb18030", "GB18030", Gb18030),
        new("utf-16le", "UTF-16 LE（带 BOM）", new UnicodeEncoding(false, true)),
        new("utf-16be", "UTF-16 BE（带 BOM）", new UnicodeEncoding(true, true)),
    ];

    public static EncodingOption? Find(string id) => Options.FirstOrDefault(o => o.Id == id);

    /// <summary>
    /// 检测字节流的编码：先看 BOM，再按严格 UTF-8 → GB18030 回退；含 NUL 视为二进制。
    /// </summary>
    public static EncodingDetection Detect(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return new EncodingDetection(null, false, false, "文件为空");

        // BOM 优先，顺序不能乱：UTF-32LE 的 BOM 以 FF FE 开头，要先于 UTF-16LE 判断
        if (Starts(bytes, 0xEF, 0xBB, 0xBF))
            return new EncodingDetection(new UTF8Encoding(true), true, false, "UTF-8 BOM");
        if (Starts(bytes, 0xFF, 0xFE, 0x00, 0x00))
            return new EncodingDetection(new UTF32Encoding(false, true), true, false, "UTF-32 LE BOM");
        if (Starts(bytes, 0x00, 0x00, 0xFE, 0xFF))
            return new EncodingDetection(new UTF32Encoding(true, true), true, false, "UTF-32 BE BOM");
        if (Starts(bytes, 0xFF, 0xFE))
            return new EncodingDetection(new UnicodeEncoding(false, true), true, false, "UTF-16 LE BOM");
        if (Starts(bytes, 0xFE, 0xFF))
            return new EncodingDetection(new UnicodeEncoding(true, true), true, false, "UTF-16 BE BOM");

        // NUL 字节是文本编码里几乎不会出现的，命中即判定为二进制
        if (Array.IndexOf(bytes, (byte)0) >= 0)
            return new EncodingDetection(null, false, true, "检测到 NUL 字节，可能是二进制文件");

        var strictUtf8 = new UTF8Encoding(false, true);
        if (TryDecode(strictUtf8, bytes))
            return new EncodingDetection(new UTF8Encoding(false), false, false, "UTF-8（无 BOM，严格校验通过）");

        var gb18030 = Gb18030;
        if (TryDecode(gb18030, bytes, strict: true))
            return new EncodingDetection(gb18030, false, false, "GB18030（兼容 GBK / GB2312）");

        return new EncodingDetection(null, false, true, "无法按常见文本编码解码，可能是二进制文件");
    }

    /// <summary>把源字节按 from 解码、按 to 编码；写 BOM 由目标 Encoding 的 preamble 决定。</summary>
    public static ConversionResult Convert(byte[] bytes, Encoding from, Encoding to)
    {
        if (bytes is null || bytes.Length == 0)
            return ConversionResult.Fail("源文件为空，无需转换");
        try
        {
            var text = from.GetString(bytes);
            // BOM 解码后会残留 U+FEFF，重新编码时会产生双重 BOM，统一去掉
            if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
            bool replaced = text.IndexOf('\uFFFD') >= 0;
            var output = new List<byte>(to.GetPreamble());
            output.AddRange(to.GetBytes(text));
            return ConversionResult.Ok(output.ToArray(), replaced);
        }
        catch (DecoderFallbackException ex)
        {
            return ConversionResult.Fail($"源编码解码失败：{ex.Message}。请在界面手动指定正确的源编码");
        }
        catch (EncoderFallbackException ex)
        {
            return ConversionResult.Fail($"目标编码无法表示部分字符：{ex.Message}。建议改用 UTF-8 或 GB18030");
        }
    }

    private static bool Starts(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
            if (bytes[i] != prefix[i]) return false;
        return true;
    }

    private static bool TryDecode(Encoding encoding, byte[] bytes, bool strict = false)
    {
        var actual = strict ? Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) : encoding;
        try { actual.GetString(bytes); return true; }
        catch (DecoderFallbackException) { return false; }
    }
}
