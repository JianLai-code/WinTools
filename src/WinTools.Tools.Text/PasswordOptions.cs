namespace WinTools.Tools.Text;

/// <summary>
/// 随机密码生成参数。用 record 便于单元测试构造与断言。
/// </summary>
/// <param name="Length">密码长度（实际会被钳制到 MinLength~MaxLength）</param>
/// <param name="UseUpper">含大写字母</param>
/// <param name="UseLower">含小写字母</param>
/// <param name="UseDigit">含数字</param>
/// <param name="UseSymbol">含符号</param>
/// <param name="ExcludeAmbiguous">排除易混淆字符（O/0/I/l/1 等）</param>
public sealed record PasswordOptions(
    int Length,
    bool UseUpper,
    bool UseLower,
    bool UseDigit,
    bool UseSymbol,
    bool ExcludeAmbiguous)
{
    public const int DefaultLength = 16;
    public const int MinLength = 4;
    public const int MaxLength = 128;

    /// <summary>默认参数：16 位、四类字符全选、排除易混淆字符。</summary>
    public static PasswordOptions Default => new(DefaultLength, true, true, true, true, true);
}
