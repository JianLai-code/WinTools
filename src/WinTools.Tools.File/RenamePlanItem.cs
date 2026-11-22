namespace WinTools.Tools.Files;

/// <summary>单个文件的重命名计划条目：只描述"打算怎么改"，不执行任何写操作。</summary>
public sealed class RenamePlanItem
{
    public RenamePlanItem(string sourceDirectory, string oldName, string newName, string note)
    {
        SourceDirectory = sourceDirectory;
        OldName = oldName;
        NewName = newName;
        Note = note;
    }

    /// <summary>文件所在目录。</summary>
    public string SourceDirectory { get; }

    /// <summary>原文件名（含扩展名）。</summary>
    public string OldName { get; }

    /// <summary>计划的新文件名（含扩展名，已避让重名）。</summary>
    public string NewName { get; }

    /// <summary>完整原路径。</summary>
    public string OldPath => Path.Combine(SourceDirectory, OldName);

    /// <summary>完整新路径。</summary>
    public string NewPath => Path.Combine(SourceDirectory, NewName);

    /// <summary>本条说明，如"非法字符已过滤""重名已避让为 xxx"。</summary>
    public string Note { get; }

    /// <summary>名称是否真的发生了变化。</summary>
    public bool IsChanged => !string.Equals(OldName, NewName, StringComparison.Ordinal);

    public override string ToString() => IsChanged ? $"{OldName} → {NewName}" : $"{OldName}（不变）";
}

/// <summary>一次重命名规划的完整结果（供预览表展示）。</summary>
public sealed class RenamePlan
{
    public RenamePlan(IReadOnlyList<RenamePlanItem> items)
    {
        Items = items;
    }

    public IReadOnlyList<RenamePlanItem> Items { get; }

    /// <summary>实际会发生改名的条目数。</summary>
    public int ChangedCount => Items.Count(i => i.IsChanged);
}
