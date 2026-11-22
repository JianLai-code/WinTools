namespace WinTools.Tools.SystemTools;

/// <summary>一条环境变量：变量名 + 变量值。只读展示用。</summary>
/// <param name="Name">变量名</param>
/// <param name="Value">变量值；原值为 null 时展示为空串</param>
public sealed record EnvironmentVariableItem(string Name, string Value);
