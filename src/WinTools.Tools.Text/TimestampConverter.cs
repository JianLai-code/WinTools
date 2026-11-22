using System.Globalization;
using System.Text.RegularExpressions;

namespace WinTools.Tools.Text;

/// <summary>
/// Unix 时间戳与日期时间互转的纯逻辑类，不依赖 UI。
/// 10 位左右按秒、13 位左右按毫秒自动识别；日期字符串带 Z/偏移时按显式偏移解析，
/// 否则按传入时区（默认本机时区）解释。时区可注入，便于单测稳定断言。
/// </summary>
public static class TimestampConverter
{
    // 秒级时间戳当前约 1.7e9，毫秒级约 1.7e12；1e11 是两者之间清晰的阈值。
    private const long MillisecondThreshold = 100_000_000_000L;

    private static readonly Regex ExplicitOffset =
        new(@"(Z|[+-]\d{2}:?\d{2})\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Unix 时间戳 → 本地时间 / UTC / 秒 / 毫秒对照。</summary>
    public static TimestampResult FromTimestamp(string? input, TimeZoneInfo? timeZone = null)
    {
        var text = input?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return TimestampResult.Fail("请输入 Unix 时间戳");
        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            return TimestampResult.Fail("时间戳只能是整数（可带负号），不支持小数或日期文本");

        var isMilliseconds = value >= MillisecondThreshold || value <= -MillisecondThreshold;
        DateTimeOffset moment;
        try
        {
            moment = isMilliseconds
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : DateTimeOffset.FromUnixTimeSeconds(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return TimestampResult.Fail("时间戳超出可转换范围，请检查位数或数值");
        }

        var detail = FormatMoment(moment, timeZone);
        return TimestampResult.Ok($"识别单位：{(isMilliseconds ? "毫秒" : "秒")}\n{detail}");
    }

    /// <summary>日期时间 → Unix 秒 / 毫秒。无显式偏移时按传入时区解释。</summary>
    public static TimestampResult FromDateTime(string? input, TimeZoneInfo? timeZone = null)
    {
        var text = input?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return TimestampResult.Fail("请输入日期时间，例如 2026-11-21 18:00:00");

        var tz = timeZone ?? TimeZoneInfo.Local;
        DateTimeOffset moment;
        if (ExplicitOffset.IsMatch(text))
        {
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out moment))
                return TimestampResult.Fail("无法识别带偏移的日期时间，请检查格式");
        }
        else
        {
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var naive))
                return TimestampResult.Fail("无法识别日期时间，请检查格式");
            moment = new DateTimeOffset(DateTime.SpecifyKind(naive, DateTimeKind.Unspecified),
                tz.GetUtcOffset(naive));
        }

        return TimestampResult.Ok(FormatMoment(moment, timeZone));
    }

    private static string FormatMoment(DateTimeOffset moment, TimeZoneInfo? timeZone)
    {
        var tz = timeZone ?? TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(moment, tz);
        return string.Join('\n',
            $"本地时间（{OffsetText(local.Offset)}）：{Clock(local)}",
            $"UTC 时间：{Clock(moment.ToUniversalTime())}",
            $"Unix 秒：{moment.ToUnixTimeSeconds()}",
            $"Unix 毫秒：{moment.ToUnixTimeMilliseconds()}");
    }

    private static string Clock(DateTimeOffset value) =>
        value.ToString(value.Millisecond == 0
            ? "yyyy-MM-dd HH:mm:ss"
            : "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private static string OffsetText(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero) return "UTC";
        var abs = offset.Duration();
        return $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{abs.Hours:00}:{abs.Minutes:00}";
    }
}
