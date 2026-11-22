using System.Collections;
using System.Text;

namespace WinTools.Tools.SystemTools;

/// <summary>
/// 环境变量读取与筛选的纯逻辑类。Collect() 读当前进程环境（继承用户与系统设置），
/// CollectFrom / Filter / FormatForCopy 都可注入数据，便于单元测试。
/// 全程只读，不修改任何环境变量。
/// </summary>
public static class EnvironmentVariableCollector
{
    /// <summary>读取当前进程的环境变量，按变量名不区分大小写排序。</summary>
    public static IReadOnlyList<EnvironmentVariableItem> Collect() =>
        CollectFrom(Environment.GetEnvironmentVariables());

    /// <summary>从任意 IDictionary 采集；跳过空变量名，null 值按空串处理。</summary>
    public static IReadOnlyList<EnvironmentVariableItem> CollectFrom(IDictionary source)
    {
        var result = new List<EnvironmentVariableItem>();
        foreach (DictionaryEntry entry in source)
        {
            var name = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            result.Add(new EnvironmentVariableItem(name, entry.Value?.ToString() ?? string.Empty));
        }
        return result
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>按变量名或变量值筛选；关键词为空时返回原列表。</summary>
    public static IReadOnlyList<EnvironmentVariableItem> Filter(
        IEnumerable<EnvironmentVariableItem> items, string? keyword)
    {
        var key = keyword?.Trim() ?? string.Empty;
        if (key.Length == 0) return items.ToList();
        return items
            .Where(x => x.Name.Contains(key, StringComparison.OrdinalIgnoreCase)
                     || x.Value.Contains(key, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>生成可复制的 NAME=VALUE 文本，每行一条。</summary>
    public static string FormatForCopy(IEnumerable<EnvironmentVariableItem> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items) sb.AppendLine($"{item.Name}={item.Value}");
        return sb.ToString();
    }
}
