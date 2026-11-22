using System.Text;

namespace WinTools.Tools.Text;

/// <summary>
/// Base64 编解码的纯逻辑类，不依赖任何 UI，便于单元测试。
/// 对应验收用例 T2-AC1 ~ AC4（见阶段二文档）。
/// </summary>
public static class Base64Codec
{
    // 输入上限：防止 UI 线程同步处理超大文本导致内存暴涨/卡死（解码峰值约为输入 3 倍）。
    public const int MaxInputLength = 2 * 1024 * 1024;

    // 严格 UTF-8：非法字节/孤立代理改为抛异常，避免静默替换成 U+FFFD 造成往返失真。
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// 编码：文本 → 标准 Base64 串（UTF-8）。对应 T2-AC1。
    /// </summary>
    public static Base64Result Encode(string? input)
    {
        // 空输入给友好提示，与 JSON 工具保持一致的交互（不抛异常）。
        if (string.IsNullOrEmpty(input))
        {
            return Base64Result.Fail("请输入要编码的内容");
        }
        if (input.Length > MaxInputLength)
        {
            return Base64Result.Fail($"内容过长（最多 {MaxInputLength / 1024 / 1024} MB）");
        }

        try
        {
            // 关键：用 UTF-8 编码，中文才能正确往返（T2-AC3）。
            var bytes = StrictUtf8.GetBytes(input);
            return Base64Result.Ok(Convert.ToBase64String(bytes));
        }
        catch (EncoderFallbackException)
        {
            return Base64Result.Fail("内容含无法编码的字符（如孤立代理项）");
        }
    }

    /// <summary>
    /// 解码：标准 Base64 串 → 原文（UTF-8）。对应 T2-AC2 / AC3 / AC4。
    /// </summary>
    public static Base64Result Decode(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Base64Result.Fail("请输入要解码的 Base64 内容");
        }
        if (input.Length > MaxInputLength)
        {
            return Base64Result.Fail($"内容过长（最多 {MaxInputLength / 1024 / 1024} MB）");
        }

        // 容错：从网页/邮件复制的 Base64 常带换行或空格，先剔除空白字符。
        // 注意剔除后若仍非法，Convert.FromBase64String 会抛 FormatException，由下方 catch 处理。
        var cleaned = RemoveWhiteSpace(input);

        try
        {
            var bytes = Convert.FromBase64String(cleaned);
            return Base64Result.Ok(StrictUtf8.GetString(bytes));
        }
        catch (FormatException)
        {
            // T2-AC4：非法输入给"无法解码"提示，不崩溃。
            return Base64Result.Fail("无法解码：输入不是有效的 Base64 字符串");
        }
        catch (DecoderFallbackException)
        {
            return Base64Result.Fail("解码结果不是有效的 UTF-8 文本");
        }
    }

    private static string RemoveWhiteSpace(string text)
    {
        // 用 StringBuilder 过滤 \r \n \t 空格等所有空白字符。
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
