namespace WinTools.Core;

/// <summary>
/// 标记一个实现类是可被发现的工具，并控制导航顺序。
/// 元数据（Id/Name/Category）由 ITool 属性提供，此处只放发现所需信息，避免重复维护。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ToolAttribute : Attribute
{
    /// <summary>导航排序，越小越靠前。默认值 100。</summary>
    public int Order { get; init; } = 100;
}
