using System.Text;
using WinTools.Tools.Text;
using Xunit;

namespace WinTools.Tools.Tests;

public class TextStatsTests
{
    [Fact]
    public void Analyze_EmptyText_AllZeros()
    {
        var r = TextStats.Analyze(string.Empty);
        Assert.Equal(new TextStatsResult(0, 0, 0, 0, 0, 0), r);
    }

    [Fact]
    public void Analyze_AsciiWordsAndLines()
    {
        var r = TextStats.Analyze("a b\r\nc");
        Assert.Equal(6, r.Characters);      // a 空格 b \r \n c
        Assert.Equal(3, r.NonWhitespace);
        Assert.Equal(2, r.Lines);
        Assert.Equal(3, r.Words);
        Assert.Equal(0, r.ChineseChars);
        Assert.Equal(6, r.Utf8Bytes);
    }

    [Fact]
    public void Analyze_ChineseAndEmoji_CountRunesNotUtf16Units()
    {
        var r = TextStats.Analyze("你好😀");
        Assert.Equal(3, r.Characters);      // emoji 是一个 Rune，不是两个 char
        Assert.Equal(3, r.NonWhitespace);
        Assert.Equal(2, r.Words);          // 中文按字计词，emoji 不作为单词计数
        Assert.Equal(2, r.ChineseChars);
        Assert.Equal(3 + 3 + 4, r.Utf8Bytes);   // 两个汉字各 3 字节 + emoji 4 字节
    }

    [Fact]
    public void Analyze_TrailingNewline_DoesNotAddExtraLine()
    {
        var r = TextStats.Analyze("Hello 世界\n");
        Assert.Equal(9, r.Characters);
        Assert.Equal(7, r.NonWhitespace);
        Assert.Equal(1, r.Lines);
        Assert.Equal(3, r.Words);           // Hello + 世 + 界
        Assert.Equal(13, r.Utf8Bytes);
    }

    [Fact]
    public void Format_ContainsEveryMetric()
    {
        var text = TextStats.Format(TextStats.Analyze("abc"));
        Assert.Contains("字符数（含空白）：3", text);
        Assert.Contains("行数：1", text);
        Assert.Contains("UTF-8 字节：3", text);
    }
}
