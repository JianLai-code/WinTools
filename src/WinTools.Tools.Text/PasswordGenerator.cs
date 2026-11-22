using System.Security.Cryptography;
using System.Text;

namespace WinTools.Tools.Text;

/// <summary>
/// 随机密码生成的纯逻辑类，不依赖任何 UI，便于单元测试。
/// 对应验收用例 T4-AC1 ~ AC4（见阶段二文档）。
///
/// 安全要求（阶段五）：必须使用密码学安全随机数 RandomNumberGenerator，
/// 绝不能用 Random——Random 是可预测的，生成的密码不安全。
/// </summary>
public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digit = "0123456789";
    private const string Symbol = "!@#$%^&*()-_=+[]{}<>?,.;:~";

    // 易混淆字符：大写 O/I、小写 l、数字 0/1、符号 | ' " ` 等。
    private const string Ambiguous = "O0Il1|'\"";

    /// <summary>
    /// 按参数生成密码。对应 T4-AC1 / AC2 / AC3。
    /// </summary>
    public static PasswordResult Generate(PasswordOptions options)
    {
        var sets = GetActiveSets(options);
        if (sets.Count == 0)
        {
            return PasswordResult.Fail("请至少选择一种字符类型");
        }

        // 长度钳制行为保持不变（既有测试依赖：越界输入钳到 Min~Max）。
        var length = Math.Clamp(options.Length, PasswordOptions.MinLength, PasswordOptions.MaxLength);
        // L20 防御：长度若不足以容纳"每类一个"，按类别数兜底，避免生成的长度超出请求上限。
        if (length < sets.Count) length = sets.Count;
        var chars = new List<char>(length);

        // T4-AC2：保证每种勾选的字符集至少出现一个字符。
        // 若只是从合并集里纯随机取，可能某种类型一个都没抽到，就不满足"全部勾选条件"。
        foreach (var set in sets)
        {
            chars.Add(Pick(set));
        }

        // M10：补齐阶段从"合并后的字符集"按字符均匀抽取。
        // 原实现先均匀选类别、再类内取字符，导致单字符概率与类别大小成反比
        // （小类如数字被过度代表），实际熵低于估算值。合并集抽取使每个字符等概率。
        var combined = string.Concat(sets);
        while (chars.Count < length)
        {
            chars.Add(Pick(combined));
        }

        // Fisher-Yates 洗牌：避免"每类第一个字符总在开头"的可预测模式。
        // 洗牌索引同样用密码学安全随机数。
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return PasswordResult.Ok(new string(chars.ToArray()));
    }

    /// <summary>
    /// 估算密码熵（bits = 长度 × log2(字符集大小)），用于界面强度提示。
    /// </summary>
    public static double EstimateEntropyBits(PasswordOptions options)
    {
        var sets = GetActiveSets(options);
        var alphabetSize = 0;
        foreach (var set in sets)
        {
            alphabetSize += set.Length;
        }
        if (alphabetSize == 0)
        {
            return 0;
        }

        var length = Math.Clamp(options.Length, PasswordOptions.MinLength, PasswordOptions.MaxLength);
        if (length < sets.Count) length = sets.Count;

        // L19：原公式 length*log2(总字符集) 假设每位都从全集独立选取，
        // 但生成器强制"每类至少一个"，短密码时实际可选空间更小、熵被高估。
        // 这里在理想熵基础上扣除未满足"每类必含"的概率，得到更接近真实的下限。
        var bits = length * Math.Log2(alphabetSize);
        foreach (var set in sets)
        {
            var coverage = 1 - Math.Pow(1 - set.Length / (double)alphabetSize, length);
            if (coverage > 0) bits += Math.Log2(coverage);
        }
        return bits;
    }

    /// <summary>把熵值映射为界面上的强度文字。</summary>
    public static string StrengthLabel(double bits) => bits switch
    {
        < 40 => "弱",
        < 60 => "中",
        < 80 => "强",
        _ => "极强"
    };

    /// <summary>取当前勾选且过滤后的字符集列表。</summary>
    private static List<string> GetActiveSets(PasswordOptions options)
    {
        var sets = new List<string>(4);
        if (options.UseUpper) sets.Add(Filter(Upper, options.ExcludeAmbiguous));
        if (options.UseLower) sets.Add(Filter(Lower, options.ExcludeAmbiguous));
        if (options.UseDigit) sets.Add(Filter(Digit, options.ExcludeAmbiguous));
        if (options.UseSymbol) sets.Add(Filter(Symbol, options.ExcludeAmbiguous));
        return sets;
    }

    private static string Filter(string set, bool excludeAmbiguous)
    {
        if (!excludeAmbiguous)
        {
            return set;
        }
        var sb = new StringBuilder(set.Length);
        foreach (var c in set)
        {
            if (!Ambiguous.Contains(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>从字符集中安全取一个字符——这是全类唯一的随机来源。</summary>
    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
