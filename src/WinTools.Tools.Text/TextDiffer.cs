namespace WinTools.Tools.Text;

public enum DiffLineKind { Equal, Added, Removed }

/// <summary>一行对比结果：保留原文与两侧行号，便于界面高亮与定位。</summary>
public sealed record DiffLine(DiffLineKind Kind, string Text, int OldLine, int NewLine);

/// <summary>文本对比结果：统一差异序列 + 统计 + 是否因过大而截断。</summary>
public sealed record DiffResult(
    IReadOnlyList<DiffLine> Lines,
    int AddedCount,
    int RemovedCount,
    bool Truncated,
    string? ErrorMessage)
{
    public static DiffResult Fail(string msg) => new([], 0, 0, false, msg);
}

/// <summary>
/// 行级文本对比：基于最长公共子序列（LCS）生成 相等 / 新增 / 删除 序列。
/// 纯函数、无副作用；超过行数上限时截断并标记，避免大文本把内存和界面拖垮。
/// </summary>
public static class TextDiffer
{
    /// <summary>参与对比的最大行数；LCS 是 O(n*m)，必须封顶。</summary>
    public const int MaxLines = 800;

    public static DiffResult Compare(string left, string right)
    {
        var a = SplitLines(left);
        var b = SplitLines(right);
        bool truncated = a.Count > MaxLines || b.Count > MaxLines;
        if (truncated)
        {
            a = a.Take(MaxLines).ToList();
            b = b.Take(MaxLines).ToList();
        }

        var ops = Lcs(a, b);
        var lines = new List<DiffLine>(ops.Count);
        int oldNo = 1, newNo = 1, added = 0, removed = 0;
        foreach (var (kind, text) in ops)
        {
            switch (kind)
            {
                case DiffLineKind.Equal:
                    lines.Add(new DiffLine(kind, text, oldNo++, newNo++));
                    break;
                case DiffLineKind.Removed:
                    // 删除行只存在于原文：新侧行号记 0，便于界面区分
                    lines.Add(new DiffLine(kind, text, oldNo++, 0));
                    removed++;
                    break;
                case DiffLineKind.Added:
                    // 新增行只存在于新文：原侧行号记 0
                    lines.Add(new DiffLine(kind, text, 0, newNo++));
                    added++;
                    break;
            }
        }
        return new DiffResult(lines, added, removed, truncated, null);
    }

    /// <summary>按 \r\n / \n / \r 统一拆行；空文本视为 0 行。</summary>
    private static List<string> SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var parts = normalized.Split('\n');
        // 末尾换行不额外产生一个空行
        if (parts.Length > 0 && parts[^1].Length == 0 && normalized.EndsWith('\n'))
            return parts[..^1].ToList();
        return parts.ToList();
    }

    /// <summary>
    /// 经典 LCS 回溯：先算 dp 表，再从右下往左上还原操作序列。
    /// 相等优先，其次取 LCS 更长的一边，保证删除在前、新增在后，读起来像补丁。
    /// </summary>
    private static List<(DiffLineKind Kind, string Text)> Lcs(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        int n = a.Count, m = b.Count;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j]
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var result = new List<(DiffLineKind, string)>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { result.Add((DiffLineKind.Equal, a[x])); x++; y++; }
            else if (dp[x + 1, y] >= dp[x, y + 1]) { result.Add((DiffLineKind.Removed, a[x])); x++; }
            else { result.Add((DiffLineKind.Added, b[y])); y++; }
        }
        while (x < n) result.Add((DiffLineKind.Removed, a[x++]));
        while (y < m) result.Add((DiffLineKind.Added, b[y++]));
        return result;
    }
}
