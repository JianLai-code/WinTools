namespace WinTools.Tools.Text;

/// <summary>
/// 密码生成结果：要么成功（带密码），要么失败（带错误原因）。
/// </summary>
/// <param name="Success">是否成功</param>
/// <param name="Password">成功时的密码；失败时为空串</param>
/// <param name="ErrorMessage">失败时的错误原因；成功时为 null</param>
public sealed record PasswordResult(
    bool Success,
    string Password,
    string? ErrorMessage)
{
    public static PasswordResult Ok(string password) => new(true, password, null);

    public static PasswordResult Fail(string message) => new(false, string.Empty, message);
}
