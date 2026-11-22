using WinTools.Tools.Text;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// URL 编解码纯逻辑单测：覆盖空格编码、中文往返、+ 与小写 hex 容错、非法输入。
/// </summary>
public sealed class UrlCodecTests
{
    [Fact]
    public void Encode_SpaceAndChinese_UsesUppercasePercentEncoding()
    {
        var result = UrlCodec.Encode("你好 a+b");

        Assert.True(result.Success);
        Assert.Equal("%E4%BD%A0%E5%A5%BD%20a%2Bb", result.Text);
    }

    [Fact]
    public void RoundTrip_ChineseWithEmoji_IsIdentical()
    {
        const string original = "你好，WinTools！😀 a=1&b=2";

        var encoded = UrlCodec.Encode(original);
        Assert.True(encoded.Success);

        var decoded = UrlCodec.Decode(encoded.Text);
        Assert.True(decoded.Success);
        Assert.Equal(original, decoded.Text);
    }

    [Fact]
    public void Decode_QueryString_PlusIsSpaceAndLowercaseHexWorks()
    {
        var result = UrlCodec.Decode("a+b%20%e4%bd%a0");

        Assert.True(result.Success);
        Assert.Equal("a b 你", result.Text);
    }

    [Theory]
    [InlineData("abc%")]
    [InlineData("a%2")]
    [InlineData("a%GG")]
    public void Decode_InvalidPercent_ReturnsFriendlyMessage(string input)
    {
        var result = UrlCodec.Decode(input);

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Text);
        Assert.Contains("%XX", result.ErrorMessage);
    }

    [Fact]
    public void Decode_InvalidUtf8Bytes_ReturnsFriendlyMessage()
    {
        var result = UrlCodec.Decode("%FF");

        Assert.False(result.Success);
        Assert.Contains("UTF-8", result.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Encode_EmptyInput_ReturnsFriendlyMessage(string? input)
    {
        var result = UrlCodec.Encode(input);

        Assert.False(result.Success);
        Assert.Contains("请输入", result.ErrorMessage);
    }
}
