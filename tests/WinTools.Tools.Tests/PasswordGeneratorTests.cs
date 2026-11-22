using System.Linq;
using WinTools.Tools.Text;
using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// PasswordGenerator 单元测试，逐条实现阶段二 T4-AC1 ~ AC4 验收用例。
/// 测试名对齐阶段七"T4 随机密码生成"单测清单。
/// </summary>
public sealed class PasswordGeneratorTests
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digit = "0123456789";
    private const string Symbol = "!@#$%^&*()-_=+[]{}<>?,.;:~";
    private const string Ambiguous = "O0Il1|'\"";

    // T4-AC1：默认生成 → 长度与复杂度达标
    [Fact]
    public void Generate_DefaultLength_MeetsRequirement()
    {
        var result = PasswordGenerator.Generate(PasswordOptions.Default);

        Assert.True(result.Success);
        Assert.Equal(PasswordOptions.DefaultLength, result.Password.Length);
        // 默认全勾选：四类字符各至少出现一个（生成器保证）。
        Assert.Contains(result.Password, c => Upper.Contains(c));
        Assert.Contains(result.Password, c => Lower.Contains(c));
        Assert.Contains(result.Password, c => Digit.Contains(c));
        Assert.Contains(result.Password, c => Symbol.Contains(c));
    }

    // T4-AC2：长度 16 + 勾选全部字符集 → 满足所有勾选条件，且排除易混淆字符
    [Fact]
    public void Generate_16CharsAllSets_MeetsAllFlags()
    {
        var options = new PasswordOptions(16, true, true, true, true, true);

        var result = PasswordGenerator.Generate(options);

        Assert.True(result.Success);
        Assert.Equal(16, result.Password.Length);
        Assert.Contains(result.Password, c => Upper.Contains(c));
        Assert.Contains(result.Password, c => Lower.Contains(c));
        Assert.Contains(result.Password, c => Digit.Contains(c));
        Assert.Contains(result.Password, c => Symbol.Contains(c));
        // 排除易混淆字符生效。
        Assert.DoesNotContain(result.Password, c => Ambiguous.Contains(c));
    }

    // T4-AC3：连续 10 次生成 → 无重复，且每个字符都在选定集内
    [Fact]
    public void Generate_TenTimes_NoDuplicateAndInCharset()
    {
        var options = new PasswordOptions(16, true, true, true, false, true);
        var allowed = Upper + Lower + Digit; // 未勾选符号

        var passwords = Enumerable.Range(0, 10)
            .Select(_ => PasswordGenerator.Generate(options))
            .Select(r => { Assert.True(r.Success); return r.Password; })
            .ToList();

        Assert.Equal(10, passwords.Distinct().Count());              // 10 次全不重复
        Assert.All(passwords, p => Assert.Equal(16, p.Length));
        // 每个字符都必须在允许的字符集内，且不出现在未勾选的符号集里。
        Assert.All(passwords, p => Assert.All(p, c =>
        {
            Assert.Contains(c, allowed);
            Assert.DoesNotContain(c, Symbol);
            Assert.DoesNotContain(c, Ambiguous);
        }));
    }

    // 安全要求（阶段五）：未勾选任何字符集 → 友好失败，不生成空密码
    [Fact]
    public void Generate_NoCharacterSetSelected_FailsWithMessage()
    {
        var options = new PasswordOptions(16, false, false, false, false, true);

        var result = PasswordGenerator.Generate(options);

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Password);
        Assert.Contains("至少选择一种", result.ErrorMessage);
    }

    // 边界：长度超范围被钳制到 Min~Max，不抛异常
    [Theory]
    [InlineData(0, PasswordOptions.MinLength)]
    [InlineData(2, PasswordOptions.MinLength)]
    [InlineData(999, PasswordOptions.MaxLength)]
    public void Generate_LengthOutOfRange_ClampsToBounds(int requested, int expected)
    {
        var options = new PasswordOptions(requested, true, true, true, true, true);

        var result = PasswordGenerator.Generate(options);

        Assert.True(result.Success);
        Assert.Equal(expected, result.Password.Length);
    }

    // 强度提示：全字符集 16 位的熵应落在"强"或"极强"区间（界面提示有依据）
    [Fact]
    public void EstimateEntropyBits_AllSets16Bits_IsStrong()
    {
        var bits = PasswordGenerator.EstimateEntropyBits(PasswordOptions.Default);

        Assert.True(bits >= 60, $"实际熵 {bits:0} bit，预期 ≥ 60");
        Assert.Contains(PasswordGenerator.StrengthLabel(bits), new[] { "强", "极强" });
    }

    // 代码审查辅助断言：确认实现源码使用 RandomNumberGenerator 而非 Random（安全要求）。
    // 源文件由 csproj 的 Content 项复制到测试输出目录，用 AppContext.BaseDirectory 定位最稳。
    [Fact]
    public void Generate_SourceUsesCryptoRng_NotRandom()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PasswordGenerator.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("RandomNumberGenerator.GetInt32", source);
        Assert.DoesNotContain("new Random(", source);
    }
}
