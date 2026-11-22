using System.Text;
using WinTools.Tools.Files;
using WinTools.Tools.Text;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// 代码审计修复的回归测试：锁定本次修复的纯逻辑层行为，防止后续回退。
/// 不依赖 UI 与真实系统状态，可稳定重复运行。
/// </summary>
public sealed class AuditRegressionTests
{
    private static readonly string Dir = Path.Combine("C:", "AuditTest");

    // M11：纯大小写改名不应被自身占用误判为冲突而加 (1) 后缀。
    [Fact]
    public void Plan_CaseOnlyRename_KeepsExactNewName()
    {
        var files = new[] { Path.Combine(Dir, "photo.jpg") };
        var rule = new RenameRule { Template = "{name}", FindText = "photo", ReplaceText = "Photo" };

        var plan = RenamePlanner.BuildPlan(files, rule, new[] { "photo.jpg" });

        Assert.Equal("Photo.jpg", plan.Items[0].NewName);
        Assert.DoesNotContain("(1)", plan.Items[0].NewName);
    }

    // M11：链式改名中，某文件的新名恰好是"同批次另一个将被移走的源文件"的原名，
    // 不应被误判为占用而加 (1) 后缀。1.txt→2.txt、2.txt→3.txt。
    [Fact]
    public void Plan_TargetIsAnotherSourceName_NoSpuriousSuffix()
    {
        var files = new[]
        {
            Path.Combine(Dir, "1.txt"),
            Path.Combine(Dir, "2.txt")
        };
        var rule = new RenameRule { Template = "{n}", StartIndex = 2 };

        var plan = RenamePlanner.BuildPlan(files, rule, new[] { "1.txt", "2.txt" });

        Assert.Equal("2.txt", plan.Items[0].NewName);
        Assert.Equal("3.txt", plan.Items[1].NewName);
        Assert.DoesNotContain("(1)", plan.Items[0].NewName);
    }

    // M13：Windows 保留设备名应回退，不生成无法写入的名字。
    [Theory]
    [InlineData("CON")]
    [InlineData("aux")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void Plan_ReservedDeviceName_FallsBackToOriginal(string reserved)
    {
        var files = new[] { Path.Combine(Dir, "original.txt") };
        var rule = new RenameRule { Template = reserved };

        var plan = RenamePlanner.BuildPlan(files, rule, Array.Empty<string>());

        Assert.NotEqual(reserved + ".txt", plan.Items[0].NewName);
        Assert.Contains("保留字", plan.Items[0].Note);
    }

    // L27：StartIndex 接近 int.MaxValue 不应溢出成负数序号。
    [Fact]
    public void Plan_StartIndexNearMax_NoNegativeNumber()
    {
        var files = new[] { Path.Combine(Dir, "x.txt"), Path.Combine(Dir, "y.txt") };
        var rule = new RenameRule { Template = "n{n}", StartIndex = int.MaxValue - 1 };

        var plan = RenamePlanner.BuildPlan(files, rule, Array.Empty<string>());

        Assert.All(plan.Items, i => Assert.False(i.NewName.StartsWith('-')));
    }

    // M6：超大输入返回友好失败，不抛 OOM/异常。
    [Fact]
    public void Base64_OverLimit_ReturnsFriendlyFailure()
    {
        var huge = new string('a', Base64Codec.MaxInputLength + 1);

        var encoded = Base64Codec.Encode(huge);
        var decoded = Base64Codec.Decode(huge);

        Assert.False(encoded.Success);
        Assert.Contains("过长", encoded.ErrorMessage);
        Assert.False(decoded.Success);
    }

    [Fact]
    public void Json_OverLimit_ReturnsFriendlyFailure()
    {
        var huge = "{\"" + new string('a', JsonFormatter.MaxInputLength) + "\":1}";

        var result = JsonFormatter.Format(huge);

        Assert.False(result.Success);
        Assert.Contains("过长", result.ErrorMessage);
    }

    // L04：非 UTF-8 字节序列的 Base64 不再静默替换成 U+FFFD，而是返回失败。
    [Fact]
    public void Decode_InvalidUtf8Bytes_ReturnsFailure()
    {
        var invalidBytes = new byte[] { 0xFF, 0xFE, 0xFD };
        var b64 = Convert.ToBase64String(invalidBytes);

        var result = Base64Codec.Decode(b64);

        Assert.False(result.Success);
        Assert.DoesNotContain("\uFFFD", result.Text);
    }

    // L19：强制"每类必含"时，短密码的熵估算不应虚高到等于理想独立抽取值。
    [Fact]
    public void Entropy_ShortLengthWithAllSets_LowerThanIdeal()
    {
        var options = new PasswordOptions(4, true, true, true, true, true);
        var ideal = 4 * Math.Log2(26 + 26 + 10 + 26);

        var actual = PasswordGenerator.EstimateEntropyBits(options);

        Assert.True(actual < ideal, $"实际熵 {actual:0.0} 应低于理想值 {ideal:0.0}");
        Assert.True(actual > 0);
    }
}
