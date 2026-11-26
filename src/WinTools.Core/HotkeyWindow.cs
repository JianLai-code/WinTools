using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinTools.Core;

/// <summary>
/// 全局热键宿主：一个只收消息的隐藏窗口（HWND_MESSAGE），用 RegisterHotKey 注册系统级热键，
/// 收到 WM_HOTKEY 后回调。
///
/// 必须创建在带消息循环的 UI 线程上（WinForms 主线程即可），否则 WM_HOTKEY 没有循环可取。
/// 注册是进程级的：窗口隐藏、主窗体最小化都不影响热键生效；进程退出时系统自动回收。
/// </summary>
public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HwndMessage = -3;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private const int ErrorHotkeyAlreadyRegistered = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 0x5100;
    private bool _disposed;

    public HotkeyWindow()
    {
        // 消息专用窗口：不出现在任务栏/Alt+Tab，也没有可见区域，只负责接收 WM_HOTKEY。
        CreateHandle(new CreateParams { Parent = (IntPtr)HwndMessage });
    }

    /// <summary>
    /// 注册一个热键。成功返回非 0 的注册 id，失败返回 0 并通过 <paramref name="error"/> 说明原因。
    /// 同一组合已被其它程序占用时返回 1409 的友好说明，调用方可据此提示用户换一个。
    /// </summary>
    public int Register(HotkeyGesture gesture, Action callback, out string? error)
    {
        error = null;
        if (_disposed) { error = "热键窗口已释放"; return 0; }
        if (gesture.IsEmpty) { error = "快捷键为空"; return 0; }
        if (!gesture.HasModifier) { error = "全局快捷键至少要带一个修饰键（Ctrl / Alt / Shift / Win）"; return 0; }
        if (!HotkeyGesture.IsBindableKey(gesture.Key)) { error = "该按键不能作为全局快捷键"; return 0; }

        var id = _nextId++;
        if (!RegisterHotKey(Handle, id, Modifiers(gesture), (uint)gesture.Key))
        {
            var code = Marshal.GetLastWin32Error();
            error = code == ErrorHotkeyAlreadyRegistered
                ? "该组合已被其它程序占用"
                : $"注册失败（系统错误码 {code}）";
            return 0;
        }
        _handlers[id] = callback;
        return id;
    }

    /// <summary>注销单个热键；id 为 0 或不存在时静默忽略。</summary>
    public void Unregister(int id)
    {
        if (id == 0) return;
        if (_handlers.Remove(id) && Handle != IntPtr.Zero)
            UnregisterHotKey(Handle, id);
    }

    public void UnregisterAll()
    {
        if (Handle == IntPtr.Zero) { _handlers.Clear(); return; }
        foreach (var id in _handlers.Keys.ToArray())
            UnregisterHotKey(Handle, id);
        _handlers.Clear();
    }

    private static uint Modifiers(HotkeyGesture g)
    {
        uint mods = ModNoRepeat;   // 长按不连续触发，避免按住热键狂开截图
        if (g.Ctrl) mods |= ModControl;
        if (g.Alt) mods |= ModAlt;
        if (g.Shift) mods |= ModShift;
        if (g.Win) mods |= ModWin;
        return mods;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey)
        {
            var id = (int)m.WParam;
            if (_handlers.TryGetValue(id, out var callback))
            {
                // 回调跑在 UI 线程：调用方在这里弹窗、截图都合法；异常不能冒泡到消息循环。
                try { callback(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"全局热键回调异常: {ex}"); }
            }
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
