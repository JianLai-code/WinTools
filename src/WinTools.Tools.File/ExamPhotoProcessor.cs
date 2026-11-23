using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace WinTools.Tools.Files;

/// <summary>要替换掉的源背景色。报名照常见红底、蓝底与白底三种。</summary>
public enum ExamBackdropSource
{
    Red,
    Blue,
    White
}

/// <summary>考试报名照片处理的全部参数。纯数据，便于单测与界面传值。</summary>
public sealed record ExamPhotoOptions
{
    /// <summary>输出 JPG 的字节上限，默认 30 KB；0 表示不限制大小，按固定高质量输出。</summary>
    public int TargetBytes { get; init; } = 30 * 1024;

    /// <summary>规范尺寸，默认一寸报名照 295×413；null 表示保留原尺寸。</summary>
    public Size? TargetSize { get; init; } = new(295, 413);

    /// <summary>是否替换背景色。</summary>
    public bool ReplaceBackdrop { get; init; } = true;

    /// <summary>源照片的背景色。</summary>
    public ExamBackdropSource SourceBackdrop { get; init; } = ExamBackdropSource.Red;

    /// <summary>替换成什么颜色。默认报名照常用蓝底 #438EDB。</summary>
    public Color TargetBackdrop { get; init; } = Color.FromArgb(0x43, 0x8E, 0xDB);

    /// <summary>容差 0-100，越大越激进（默认 40，不会误伤肤色）。</summary>
    public int Tolerance { get; init; } = 40;
}

/// <summary>处理结果。失败时不给半成品字节，与项目其它图片工具一致。</summary>
public sealed record ExamPhotoResult
{
    public bool Success { get; init; }
    public byte[]? Bytes { get; init; }
    public string? ErrorMessage { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Quality { get; init; }
    public int ReplacedPixels { get; init; }
    /// <summary>被主体轮廓保护、未参与替换的像素数（含白色衣物）。</summary>
    public int ProtectedPixels { get; init; }
    public bool Downscaled { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];

    public static ExamPhotoResult Fail(string message) => new() { ErrorMessage = message };
}

/// <summary>
/// 公考/事业单位报名照片的纯处理逻辑：尺寸规范 → 纯色背景替换 → JPG 限体积。
///
/// 三个设计约束：
/// - 背景替换只处理「与画面四边连通」的候选像素（flood fill），红衣服、白衬衫不会被误换；
/// - 边缘按颜色距离做渐变过渡，避免硬切产生锯齿与残红；
/// - 体积用质量二分搜索收敛，实在压不下才按比例缩小，不静默降质到模糊。
///
/// 全部基于系统 GDI+，无第三方库、无网络请求。
/// </summary>
public static class ExamPhotoProcessor
{
    /// <summary>界面预设的目标底色。</summary>
    public static readonly (string Name, Color Color)[] BackdropPresets =
    [
        ("蓝底 #438EDB", Color.FromArgb(0x43, 0x8E, 0xDB)),
        ("白底 #FFFFFF", Color.White),
        ("红底 #FF0000", Color.FromArgb(255, 0, 0)),
        ("蓝底 #2E7BC4", Color.FromArgb(0x2E, 0x7B, 0xC4)),
    ];

    /// <summary>常见证件照规格，像素按 300dpi 换算。null 尺寸表示保留原图。</summary>
    public static readonly (string Name, Size Size)[] SizePresets =
    [
        ("小一寸 260×378", new Size(260, 378)),
        ("一寸 295×413", new Size(295, 413)),
        ("大一寸 390×567", new Size(390, 567)),
        ("小二寸 413×531", new Size(413, 531)),
        ("二寸 413×579", new Size(413, 579)),
        ("大二寸 413×626", new Size(413, 626)),
    ];

    /// <summary>解析 #RRGGBB；界面输入框用，逻辑类保持可测。</summary>
    public static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var hex = text.Trim().TrimStart('#');
        if (hex.Length != 6) return false;
        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return false;
        color = Color.FromArgb((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
        return true;
    }

    public static ExamPhotoResult Process(byte[] source, ExamPhotoOptions? options = null)
    {
        options ??= new ExamPhotoOptions();
        if (source is null || source.Length == 0) return ExamPhotoResult.Fail("源图片为空。");
        if (options.TargetBytes < 0 || (options.TargetBytes > 0 && options.TargetBytes < 1024))
            return ExamPhotoResult.Fail("目标大小不能小于 1 KB；填 0 表示不限制大小。");

        try
        {
            using var input = new MemoryStream(source, writable: false);
            using var loaded = Image.FromStream(input);
            if (loaded.Width < 1 || loaded.Height < 1) return ExamPhotoResult.Fail("图片尺寸无效。");

            // 先规范尺寸：后续逐像素操作只跑在最终分辨率上，大照片也不会占用几十 MB 数组。
            using var sized = NormalizeSize(loaded, options.TargetSize);

            var replaced = 0;
            var protectedPixels = 0;
            if (options.ReplaceBackdrop)
                (replaced, protectedPixels) = ReplaceBackdrop(
                    sized, options.SourceBackdrop, options.TargetBackdrop, options.Tolerance);

            var (bytes, quality, downscaled) = FitToLimit(sized, options.TargetBytes);
            if (bytes is null)
                return ExamPhotoResult.Fail(
                    $"即使缩到最低质量仍超过 {ImageFormatCodec.FormatSize(options.TargetBytes)}，请放宽大小限制。");

            var notes = new List<string>
            {
                options.TargetSize is null
                    ? $"保留原始尺寸 {sized.Width}×{sized.Height}"
                    : $"已按 {sized.Width}×{sized.Height} 等比居中裁剪",
                options.TargetBytes <= 0
                    ? $"不限制大小，JPG 质量 {quality}，最终 {ImageFormatCodec.FormatSize(bytes.Length)}"
                    : $"JPG 质量 {quality}，最终 {ImageFormatCodec.FormatSize(bytes.Length)}"
            };
            if (replaced > 0)
                notes.Insert(0, $"已替换背景像素 {replaced:N0} 个");
            else if (options.ReplaceBackdrop)
                notes.Insert(0, "未发现可替换的连通背景，原背景保持不变");
            if (protectedPixels > 0)
                notes.Add($"已保护主体区域 {protectedPixels:N0} 个像素（白色衣物不会被换色）");
            if (downscaled)
                notes.Add("为达到大小上限已额外按比例缩小");

            return new ExamPhotoResult
            {
                Success = true,
                Bytes = bytes,
                Width = sized.Width,
                Height = sized.Height,
                Quality = quality,
                ReplacedPixels = replaced,
                ProtectedPixels = protectedPixels,
                Downscaled = downscaled,
                Notes = notes
            };
        }
        catch (ArgumentException)
        {
            return ExamPhotoResult.Fail("无法识别的图片格式，或文件已损坏。");
        }
        catch (OutOfMemoryException)
        {
            return ExamPhotoResult.Fail("图片过大或格式不受支持。");
        }
        catch (Exception ex)
        {
            return ExamPhotoResult.Fail($"处理失败：{ex.Message}");
        }
    }

    /// <summary>等比放大到铺满目标框再居中裁剪（cover），绝不拉伸变形。</summary>
    private static Bitmap NormalizeSize(Image source, Size? target)
    {
        if (target is null) return new Bitmap(source);

        var tw = Math.Max(1, target.Value.Width);
        var th = Math.Max(1, target.Value.Height);
        var result = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
        if (source.Width == tw && source.Height == th)
        {
            using var same = Graphics.FromImage(result);
            ConfigureGraphics(same);
            same.DrawImage(source, new Rectangle(0, 0, tw, th));
            return result;
        }

        var scale = Math.Max((double)tw / source.Width, (double)th / source.Height);
        var drawW = Math.Max(1, (int)Math.Ceiling(source.Width * scale));
        var drawH = Math.Max(1, (int)Math.Ceiling(source.Height * scale));

        using var temp = new Bitmap(drawW, drawH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(temp))
        {
            ConfigureGraphics(g);
            g.DrawImage(source, new Rectangle(0, 0, drawW, drawH),
                0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
        }
        using (var g = Graphics.FromImage(result))
        {
            ConfigureGraphics(g);
            g.DrawImage(temp, new Rectangle((tw - drawW) / 2, (th - drawH) / 2, drawW, drawH));
        }
        return result;
    }

    private static void ConfigureGraphics(Graphics g)
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
    }

    /// <summary>
    /// 从四边向内 flood fill，只替换「与边缘连通、且不在主体轮廓内」的候选像素。
    ///
    /// 单靠连通性挡不住白衬衫：衬衫贴着画面下边缘时会被直接当作边缘种子，
    /// 白衬衫和白背景之间的抗锯齿像素也会连成一片。因此先逐行扫出一定属于人物的
    /// 像素（肤色、头发、深色衣物）作为主体骨架，再向下缓慢外扩成轮廓；
    /// 轮廓内一律保护，替换只发生在轮廓外的连通背景上。
    ///
    /// 返回 (替换像素数, 受保护像素数)。
    /// </summary>
    private static (int Replaced, int Protected) ReplaceBackdrop(
        Bitmap bitmap, ExamBackdropSource source, Color target, int tolerance)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        var rect = new Rectangle(0, 0, w, h);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[w * h];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            var strength = new byte[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
                strength[i] = (byte)Math.Round(Strength(pixels[i], source, tolerance) * 255);

            var mask = BuildSubjectMask(pixels, source, w, h);
            var protectedCount = 0;
            for (var i = 0; i < mask.Length; i++) if (mask[i]) protectedCount++;

            var visited = new bool[pixels.Length];
            var queue = new Queue<int>();

            void Seed(int x, int y)
            {
                var idx = y * w + x;
                if (strength[idx] > 0 && !mask[idx] && !visited[idx])
                {
                    visited[idx] = true;
                    queue.Enqueue(idx);
                }
            }

            for (var x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
            for (var y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }

            var replaced = 0;
            var tr = target.R;
            var tg = target.G;
            var tb = target.B;

            while (queue.Count > 0)
            {
                var idx = queue.Dequeue();

                var s = strength[idx] / 255f;
                if (s > 0f)
                {
                    var argb = pixels[idx];
                    var r = (argb >> 16) & 0xFF;
                    var g = (argb >> 8) & 0xFF;
                    var b = argb & 0xFF;
                    pixels[idx] = Color.FromArgb(
                        (argb >> 24) & 0xFF,
                        (int)Math.Round(r + (tr - r) * s),
                        (int)Math.Round(g + (tg - g) * s),
                        (int)Math.Round(b + (tb - b) * s)).ToArgb();
                    replaced++;
                }

                var x = idx % w;
                var y = idx / w;

                void Visit(int next)
                {
                    if (strength[next] > 0 && !mask[next] && !visited[next])
                    {
                        visited[next] = true;
                        queue.Enqueue(next);
                    }
                }

                if (x > 0) Visit(idx - 1);
                if (x < w - 1) Visit(idx + 1);
                if (y > 0) Visit(idx - w);
                if (y < h - 1) Visit(idx + w);
            }

            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            return (replaced, protectedCount);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>
    /// 「一定属于人物」的像素。肤色、头发、深色/彩色衣物都算；纯白或近白不算，
    /// 它们要么是真背景，要么是需要靠轮廓保护的白衬衫。
    /// </summary>
    private static bool IsForegroundCore(int argb, ExamBackdropSource source)
    {
        var r = (argb >> 16) & 0xFF;
        var g = (argb >> 8) & 0xFF;
        var b = argb & 0xFF;
        var lo = Math.Min(r, Math.Min(g, b));
        var hi = Math.Max(r, Math.Max(g, b));

        if (source == ExamBackdropSource.White)
            // 只有「明显偏暗」或「明显有色彩倾向」的像素才确定是人物。
            // 抗锯齿边缘的浅灰（如 199,191,189）不能算主体，否则会在轮廓外留一圈白边。
            return lo < 100 || hi - lo > 45;

        if (source == ExamBackdropSource.Blue)
            // 蓝底：蓝色优势很弱的一定不是蓝背景。不加亮度兜底，
            // 否则蓝底与头发之间的深灰抗锯齿会被保护，留一圈蓝边。
            return b - Math.Max(r, g) < 30;

        // 红底：红色优势很弱的一定不是红背景（肤色、头发、白衬衫都在此列）。
        return r - Math.Max(g, b) < 70;
    }

    /// <summary>
    /// 逐行扫出人物左右边界并向下外扩，得到需要保护的主体掩膜。
    /// 头顶以上的行还没有主体特征，不保护——否则整张图都不会换底色。
    /// 每行用当前行的实际宽度，肩部收窄时不能沿用上方更宽的范围；
    /// 只有没特征的白衬衫行才继承上一行并有限外扩。
    /// </summary>
    private static bool[] BuildSubjectMask(int[] pixels, ExamBackdropSource source, int w, int h)
    {
        var mask = new bool[pixels.Length];
        // 不再额外外扩主体：浅灰抗锯齿像素由 Strength 当作背景替换掉，
        // 多留 1px 就会在头发/肩膀外形成可见白边。
        var margin = 0;
        var grow = Math.Max(1, w / 120);
        // 外扩要够护住肩膀，但不能无限扩到整张图——否则两侧背景永远换不了色。
        var maxExpand = Math.Max(4, w / 5);
        // 每行用自己的实际边界，不能用「迄今最宽」——肩膀收窄时会把旁边的白背景一起护住。
        var lastLeft = -1;
        var lastRight = -1;
        var expand = 0;

        for (var y = 0; y < h; y++)
        {
            var rowStart = y * w;
            var rowLeft = w;
            var rowRight = -1;
            for (var x = 0; x < w; x++)
            {
                if (!IsForegroundCore(pixels[rowStart + x], source)) continue;
                if (x < rowLeft) rowLeft = x;
                if (x > rowRight) rowRight = x;
            }

            int left;
            int right;
            if (rowRight >= 0)
            {
                left = Math.Max(0, rowLeft - margin);
                right = Math.Min(w - 1, rowRight + margin);
                lastLeft = left;
                lastRight = right;
                expand = 0;
            }
            else if (lastRight >= 0)
            {
                // 无特征行（白衬衫）：沿用最近一行的轮廓并缓慢外扩。
                expand = Math.Min(maxExpand, expand + grow);
                left = Math.Max(0, lastLeft - expand);
                right = Math.Min(w - 1, lastRight + expand);
            }
            else
            {
                // 头顶以上还没有主体特征，不保护，否则整张图都不会换底色。
                continue;
            }

            for (var x = left; x <= right; x++) mask[rowStart + x] = true;
        }

        return mask;
    }

    /// <summary>
    /// 颜色属于目标底色的强度 0~1。渐变带把抗锯齿边缘从「全换」过渡到「不换」，
    /// 避免发丝一圈残留红边；容差越大判定越宽松。
    /// </summary>
    public static float Strength(int argb, ExamBackdropSource source, int tolerance)
    {
        var tol = Math.Clamp(tolerance, 0, 100);
        const float soft = 20f;
        var r = (argb >> 16) & 0xFF;
        var g = (argb >> 8) & 0xFF;
        var b = argb & 0xFF;

        if (source == ExamBackdropSource.Red)
        {
            // 红底：红色通道显著高于绿蓝；肤色红差通常 < 55，默认阈值 80 留足安全距离。
            var diff = r - Math.Max(g, b);
            var threshold = 100 - tol * 0.5;
            if (r < 70) return 0f;
            return (float)Math.Clamp((diff - (threshold - soft)) / soft, 0, 1);
        }
        else if (source == ExamBackdropSource.White)
        {
            // 肤色暖调保护：皮肤再白也带暖调（实测高光脸颊 r-b ≥ 21），
            // 而纯白/冷灰背景 r-b ≤ 10。苍白高光若只按「亮度 + 色彩离散度」判断
            // 会被当成背景，flood fill 会顺着脸颊扫出一整条横带。
            if (r - b > 20 + tol * 0.1) return 0f;

            var lo = Math.Min(r, Math.Min(g, b));
            var hi = Math.Max(r, Math.Max(g, b));
            var threshold = 30 + tol * 0.5;
            // 下限放到 100，把轮廓外那圈抗锯齿浅灰也纳入背景；
            // 更深的一律保留，避免啃掉头发和深色衣物。
            if (lo < 100) return 0f;
            return (float)Math.Clamp((threshold - (hi - lo)) / soft, 0, 1);
        }
        else
        {
            // 蓝底：蓝色通道显著高于红绿。常见 #438EDB 的 diff 为 77，
            // 默认阈值 42 留足安全距离；肤色/白衬衫的蓝色优势都远低于此。
            var diff = b - Math.Max(r, g);
            var threshold = 50 - tol * 0.2;
            if (b < 70) return 0f;
            return (float)Math.Clamp((diff - (threshold - soft)) / soft, 0, 1);
        }
    }

    /// <summary>按指定质量编码 JPG，供无体积上限与二分搜索共用。</summary>
    private static byte[] EncodeJpeg(Bitmap bitmap, long quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
        using var parameters = new EncoderParameters(1);
        using var parameter = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        parameters.Param[0] = parameter;
        using var ms = new MemoryStream();
        bitmap.Save(ms, codec, parameters);
        return ms.ToArray();
    }

    /// <summary>二分搜索能塞进字节上限的最高 JPG 质量。</summary>
    private static (byte[]? Bytes, int Quality) BestJpeg(Bitmap bitmap, int limit)
    {
        byte[]? best = null;
        var bestQuality = 0;
        var lo = 10;
        var hi = 100;

        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var bytes = EncodeJpeg(bitmap, mid);
            if (bytes.Length <= limit)
            {
                best = bytes;
                bestQuality = mid;
                lo = mid + 1;
            }
            else hi = mid - 1;
        }
        return (best, bestQuality);
    }

    /// <summary>质量压到 10 仍超限才按比例缩小，每轮 ×0.85，最多 10 轮。</summary>
    private static (byte[]? Bytes, int Quality, bool Downscaled) FitToLimit(Bitmap bitmap, int limit)
    {
        if (limit <= 0) return (EncodeJpeg(bitmap, 95), 95, false);

        Bitmap? scaled = null;
        try
        {
            var working = bitmap;
            var downscaled = false;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var (bytes, quality) = BestJpeg(working, limit);
                if (bytes is not null) return (bytes, quality, downscaled);

                var nw = Math.Max(32, (int)Math.Round(working.Width * 0.85));
                var nh = Math.Max(32, (int)Math.Round(working.Height * 0.85));
                // 先用 working 画完新图，再释放上一轮缩放图——顺序反了会拿已释放的位图当源。
                var next = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(next))
                {
                    ConfigureGraphics(g);
                    g.DrawImage(working, new Rectangle(0, 0, nw, nh));
                }
                scaled?.Dispose();
                scaled = next;
                working = next;
                downscaled = true;
            }
            return (null, 10, downscaled);
        }
        finally
        {
            scaled?.Dispose();
        }
    }
}
