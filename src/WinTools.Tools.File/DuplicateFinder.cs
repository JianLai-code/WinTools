using System.Security.Cryptography;
using System.Text;

namespace WinTools.Tools.Files;

/// <summary>一组内容相同的文件：-size 阶段先按大小分组，hash 阶段再按 SHA-256 分组。</summary>
public sealed record DuplicateGroup(string Hash, long Size, IReadOnlyList<string> Paths);

/// <summary>
/// 重复文件查找的纯逻辑层。文件枚举与哈希读取通过参数/委托注入，
/// 单测无需在磁盘上造大文件即可覆盖分组、阈值、取消与"只报告不删除"。
/// </summary>
public static class DuplicateFinder
{
    public const int BufferSize = 1024 * 1024;

    /// <summary>
    /// 在候选文件中找重复：先按大小分组（大小唯一的文件根本不可能是副本，不读内容），
    /// 只对同大小组计算哈希，最后按哈希分组。全程只读，绝不删除或移动文件。
    /// </summary>
    public static IReadOnlyList<DuplicateGroup> Find(
        IEnumerable<string> files,
        Func<string, string> hash,
        long minBytes = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(hash);

        var bySize = new Dictionary<long, List<string>>();
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long size;
            try { size = new FileInfo(path).Length; }
            catch { continue; } // 文件被删/无权读取：跳过，不影响其他条目
            if (size <= 0 || size < minBytes) continue;
            if (!bySize.TryGetValue(size, out var list)) bySize[size] = list = new List<string>();
            list.Add(path);
        }

        var groups = new List<DuplicateGroup>();
        foreach (var (size, paths) in bySize.OrderByDescending(kv => kv.Key))
        {
            if (paths.Count < 2) continue;
            var byHash = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string digest;
                try { digest = hash(path); }
                catch { continue; } // 单个文件读失败不拖垮整次扫描
                if (!byHash.TryGetValue(digest, out var same)) byHash[digest] = same = new List<string>();
                same.Add(path);
            }
            foreach (var (digest, same) in byHash)
                if (same.Count > 1)
                    groups.Add(new DuplicateGroup(digest, size, same.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()));
        }
        return groups.OrderByDescending(g => g.Size * (g.Paths.Count - 1)).ToList();
    }

    /// <summary>递归枚举文件。无权限子目录跳过并通过 onError 回调报告，扫描不中断。</summary>
    public static IReadOnlyList<string> EnumerateFiles(
        string root,
        CancellationToken cancellationToken = default,
        Action<string>? onError = null)
    {
        var result = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    result.Add(file);
                foreach (var child in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                    stack.Push(child);
            }
            catch (UnauthorizedAccessException) { onError?.Invoke(dir); }
            catch (IOException) { onError?.Invoke(dir); }
        }
        return result;
    }

    /// <summary>流式 SHA-256：一次只读一遍文件，内存恒定；FileShare.ReadWrite 允许读取被其他程序打开的文件。</summary>
    public static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            BufferSize, FileOptions.SequentialScan);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            sha.AppendData(buffer.AsSpan(0, read));
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        foreach (var unit in new[] { "KB", "MB", "GB", "TB" })
        {
            v /= 1024;
            if (v < 1024 || unit == "TB") return $"{v:F1} {unit}";
        }
        return $"{bytes} B";
    }

    /// <summary>可复制的报告文本：每组列出哈希、大小与全部路径，一眼可核对。只报告、不提供删除动作。</summary>
    public static string BuildReport(IEnumerable<DuplicateGroup> groups)
    {
        var sb = new StringBuilder();
        foreach (var g in groups)
        {
            sb.AppendLine($"SHA-256: {g.Hash}");
            sb.AppendLine($"单个大小: {FormatSize(g.Size)}；共 {g.Paths.Count} 份；可释放 {FormatSize(g.Size * (g.Paths.Count - 1L))}");
            foreach (var p in g.Paths) sb.AppendLine($"  {p}");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
