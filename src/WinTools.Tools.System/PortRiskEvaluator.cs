namespace WinTools.Tools.SystemTools;

/// <summary>
/// 高危端口标记规则。基于「端口常见用途 + 监听范围」的经验判断，
/// 目的是提醒排查方向，不等同于漏洞扫描或安全审计结论。
/// </summary>
public static class PortRiskEvaluator
{
    private const string High = "high";
    private const string Medium = "medium";
    private const string Low = "low";
    private const string None = "none";

    // 端口 →（常见服务, 基础风险, 说明）
    private static readonly Dictionary<int, (string Service, string Level, string Reason)> Catalog = new()
    {
        [21] = ("FTP", High, "明文文件传输，账号口令可被嗅探"),
        [22] = ("SSH", Medium, "远程登录入口，暴露后是暴力破解重点目标"),
        [23] = ("Telnet", High, "明文远程登录，已被视为不安全协议"),
        [25] = ("SMTP", Medium, "邮件投递服务，开放中继会被滥用"),
        [53] = ("DNS", Medium, "域名服务，开放解析可能被用于放大攻击"),
        [80] = ("HTTP", Medium, "明文 Web 服务，传输内容不加密"),
        [110] = ("POP3", High, "明文邮件收取协议，口令可被嗅探"),
        [135] = ("RPC", High, "Windows RPC，常被用于内网横向移动"),
        [139] = ("NetBIOS", High, "旧式文件/打印共享，信息泄露风险高"),
        [143] = ("IMAP", High, "明文邮件访问协议，口令可被嗅探"),
        [389] = ("LDAP", Medium, "目录服务，匿名访问可能泄露账户信息"),
        [443] = ("HTTPS", Low, "加密 Web 服务，属常见业务端口"),
        [445] = ("SMB", High, "文件共享服务，勒索软件与横向移动高频入口"),
        [1433] = ("MSSQL", High, "数据库服务，弱口令或未授权访问风险高"),
        [1521] = ("Oracle", High, "数据库服务，弱口令或未授权访问风险高"),
        [3306] = ("MySQL", High, "数据库服务，弱口令或未授权访问风险高"),
        [3389] = ("RDP", High, "远程桌面，暴力破解与勒索入侵高频入口"),
        [5432] = ("PostgreSQL", High, "数据库服务，弱口令或未授权访问风险高"),
        [5900] = ("VNC", High, "远程桌面协议，常缺少强认证"),
        [6379] = ("Redis", High, "未授权访问可导致数据泄露或提权"),
        [8080] = ("HTTP 替代端口", Medium, "常见明文 Web/代理端口"),
        [8443] = ("HTTPS 替代端口", Low, "常见加密 Web 备用端口"),
        [9200] = ("Elasticsearch", High, "搜索服务默认端口，未授权访问可泄露全量数据"),
        [11211] = ("Memcached", High, "缓存服务，可被用于放大攻击或数据泄露"),
        [27017] = ("MongoDB", High, "数据库服务，默认无认证风险高")
    };

    // 这些服务一旦监听所有网卡，风险升到高（普通 Web 端口不在此列，避免正常业务误报）。
    private static readonly HashSet<int> SensitiveWhenExposed =
        new() { 22, 25, 53, 135, 139, 389, 1433, 1521, 3306, 3389, 5432, 5900, 6379, 8080, 9200, 11211, 27017 };

    public static (string Level, IReadOnlyList<string> Reasons) Evaluate(
        int port, IReadOnlyList<PortConnection> connections)
    {
        if (!Catalog.TryGetValue(port, out var entry))
            return (None, Array.Empty<string>());

        var reasons = new List<string> { $"端口 {port} 通常为 {entry.Service}：{entry.Reason}" };
        var level = entry.Level;

        var wildcard = connections.Any(c => c.IsWildcard);
        var loopbackOnly = connections.Count > 0 && connections.All(c => c.IsLoopback);

        if (wildcard)
        {
            reasons.Add("监听 0.0.0.0 / [::]，对所有网卡开放");
            if (SensitiveWhenExposed.Contains(port) && level != High) level = High;
        }
        if (loopbackOnly)
        {
            reasons.Add("仅本地回环监听，外部网络无法直接访问");
            if (level == High) level = Medium;
        }

        return (level, reasons);
    }

    public static string LevelText(string level) => level switch
    {
        High => "高危",
        Medium => "中危",
        Low => "低危",
        _ => "正常"
    };
}
