namespace WinTools.Tools.Files;

/// <summary>
/// 重命名规则（纯数据）。规则引擎只读取它，不产生任何副作用。
///
/// 模板占位符：
///   {name} —— 原文件名（不含扩展名）
///   {n}    —— 序号，从 StartIndex 起逐个递增；IndexWidth &gt; 0 时左侧补零
///
/// 扩展名始终自动保留原样，模板只作用于主文件名。
/// 例：模板"照片{n}" + StartIndex=1 + 三个 .jpg → 照片1.jpg / 照片2.jpg / 照片3.jpg
/// </summary>
public sealed class RenameRule
{
    /// <summary>命名模板，默认保留原名（便于配合查找替换做纯替换）。</summary>
    public string Template { get; set; } = "{name}";

    /// <summary>起始序号。</summary>
    public int StartIndex { get; set; } = 1;

    /// <summary>序号补零宽度：0 不补零，3 表示 001、002…。</summary>
    public int IndexWidth { get; set; }

    /// <summary>可选：在模板结果上做查找替换。</summary>
    public string FindText { get; set; } = string.Empty;

    /// <summary>查找替换的目标文本。</summary>
    public string ReplaceText { get; set; } = string.Empty;
}
