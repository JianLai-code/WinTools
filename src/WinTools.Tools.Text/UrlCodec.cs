using System.Text;

namespace WinTools.Tools.Text;

/// <summary>
/// URL 编解码的纯逻辑类，不依赖 UI，便于单元测试。
/// 编码：UTF-8 字节逐个百分号编码，空格得 %20（不是 +），保留 A-Z a-z 0-9 - _ . ~。
/// 解码：%XX 还原为字节，+ 按空格处理（查询串习惯），再用严格 UTF-8 解码，非法输入给友好提示。
/// </summary>
public static class UrlCodec
{
    public const int MaxInputLength = 2 * 1024 * 1024;

    // 严格 UTF-8：孤立代理项和非法字节都抛异常，避免静默替换成 U+FFFD 造成往返失真。
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>文本 → URL 编码串（UTF-8，百分号大写）。</summary>
    public static UrlCodecResult Encode(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return UrlCodecResult.Fail("请输入要编码的内容");
        if (input.Length > MaxInputLength)
            return UrlCodecResult.Fail($"内容过长（最多 {MaxInputLength / 1024 / 1024} MB）");

        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(input);
        }
        catch (EncoderFallbackException)
        {
            return UrlCodecResult.Fail("内容含无法编码的字符（如孤立代理项）");
        }

        var sb = new StringBuilder(bytes.Length * 3);
        foreach (var b in bytes)
        {
            if (IsUnreserved(b))
                sb.Append((char)b);
            else
                sb.Append('%').Append(b.ToString("X2"));
        }
        return UrlCodecResult.Ok(sb.ToString());
    }

    /// <summary>URL 编码串 → 原文。+ 视为空格；% 后必须是两位十六进制。</summary>
    public static UrlCodecResult Decode(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return UrlCodecResult.Fail("请输入要解码的 URL 内容");
        if (input.Length > MaxInputLength)
            return UrlCodecResult.Fail($"内容过长（最多 {MaxInputLength / 1024 / 1024} MB）");

        var bytes = new List<byte>(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '+')
            {
                bytes.Add((byte)' ');
            }
            else if (c == '%')
            {
                if (i + 2 >= input.Length || !TryHex(input[i + 1], out var hi) || !TryHex(input[i + 2], out var lo))
                    return UrlCodecResult.Fail($"第 {i + 1} 位不是有效的 %XX 转义序列");
                bytes.Add((byte)((hi << 4) | lo));
                i += 2;
            }
            else if (c <= 0x7F)
            {
                bytes.Add((byte)c);
            }
            else if (char.IsHighSurrogate(c) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
            {
                try
                {
                    bytes.AddRange(StrictUtf8.GetBytes(input.Substring(i, 2)));
                }
                catch (EncoderFallbackException)
                {
                    return UrlCodecResult.Fail($"第 {i + 1} 位含无法编码的字符");
                }
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                return UrlCodecResult.Fail($"第 {i + 1} 位是孤立的代理项字符");
            }
            else
            {
                bytes.AddRange(StrictUtf8.GetBytes(c.ToString()));
            }
        }

        try
        {
            return UrlCodecResult.Ok(StrictUtf8.GetString(bytes.ToArray()));
        }
        catch (DecoderFallbackException)
        {
            return UrlCodecResult.Fail("解码结果不是有效的 UTF-8 文本，请检查 %XX 序列");
        }
    }

    private static bool IsUnreserved(byte b) =>
        b is >= (byte)'A' and <= (byte)'Z'
        or >= (byte)'a' and <= (byte)'z'
        or >= (byte)'0' and <= (byte)'9'
        or (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~';

    private static bool TryHex(char c, out int value)
    {
        if (c is >= '0' and <= '9') { value = c - '0'; return true; }
        if (c is >= 'A' and <= 'F') { value = c - 'A' + 10; return true; }
        if (c is >= 'a' and <= 'f') { value = c - 'a' + 10; return true; }
        value = 0;
        return false;
    }
}
