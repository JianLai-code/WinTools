namespace WinTools.Tools.Text;

/// <summary>
/// Base64 处理结果：要么成功（带结果文本），要么失败（带错误原因）。
/// 与 JsonFormatResult 保持一致的风格，便于单元测试断言。
/// </summary>
/// <param name="Success">是否成功</param>
/// <param name="Text">成功时的结果文本；失败时为空串</param>
/// <param name="ErrorMessage">失败时的错误原因；成功时为 null</param>
public sealed record Base64Result(
    bool Success,
    string Text,
    string? ErrorMessage)
{
    public static Base64Result Ok(string text) => new(true, text, null);

    public static Base64Result Fail(string message) => new(false, string.Empty, message);
}
