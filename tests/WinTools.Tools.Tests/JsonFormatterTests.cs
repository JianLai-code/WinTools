using WinTools.Tools.Text;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// JsonFormatter 单元测试，逐条实现阶段二 T1-AC1 ~ AC4 验收用例。
/// 命名约定：方法名_场景_预期结果。
/// </summary>
public sealed class JsonFormatterTests
{
    // T1-AC1：合法嵌套 JSON → 按层级缩进输出
    [Fact]
    public void Format_ValidNestedJson_ReturnsIndented()
    {
        const string input = "{\"name\":\"test\",\"obj\":{\"a\":1,\"b\":2}}";

        var result = JsonFormatter.Format(input);

        Assert.True(result.Success);
        Assert.Contains("\n", result.Text);                 // 有换行说明已缩进
        Assert.Contains("\"name\"", result.Text);
        Assert.Contains("\"obj\"", result.Text);
    }

    // T1-AC2：语法错误（缺右括号）→ 失败，带错误行号，且不输出半成品
    [Fact]
    public void Format_MissingBracket_FailsWithLineNumber()
    {
        const string input = "{\"a\":1,\"b\":{\"c\":2}";

        var result = JsonFormatter.Format(input);

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Text);             // 不输出半成品结果
        Assert.NotNull(result.ErrorMessage);
        Assert.NotNull(result.LineNumber);                   // 能定位错误行
    }

    // T1-AC3：空输入 → 友好提示，不抛异常
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Format_EmptyInput_FailsWithFriendlyMessage(string? input)
    {
        var result = JsonFormatter.Format(input);

        Assert.False(result.Success);
        Assert.Equal("请输入内容", result.ErrorMessage);
    }

    // T1-AC4：中文内容 → 格式化后不乱码（关键：UnsafeRelaxedJsonEscaping）
    [Fact]
    public void Format_ChineseContent_PreservesCharacters()
    {
        const string input = "{\"姓名\":\"张三\",\"城市\":\"北京\"}";

        var result = JsonFormatter.Format(input);

        Assert.True(result.Success);
        Assert.Contains("张三", result.Text);
        Assert.Contains("北京", result.Text);
        Assert.DoesNotContain("\\u", result.Text);           // 没有被转义成 \uXXXX
    }
}
