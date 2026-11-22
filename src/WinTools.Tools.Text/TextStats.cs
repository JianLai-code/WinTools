using System.Globalization;
using System.Text;

namespace WinTools.Tools.Text;

/// <summary>文本统计结果。定义写在注释里，测试与界面都按同一套语义断言。</summary>
public sealed record TextStatsResult(
    int Characters,      // Unicode 字符数（按 Rune 计，emoji 算一个）
    int NonWhitespace,   // 非空白字符数
    int Lines,           // 行数：空文本 0，末尾换行不额外加一行
    int Words,           // 英文/数字连续串算一个词，每个中日韩字符算一个词
    int ChineseChars,    // 中日韩统一表意文字个数
    long Utf8Bytes);     // UTF-8 编码字节数

/// <summary>纯函数文本统计：不读写剪贴板、不碰控件，界面只负责展示。</summary>
public static class TextStats
{
    public static TextStatsResult Analyze(string? text)
    {
        var s = text ?? string.Empty;
        int chars = 0, nonWhite = 0, chinese = 0, words = 0;
        var inWord = false;

        foreach (var rune in s.EnumerateRunes())
        {
            chars++;
            if (!Rune.IsWhiteSpace(rune)) nonWhite++;
            var cp = rune.Value;
            bool cjk = cp is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF
                or >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x2FFFF;
            if (cjk)
            {
                chinese++;
                words++;      // 中文按字计词
                inWord = false;
                continue;
            }
            bool wordChar = Rune.IsLetterOrDigit(rune);
            if (wordChar && !inWord) words++;
            inWord = wordChar;
        }

        int lines = s.Length == 0 ? 0 : s.Count(c => c == '\n') + (s.EndsWith('\n') ? 0 : 1);
        var bytes = Encoding.UTF8.GetByteCount(s);
        return new TextStatsResult(chars, nonWhite, lines, words, chinese, bytes);
    }

    public static string Format(TextStatsResult r) => string.Join(Environment.NewLine, new[]
    {
        $"字符数（含空白）：{r.Characters.ToString(CultureInfo.InvariantCulture)}",
        $"非空白字符：{r.NonWhitespace.ToString(CultureInfo.InvariantCulture)}",
        $"行数：{r.Lines.ToString(CultureInfo.InvariantCulture)}",
        $"词数（中文按字计）：{r.Words.ToString(CultureInfo.InvariantCulture)}",
        $"中日韩字符：{r.ChineseChars.ToString(CultureInfo.InvariantCulture)}",
        $"UTF-8 字节：{r.Utf8Bytes.ToString(CultureInfo.InvariantCulture)}"
    });
}
