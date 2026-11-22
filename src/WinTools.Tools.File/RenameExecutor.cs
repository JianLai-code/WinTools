namespace WinTools.Tools.Files;

/// <summary>一条真实的重命名记录（成功才入栈），供撤销使用。</summary>
public sealed record RenameRecord(string OldPath, string NewPath)
{
    public string OldName => Path.GetFileName(OldPath);
    public string NewName => Path.GetFileName(NewPath);
}

/// <summary>执行结果：成功的记录 + 失败的说明。</summary>
public sealed class RenameExecutionResult
{
    public RenameExecutionResult(IReadOnlyList<RenameRecord> records, IReadOnlyList<string> errors)
    {
        Records = records;
        Errors = errors;
    }

    public IReadOnlyList<RenameRecord> Records { get; }
    public IReadOnlyList<string> Errors { get; }
    public int SucceededCount => Records.Count;
}

/// <summary>
/// F2 中唯一修改文件系统的组件。所有写操作集中在此，便于审计。
///
/// 安全底线：
/// 1. 执行前对每条再次校验——源文件必须存在、目标文件必须不存在（防生成计划后到执行前的竞态）
/// 2. 只用 File.Move 且目标已确认空缺，绝不覆盖（F2-AC2 阻断级缺陷的红线）
/// 3. 单条失败不影响其他条目，错误逐条收集
/// 4. 成功的记录进撤销栈，可原路恢复（F2-AC5）
/// </summary>
public static class RenameExecutor
{
    /// <summary>执行重命名计划。真正写磁盘。</summary>
    public static RenameExecutionResult Execute(RenamePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var records = new List<RenameRecord>();
        var errors = new List<string>();

        foreach (var item in plan.Items)
        {
            if (!item.IsChanged) continue;

            try
            {
                // 二次校验：防覆盖、防源文件被移走
                if (!File.Exists(item.OldPath))
                {
                    errors.Add($"{item.OldName}：源文件不存在，已跳过");
                    continue;
                }
                if (File.Exists(item.NewPath) || Directory.Exists(item.NewPath))
                {
                    errors.Add($"{item.OldName}：目标 {item.NewName} 已存在，为防覆盖已跳过");
                    continue;
                }

                File.Move(item.OldPath, item.NewPath);
                records.Add(new RenameRecord(item.OldPath, item.NewPath));
            }
            catch (Exception ex)
            {
                // L13：保留异常类型与完整路径，便于诊断权限不足/占用等根因。
                errors.Add($"{item.OldName}（{item.OldPath} → {item.NewPath}）{ex.GetType().Name}：{ex.Message}");
            }
        }

        return new RenameExecutionResult(records, errors);
    }

    /// <summary>
    /// 纯函数：把成功记录反转成"撤销计划"（新名 → 原名）。
    /// 单测用它验证撤销映射的正确性，无需真实读写文件。
    /// 逆序处理，避免恢复时与后续记录冲突。
    /// </summary>
    public static RenamePlan BuildUndoPlan(IReadOnlyList<RenameRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var items = new List<RenamePlanItem>(records.Count);
        for (var i = records.Count - 1; i >= 0; i--)
        {
            var r = records[i];
            var directory = Path.GetDirectoryName(r.NewPath) ?? string.Empty;
            items.Add(new RenamePlanItem(directory, r.NewName, r.OldName, "撤销恢复原名"));
        }
        return new RenamePlan(items);
    }

    /// <summary>真正执行撤销：按 BuildUndoPlan 的结果逐个恢复。同样二次校验防覆盖。</summary>
    public static IReadOnlyList<string> Undo(IReadOnlyList<RenameRecord> records)
    {
        var undoPlan = BuildUndoPlan(records);
        var errors = new List<string>();
        foreach (var item in undoPlan.Items)
        {
            try
            {
                if (!File.Exists(item.OldPath))
                {
                    errors.Add($"{item.OldName}：文件不存在，无法撤销");
                    continue;
                }
                if (File.Exists(item.NewPath) || Directory.Exists(item.NewPath))
                {
                    errors.Add($"{item.OldName}：原名位置已被占用，无法撤销");
                    continue;
                }
                File.Move(item.OldPath, item.NewPath);
            }
            catch (Exception ex)
            {
                errors.Add($"{item.OldName}（{item.OldPath} → {item.NewPath}）{ex.GetType().Name}：{ex.Message}");
            }
        }
        return errors;
    }
}
