namespace WinTools.Tools.Text;

/// <summary>
/// URL 编解码结果：成功带文本，失败带原因。风格与 Base64Result / JsonFormatResult 一致。
/// </summary>
/// <param name="Success">是否成功</param>
/// <param name="Text">成功时的结果文本；失败时为空串</param>
/// <param name="ErrorMessage">失败时的错误原因；成功时为 null</param>
public sealed record UrlCodecResult(
    bool Success,
    string Text,
    string? ErrorMessage)
{
    public static UrlCodecResult Ok(string text) => new(true, text, null);

    public static UrlCodecResult Fail(string message) => new(false, string.Empty, message);
}
