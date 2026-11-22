using System.Text;
using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// F2 批量重命名规则引擎测试（阶段七指定用例）。
/// 纯逻辑测试用注入 existingNames 的重载，不访问真实文件系统；
/// Execute/Undo 用临时目录做端到端验证，验证后清理。
/// </summary>
public class RenameTests
{
    private static readonly string VirtualDir = Path.Combine("C:", "WinToolsTest");

    // 阶段七：Plan_FiveFilesWithPrefix_ProducesExpectedNames（F2-AC1）
    [Fact]
    public void Plan_FiveFilesWithPrefix_ProducesExpectedNames()
    {
        var files = Enumerable.Range(1, 5)
            .Select(i => Path.Combine(VirtualDir, $"file{i}.jpg"))
            .ToArray();
        var rule = new RenameRule { Template = "照片{n}", StartIndex = 1 };

        var plan = RenamePlanner.BuildPlan(files, rule, Array.Empty<string>());

        Assert.Equal(5, plan.Items.Count);
        Assert.Equal(5, plan.ChangedCount);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal($"照片{i + 1}.jpg", plan.Items[i].NewName);
            Assert.Equal(".jpg", Path.GetExtension(plan.Items[i].NewName)); // 扩展名保留
        }
    }

    // 阶段七：Plan_IllegalChars_AreFiltered（F2-AC3）
    [Fact]
    public void Plan_IllegalChars_AreFiltered()
    {
        var files = new[] { Path.Combine(VirtualDir, "src.txt") };
        var rule = new RenameRule { Template = "a\\b/c:d*e?f\"g<h>i|j{n}" };

        var plan = RenamePlanner.BuildPlan(files, rule, Array.Empty<string>());
        var newName = plan.Items[0].NewName;

        Assert.DoesNotContain("\\", newName);
        Assert.DoesNotContain("/", newName);
        Assert.DoesNotContain(":", newName);
        Assert.DoesNotContain("*", newName);
        Assert.DoesNotContain("?", newName);
        Assert.DoesNotContain("\"", newName);
        Assert.DoesNotContain("<", newName);
        Assert.DoesNotContain(">", newName);
        Assert.DoesNotContain("|", newName);
        Assert.Equal("abcdefghij1.txt", newName);
        Assert.Contains("非法字符", plan.Items[0].Note); // 必须告知用户
    }

    // 阶段七：Plan_NameCollision_AutoDisambiguates（F2-AC2）
    [Fact]
    public void Plan_NameCollision_AutoDisambiguates()
    {
        var files = new[] { Path.Combine(VirtualDir, "a.jpg") }; // 源扩展名须为 .jpg 才会与现有文件冲突
        var rule = new RenameRule { Template = "照片{n}", StartIndex = 1 };
        // 磁盘上已存在 照片1.jpg 与 照片1(1).jpg，应避让到 (2)
        var existing = new[] { "照片1.jpg", "照片1(1).jpg" };

        var plan = RenamePlanner.BuildPlan(files, rule, existing);
        var newName = plan.Items[0].NewName;

        Assert.Equal("照片1(2).jpg", newName);
        Assert.Contains("避让", plan.Items[0].Note);
    }

    // 大小写不敏感避让（Windows 文件系统语义）
    [Fact]
    public void Plan_CollisionIsCaseInsensitive()
    {
        var files = new[] { Path.Combine(VirtualDir, "a.txt") };
        var rule = new RenameRule { Template = "report{n}", StartIndex = 1 };
        var existing = new[] { "REPORT1.TXT" }; // 仅大小写不同

        var plan = RenamePlanner.BuildPlan(files, rule, existing);

        Assert.Equal("report1(1).txt", plan.Items[0].NewName);
    }

    // 阶段七：Plan_ExtensionAlwaysPreserved
    [Fact]
    public void Plan_ExtensionAlwaysPreserved()
    {
        var files = new[]
        {
            Path.Combine(VirtualDir, "a.jpg"),
            Path.Combine(VirtualDir, "b.PNG"),
            Path.Combine(VirtualDir, "noext")
        };
        var rule = new RenameRule { Template = "统一{n}{name}" };

        var plan = RenamePlanner.BuildPlan(files, rule, Array.Empty<string>());

        Assert.Equal(".jpg", Path.GetExtension(plan.Items[0].NewName));
        Assert.Equal(".PNG", Path.GetExtension(plan.Items[1].NewName)); // 大小写也保留
        Assert.Equal(string.Empty, Path.GetExtension(plan.Items[2].NewName));
    }

    // 序号补零与查找替换
    [Fact]
    public void Plan_PaddedIndexAndFindReplace()
    {
        var files = new[] { Path.Combine(VirtualDir, "old-1.txt"), Path.Combine(VirtualDir, "old-2.txt") };
        var rule = new RenameRule
        {
            Template = "item{n}",
            StartIndex = 7,
            FindText = "old",
            ReplaceText = "new" // 对模板结果做替换（此处模板无 old，验证不误伤）
        };

        var plan = RenamePlanner.BuildPlan(files, rule, Array.Empty<string>());

        Assert.Equal("item7.txt", plan.Items[0].NewName);
        Assert.Equal("item8.txt", plan.Items[1].NewName);
    }

    // 纯函数不改磁盘：BuildPlan 之后源文件必须原封不动
    [Fact]
    public void BuildPlan_DoesNotTouchDisk()
    {
        var dir = CreateTempDir();
        try
        {
            var a = Path.Combine(dir, "a.txt");
            var b = Path.Combine(dir, "b.txt");
            File.WriteAllText(a, "AAA");
            File.WriteAllText(b, "BBB", Encoding.UTF8);

            var plan = RenamePlanner.BuildPlan(new[] { a, b }, new RenameRule { Template = "x{n}" });

            Assert.True(File.Exists(a));
            Assert.True(File.Exists(b));
            Assert.Equal("AAA", File.ReadAllText(a));
            Assert.Equal("BBB", File.ReadAllText(b));
            Assert.Equal(2, plan.ChangedCount);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    // 阶段七：Undo_RestoresOriginalNames（纯函数验证撤销映射）
    [Fact]
    public void Undo_RestoresOriginalNames()
    {
        var r1 = new RenameRecord(Path.Combine(VirtualDir, "a.txt"), Path.Combine(VirtualDir, "照片1.jpg"));
        var r2 = new RenameRecord(Path.Combine(VirtualDir, "b.png"), Path.Combine(VirtualDir, "照片2.png"));

        var undo = RenameExecutor.BuildUndoPlan(new[] { r1, r2 });

        Assert.Equal(2, undo.Items.Count);
        // 逆序：最后改的先撤销
        Assert.Equal("照片2.png", undo.Items[0].OldName);
        Assert.Equal("b.png", undo.Items[0].NewName);
        Assert.Equal("照片1.jpg", undo.Items[1].OldName);
        Assert.Equal("a.txt", undo.Items[1].NewName);
    }

    // 端到端：Execute 真改名，Undo 真恢复（F2-AC5）
    [Fact]
    public void ExecuteAndUndo_RoundTripOnRealFiles()
    {
        var dir = CreateTempDir();
        try
        {
            var a = Path.Combine(dir, "a.txt");
            var b = Path.Combine(dir, "b.txt");
            File.WriteAllText(a, "AAA");
            File.WriteAllText(b, "BBB");

            var plan = RenamePlanner.BuildPlan(new[] { a, b }, new RenameRule { Template = "doc{n}" });
            var result = RenameExecutor.Execute(plan);

            Assert.Equal(2, result.SucceededCount);
            Assert.Empty(result.Errors);
            Assert.False(File.Exists(a));
            Assert.True(File.Exists(Path.Combine(dir, "doc1.txt")));
            Assert.Equal("AAA", File.ReadAllText(Path.Combine(dir, "doc1.txt")));

            var undoErrors = RenameExecutor.Undo(result.Records);
            Assert.Empty(undoErrors);
            Assert.True(File.Exists(a) && File.Exists(b));
            Assert.False(File.Exists(Path.Combine(dir, "doc1.txt")));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    // F2-AC2 红线：目标已存在时，绝不覆盖
    [Fact]
    public void Execute_TargetExists_NeverOverwrites()
    {
        var dir = CreateTempDir();
        try
        {
            var src = Path.Combine(dir, "src.txt");
            var existingTarget = Path.Combine(dir, "target.txt");
            File.WriteAllText(src, "NEW-CONTENT");
            File.WriteAllText(existingTarget, "ORIGINAL-CONTENT");

            // 手工构造一个指向已存在目标的计划条目（模拟生成计划后目标被占用的竞态）
            var item = new RenamePlanItem(dir, "src.txt", "target.txt", "");
            var plan = new RenamePlan(new[] { item });

            var result = RenameExecutor.Execute(plan);

            Assert.Equal(0, result.SucceededCount);
            Assert.Single(result.Errors);
            Assert.Contains("防覆盖", result.Errors[0]);
            Assert.True(File.Exists(src)); // 源文件没动
            Assert.Equal("ORIGINAL-CONTENT", File.ReadAllText(existingTarget)); // 目标内容未被覆盖
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wintools_rename_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* 临时目录清理失败不影响测试结论 */ }
    }
}
