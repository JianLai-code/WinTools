using WinTools.Tools.Text;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// 时间戳转换纯逻辑单测：秒/毫秒识别、时区注入、显式偏移日期、异常输入。
/// 时区全部显式注入，测试不依赖运行机器的本地时区。
/// </summary>
public sealed class TimestampConverterTests
{
    private static readonly TimeZoneInfo Plus8 = TimeZoneInfo.CreateCustomTimeZone(
        "Test+8", TimeSpan.FromHours(8), "Test+8", "Test+8");

    [Fact]
    public void FromTimestamp_Seconds_ShowsUtcAndMilliseconds()
    {
        var result = TimestampConverter.FromTimestamp("1700000000", TimeZoneInfo.Utc);

        Assert.True(result.Success);
        Assert.Contains("识别单位：秒", result.Text);
        Assert.Contains("2023-11-14 22:13:20", result.Text);
        Assert.Contains("Unix 毫秒：1700000000000", result.Text);
    }

    [Fact]
    public void FromTimestamp_Milliseconds_KeepsFraction()
    {
        var result = TimestampConverter.FromTimestamp("1700000000123", TimeZoneInfo.Utc);

        Assert.True(result.Success);
        Assert.Contains("识别单位：毫秒", result.Text);
        Assert.Contains("2023-11-14 22:13:20.123", result.Text);
        Assert.Contains("Unix 秒：1700000000", result.Text);
    }

    [Fact]
    public void FromTimestamp_Zero_IsUnixEpoch()
    {
        var result = TimestampConverter.FromTimestamp("0", TimeZoneInfo.Utc);

        Assert.True(result.Success);
        Assert.Contains("1970-01-01 00:00:00", result.Text);
        Assert.Contains("Unix 秒：0", result.Text);
    }

    [Fact]
    public void FromDateTime_NaiveInput_UsesInjectedTimeZone()
    {
        var result = TimestampConverter.FromDateTime("1970-01-01 08:00:00", Plus8);

        Assert.True(result.Success);
        Assert.Contains("Unix 秒：0", result.Text);
        Assert.Contains("UTC+08:00", result.Text);
    }

    [Fact]
    public void FromDateTime_ExplicitZ_IgnoresLocalTimeZone()
    {
        var result = TimestampConverter.FromDateTime("2023-11-14T22:13:20Z", Plus8);

        Assert.True(result.Success);
        Assert.Contains("Unix 秒：1700000000", result.Text);
        Assert.Contains("Unix 毫秒：1700000000000", result.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1.5")]
    public void FromTimestamp_InvalidInput_ReturnsFriendlyMessage(string? input)
    {
        var result = TimestampConverter.FromTimestamp(input, TimeZoneInfo.Utc);

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Text);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void FromTimestamp_OutOfRange_ReturnsFriendlyMessage()
    {
        var result = TimestampConverter.FromTimestamp("9999999999999999", TimeZoneInfo.Utc);

        Assert.False(result.Success);
        Assert.Contains("范围", result.ErrorMessage);
    }

    [Fact]
    public void FromDateTime_InvalidText_ReturnsFriendlyMessage()
    {
        var result = TimestampConverter.FromDateTime("这不是日期", TimeZoneInfo.Utc);

        Assert.False(result.Success);
        Assert.Contains("无法识别", result.ErrorMessage);
    }
}
