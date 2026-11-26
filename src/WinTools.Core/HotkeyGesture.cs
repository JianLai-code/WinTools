using System.Windows.Forms;

namespace WinTools.Core;

/// <summary>
/// 全局快捷键组合：修饰键 + 主键。
/// 解析、格式化、合法性校验集中在这里——设置界面、持久化与注册端共用同一套规则，
/// 避免"界面能录进去、注册时却失败"这类不一致。
/// </summary>
public readonly record struct HotkeyGesture(bool Ctrl, bool Alt, bool Shift, bool Win, Keys Key)
{
    public static readonly HotkeyGesture Empty = new(false, false, false, false, Keys.None);

    public bool IsEmpty => Key == Keys.None;

    public bool HasModifier => Ctrl || Alt || Shift || Win;

    /// <summary>
    /// 可注册的快捷键：非空、至少一个修饰键、主键不是修饰键本身。
    /// 全局热键不抢单键——那会把整个系统的该按键吃掉。
    /// </summary>
    public bool IsValid => !IsEmpty && HasModifier && IsBindableKey(Key);

    /// <summary>从 WinForms 的 KeyData 取组合（Win 键不会进 KeyData，因此恒为 false）。</summary>
    public static HotkeyGesture FromKeyData(Keys keyData) => new(
        (keyData & Keys.Control) != 0,
        (keyData & Keys.Alt) != 0,
        (keyData & Keys.Shift) != 0,
        false,
        keyData & Keys.KeyCode);

    public override string ToString()
    {
        if (IsEmpty) return string.Empty;
        var parts = new List<string>(5);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    /// <summary>解析 "Ctrl+Shift+A" / "Alt+F1" 这类文本；空串视为"未设置"并返回 false。</summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        bool ctrl = false, alt = false, shift = false, win = false;
        var key = Keys.None;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control": ctrl = true; continue;
                case "alt": alt = true; continue;
                case "shift": shift = true; continue;
                case "win":
                case "windows":
                case "meta": win = true; continue;
            }
            if (key != Keys.None) return false;   // 出现了两个主键
            if (!TryParseKey(raw, out key)) return false;
        }

        if (key == Keys.None) return false;
        gesture = new HotkeyGesture(ctrl, alt, shift, win, key);
        return true;
    }

    private static string KeyName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        _ => key.ToString(),
    };

    private static bool TryParseKey(string token, out Keys key)
    {
        key = Keys.None;
        if (token.Length == 1)
        {
            var ch = char.ToUpperInvariant(token[0]);
            if (ch is >= 'A' and <= 'Z') { key = (Keys)ch; return true; }
            if (ch is >= '0' and <= '9') { key = Keys.D0 + (ch - '0'); return true; }
            return false;
        }
        if (!Enum.TryParse<Keys>(token, ignoreCase: true, out var parsed)) return false;
        if ((parsed & Keys.Modifiers) != 0) return false;   // "Alt" 这类纯修饰位不是主键
        if (!IsBindableKey(parsed)) return false;
        key = parsed;
        return true;
    }

    /// <summary>修饰键本身、鼠标键、None 都不能作为全局热键主键。</summary>
    public static bool IsBindableKey(Keys key) => key switch
    {
        Keys.None => false,
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => false,
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => false,
        Keys.Menu or Keys.LMenu or Keys.RMenu => false,
        Keys.LWin or Keys.RWin or Keys.Apps => false,
        Keys.LButton or Keys.RButton or Keys.MButton or Keys.XButton1 or Keys.XButton2 => false,
        _ => (int)key < 0x100,   // 排除带修饰位的合成值与高位虚拟键
    };
}
