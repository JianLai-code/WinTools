using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WinTools.Tools.SystemTools;

/// <summary>
/// 本机网络信息收集的纯逻辑层，不依赖 UI。
///
/// 设计要点与 SystemInfoCollector 一致：
/// 1. 每个字段独立 SafeRead，某一项失败只标记该项"不可用"，其余照常显示。
/// 2. 只调用只读 API（NetworkInterface / IPGlobalProperties / Dns.GetHostName），
///    不发起 DNS 反查、不访问外网、不需要管理员权限。
/// 3. 格式化函数（FormatMac / FormatSpeed / FormatAddresses）是纯函数，便于单元测试。
/// </summary>
public static class NetworkInfoCollector
{
    /// <summary>采集本机概要与各网卡明细。正常路径不会抛异常。</summary>
    public static NetworkInfoSnapshot Collect()
    {
        var interfaces = SafeGetInterfaces();
        var active = interfaces.Where(IsUp).ToList();

        var sources = new Dictionary<string, Func<string>>
        {
            ["计算机名"] = () => Environment.MachineName,
            ["完整计算机名"] = ReadFullyQualifiedName,
            ["主 IPv4 地址"] = () => FormatAddresses(GetActiveIPv4(active)),
            ["默认网关"] = () => FormatAddresses(GetGateways(active)),
            ["DNS 服务器"] = () => FormatAddresses(GetDnsServers(active)),
            ["网卡数量"] = () => $"活动 {active.Count} 个 / 共 {interfaces.Count} 个",
            ["网络连接"] = () => NetworkInterface.GetIsNetworkAvailable() ? "网络可用" : "网络不可用"
        };

        return new NetworkInfoSnapshot(CollectFrom(sources), DescribeAdapters(interfaces));
    }

    /// <summary>
    /// 可注入版本：每个源独立 SafeRead，一个源抛异常不影响其他源（供单测验证容错）。
    /// </summary>
    public static List<NetworkInfoItem> CollectFrom(IReadOnlyDictionary<string, Func<string>> sources)
    {
        var result = new List<NetworkInfoItem>();
        foreach (var (name, reader) in sources)
        {
            result.Add(SafeRead(name, reader));
        }
        return result;
    }

    /// <summary>只读枚举本机网卡；枚举失败时返回空集合，由上层显示"未发现"。</summary>
    public static IReadOnlyList<NetworkInterface> SafeGetInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces();
        }
        catch
        {
            return Array.Empty<NetworkInterface>();
        }
    }

    /// <summary>把网卡集合转成明细快照；跳过回环网卡，活动网卡排在前面。</summary>
    public static IReadOnlyList<NetworkAdapterInfo> DescribeAdapters(IEnumerable<NetworkInterface> interfaces)
    {
        return interfaces
            .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(DescribeAdapter)
            .OrderByDescending(a => a.Connected)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>读取单块网卡；任何字段读取失败都只降级该字段，整块网卡失败时返回失败占位。</summary>
    public static NetworkAdapterInfo DescribeAdapter(NetworkInterface nic)
    {
        try
        {
            var properties = nic.GetIPProperties();
            var connected = nic.OperationalStatus == OperationalStatus.Up;

            var fields = new List<NetworkInfoItem>
            {
                SafeRead("描述", () => string.IsNullOrWhiteSpace(nic.Description) ? "无描述" : nic.Description),
                SafeRead("状态", () => DescribeStatus(nic.OperationalStatus)),
                SafeRead("连接速度", () => connected ? FormatSpeed(nic.Speed) : "未连接"),
                SafeRead("MAC 地址", () => FormatMac(nic.GetPhysicalAddress())),
                SafeRead("IPv4 地址", () => FormatAddresses(GetIPv4WithMask(properties), "未获取到")),
                SafeRead("IPv6 地址", () => FormatAddresses(
                    properties.UnicastAddresses
                        .Where(u => u.Address.AddressFamily == AddressFamily.InterNetworkV6)
                        .Select(u => u.Address.ToString()), "未获取到")),
                SafeRead("默认网关", () => FormatAddresses(
                    properties.GatewayAddresses.Select(g => g.Address.ToString()), "未配置")),
                SafeRead("DNS 服务器", () => FormatAddresses(
                    properties.DnsAddresses.Select(a => a.ToString()), "未配置")),
                SafeRead("IP 获取方式", () => DescribeDhcp(properties))
            };

            return new NetworkAdapterInfo(nic.Name, DescribeType(nic.NetworkInterfaceType),
                connected, fields, true);
        }
        catch
        {
            return NetworkAdapterInfo.Failed(nic.Name);
        }
    }

    private static NetworkInfoItem SafeRead(string name, Func<string> reader)
    {
        try
        {
            var value = reader();
            return string.IsNullOrWhiteSpace(value)
                ? NetworkInfoItem.Unavailable(name)
                : NetworkInfoItem.Ok(name, value);
        }
        catch
        {
            return NetworkInfoItem.Unavailable(name);
        }
    }

    private static bool IsUp(NetworkInterface nic)
    {
        try
        {
            return nic.OperationalStatus == OperationalStatus.Up;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadFullyQualifiedName()
    {
        var host = Dns.GetHostName();
        var domain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
        return string.IsNullOrWhiteSpace(domain) ||
               host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)
            ? host
            : $"{host}.{domain}";
    }

    /// <summary>汇总活动网卡上的 IPv4 地址，附子网掩码；去重并保持发现顺序。</summary>
    private static IEnumerable<string> GetIPv4WithMask(IPInterfaceProperties properties)
    {
        foreach (var item in properties.UnicastAddresses)
        {
            if (item.Address.AddressFamily != AddressFamily.InterNetwork ||
                IPAddress.IsLoopback(item.Address))
            {
                continue;
            }

            var address = item.Address.ToString();
            string? mask = null;
            try
            {
                mask = item.IPv4Mask?.ToString();
            }
            catch
            {
                // 部分虚拟网卡不支持读取掩码：保留地址本身。
            }
            yield return string.IsNullOrEmpty(mask) ? address : $"{address} / {mask}";
        }
    }

    private static IEnumerable<string> GetActiveIPv4(IEnumerable<NetworkInterface> active)
    {
        foreach (var nic in active)
        {
            IPInterfaceProperties properties;
            try { properties = nic.GetIPProperties(); }
            catch { continue; }

            foreach (var address in GetIPv4WithMask(properties)) yield return address;
        }
    }

    /// <summary>优先展示 IPv4 网关；没有 IPv4 网关时才退回 IPv6。</summary>
    private static IEnumerable<string> GetGateways(IEnumerable<NetworkInterface> active)
    {
        var all = new List<string>();
        foreach (var nic in active)
        {
            try
            {
                all.AddRange(nic.GetIPProperties().GatewayAddresses.Select(g => g.Address.ToString()));
            }
            catch
            {
                // 单块网卡读取失败不影响其余网卡。
            }
        }
        return PreferIPv4(all);
    }

    private static IEnumerable<string> GetDnsServers(IEnumerable<NetworkInterface> active)
    {
        var all = new List<string>();
        foreach (var nic in active)
        {
            try
            {
                all.AddRange(nic.GetIPProperties().DnsAddresses.Select(a => a.ToString()));
            }
            catch
            {
                // 同上，逐块容错。
            }
        }
        return PreferIPv4(all);
    }

    private static IReadOnlyList<string> PreferIPv4(IEnumerable<string> addresses)
    {
        var list = addresses.Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.Ordinal).ToList();
        var ipv4 = list.Where(a => IPAddress.TryParse(a, out var ip) &&
                                   ip.AddressFamily == AddressFamily.InterNetwork).ToList();
        return ipv4.Count > 0 ? ipv4 : list;
    }

    /// <summary>把地址列表拼成一行；空列表给出明确占位，绝不返回空字符串。</summary>
    public static string FormatAddresses(IEnumerable<string> addresses, string emptyText = "未获取到")
    {
        var list = addresses.Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.Ordinal).ToList();
        return list.Count == 0 ? emptyText : string.Join("、", list);
    }

    /// <summary>MAC 地址转成 XX-XX-XX-XX-XX-XX；虚拟网卡可能没有物理地址。</summary>
    public static string FormatMac(PhysicalAddress? address)
    {
        var bytes = address?.GetAddressBytes();
        return bytes is null || bytes.Length == 0
            ? "无（虚拟网卡）"
            : string.Join("-", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }

    /// <summary>链路速度（bit/s）转成人读的 Kbps / Mbps / Gbps。</summary>
    public static string FormatSpeed(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0) return "未知";
        if (bitsPerSecond >= 1_000_000_000)
            return $"{bitsPerSecond / 1_000_000_000d:0.##} Gbps";
        if (bitsPerSecond >= 1_000_000)
            return $"{bitsPerSecond / 1_000_000d:0.##} Mbps";
        return $"{bitsPerSecond / 1_000d:0.##} Kbps";
    }

    private static string DescribeDhcp(IPInterfaceProperties properties)
    {
        try
        {
            var ipv4 = properties.GetIPv4Properties();
            return ipv4 is null
                ? "未知"
                : ipv4.IsDhcpEnabled ? "自动获取（DHCP）" : "静态配置";
        }
        catch
        {
            return "未知";
        }
    }

    private static string DescribeStatus(OperationalStatus status) => status switch
    {
        OperationalStatus.Up => "已连接",
        OperationalStatus.Down => "已断开",
        OperationalStatus.Testing => "测试中",
        OperationalStatus.Dormant => "休眠",
        OperationalStatus.NotPresent => "不存在",
        OperationalStatus.LowerLayerDown => "底层未连接",
        OperationalStatus.Unknown => "未知",
        _ => status.ToString()
    };

    private static string DescribeType(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet => "以太网",
        NetworkInterfaceType.Wireless80211 => "无线（Wi-Fi）",
        NetworkInterfaceType.Loopback => "回环",
        NetworkInterfaceType.Tunnel => "隧道 / 虚拟",
        NetworkInterfaceType.Ppp => "拨号",
        NetworkInterfaceType.Fddi => "FDDI",
        NetworkInterfaceType.TokenRing => "令牌环",
        NetworkInterfaceType.Atm => "ATM",
        NetworkInterfaceType.GenericModem => "调制解调器",
        NetworkInterfaceType.Ethernet3Megabit => "以太网（3 Mbps）",
        NetworkInterfaceType.FastEthernetFx => "快速以太网（FX）",
        NetworkInterfaceType.FastEthernetT => "快速以太网（T）",
        NetworkInterfaceType.GigabitEthernet => "千兆以太网",
        _ => type.ToString()
    };
}
