namespace WinTools.Tools.SystemTools;

/// <summary>
/// 系统信息中的一项（如 CPU 型号、内存容量）。
/// 每项独立标记是否可用——某一项读取失败不影响其他项（S1-AC4）。
/// </summary>
/// <param name="Name">显示名称，如 "CPU 型号"</param>
/// <param name="Value">值；不可用时为 "不可用"</param>
/// <param name="Available">是否成功读取</param>
public sealed record SystemInfoItem(string Name, string Value, bool Available)
{
    public static SystemInfoItem Ok(string name, string value) => new(name, value, true);

    public static SystemInfoItem Unavailable(string name) => new(name, "不可用", false);
}
