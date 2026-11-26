using WinTools.Services.Settings;

namespace WinTools.Tools.Files;

/// <summary>截图工具的持久化设置：热键组合与"截完自动复制"。</summary>
public sealed class ScreenCaptureSettings
{
    /// <summary>截图完成后自动写入剪贴板（微信截图的默认行为）。</summary>
    public bool AutoCopy { get; set; } = true;

    /// <summary>区域截图全局热键；空串表示不注册。</summary>
    public string RegionHotkey { get; set; } = "Ctrl+Shift+A";

    /// <summary>全屏截图全局热键；空串表示不注册。</summary>
    public string FullHotkey { get; set; } = "Ctrl+Shift+F";

    /// <summary>滚动截图全局热键；空串表示不注册。</summary>
    public string ScrollHotkey { get; set; } = "Ctrl+Shift+S";
}

/// <summary>
/// 设置读写：存到 %AppData%\WinTools\screen-capture.json。
/// 复用 Shell 也在用的 JsonSettingsService——它已经处理了损坏文件隔离与原子写入。
/// </summary>
internal static class ScreenCaptureSettingsStore
{
    private static readonly JsonSettingsService Service = new("screen-capture.json");

    public static ScreenCaptureSettings Load() => Service.Load(new ScreenCaptureSettings());

    public static bool Save(ScreenCaptureSettings settings) => Service.Save(settings);
}
