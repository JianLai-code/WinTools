namespace WinTools.Tools.Text;

public enum UuidFormat
{
    Dashed,      // xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
    NoHyphens,   // xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
    Braces       // {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}
}

/// <summary>UUID 生成的纯逻辑层。Guid.NewGuid 使用系统密码学安全随机源，可被单测反复调用验证唯一性。</summary>
public static class UuidGenerator
{
    public const int MaxCount = 200;

    public static int ClampCount(int count) => Math.Clamp(count, 1, MaxCount);

    public static string Format(Guid guid, UuidFormat format, bool uppercase)
    {
        var shape = format switch
        {
            UuidFormat.NoHyphens => "N",
            UuidFormat.Braces => "B",
            _ => "D"
        };
        var s = guid.ToString(shape);
        return uppercase ? s.ToUpperInvariant() : s;
    }

    public static string[] Generate(int count, UuidFormat format = UuidFormat.Dashed, bool uppercase = false)
    {
        var n = ClampCount(count);
        var result = new string[n];
        for (var i = 0; i < n; i++) result[i] = Format(Guid.NewGuid(), format, uppercase);
        return result;
    }
}
