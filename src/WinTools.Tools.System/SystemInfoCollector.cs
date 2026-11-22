using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinTools.Tools.SystemTools;

/// <summary>
/// 系统信息收集的纯逻辑类，不依赖 UI。对应 S1-AC1~AC4。
///
/// 核心设计：每个字段的读取都包在独立的 try-catch 里（由 SafeRead 统一包装）。
/// 任何一项失败都只标记该项"不可用"，其余照常显示——这是 S1-AC4 的关键。
/// 全部使用只读 API，普通用户权限即可，无需管理员（S1-AC2）。
/// 零第三方依赖：CPU 从注册表读，内存用 Win32 API，磁盘用 DriveInfo。
/// </summary>
public static class SystemInfoCollector
{
    /// <summary>收集全部信息。返回值中每项都带 Available 标记。</summary>
    public static List<SystemInfoItem> Collect()
    {
        // 注入默认读取源：每个 key 对应一个返回字符串的委托。
        var sources = new Dictionary<string, Func<string>>
        {
            ["操作系统"] = ReadOsVersion,
            ["CPU 型号"] = ReadCpuName,
            ["物理内存"] = ReadTotalMemory,
            ["磁盘容量"] = ReadDiskCapacity,
            ["计算机名"] = () => Environment.MachineName,
            [".NET 版本"] = () => Environment.Version.ToString()
        };
        return CollectFrom(sources);
    }

    /// <summary>
    /// 可注入版本：供单元测试传入"会抛异常的读取源"，验证容错逻辑（阶段七 S1 单测要求）。
    /// 每个源独立 SafeRead，一个抛异常不影响其他。
    /// </summary>
    public static List<SystemInfoItem> CollectFrom(IReadOnlyDictionary<string, Func<string>> sources)
    {
        var result = new List<SystemInfoItem>();
        foreach (var (name, reader) in sources)
        {
            result.Add(SafeRead(name, reader));
        }
        return result;
    }

    /// <summary>安全读取单个字段：成功返回值，失败返回"不可用"，绝不抛出。</summary>
    private static SystemInfoItem SafeRead(string name, Func<string> reader)
    {
        try
        {
            var value = reader();
            return string.IsNullOrWhiteSpace(value)
                ? SystemInfoItem.Unavailable(name)
                : SystemInfoItem.Ok(name, value);
        }
        catch
        {
            return SystemInfoItem.Unavailable(name);
        }
    }

    // ---- 各字段的实际读取实现（均为只读、普通权限）----

    private static string ReadOsVersion()
    {
        var os = Environment.OSVersion;
        // 注册表补上 Windows 显示版本（如 "Windows 11 Pro"），读不到就退回版本号。
        var display = ReadRegistryString(
            Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "ProductName");
        return string.IsNullOrEmpty(display)
            ? $"{os.Platform} {os.Version}"
            : $"{display} ({os.Version})";
    }

    private static string ReadCpuName()
        => ReadRegistryString(
            Registry.LocalMachine,
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "ProcessorNameString") ?? "未知 CPU";

    private static string ReadTotalMemory()
    {
        if (!TryGetMemoryStatus(out var mem))
        {
            throw new InvalidOperationException("无法读取内存信息");
        }
        // 注意字段类型是 ulong，表示字节数。
        return FormatBytes((long)mem.ullTotalPhys);
    }

    private static string ReadDiskCapacity()
    {
        // 汇总所有"就绪的固定磁盘"的总容量与可用空间。
        long total = 0, free = 0;
        var any = false;
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                {
                    total += drive.TotalSize;
                    free += drive.AvailableFreeSpace;
                    any = true;
                }
            }
            catch
            {
                // 单个磁盘读取失败跳过，不影响其他磁盘（同样是容错思路）。
            }
        }
        if (!any)
        {
            throw new InvalidOperationException("没有可用的固定磁盘");
        }
        return $"{FormatBytes(total)}（可用 {FormatBytes(free)}）";
    }

    private static string? ReadRegistryString(RegistryKey root, string subKey, string valueName)
    {
        using var key = root.OpenSubKey(subKey);
        return key?.GetValue(valueName)?.ToString();
    }

    /// <summary>把字节数格式化成易读的 B/KB/MB/GB/TB。</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    // ---- P/Invoke：读取物理内存 ----
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    private static bool TryGetMemoryStatus(out MemoryStatusEx status)
    {
        status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status);
    }
}
