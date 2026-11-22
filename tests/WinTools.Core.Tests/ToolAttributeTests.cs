using WinTools.Core;
using Xunit;

namespace WinTools.Core.Tests;

/// <summary>
/// 验证 Core 层契约。迭代 0 只有一个契约测试，后续迭代会加入各工具的逻辑测试。
/// </summary>
public sealed class ToolAttributeTests
{
    [Fact]
    public void ToolAttribute_DefaultOrder_Is100()
    {
        var attr = new ToolAttribute();
        Assert.Equal(100, attr.Order);
    }

    [Fact]
    public void ToolAttribute_CustomOrder_IsRespected()
    {
        var attr = new ToolAttribute { Order = 5 };
        Assert.Equal(5, attr.Order);
    }

    [Fact]
    public void ToolAttribute_CanOnlyApplyToClass()
    {
        var usage = typeof(ToolAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }
}
