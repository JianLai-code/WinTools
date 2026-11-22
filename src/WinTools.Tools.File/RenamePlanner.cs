using System.Globalization;
using System.Text;

namespace WinTools.Tools.Files;

/// <summary>
/// 批量重命名规则引擎——纯函数，只生成"原名 → 新名"计划，绝不执行任何写操作。
///
/// 安全设计（F2 是全局唯一写操作工具，这是最重要的防线）：
/// 1. 非法字符过滤：\ / : * ? " &lt; &gt; | 及控制字符（用 Path.GetInvalidFileNameChars）
/// 2. 扩展名永远保留，模板只作用于主文件名
/// 3. 重名避让：大小写不敏感，与磁盘现有文件、计划内新名冲突时自动加 (1)(2)…
/// 4. 绝不返回"覆盖计划"：任何冲突都转化为避让后的新名
///
/// 可测试性：提供注入 existingNames 的重载，单测无需真实建文件即可验证避让。
/// </summary>
public static class RenamePlanner
{
    /// <summary>默认重载：扫描每个源文件所在目录，以真实文件名作为避让依据。</summary>
    public static RenamePlan BuildPlan(IReadOnlyList<string> sourcePaths, RenameRule rule)
        => BuildPlanCore(sourcePaths, rule, directory => ScanDirectory(directory));

    /// <summary>
    /// 可注入重载：existingNames 视为"磁盘上已存在的文件名"，用于避让。
    /// 供单元测试使用——不访问真实文件系统。
    /// </summary>
    public static RenamePlan BuildPlan(
        IReadOnlyList<string> sourcePaths,
        RenameRule rule,
        IReadOnlyCollection<string> existingNames)
        => BuildPlanCore(sourcePaths, rule, _ => existingNames);

    private static RenamePlan BuildPlanCore(
        IReadOnlyList<string> sourcePaths,
        RenameRule rule,
        Func<string, IEnumerable<string>> namesForDirectory)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentNullException.ThrowIfNull(rule);

        var results = new RenamePlanItem[sourcePaths.Count];

        // 两阶段构建（M11）：先算出每条的候选名与"是否变化"，再统一避让。
        // 原实现一边遍历一边把源文件当前名当成"已占用"，导致：
        //   · 纯大小写改名 photo.jpg→Photo.jpg 被自身占用误判，避让成 Photo(1).jpg；
        //   · A↔B 交换时新名撞上"即将被移走"的对方文件，各自加 (1)。
        // 现在阶段二会先把所有"发生变化"的源文件名从占用集合移除——腾空的名字可由新名安全复用。
        var directories = new string[sourcePaths.Count];
        var oldNames = new string[sourcePaths.Count];
        var candidates = new string[sourcePaths.Count];
        var notesList = new List<string>[sourcePaths.Count];

        for (var i = 0; i < sourcePaths.Count; i++)
        {
            var path = sourcePaths[i];
            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            var oldName = Path.GetFileName(path);
            var extension = Path.GetExtension(oldName);
            var oldBase = Path.GetFileNameWithoutExtension(oldName);
            directories[i] = directory;
            oldNames[i] = oldName;

            var number = SafeNumber(rule, i);
            var numberText = rule.IndexWidth > 0
                ? number.ToString("D" + rule.IndexWidth, CultureInfo.InvariantCulture)
                : number.ToString(CultureInfo.InvariantCulture);

            var baseAfterTemplate = (rule.Template ?? string.Empty)
                .Replace("{name}", oldBase, StringComparison.Ordinal)
                .Replace("{n}", numberText, StringComparison.Ordinal);

            if (!string.IsNullOrEmpty(rule.FindText))
                baseAfterTemplate = baseAfterTemplate.Replace(
                    rule.FindText, rule.ReplaceText ?? string.Empty, StringComparison.Ordinal);

            var sanitized = Sanitize(baseAfterTemplate);
            var notes = new List<string>();
            if (!string.Equals(sanitized, baseAfterTemplate, StringComparison.Ordinal))
                notes.Add("含非法字符，已自动过滤");

            if (string.IsNullOrEmpty(sanitized))
            {
                // 模板结果被过滤空了：退回原名主名，保证不产生无主名文件
                sanitized = Sanitize(oldBase);
                if (string.IsNullOrEmpty(sanitized)) sanitized = "文件";
            }

            // M13：拦截 Windows 保留设备名与尾随空格/句点，避免 File.Move 抛异常被吞成"跳过"。
            if (!IsUsableBaseName(sanitized))
            {
                notes.Add("名称含保留字或结尾空格/句点，已回退原名");
                sanitized = Sanitize(oldBase);
                if (string.IsNullOrEmpty(sanitized) || !IsUsableBaseName(sanitized)) sanitized = "文件";
            }

            candidates[i] = sanitized + extension; // 扩展名原样保留（F2-AC1）
            notesList[i] = notes;
        }

        // 每个目录独立的"已占用文件名"集合（大小写不敏感，符合 Windows 文件系统语义）
        var occupiedByDir = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sourcePaths.Count; i++)
        {
            if (!occupiedByDir.TryGetValue(directories[i], out var occupied))
            {
                occupied = new HashSet<string>(namesForDirectory(directories[i]), StringComparer.OrdinalIgnoreCase);
                occupiedByDir[directories[i]] = occupied;
            }
            // 名称确实变化（区分大小写：纯大小写改名也算变化）的源文件会腾出原名，
            // 从占用集合移除，避免自身/交换对象被误判为冲突。
            if (!string.Equals(candidates[i], oldNames[i], StringComparison.Ordinal))
                occupied.Remove(oldNames[i]);
        }

        for (var i = 0; i < sourcePaths.Count; i++)
        {
            var occupied = occupiedByDir[directories[i]];
            var candidate = candidates[i];
            var notes = notesList[i];
            string finalName;
            if (string.Equals(candidate, oldNames[i], StringComparison.Ordinal))
            {
                // 名称无变化：不执行写操作，也无需避让（它本就是自己）
                finalName = oldNames[i];
            }
            else
            {
                finalName = Disambiguate(candidate, occupied);
                if (!string.Equals(finalName, candidate, StringComparison.Ordinal))
                    notes.Add($"重名已避让为 {finalName}");
            }

            occupied.Add(finalName); // 计划内新名之间也互相避让
            results[i] = new RenamePlanItem(directories[i], oldNames[i], finalName, string.Join("；", notes));
        }

        return new RenamePlan(results);
    }

    /// <summary>
    /// L27：序号溢出保护。UI 已把 StartIndex 限制在合理范围，但公共 API 可传任意 int，
    /// 接近 int.MaxValue 时 StartIndex+i 会溢出成负数，生成异常序号名。
    /// </summary>
    private static int SafeNumber(RenameRule rule, int index)
    {
        try { return checked(rule.StartIndex + index); }
        catch (OverflowException) { return int.MaxValue; }
    }

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>M13：Windows 保留设备名（含带扩展名形式）与尾随空格/句点不可用作主名。</summary>
    private static bool IsUsableBaseName(string baseName)
    {
        if (string.IsNullOrEmpty(baseName)) return false;
        if (char.IsWhiteSpace(baseName[^1]) || baseName[^1] == '.') return false;
        var stem = baseName.Split('.')[0];
        return !ReservedNames.Contains(stem);
    }

    /// <summary>
    /// 过滤 Windows 文件名非法字符（\ / : * ? " &lt; &gt; | 及控制字符）。
    /// 公开供单测直接验证过滤规则（F2-AC3）。
    /// </summary>
    public static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            // \0 等控制字符在部分运行时未纳入 GetInvalidFileNameChars，额外拦截
            if (c < ' ' || Array.IndexOf(invalid, c) >= 0) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>与已占用名（大小写不敏感）冲突时，依次尝试 name(1).ext、name(2).ext…。</summary>
    private static string Disambiguate(string candidate, HashSet<string> occupied)
    {
        if (!occupied.Contains(candidate)) return candidate;

        var extension = Path.GetExtension(candidate);
        var baseName = Path.GetFileNameWithoutExtension(candidate);
        for (var i = 1; ; i++)
        {
            var next = $"{baseName}({i}){extension}";
            if (!occupied.Contains(next)) return next;
        }
    }

    private static IEnumerable<string> ScanDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return Array.Empty<string>();
        try
        {
            // 必须立即 ToList：延迟枚举会把 UnauthorizedAccessException/IOException
            // 拖到预览刷新的枚举阶段才抛，那时已没有调用方能接住。
            return Directory.EnumerateFileSystemEntries(directory)
                .Select(Path.GetFileName).OfType<string>().ToList();
        }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
}
