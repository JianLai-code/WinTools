using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace WinTools.Tools.SystemTools;

/// <summary>
/// 端口占用查询的纯逻辑类。对应 S2-AC1~AC4。
///
/// 设计要点：
/// 1. 输入解析是纯函数：TryParsePort 保留单端口语义，TryParsePorts 支持逗号列表 / 区间 / 留空全量。
/// 2. 端口→PID 映射用 netstat -ano 解析（零第三方依赖、普通权限可用），批量查询只调用一次。
/// 3. 解析逻辑 ParseListeners / BuildBatchResults 是纯函数，可注入样例单测，不依赖真实系统状态。
/// 4. 进程名读取失败时降级为"未知进程"但保留 PID（S2-AC4 容错）。
/// </summary>
public static class PortQuery
{
    // 列表分隔符同时兼容半角与全角，中文输入法下直接复制也不用改标点。
    private static readonly char[] ListSeparators = { ',', '，', '、', ';', '；' };
    // 区间分隔符：半角/全角连字符与波浪号。
    private static readonly char[] RangeSeparators = { '-', '–', '—', '~', '～' };

    /// <summary>解析端口输入。空/非数字/超范围都判非法，给明确提示（S2-AC3）。</summary>
    public static bool TryParsePort(string? input, out int port, out string message)
    {
        port = -1;
        if (string.IsNullOrWhiteSpace(input))
        {
            message = "请输入端口号（1-65535）";
            return false;
        }
        if (!int.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port)
            || port < 1 || port > 65535)
        {
            port = -1;
            message = "端口无效：请输入 1-65535 之间的整数";
            return false;
        }
        message = string.Empty;
        return true;
    }

    /// <summary>
    /// 解析端口输入，支持以下写法（可混合）：
    /// 单个 80；列表 80,443；区间 1-20；留空表示全部 1-65535。
    /// 结果去重并按升序返回，任一 token 非法则整体失败并指出具体 token。
    /// </summary>
    public static bool TryParsePorts(string? input, out IReadOnlyList<int> ports, out string message)
    {
        // 留空查全部：直接生成全集，后续只遍历监听表，不会逐个探测端口。
        if (string.IsNullOrWhiteSpace(input))
        {
            ports = Enumerable.Range(1, 65535).ToArray();
            message = string.Empty;
            return true;
        }

        var found = new SortedSet<int>();
        var tokens = input.Split(ListSeparators, StringSplitOptions.None);
        foreach (var raw in tokens)
        {
            var token = raw.Trim();
            if (token.Length == 0)
            {
                ports = Array.Empty<int>();
                message = "端口列表含空项（输入无效）：请使用 80,443 或 1-20 这样的写法";
                return false;
            }

            if (token.IndexOfAny(RangeSeparators) >= 0)
            {
                var parts = token.Split(RangeSeparators, StringSplitOptions.None);
                if (parts.Length != 2
                    || !TryParsePortToken(parts[0].Trim(), out var start)
                    || !TryParsePortToken(parts[1].Trim(), out var end))
                {
                    ports = Array.Empty<int>();
                    message = $"区间无效：{token}（应写成 1-20，且两端都在 1-65535 内）";
                    return false;
                }
                if (start > end)
                {
                    ports = Array.Empty<int>();
                    message = $"区间无效：{token}（起点不能大于终点）";
                    return false;
                }
                for (var p = start; p <= end; p++) found.Add(p);
                continue;
            }

            if (!TryParsePortToken(token, out var port))
            {
                ports = Array.Empty<int>();
                message = $"端口无效：{token}（请输入 1-65535 之间的整数，多个端口用逗号分隔）";
                return false;
            }
            found.Add(port);
        }

        ports = found.ToArray();
        message = string.Empty;
        return true;
    }

    private static bool TryParsePortToken(string token, out int port)
        => int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out port)
           && port >= 1 && port <= 65535;

    /// <summary>查询单个端口占用情况（保留原有调用方式）。</summary>
    public static PortQueryResult Query(int port)
    {
        if (port < 1 || port > 65535)
        {
            return PortQueryResult.Invalid("端口无效：请输入 1-65535 之间的整数");
        }

        var batch = QueryPorts(new[] { port });
        if (batch.Status == "error")
        {
            return PortQueryResult.Error(port, batch.Message);
        }
        return batch.Results[0];
    }

    /// <summary>
    /// 批量查询端口占用：一次 netstat 拿到全部监听项，再按给定端口集合组装结果。
    /// 端口集合需已通过 TryParsePorts 校验；这里仍做一次范围防御。
    /// </summary>
    public static PortQueryBatchResult QueryPorts(IEnumerable<int> ports)
    {
        if (!TryNormalizePorts(ports, out var list, out var error))
        {
            return PortQueryBatchResult.Error(error);
        }

        try
        {
            var listeners = GetListeningPids();
            return PortQueryBatchResult.Ok(BuildBatchResults(list, listeners, GetProcessNameOrUnknown), list.Count);
        }
        catch (Exception ex)
        {
            return PortQueryBatchResult.Error($"查询失败：{ex.Message}", list.Count);
        }
    }

    private static bool TryNormalizePorts(IEnumerable<int> ports, out List<int> list, out string error)
    {
        list = ports.Distinct().OrderBy(p => p).ToList();
        if (list.Count == 0)
        {
            error = "没有可查询的端口";
            return false;
        }
        if (list.Any(p => p < 1 || p > 65535))
        {
            error = "端口无效：请输入 1-65535 之间的整数";
            return false;
        }
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 详细查询：一次 netstat 拿到 TCP/UDP 全部连接，按端口聚合连接、进程全量信息与风险标记。
    /// </summary>
    public static PortQueryDetailResult QueryDetailedPorts(IEnumerable<int> ports)
    {
        if (!TryNormalizePorts(ports, out var list, out var error))
        {
            return PortQueryDetailResult.Error(error);
        }

        try
        {
            var connections = GetAllConnections();
            return PortQueryDetailResult.Ok(
                BuildOccupancies(list, connections, ProcessDetailReader.Read), list.Count);
        }
        catch (Exception ex)
        {
            return PortQueryDetailResult.Error($"查询失败：{ex.Message}", list.Count);
        }
    }

    /// <summary>
    /// 纯函数：把连接明细按端口聚合成完整占用画像。
    /// processReader 可注入，避免单测依赖真实进程/WMI。
    /// </summary>
    public static IReadOnlyList<PortOccupancy> BuildOccupancies(
        IEnumerable<int> ports,
        IReadOnlyList<PortConnection> connections,
        Func<int, ProcessDetail>? processReader = null)
    {
        var wanted = ports.Distinct().ToHashSet();
        var cache = new Dictionary<int, ProcessDetail>();
        var occupancies = new List<PortOccupancy>();

        foreach (var group in connections
                     .Where(c => wanted.Contains(c.LocalPort))
                     .GroupBy(c => c.LocalPort)
                     .OrderBy(g => g.Key))
        {
            var rows = group.OrderBy(c => c.Pid).ThenBy(c => c.Protocol).ToArray();
            var processes = new List<ProcessDetail>();
            foreach (var pid in rows.Select(c => c.Pid).Distinct())
            {
                if (!cache.TryGetValue(pid, out var detail))
                {
                    detail = processReader?.Invoke(pid) ?? ProcessDetail.Unknown(pid, "未读取进程详情");
                    cache[pid] = detail;
                }
                processes.Add(detail);
            }

            var (level, reasons) = PortRiskEvaluator.Evaluate(group.Key, rows);
            occupancies.Add(new PortOccupancy(group.Key, rows, processes, level, reasons));
        }
        return occupancies;
    }

    /// <summary>
    /// 纯函数：把监听表按端口集合组装成逐端口结果，同一 PID 只解析一次进程名。
    /// processNameResolver 可注入，便于单测不依赖真实进程状态。
    /// </summary>
    public static IReadOnlyList<PortQueryResult> BuildBatchResults(
        IEnumerable<int> ports,
        IReadOnlyDictionary<int, int> listeners,
        Func<int, string>? processNameResolver = null)
    {
        var names = new Dictionary<int, string>();
        var results = new List<PortQueryResult>();
        foreach (var port in ports.Distinct().OrderBy(p => p))
        {
            if (!listeners.TryGetValue(port, out var pid))
            {
                results.Add(PortQueryResult.Free(port));
                continue;
            }

            if (!names.TryGetValue(pid, out var name))
            {
                name = processNameResolver?.Invoke(pid) ?? "未知进程";
                names[pid] = name;
            }
            results.Add(PortQueryResult.Occupied(port, name, pid));
        }
        return results;
    }

    /// <summary>用 netstat -ano -p tcp 获取"监听端口 → PID"映射（保留原有简单查询路径）。</summary>
    private static Dictionary<int, int> GetListeningPids()
        => ParseListeners(RunNetstat("-ano -p tcp"));

    /// <summary>用 netstat -ano 拿到 TCP + UDP 全部连接明细（详细查询路径只调用一次）。</summary>
    private static IReadOnlyList<PortConnection> GetAllConnections()
        => ParseConnections(RunNetstat("-ano"));

    /// <summary>
    /// 启动系统 netstat 并读取完整输出；超时/非零退出码都当作失败，
    /// 绝不静默当成"没有监听"。
    /// </summary>
    private static string RunNetstat(string arguments)
    {
        // 绝对路径调用系统 netstat，避免 PATH/当前目录的同名程序劫持。
        var netstatPath = Path.Combine(Environment.SystemDirectory, "netstat.exe");
        var psi = new ProcessStartInfo
        {
            FileName = File.Exists(netstatPath) ? netstatPath : "netstat.exe",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 netstat");
        // 排空 stderr：否则错误输出写满管道会阻塞 netstat，导致查询挂起且诊断信息丢失。
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) stderr.AppendLine(line); };
        process.BeginErrorReadLine();

        // 同步 ReadToEnd 没有超时：netstat 卡住时后台任务永远不返回。
        var readTask = process.StandardOutput.ReadToEndAsync();
        if (!readTask.Wait(TimeSpan.FromSeconds(5)))
        {
            TryKillAndWait(process);
            throw new TimeoutException("netstat 查询超时，请稍后重试");
        }
        var output = readTask.GetAwaiter().GetResult();
        if (!process.WaitForExit(1000))
        {
            TryKillAndWait(process);
            throw new TimeoutException("netstat 未能在限定时间内退出");
        }

        // 关键：非零退出码代表查询失败，绝不能当成"无监听"返回空结果，
        // 否则会把查询失败误报成"端口空闲"。
        if (process.ExitCode != 0)
        {
            var detail = stderr.ToString().Trim();
            throw new InvalidOperationException($"netstat 查询失败（退出码 {process.ExitCode}）{(detail.Length > 0 ? "：" + detail : "")}");
        }
        return output;
    }

    private static void TryKillAndWait(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* 进程已退出 */ }
        try { process.WaitForExit(1000); } catch { /* 已回收 */ }
    }

    /// <summary>
    /// 纯函数：解析 netstat -ano -p tcp 的文本，提取处于 LISTENING 状态的
    /// "本地端口 → PID"映射。中文/英文 Windows 的 netstat 输出关键字一致（LISTENING），
    /// 列顺序固定：Proto / Local Address / Foreign Address / State / PID。
    /// 供单元测试注入样例文本验证。
    /// </summary>
    public static Dictionary<int, int> ParseListeners(string netstatOutput)
    {
        var map = new Dictionary<int, int>();
        if (string.IsNullOrEmpty(netstatOutput))
        {
            return map;
        }

        foreach (var rawLine in netstatOutput.Split('\n'))
        {
            var line = rawLine.Trim();
            // 只关心 LISTENING 行（大小写不敏感，兼容不同系统）。
            if (!line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // TCP    0.0.0.0:80    0.0.0.0:0    LISTENING    4
            // 列数：协议 / 本地地址 / 外部地址 / 状态 / PID
            if (columns.Length < 5)
            {
                continue;
            }
            var localAddress = columns[1];
            if (!TryExtractPort(localAddress, out var port))
            {
                continue;
            }
            if (!int.TryParse(columns[^1], out var pid))
            {
                continue;
            }
            map[port] = pid; // 同一端口多个监听时，保留最后一个（通常就是实际占用者）
        }
        return map;
    }

    /// <summary>
    /// 纯函数：解析 netstat -ano 输出，提取 TCP/UDP 全部连接记录。
    /// TCP 列：Proto / Local / Foreign / State / PID；UDP 没有 State 列，
    /// 统一把状态记为 "—"。中文/英文 Windows 的关键字与列顺序一致。
    /// </summary>
    public static IReadOnlyList<PortConnection> ParseConnections(string netstatOutput)
    {
        var rows = new List<PortConnection>();
        if (string.IsNullOrEmpty(netstatOutput)) return rows;

        foreach (var rawLine in netstatOutput.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 4) continue;

            var protocol = columns[0].ToUpperInvariant();
            if (protocol is not ("TCP" or "UDP")) continue;
            if (!TryExtractPort(columns[1], out var port)) continue;
            if (!int.TryParse(columns[^1], out var pid)) continue;

            var remote = columns[2];
            var state = protocol == "TCP" && columns.Length >= 5 ? columns[^2] : "—";
            rows.Add(new PortConnection(protocol, ExtractAddress(columns[1]), port, remote, state, pid));
        }
        return rows;
    }

    /// <summary>从 "0.0.0.0:80" / "[::]:443" 中提取不含端口的地址部分。</summary>
    private static string ExtractAddress(string endpoint)
    {
        var lastColon = endpoint.LastIndexOf(':');
        return lastColon > 0 ? endpoint[..lastColon] : endpoint;
    }

    /// <summary>从 "0.0.0.0:80" / "[::]:443" 中提取端口部分。</summary>
    private static bool TryExtractPort(string localAddress, out int port)
    {
        port = -1;
        var lastColon = localAddress.LastIndexOf(':');
        if (lastColon < 0 || lastColon == localAddress.Length - 1)
        {
            return false;
        }
        return int.TryParse(localAddress[(lastColon + 1)..], out port);
    }

    /// <summary>读取进程名，失败时降级为"未知进程"但保留 PID。</summary>
    private static string GetProcessNameOrUnknown(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return string.IsNullOrWhiteSpace(process.ProcessName) ? "未知进程" : process.ProcessName;
        }
        catch
        {
            // 进程已退出或权限不足（S2-AC4）：不崩溃，降级展示。
            return "未知进程";
        }
    }
}
