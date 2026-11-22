namespace WinTools.Tools.Text;

/// <summary>
/// JSON 格式化结果：要么成功（带格式化文本），要么失败（带错误原因和行号）。
/// 用记录类型（record）让代码简洁，且天然支持值相等比较，方便单元测试断言。
/// </summary>
/// <param name="Success">是否成功</param>
/// <param name="Text">成功时的格式化文本；失败时为空串</param>
/// <param name="ErrorMessage">失败时的错误原因；成功时为 null</param>
/// <param name="LineNumber">失败时的错误行号（从 1 开始）；无行号信息时为 null</param>
public sealed record JsonFormatResult(
    bool Success,
    string Text,
    string? ErrorMessage,
    int? LineNumber)
{
    public static JsonFormatResult Ok(string text) => new(true, text, null, null);

    public static JsonFormatResult Fail(string message, int? lineNumber = null)
        => new(false, string.Empty, message, lineNumber);
}
