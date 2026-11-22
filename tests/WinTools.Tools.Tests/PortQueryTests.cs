using WinTools.Tools.SystemTools;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// PortQuery 单元测试，实现阶段二 S2-AC1~AC4 与阶段七 S2 校验单测清单。
/// 输入校验与 netstat 解析都是纯函数，可脱离真实系统状态测试。
/// </summary>
public sealed class PortQueryTests
{
    // 阶段七 Validate_PortInRange_ReturnsTrue
    [Theory]
    [InlineData("1")]
    [InlineData("80")]
    [InlineData("443")]
    [InlineData("65535")]
    [InlineData("  8080  ")] // 带空格应能 Trim
    public void Validate_PortInRange_ReturnsTrue(string input)
    {
        var ok = PortQuery.TryParsePort(input, out var port, out _);

        Assert.True(ok);
        Assert.InRange(port, 1, 65535);
    }

    // 阶段七 Validate_PortOutOfRange_ReturnsFalse：99999、0、负数、非数字、空
    [Theory]
    [InlineData("99999")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("12.5")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_PortOutOfRange_ReturnsFalse(string? input)
    {
        var ok = PortQuery.TryParsePort(input, out var port, out var message);

        Assert.False(ok);
        Assert.Equal(-1, port);
        Assert.NotEmpty(message);
    }

    // S2-AC3：非法输入的具体结果对象给"1-65535"提示
    [Fact]
    public void Query_InvalidInput_ReturnsInvalidWithRangeMessage()
    {
        var result = PortQueryResult.Invalid("端口无效：请输入 1-65535 之间的整数");

        Assert.Equal("invalid", result.Status);
        Assert.Contains("1-65535", result.Message);
    }

    // netstat 解析（英文格式）：提取 LISTENING 行的端口→PID
    [Fact]
    public void ParseListeners_EnglishFormat_ExtractsPortAndPid()
    {
        const string sample = """
            Active Connections

              Proto  Local Address          Foreign Address        State           PID
              TCP    0.0.0.0:80             0.0.0.0:0              LISTENING       4
              TCP    0.0.0.0:443            0.0.0.0:0              LISTENING       1234
              TCP    127.0.0.1:8080         0.0.0.0:0              LISTENING       5678
              TCP    10.0.0.5:54321         1.2.3.4:80             ESTABLISHED     9999
            """;

        var map = PortQuery.ParseListeners(sample);

        Assert.Equal(3, map.Count);        // ESTABLISHED 行被忽略
        Assert.Equal(4, map[80]);
        Assert.Equal(1234, map[443]);
        Assert.Equal(5678, map[8080]);
        Assert.False(map.ContainsKey(54321));
    }

    // netstat 解析（IPv6 格式 [::]:443）：正确提取端口
    [Fact]
    public void ParseListeners_IPv6Format_ExtractsPort()
    {
        const string sample = "  TCP    [::]:443    [::]:0    LISTENING    2468\n";

        var map = PortQuery.ParseListeners(sample);

        Assert.Equal(2468, map[443]);
    }

    // 中文 Windows 的 netstat 输出同样带 LISTENING 关键字，列结构一致
    [Fact]
    public void ParseListeners_ChineseWindowsFormat_ExtractsPort()
    {
        const string sample = "  协议  本地地址  外部地址  状态  PID\n  TCP  0.0.0.0:135  0.0.0.0:0  LISTENING  916\n";

        var map = PortQuery.ParseListeners(sample);

        Assert.Equal(916, map[135]);
    }

    // 空输出 / 无监听行 → 空映射
    [Fact]
    public void ParseListeners_NoListeners_ReturnsEmpty()
    {
        Assert.Empty(PortQuery.ParseListeners(string.Empty));
        Assert.Empty(PortQuery.ParseListeners("  TCP  1.2.3.4:80  5.6.7.8:99  ESTABLISHED  123\n"));
    }

    // S2-AC2：未占用端口的结果对象语义
    [Fact]
    public void FreeResult_HasCorrectStatusAndMessage()
    {
        var result = PortQueryResult.Free(54321);

        Assert.Equal("free", result.Status);
        Assert.Equal(54321, result.Port);
        Assert.Null(result.ProcessName);
        Assert.Null(result.Pid);
        Assert.Contains("未被占用", result.Message);
    }

    // S2-AC1：占用结果携带进程名与 PID
    [Fact]
    public void OccupiedResult_CarriesProcessNameAndPid()
    {
        var result = PortQueryResult.Occupied(80, "System", 4);

        Assert.Equal("occupied", result.Status);
        Assert.Equal("System", result.ProcessName);
        Assert.Equal(4, result.Pid);
        Assert.Contains("PID", result.Message);
    }

    // 批量：逗号列表去重升序，兼容中文标点与分号
    [Fact]
    public void TryParsePorts_List_ReturnsSortedDistinctPorts()
    {
        var ok = PortQuery.TryParsePorts("443, 80,80，443、8080;135", out var ports, out var message);

        Assert.True(ok, message);
        Assert.Equal(new[] { 80, 135, 443, 8080 }, ports);
    }

    // 批量：区间两端都包含
    [Fact]
    public void TryParsePorts_Range_ReturnsInclusivePorts()
    {
        var ok = PortQuery.TryParsePorts("1-5", out var ports, out var message);

        Assert.True(ok, message);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, ports);
    }

    // 批量：列表与区间混合，区间展开后同样去重排序
    [Fact]
    public void TryParsePorts_MixedListAndRange_ReturnsAllPorts()
    {
        var ok = PortQuery.TryParsePorts("8080,80-81,443,80", out var ports, out var message);

        Assert.True(ok, message);
        Assert.Equal(new[] { 80, 81, 443, 8080 }, ports);
    }

    // 批量：空输入表示全量 1-65535
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParsePorts_EmptyInput_ReturnsAllPorts(string? input)
    {
        var ok = PortQuery.TryParsePorts(input, out var ports, out var message);

        Assert.True(ok, message);
        Assert.Equal(65535, ports.Count);
        Assert.Equal(1, ports[0]);
        Assert.Equal(65535, ports[^1]);
    }

    // 批量：非法 token 整体失败，并明确指出是哪一项
    [Theory]
    [InlineData("80,,443")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("abc")]
    [InlineData("20-1")]
    [InlineData("1-2-3")]
    [InlineData("-1")]
    [InlineData("80-abc")]
    [InlineData("1～65536")]
    public void TryParsePorts_InvalidInput_ReturnsFalseWithEmptyPorts(string input)
    {
        var ok = PortQuery.TryParsePorts(input, out var ports, out var message);

        Assert.False(ok);
        Assert.Empty(ports);
        Assert.Contains("无效", message);
    }

    // 批量结果组装：纯函数按端口升序输出，同一 PID 只解析一次进程名
    [Fact]
    public void BuildBatchResults_MixedPorts_DeduplicatesAndReusesProcessName()
    {
        var listeners = new Dictionary<int, int> { [80] = 4, [443] = 4, [8080] = 1234 };
        var resolveCount = 0;
        string Resolve(int pid)
        {
            resolveCount++;
            return pid == 4 ? "System" : "App";
        }

        var results = PortQuery.BuildBatchResults(
            new[] { 8080, 80, 99, 443, 80 }, listeners, Resolve);

        Assert.Equal(4, results.Count);
        Assert.Equal(new[] { 80, 99, 443, 8080 }, results.Select(r => r.Port));
        Assert.Equal("occupied", results[0].Status);
        Assert.Equal("System", results[0].ProcessName);
        Assert.Equal("free", results[1].Status);
        Assert.Null(results[1].Pid);
        Assert.Equal(2, resolveCount); // PID 4 与 1234 各解析一次
    }

    // 无进程名解析器时，占用端口仍保留 PID，只把名字降级为"未知进程"
    [Fact]
    public void BuildBatchResults_WithoutResolver_FallsBackToUnknownProcess()
    {
        var results = PortQuery.BuildBatchResults(
            new[] { 135 }, new Dictionary<int, int> { [135] = 916 });

        Assert.Equal("未知进程", results[0].ProcessName);
        Assert.Equal(916, results[0].Pid);
    }

    // 批量查询入口：空集合与越界端口直接报错，不启动 netstat
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QueryPorts_InvalidRequest_ReturnsError(bool empty)
    {
        var result = PortQuery.QueryPorts(empty ? Array.Empty<int>() : new[] { 70000 });

        Assert.Equal("error", result.Status);
        Assert.Empty(result.Results);
        Assert.NotEmpty(result.Message);
    }

    // 详情：netstat -ano 同时解析 TCP 连接与 UDP 行（UDP 无状态列）
    [Fact]
    public void ParseConnections_TcpAndUdp_ExtractsFullRows()
    {
        const string sample = """
            Active Connections

              Proto  Local Address          Foreign Address        State           PID
              TCP    0.0.0.0:6379           0.0.0.0:0              LISTENING       1234
              TCP    127.0.0.1:6379         127.0.0.1:54321        ESTABLISHED     1234
              UDP    0.0.0.0:5353           *:*                                    2468
            """;

        var rows = PortQuery.ParseConnections(sample);

        Assert.Equal(3, rows.Count);
        Assert.Equal("TCP", rows[0].Protocol);
        Assert.Equal("0.0.0.0", rows[0].LocalAddress);
        Assert.Equal("LISTENING", rows[0].State);
        Assert.True(rows[0].IsWildcard);
        Assert.Equal("ESTABLISHED", rows[1].State);
        Assert.Equal("UDP", rows[2].Protocol);
        Assert.Equal("—", rows[2].State);
        Assert.Equal(2468, rows[2].Pid);
    }

    // 详情：按端口聚合，状态计数，同一 PID 只读一次进程信息
    [Fact]
    public void BuildOccupancies_GroupsRowsAndReadsEachPidOnce()
    {
        const string sample = """
              TCP    0.0.0.0:6379     0.0.0.0:0       LISTENING     1234
              TCP    127.0.0.1:6379   127.0.0.1:5555  ESTABLISHED   1234
              UDP    0.0.0.0:5353     *:*             —             2468
            """;
        var rows = PortQuery.ParseConnections(sample);
        var readPids = new List<int>();
        ProcessDetail FakeRead(int pid)
        {
            readPids.Add(pid);
            return new ProcessDetail(pid, "proc" + pid, null, null, null, null, null, null, null, null, "");
        }

        var result = PortQuery.BuildOccupancies(new[] { 6379, 5353, 9999 }, rows, FakeRead);

        Assert.Equal(2, result.Count);
        Assert.Equal(5353, result[0].Port); // 按端口升序
        Assert.Equal(6379, result[1].Port);
        Assert.Equal("UDP", result[0].Protocols);
        Assert.Equal(2, result[1].Connections.Count);
        Assert.Contains("LISTENING", result[1].States);
        Assert.Contains("ESTABLISHED", result[1].States);
        Assert.Equal("high", result[1].RiskLevel);
        Assert.Equal(new[] { 1234 }, result[1].Pids.ToArray());
        Assert.Equal(new[] { 1234, 2468 }, readPids.OrderBy(p => p).ToArray());
    }

    // 风险：常见高危端口、回环降级、普通端口不误报
    [Theory]
    [InlineData(6379, "0.0.0.0", "high")]
    [InlineData(6379, "127.0.0.1", "medium")]
    [InlineData(3389, "0.0.0.0", "high")]
    [InlineData(80, "0.0.0.0", "medium")]
    [InlineData(443, "0.0.0.0", "low")]
    [InlineData(54321, "0.0.0.0", "none")]
    public void PortRisk_EvaluatesByPortAndListenScope(int port, string address, string expected)
    {
        var rows = new[] { new PortConnection("TCP", address, port, "0.0.0.0:0", "LISTENING", 1) };

        var (level, reasons) = PortRiskEvaluator.Evaluate(port, rows);

        Assert.Equal(expected, level);
        if (expected == "none") Assert.Empty(reasons);
        else Assert.NotEmpty(reasons);
    }

    [Fact]
    public void DetailResult_CountsOccupiedFreeAndHighRisk()
    {
        var item = new PortOccupancy(
            6379,
            Array.Empty<PortConnection>(),
            Array.Empty<ProcessDetail>(),
            "high",
            new[] { "Redis 未授权访问风险" });

        var result = PortQueryDetailResult.Ok(new[] { item }, 3);

        Assert.Equal("ok", result.Status);
        Assert.Equal(1, result.OccupiedCount);
        Assert.Equal(2, result.FreeCount);
        Assert.Equal(1, result.HighRiskCount);
    }

    [Fact]
    public void FormatAge_NullAndMultiDay_AreReadable()
    {
        Assert.Equal("未知（进程启动时间不可读）", PortDetailFormat.FormatAge(null));
        Assert.Contains("天", PortDetailFormat.FormatAge(TimeSpan.FromDays(2) + TimeSpan.FromHours(3)));
        Assert.Contains("小时", PortDetailFormat.FormatAge(TimeSpan.FromHours(5) + TimeSpan.FromMinutes(12)));
    }
}
