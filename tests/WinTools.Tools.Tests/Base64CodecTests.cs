using System.Text;
using WinTools.Tools.Text;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// Base64Codec 单元测试，逐条实现阶段二 T2-AC1 ~ AC4 验收用例。
/// 测试名对齐阶段七"T2 Base64 编解码"单测清单。
/// </summary>
public sealed class Base64CodecTests
{
    // T2-AC1：文本 → 正确的标准 Base64 串（与框架结果一致）
    [Fact]
    public void Encode_Text_MatchesFrameworkResult()
    {
        const string input = "Hello, WinTools!";

        var result = Base64Codec.Encode(input);

        Assert.True(result.Success);
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes(input));
        Assert.Equal(expected, result.Text);
    }

    // T2-AC2：合法 Base64 → 还原原文
    [Fact]
    public void Decode_ValidBase64_RestoresOriginal()
    {
        const string input = "SGVsbG8sIFdpblRvb2xzIQ==";

        var result = Base64Codec.Decode(input);

        Assert.True(result.Success);
        Assert.Equal("Hello, WinTools!", result.Text);
    }

    // T2-AC3：中文编码后再解码 → 内容完全一致（关键：两端都用 UTF-8）
    [Fact]
    public void RoundTrip_ChineseText_IsIdentical()
    {
        const string original = "你好，WinTools！工具箱测试 😀";

        var encoded = Base64Codec.Encode(original);
        Assert.True(encoded.Success);

        var decoded = Base64Codec.Decode(encoded.Text);
        Assert.True(decoded.Success);
        Assert.Equal(original, decoded.Text);
    }

    // T2-AC4：非法 Base64 串 → 友好提示，不崩溃
    [Theory]
    [InlineData("这不是Base64!!!")]
    [InlineData("@@@@")]
    [InlineData("A")]
    public void Decode_InvalidInput_ReturnsFriendlyMessage(string input)
    {
        var result = Base64Codec.Decode(input);

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Text);          // 不输出半成品
        Assert.Contains("无法解码", result.ErrorMessage);
    }

    // 容错增强：带换行/空格的 Base64（从网页复制常见）也能正确解码
    [Fact]
    public void Decode_WithLineBreaks_DecodesCorrectly()
    {
        // 在正确的 Base64 串 "SGVsbG8sIFdpblRvb2xzIQ==" 中间插入换行与空格
        const string withBreaks = "SGVsbG8s\r\n IFdpblRvb2xzIQ==\n";

        var result = Base64Codec.Decode(withBreaks);

        Assert.True(result.Success);
        Assert.Equal("Hello, WinTools!", result.Text);
    }

    // 空输入：与 JSON 工具一致，给友好提示不抛异常
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Encode_EmptyInput_ReturnsFriendlyMessage(string? input)
    {
        var result = Base64Codec.Encode(input);

        Assert.False(result.Success);
        Assert.Contains("请输入", result.ErrorMessage);
    }
}
