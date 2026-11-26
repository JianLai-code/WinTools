using System.Windows.Forms;
using WinTools.Core;
using Xunit;

namespace WinTools.Core.Tests;

/// <summary>
/// 全局快捷键的解析/格式化/合法性：设置界面、持久化与注册端都走这套规则，
/// 这里锁住"界面录得进、注册端也认"的一致性。
/// </summary>
public class HotkeyGestureTests
{
    [Fact]
    public void Parse_ReadsModifiersAndKey()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Shift+A", out var gesture));
        Assert.True(gesture.Ctrl);
        Assert.True(gesture.Shift);
        Assert.False(gesture.Alt);
        Assert.False(gesture.Win);
        Assert.Equal(Keys.A, gesture.Key);
        Assert.True(gesture.IsValid);
    }

    [Fact]
    public void Parse_IsCaseInsensitive_AndAcceptsAliases()
    {
        Assert.True(HotkeyGesture.TryParse("control+alt+f1", out var gesture));
        Assert.True(gesture.Ctrl);
        Assert.True(gesture.Alt);
        Assert.Equal(Keys.F1, gesture.Key);

        Assert.True(HotkeyGesture.TryParse("windows+shift+s", out var win));
        Assert.True(win.Win);
        Assert.Equal(Keys.S, win.Key);
    }

    [Fact]
    public void Parse_MapsDigitsToD0ThroughD9()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+7", out var gesture));
        Assert.Equal(Keys.D7, gesture.Key);
        Assert.Equal("Ctrl+Alt+7", gesture.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+Shift+A+B")]
    [InlineData("Ctrl+Shift+Foo")]
    [InlineData("Ctrl+Shift+ControlKey")]
    [InlineData("Ctrl+Shift+LButton")]
    public void Parse_RejectsInvalidText(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out var gesture));
        Assert.True(gesture.IsEmpty);
    }

    [Fact]
    public void IsValid_RequiresModifier()
    {
        Assert.True(HotkeyGesture.TryParse("A", out var bare));
        Assert.Equal(Keys.A, bare.Key);
        // 单键不抢全局：解析得出来，但不是合法热键。
        Assert.False(bare.HasModifier);
        Assert.False(bare.IsValid);
    }

    [Fact]
    public void ToString_IsStableAndRoundTrips()
    {
        var original = new HotkeyGesture(Ctrl: true, Alt: false, Shift: true, Win: false, Key: Keys.F12);
        var text = original.ToString();
        Assert.Equal("Ctrl+Shift+F12", text);
        Assert.True(HotkeyGesture.TryParse(text, out var parsed));
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Empty_FormatsAsEmptyText()
    {
        Assert.Equal(string.Empty, HotkeyGesture.Empty.ToString());
        Assert.True(HotkeyGesture.Empty.IsEmpty);
        Assert.False(HotkeyGesture.Empty.IsValid);
    }

    [Fact]
    public void FromKeyData_ExtractsModifiers()
    {
        var gesture = HotkeyGesture.FromKeyData(Keys.Control | Keys.Alt | Keys.K);
        Assert.True(gesture.Ctrl);
        Assert.True(gesture.Alt);
        Assert.False(gesture.Shift);
        Assert.Equal(Keys.K, gesture.Key);
    }

    [Theory]
    [InlineData(Keys.A, true)]
    [InlineData(Keys.D0, true)]
    [InlineData(Keys.F24, true)]
    [InlineData(Keys.PrintScreen, true)]
    [InlineData(Keys.ControlKey, false)]
    [InlineData(Keys.ShiftKey, false)]
    [InlineData(Keys.Menu, false)]
    [InlineData(Keys.LWin, false)]
    [InlineData(Keys.LButton, false)]
    [InlineData(Keys.None, false)]
    public void IsBindableKey_CoversModifiersAndMouse(Keys key, bool expected)
    {
        Assert.Equal(expected, HotkeyGesture.IsBindableKey(key));
    }
}
