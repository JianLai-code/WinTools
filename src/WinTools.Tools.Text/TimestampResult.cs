namespace WinTools.Tools.Text;

/// <summary>
/// 时间戳转换结果：成功带多行结果文本，失败带原因。
/// </summary>
/// <param name="Success">是否成功</param>
/// <param name="Text">成功时的结果文本；失败时为空串</param>
/// <param name="ErrorMessage">失败时的错误原因；成功时为 null</param>
public sealed record TimestampResult(
    bool Success,
    string Text,
    string? ErrorMessage)
{
    public static TimestampResult Ok(string text) => new(true, text, null);

    public static TimestampResult Fail(string message) => new(false, string.Empty, message);
}
