namespace WinTools.Tools.Files;

/// <summary>一个可展示的目录节点：路径、直接文件数、递归总字节、递归文件数、是否部分目录无权限。</summary>
public sealed record FolderSizeNode(
    string Path,
    string Name,
    int DirectFiles,
    long TotalBytes,
    int TotalFiles,
    bool HasDenied);

/// <summary>文件夹大小分析结果：根节点汇总 + 一级子项（按大小降序）。</summary>
public sealed class FolderSizeResult
{
    public string Root { get; init; } = "";
    public long TotalBytes { get; init; }
    public int TotalFiles { get; init; }
    public int TotalDirs { get; init; }
    public int DeniedCount { get; init; }
    public IReadOnlyList<FolderSizeNode> Children { get; init; } = [];
}

/// <summary>
/// F3 文件夹大小分析的纯逻辑：统计根目录下每个一级子项的递归总大小，按大小降序。
///
/// 只统计、只读，不修改任何文件。无权限的子目录跳过并计入 DeniedCount，
/// 不因单个目录失败而中断整体。扫描过程由调用方传 CancellationToken 支持取消。
/// </summary>
public static class FolderSizeAnalyzer
{
    /// <summary>递归统计一个目录的总字节与文件数；无权限/竞态消失的目录被跳过并记账。</summary>
    public static (long Bytes, int Files, int Denied) SumDirectory(string path, CancellationToken ct = default)
    {
        long bytes = 0;
        int files = 0;
        int denied = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    bytes += new FileInfo(file).Length;
                    files++;
                }
                catch (IOException) { /* 文件被占用或消失：跳过 */ }
                catch (UnauthorizedAccessException) { denied++; }
            }
        }
        catch (UnauthorizedAccessException) { denied++; return (bytes, files, denied); }
        catch (DirectoryNotFoundException) { return (bytes, files, denied); }
        catch (IOException) { denied++; }

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                var sub = SumDirectory(dir, ct);
                bytes += sub.Bytes;
                files += sub.Files;
                denied += sub.Denied;
            }
        }
        catch (UnauthorizedAccessException) { denied++; }
        catch (DirectoryNotFoundException) { /* 竞态删除 */ }
        catch (IOException) { denied++; }

        return (bytes, files, denied);
    }

    /// <summary>分析根目录：先统计一级子目录/文件，再按大小降序排列。</summary>
    public static FolderSizeResult Analyze(string root, CancellationToken ct = default)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"目录不存在：{root}");

        var nodes = new List<FolderSizeNode>();
        long rootBytes = 0;
        int rootFiles = 0;
        int rootDenied = 0;

        // 根目录直属文件
        int direct = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                rootBytes += new FileInfo(file).Length;
                rootFiles++;
                direct++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { rootDenied++; }
        }

        int dirCount = 0;
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            dirCount++;
            var (bytes, files, denied) = SumDirectory(dir, ct);
            rootBytes += bytes;
            rootFiles += files;
            rootDenied += denied;
            int subDirect = 0;
            try
            {
                subDirect = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly).Count();
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            nodes.Add(new FolderSizeNode(dir, Path.GetFileName(dir), subDirect, bytes, files, denied > 0));
        }

        var children = nodes.OrderByDescending(n => n.TotalBytes).ToList();
        return new FolderSizeResult
        {
            Root = root,
            TotalBytes = rootBytes,
            TotalFiles = rootFiles,
            TotalDirs = dirCount,
            DeniedCount = rootDenied,
            Children = children
        };
    }

    public static string FormatSize(long bytes) => ImageFormatCodec.FormatSize(bytes);
}
