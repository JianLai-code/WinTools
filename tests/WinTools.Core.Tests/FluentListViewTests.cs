using System.Windows.Forms;
using WinTools.Core.Ui;
using Xunit;

namespace WinTools.Core.Tests;

/// <summary>
/// FluentListView 回归测试：Details 视图挂上 SmallImageList 后自绘回调全部失效，
/// 表现为列表有数据却连表头和行都不画。这里锁定"不挂 ImageList"这个关键前提。
/// </summary>
public sealed class FluentListViewTests
{
    [Fact]
    public void FluentListView_KeepsOwnerDrawWithoutImageList()
    {
        using var list = new FluentListView();

        Assert.Equal(View.Details, list.View);
        Assert.True(list.OwnerDraw);
        Assert.Null(list.SmallImageList);
    }
}
