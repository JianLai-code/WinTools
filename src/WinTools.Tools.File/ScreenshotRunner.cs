using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;
using WinTools.Core;

namespace WinTools.Tools.Files;

/// <summary>四种截图入口。延时截图在倒计时结束后走全屏抓取。</summary>
internal enum CaptureMode
{
    FullScreen,
    Region,
    Delayed,
    /// <summary>框选区域后逐帧抓取并拼接成长图。</summary>
    Scroll,
}

/// <summary>一次截图流程的产物。Image 的所有权归调用方，不使用时必须 Dispose。</summary>
internal sealed class CaptureOutcome
{
    public bool Completed { get; init; }
    public Bitmap? Image { get; init; }
    public bool Pinned { get; init; }
    public bool Copied { get; init; }
    public bool IsError { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// 截图流程本体：隐藏宿主 → 抓屏/框选/倒计时 → 标注编辑 → 置顶与自动复制。
///
/// 从工具界面里抽出来的原因：全局热键触发时并没有打开工具页，但要走同一条流程。
/// 两条入口共用本类，行为才不会分叉。所有调用都必须发生在 UI 线程（抓屏、弹窗、剪贴板都是）。
/// </summary>
internal static class ScreenshotRunner
{
    private static bool _busy;

    /// <param name="host">宿主主窗体；可见时先隐藏，避免把 WinTools 自己截进画面。</param>
    /// <param name="preset">预设选区（物理屏幕坐标）；给了就跳过框选，直接按这块区域截图。</param>
    public static CaptureOutcome Run(CaptureMode mode, int delaySeconds, Form? host, bool autoCopy,
        Rectangle? preset = null)
    {
        // 热键连按 / 同时点按钮时只放行第一次，否则会叠出多个全屏遮罩层。
        if (_busy) return new CaptureOutcome { Message = "上一次截图还没结束，请稍候再试。", IsError = true };

        _busy = true;
        var hidden = false;
        try
        {
            if (host is { IsDisposed: false, Visible: true })
            {
                host.Hide();
                Application.DoEvents();
                Thread.Sleep(180);   // 等桌面重绘，否则会截到窗口残影
                hidden = true;
            }
            return CaptureCore(mode, delaySeconds, autoCopy, preset);
        }
        catch (Exception ex)
        {
            return new CaptureOutcome { Message = $"截图失败：{ex.Message}", IsError = true };
        }
        finally
        {
            if (hidden && host is { IsDisposed: false })
            {
                host.Show();
                host.BringToFront();
            }
            _busy = false;
        }
    }

    private static CaptureOutcome CaptureCore(CaptureMode mode, int delaySeconds, bool autoCopy,
        Rectangle? preset = null)
    {
        var virtualScreen = SystemInformation.VirtualScreen;
        Bitmap? pending = null;
        // 当前图片对应的屏幕区域：标注工具栏上的「滚动截图」要按这块区域重新连续抓屏。
        var region = virtualScreen;
        var usedScroll = mode == CaptureMode.Scroll;
        try
        {
            if (mode == CaptureMode.Delayed)
            {
                using var countdown = new CountdownOverlay(ScreenCapture.ClampDelay(delaySeconds));
                if (countdown.ShowDialog() != DialogResult.OK)
                    return new CaptureOutcome { Message = "已取消延时截图。" };
            }

            if (mode == CaptureMode.Scroll)
            {
                using var backdrop = Grab(virtualScreen);
                if (preset is { } presetScroll && ScreenCapture.IsValidSelection(presetScroll))
                {
                    region = presetScroll;
                }
                else
                {
                    using var overlay = new SelectionOverlay(backdrop, virtualScreen,
                        "拖动框选滚动截图区域  ·  确认后向下滚动页面自动拼接",
                        ListWindows(virtualScreen));
                    if (overlay.ShowDialog() != DialogResult.OK)
                        return new CaptureOutcome { Message = "已取消滚动截图。" };
                    region = overlay.SelectionScreen;
                }

                pending = RunScroll(region, out var failure);
                if (pending is null) return new CaptureOutcome { Message = failure };
            }
            else if (mode == CaptureMode.Region)
            {
                // 先抓整屏当底图：遮罩层直接画在这张图上，框选结果所见即所得。
                using var backdrop = Grab(virtualScreen);
                // 把桌面上打开的窗口作为候选交给选框层：用户点一下编号就能选中微信/浏览器/任务栏。
                // 从"桌面窗口"列表点来的 preset 预填进同一个选框层，仍可拖边/移动后再确认。
                using var overlay = new SelectionOverlay(backdrop, virtualScreen, null,
                    ListWindows(virtualScreen),
                    preset is { } presetRegion && ScreenCapture.IsValidSelection(presetRegion)
                        ? presetRegion : null);
                if (overlay.ShowDialog() != DialogResult.OK)
                    return new CaptureOutcome { Message = "已取消区域截图。" };
                var selection = overlay.SelectionScreen;

                region = selection;
                var local = new Rectangle(selection.X - virtualScreen.X, selection.Y - virtualScreen.Y,
                    selection.Width, selection.Height);
                var cropped = ScreenCapture.Crop(backdrop, local);
                if (!cropped.Success || cropped.Image is null)
                    return new CaptureOutcome { Message = cropped.ErrorMessage ?? "裁剪失败。", IsError = true };
                pending = cropped.Image;
            }
            else
            {
                var captured = ScreenCapture.Capture(virtualScreen, Grab);
                if (!captured.Success || captured.Image is null)
                    return new CaptureOutcome { Message = captured.ErrorMessage ?? "截图失败。", IsError = true };
                pending = captured.Image;
            }

            // 编辑 →（点了工具栏「滚动截图」就带着长图重开编辑）→ 收尾。
            while (true)
            {
                var annotation = new AnnotationOverlay(pending!, region)
                {
                    ScrollCapture = r =>
                    {
                        var stitched = RunScroll(r, out var reason);
                        return new ScrollCaptureReply(stitched, stitched is null ? reason : "");
                    }
                };
                pending = null;   // 所有权已交给标注窗，由它在 Dispose 里释放

                using (annotation)
                {
                    var dialog = annotation.ShowDialog();

                    if (dialog == DialogResult.Retry && annotation.ScrollResult is { } stitched)
                    {
                        pending = stitched;
                        usedScroll = true;
                        continue;
                    }

                    if (dialog != DialogResult.OK || annotation.ResultImage is null)
                        return new CaptureOutcome { Message = "已放弃本次截图。" };

                    var final = annotation.ResultImage;
                    if (annotation.PinRequested)
                    {
                        var pinned = new PinnedImageForm((Bitmap)final.Clone());
                        pinned.Show();
                    }

                    // 剪贴板只保存引用，final 由调用方保活：热键路径会把它留在 Runtime.LastImage 里。
                    var copied = autoCopy && ClipboardUtil.TryCopyImage(final);
                    var size = ScreenCapture.DescribeSize(final.Size);
                    var what = usedScroll ? "滚动截图" : "截图";
                    var message = annotation.PinRequested
                        ? $"{what}已置顶到桌面 · {size}"
                        : copied
                            ? $"{what}已复制到剪贴板 · {size}"
                            : autoCopy
                                ? $"{what}完成，但剪贴板被其它程序占用，未自动复制"
                                : $"{what}完成 · {size}";

                    return new CaptureOutcome
                    {
                        Completed = true,
                        Image = final,
                        Pinned = annotation.PinRequested,
                        Copied = copied,
                        Message = message,
                    };
                }
            }
        }
        finally
        {
            pending?.Dispose();
        }
    }

    /// <summary>
    /// 列出桌面上可识别的窗口（微信、浏览器、任务栏……），排除截图工具自身的窗口。
    /// 枚举失败时返回空列表，选框层退化成纯手动框选，不会因此报错。
    /// </summary>
    internal static IReadOnlyList<WindowInfo> ListWindows(Rectangle virtualScreen)
    {
        try
        {
            return WindowEnumerator.List(virtualScreen);
        }
        catch
        {
            return Array.Empty<WindowInfo>();
        }
    }

    /// <summary>按指定屏幕区域进入可调整的选框层并截图（供"桌面窗口"列表点选使用）。</summary>
    public static CaptureOutcome CaptureRegion(Rectangle region, Form? host, bool autoCopy) =>
        Run(CaptureMode.Region, 0, host, autoCopy, region);

    /// <summary>在指定屏幕区域连续抓屏拼长图；取消/失败返回 null，并给出给用户看的原因。</summary>
    private static Bitmap? RunScroll(Rectangle region, out string failure)
    {
        using var scroller = new ScrollCaptureForm(region);
        if (scroller.ShowDialog() != DialogResult.OK || scroller.Result is null)
        {
            failure = scroller.FinishMessage.Length > 0 ? scroller.FinishMessage : "已取消滚动截图。";
            return null;
        }

        failure = "";
        return scroller.Result;
    }

    /// <summary>真实抓屏。坐标是物理像素，不能乘 UiTheme.S。</summary>
    private static Bitmap Grab(Rectangle physical)
    {
        var bmp = new Bitmap(physical.Width, physical.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(physical.X, physical.Y, 0, 0, physical.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
    }
}
