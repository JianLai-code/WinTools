namespace WinTools.Tools.SystemTools;

/// <summary>
/// 网络信息中的一项（如 IPv4 地址、默认网关）。
/// 每项独立标记是否可用——某一项读取失败不影响其他项。
/// </summary>
/// <param name="Name">显示名称</param>
/// <param name="Value">值；不可用时为 "不可用"</param>
/// <param name="Available">是否成功读取</param>
public sealed record NetworkInfoItem(string Name, string Value, bool Available)
{
    public static NetworkInfoItem Ok(string name, string value) => new(name, value, true);

    public static NetworkInfoItem Unavailable(string name) => new(name, "不可用", false);
}

/// <summary>单块网卡的快照。Fields 用键值项承载，UI 可直接沿用统一的键值表格。</summary>
/// <param name="Name">网卡名称</param>
/// <param name="Kind">网卡类型（以太网 / 无线 / 虚拟等）</param>
/// <param name="Connected">是否处于已连接状态，供排序与状态着色</param>
/// <param name="Fields">网卡明细字段</param>
/// <param name="Available">整块网卡是否读取成功</param>
public sealed record NetworkAdapterInfo(
    string Name,
    string Kind,
    bool Connected,
    IReadOnlyList<NetworkInfoItem> Fields,
    bool Available)
{
    public static NetworkAdapterInfo Failed(string name) =>
        new(name, "未知", false, new[] { NetworkInfoItem.Unavailable("网卡信息") }, false);
}

/// <summary>一次采集的完整结果：本机概要 + 各网卡明细。</summary>
public sealed record NetworkInfoSnapshot(
    IReadOnlyList<NetworkInfoItem> Summary,
    IReadOnlyList<NetworkAdapterInfo> Adapters);
