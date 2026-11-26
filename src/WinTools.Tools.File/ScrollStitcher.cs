using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WinTools.Tools.Files;

/// <summary>追加一帧后的状态。</summary>
public enum StitchStatus
{
    /// <summary>有新内容，已追加。</summary>
    Appended,
    /// <summary>画面没变：还没滚动，或已经到页面底部。</summary>
    NoNewContent,
    /// <summary>与上一帧找不到重叠：滚太快、切换了页面，或区域里有动画。</summary>
    NoOverlap,
    /// <summary>已达到高度上限，后续帧不再追加。</summary>
    HeightLimit,
}

public readonly record struct StitchResult(StitchStatus Status, int AddedRows, int TotalHeight);

/// <summary>
/// 滚动截图的拼接器：把同一块屏幕区域反复抓到的帧，按"上下重叠内容"拼成一张长图。
///
/// 原理：滚动时新帧只是旧帧的内容整体上移，因此新帧的**顶部**一定能和上一帧的**底部**对上。
/// 逐行算一个采样哈希，从大到小试重叠行数，第一个通过"哈希一致率 + 像素级校验"的位移就是真实的滚动量；
/// 重叠部分丢弃、只追加下方新露出的行，所以不会重复内容。
///
/// 吸顶处理：网页/文档常有一条固定头部，它每帧都在最上面出现。检测"两帧开头有多少行完全相同"，
/// 若像固定头部就把它从匹配和对齐中整体跳过——成品里头部只保留一次。
///
/// 本类是纯逻辑（不碰屏幕、不碰窗体），可以拿合成图片直接单测。
/// </summary>
public sealed class ScrollStitcher : IDisposable
{
    /// <summary>少于这么多行的重叠不算数，避免把"碰巧有几行相同"当成滚动。</summary>
    public const int MinOverlap = 12;

    /// <summary>开头连续相同、达到这么多行才当作吸顶头部。</summary>
    public const int MinStickyRows = 8;

    /// <summary>默认高度上限：约 1.6 万像素，1000 宽时约 64 MB。</summary>
    public const int DefaultMaxHeight = 16000;

    /// <summary>每行采样多少列；越大越准、越慢。</summary>
    private const int SampleColumns = 160;

    /// <summary>重叠区像素平均差超过这个值就判为"不是同一段内容"。</summary>
    private const double MaxMeanDiff = 10.0;

    /// <summary>有意义的行（非纯色）少于这个数就不足以定位，宁可判失败。</summary>
    private const int MinInformativeRows = 8;

    private readonly int _width;
    private readonly int _maxHeight;
    private readonly List<Bitmap> _segments = [];

    private Bitmap? _previous;
    private int _height;
    private int _frames;

    public ScrollStitcher(int width, int maxHeight = DefaultMaxHeight)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "拼接宽度必须大于 0。");
        _width = width;
        _maxHeight = Math.Max(1, maxHeight);
    }

    public int Width => _width;
    public int Height => _height;
    public int Frames => _frames;

    /// <summary>追加一帧（帧的尺寸必须与构造时的宽度一致；本方法不接管帧的所有权）。</summary>
    public StitchResult Add(Bitmap frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Width != _width)
            throw new ArgumentException($"帧宽 {frame.Width} 与拼接宽度 {_width} 不一致。", nameof(frame));
        if (frame.Height <= 0) return new StitchResult(StitchStatus.NoNewContent, 0, _height);

        _frames++;

        if (_previous is null)
        {
            var first = Append(frame, 0, frame.Height);
            _previous = Clone(frame);
            var status = first < frame.Height ? StitchStatus.HeightLimit : StitchStatus.Appended;
            return new StitchResult(status, first, _height);
        }

        if (_height >= _maxHeight) return new StitchResult(StitchStatus.HeightLimit, 0, _height);

        // 还没滚动（或已到页面底部）时两帧几乎一模一样，直接跳过，不必做重叠搜索。
        if (MeanDiff(_previous, 0, frame, 0, frame.Height) < 1.2)
            return new StitchResult(StitchStatus.NoNewContent, 0, _height);

        var previousSignatures = Signatures(_previous);
        var frameSignatures = Signatures(frame);

        var match = FindOverlap(previousSignatures, _previous, frameSignatures, frame, topSkip: 0);

        // 有固定头部时，真正的重叠从头部下方开始；两条路都试，取"新内容更多"的那条。
        var sticky = StickyRows(previousSignatures, frameSignatures);
        if (sticky >= MinStickyRows)
        {
            var alt = FindOverlap(previousSignatures, _previous, frameSignatures, frame, sticky);
            if (alt.Matched && (!match.Matched || alt.NewRows > match.NewRows)) match = alt;
        }

        if (!match.Matched) return new StitchResult(StitchStatus.NoOverlap, 0, _height);
        if (match.NewRows <= 0) return new StitchResult(StitchStatus.NoNewContent, 0, _height);

        ReplacePrevious(frame);
        var added = Append(frame, match.SourceTop, match.NewRows);
        var result = _height >= _maxHeight ? StitchStatus.HeightLimit : StitchStatus.Appended;
        return new StitchResult(result, added, _height);
    }

    /// <summary>把已接受的片段合成为一张长图；调用方负责 Dispose。</summary>
    public Bitmap Compose()
    {
        if (_height <= 0) throw new InvalidOperationException("还没有可拼接的画面。");
        var result = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        // SourceCopy + 像素对齐：逐段原样搬运，不做插值混色。
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        var y = 0;
        foreach (var segment in _segments)
        {
            g.DrawImage(segment, new Rectangle(0, y, segment.Width, segment.Height));
            y += segment.Height;
        }
        return result;
    }

    public void Dispose()
    {
        foreach (var segment in _segments) segment.Dispose();
        _segments.Clear();
        _previous?.Dispose();
        _previous = null;
        _height = 0;
    }

    // ── 内部实现 ──────────────────────────────────────────

    private readonly record struct OverlapMatch(bool Matched, int SourceTop, int OverlapRows, int NewRows);

    private readonly record struct RowSignature(ulong Hash, bool Flat);

    private int Append(Bitmap frame, int sourceTop, int rows)
    {
        var allow = Math.Min(rows, _maxHeight - _height);
        if (allow <= 0) return 0;
        var band = frame.Clone(new Rectangle(0, sourceTop, _width, allow), PixelFormat.Format32bppArgb);
        _segments.Add(band);
        _height += allow;
        return allow;
    }

    private void ReplacePrevious(Bitmap frame)
    {
        var old = _previous;
        _previous = Clone(frame);
        old?.Dispose();
    }

    private Bitmap Clone(Bitmap frame) =>
        frame.Clone(new Rectangle(0, 0, frame.Width, frame.Height), PixelFormat.Format32bppArgb);

    /// <summary>从大到小试重叠行数，返回第一个通过校验的位移。</summary>
    private static OverlapMatch FindOverlap(RowSignature[] previousSignature, Bitmap previous,
        RowSignature[] frameSignature, Bitmap frame, int topSkip)
    {
        var maxOverlap = Math.Min(frame.Height - topSkip, previous.Height);
        for (var overlap = maxOverlap; overlap >= MinOverlap; overlap--)
        {
            var informative = 0;
            var matched = 0;
            var start = previous.Height - overlap;
            for (var i = 0; i < overlap; i++)
            {
                var row = previousSignature[start + i];
                if (row.Flat) continue;   // 纯色行不含信息，参与比较只会制造假匹配
                informative++;
                if (row.Hash == frameSignature[topSkip + i].Hash) matched++;
            }

            if (informative < MinInformativeRows) continue;
            if (matched * 100 < informative * 96) continue;   // 允许极少量行差异：光标、时间戳
            if (MeanDiff(previous, start, frame, topSkip, overlap) > MaxMeanDiff) continue;

            return new OverlapMatch(true, topSkip + overlap, overlap, frame.Height - topSkip - overlap);
        }
        return default;
    }

    /// <summary>两帧开头连续相同的行数（可能是吸顶头部）。</summary>
    private static int StickyRows(RowSignature[] previous, RowSignature[] frame)
    {
        var limit = Math.Min(Math.Min(previous.Length, frame.Length) / 4, 240);
        var n = 0;
        while (n < limit && previous[n].Hash == frame[n].Hash) n++;
        // 纯色条幅（空白、纯色页头）不当作吸顶头部，否则会白丢一截内容。
        return n >= MinStickyRows && HasDetail(previous, n) ? n : 0;
    }

    private static bool HasDetail(RowSignature[] rows, int count)
    {
        var detail = 0;
        for (var i = 0; i < count; i++)
        {
            if (rows[i].Flat) continue;
            if (++detail >= 2) return true;
        }
        return false;
    }

    /// <summary>逐行采样签名：哈希用于快速比对，"纯色行"标记用于剔除无信息行。</summary>
    private static RowSignature[] Signatures(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var signatures = new RowSignature[height];
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var buffer = new byte[stride];
            var step = Math.Max(1, width / SampleColumns);
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * stride), buffer, 0, stride);
                var hash = 14695981039346656037UL;
                int min = 255, max = 0;
                for (var x = 0; x < width; x += step)
                {
                    var i = x * 4;
                    var b = buffer[i];
                    var g = buffer[i + 1];
                    var r = buffer[i + 2];
                    hash = (hash ^ b) * 1099511628211UL;
                    hash = (hash ^ g) * 1099511628211UL;
                    hash = (hash ^ r) * 1099511628211UL;
                    var lum = (r * 77 + g * 151 + b * 28) >> 8;
                    if (lum < min) min = lum;
                    if (lum > max) max = lum;
                }
                signatures[y] = new RowSignature(hash, max - min <= 2);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return signatures;
    }

    /// <summary>两段区域的平均像素差（0-255），按行列抽样，只用于校验候选位移。</summary>
    private static double MeanDiff(Bitmap a, int aTop, Bitmap b, int bTop, int rows)
    {
        var width = Math.Min(a.Width, b.Width);
        var stepX = Math.Max(1, width / 64);
        var stepY = Math.Max(1, rows / 48);
        var aData = a.LockBits(new Rectangle(0, 0, a.Width, a.Height), ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var bData = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                var aRow = new byte[aData.Stride];
                var bRow = new byte[bData.Stride];
                long total = 0;
                var samples = 0L;
                for (var i = 0; i < rows; i += stepY)
                {
                    Marshal.Copy(IntPtr.Add(aData.Scan0, (aTop + i) * aData.Stride), aRow, 0, aData.Stride);
                    Marshal.Copy(IntPtr.Add(bData.Scan0, (bTop + i) * bData.Stride), bRow, 0, bData.Stride);
                    for (var x = 0; x < width; x += stepX)
                    {
                        var p = x * 4;
                        total += Math.Abs(aRow[p] - bRow[p])
                               + Math.Abs(aRow[p + 1] - bRow[p + 1])
                               + Math.Abs(aRow[p + 2] - bRow[p + 2]);
                        samples += 3;
                    }
                }
                return samples == 0 ? 0 : (double)total / samples;
            }
            finally
            {
                b.UnlockBits(bData);
            }
        }
        finally
        {
            a.UnlockBits(aData);
        }
    }
}
