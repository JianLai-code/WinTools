namespace WinTools.Tools.Files;

/// <summary>
/// 单个文件的哈希结果：要么成功（带三个哈希值），要么失败（带错误原因）。
/// 用 record 便于单元测试做值比较。
/// </summary>
/// <param name="FilePath">文件完整路径</param>
/// <param name="FileName">文件名（显示用）</param>
/// <param name="Success">是否成功</param>
/// <param name="Md5">MD5（大写十六进制）；失败时为空串</param>
/// <param name="Sha1">SHA1（大写十六进制）；失败时为空串</param>
/// <param name="Sha256">SHA256（大写十六进制）；失败时为空串</param>
/// <param name="ErrorMessage">失败原因；成功时为 null</param>
public sealed record FileHashResult(
    string FilePath,
    string FileName,
    bool Success,
    string Md5,
    string Sha1,
    string Sha256,
    string? ErrorMessage)
{
    public static FileHashResult Ok(string path, string md5, string sha1, string sha256)
        => new(path, System.IO.Path.GetFileName(path), true, md5, sha1, sha256, null);

    public static FileHashResult Fail(string path, string message)
        => new(path, System.IO.Path.GetFileName(path), false, string.Empty, string.Empty, string.Empty, message);
}
