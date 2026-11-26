using System.Drawing;
using System.Drawing.Imaging;

namespace WinTools.Tools.Files;

/// <summary>一次截图的结果。成功时 Image 由调用方负责 Dispose。</summary>
public sealed class CaptureResult
{
    public bool Success { get; private init; }
    public Bitmap? Image { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static CaptureResult Ok(Bitmap image) => new() { Success = true, Image = image };
    public static CaptureResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

/// <summary>
/// 截图工具的纯逻辑：坐标归一化、区域合法性、延时钳制、文件名、PNG/JPG 编码、捕获结果封装。
///
/// DPI 约定（关键）：本类所有 Rectangle/Point 都是**物理屏幕像素**——来自 Screen.VirtualScreen、
/// Cursor.Position、SelectionOverlay。PerMonitorV2 进程里这些 API 返回物理坐标，而 UiTheme.Sx
/// 只服务于界面控件布局。截图坐标若乘以 UiTheme.S 会在高缩放屏上截错位置，绝不能混用。
///
/// 捕获通过 Func&lt;Rectangle, Bitmap&gt; 注入：单测塞假抓屏器即可覆盖成功/异常/空图路径，
/// 不必真的依赖测试机的显示器；界面层传入 Graphics.CopyFromScreen 的真实实现。
/// </summary>
public static class ScreenCapture
{
    /// <summary>延时上限（秒），防止误填大数让窗口长期隐藏。</summary>
    public const int MaxDelaySeconds = 60;

    /// <summary>小于这个边长的选区视为误点，避免截出一条线。</summary>
    public const int MinSelectionSide = 8;

    public static int ClampDelay(int seconds) => Math.Clamp(seconds, 0, MaxDelaySeconds);

    /// <summary>
    /// 把拖拽起点/终点归一化为矩形，并裁剪进 screenBounds。
    /// 反向拖动（从右下拖到左上）自动纠正；负值/越界坐标一律夹到屏幕内。
    /// </summary>
    public static Rectangle NormalizeSelection(Point start, Point end, Rectangle screenBounds)
    {
        int left = Math.Clamp(Math.Min(start.X, end.X), screenBounds.Left, screenBounds.Right);
        int top = Math.Clamp(Math.Min(start.Y, end.Y), screenBounds.Top, screenBounds.Bottom);
        int right = Math.Clamp(Math.Max(start.X, end.X), screenBounds.Left, screenBounds.Right);
        int bottom = Math.Clamp(Math.Max(start.Y, end.Y), screenBounds.Top, screenBounds.Bottom);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    /// <summary>选区是否达到最小边长（宽高都要够）。</summary>
    public static bool IsValidSelection(Rectangle rect) =>
        rect.Width >= MinSelectionSide && rect.Height >= MinSelectionSide;

    /// <summary>调用注入的抓屏器；任何异常/空图都转成结果对象，不让事件处理器崩进程。</summary>
    public static CaptureResult Capture(Rectangle physicalBounds, Func<Rectangle, Bitmap> grab)
    {
        if (physicalBounds.Width <= 0 || physicalBounds.Height <= 0)
            return CaptureResult.Fail("截图区域为空，请重新框选。");
        try
        {
            var image = grab(physicalBounds);
            if (image is null || image.Width <= 0 || image.Height <= 0)
                return CaptureResult.Fail("抓屏返回了空图像。");
            return CaptureResult.Ok(image);
        }
        catch (Exception ex)
        {
            return CaptureResult.Fail($"截图失败：{ex.Message}");
        }
    }

    /// <summary>从已抓好的全屏底图里裁剪选区（坐标同为物理像素），实现"所见即所得"。</summary>
    public static CaptureResult Crop(Bitmap fullScreen, Rectangle physicalBounds)
    {
        if (fullScreen is null) return CaptureResult.Fail("底图为空。");
        var clipped = Rectangle.Intersect(physicalBounds, new Rectangle(0, 0, fullScreen.Width, fullScreen.Height));
        if (clipped.Width <= 0 || clipped.Height <= 0)
            return CaptureResult.Fail("选区与屏幕没有交集。");
        try
        {
            return CaptureResult.Ok(fullScreen.Clone(clipped, fullScreen.PixelFormat));
        }
        catch (Exception ex)
        {
            return CaptureResult.Fail($"裁剪失败：{ex.Message}");
        }
    }

    /// <summary>默认文件名：截图_yyyyMMdd_HHmmss.png，序号 > 1 时追加 _2。</summary>
    public static string BuildFileName(DateTime now, string extension, int sequence = 1)
    {
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        var suffix = sequence > 1 ? $"_{sequence}" : string.Empty;
        return $"截图_{now:yyyyMMdd_HHmmss}{suffix}{ext}";
    }

    /// <summary>PNG/JPG 编码为字节；JPG 质量 1-100，其它格式忽略 quality。</summary>
    public static byte[] Encode(Bitmap image, string formatId, long quality = 92)
    {
        using var ms = new MemoryStream();
        if (string.Equals(formatId, "jpg", StringComparison.OrdinalIgnoreCase))
        {
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,
                Math.Clamp(quality, 1, 100));
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
            image.Save(ms, codec, parameters);
        }
        else
        {
            image.Save(ms, ImageFormat.Png);
        }
        return ms.ToArray();
    }

    /// <summary>人类可读的像素尺寸。</summary>
    public static string DescribeSize(Size size) => $"{size.Width} × {size.Height} px";
}
