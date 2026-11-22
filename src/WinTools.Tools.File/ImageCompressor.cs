using System.Drawing;
using System.Drawing.Imaging;

namespace WinTools.Tools.Files;

/// <summary>单个文件的压缩结果。</summary>
public sealed record ImageCompressItem(
    string Path,
    long OriginalBytes,
    long ResultBytes,
    bool Written,        // 是否已写出（变大时保留原图、不写出）
    string? Note)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public long Delta => ResultBytes - OriginalBytes;
    public bool Grew => Delta > 0;
}

public sealed class ImageCompressResult
{
    public IReadOnlyList<ImageCompressItem> Items { get; init; } = [];
    public int Succeeded => Items.Count(i => i.Written);
    public int Skipped => Items.Count(i => !i.Written);
    public long TotalOriginal => Items.Sum(i => i.OriginalBytes);
    public long TotalSaved => Items.Where(i => i.Written).Sum(i => i.OriginalBytes - i.ResultBytes);
}

/// <summary>
/// I2 图片批量压缩的纯逻辑：把图片按指定质量重新编码为 JPG，结果另存不覆盖。
///
/// 策略：
/// - 只输出 JPG（压缩收益最明确、跨软件兼容最好），透明通道合成到白底；
/// - 若压缩后反而变大（小图/已是高压缩图常见），保留原图、不写出新文件，并在结果里标注；
/// - 单张失败不影响其余条目，逐条收集说明。
/// </summary>
public static class ImageCompressor
{
    /// <summary>压缩单张图片到字节；调用方负责另存与"变大则保留原图"的决策。</summary>
    public static ImageConvertResult CompressToBytes(byte[] source, long quality)
        => ImageFormatCodec.Convert(source, ImageFormatCodec.Find("jpg")!, quality);

    /// <summary>
    /// 批量压缩：把 sourceBytes 按质量压成 JPG 字节返回，供界面另存。
    /// 这里不直接写文件——写路径、防覆盖、变大保留原图的策略集中在 CompressFiles 便于审计。
    /// </summary>
    public static ImageCompressResult CompressFiles(
        IReadOnlyList<(string Path, byte[] Bytes)> inputs,
        long quality,
        Func<string, string> outputPathFor,
        Action<string, byte[]> write)
    {
        var items = new List<ImageCompressItem>();
        foreach (var (path, bytes) in inputs)
        {
            var converted = CompressToBytes(bytes, quality);
            if (!converted.Success || converted.Bytes is null)
            {
                items.Add(new ImageCompressItem(path, bytes.Length, 0, false,
                    converted.ErrorMessage ?? "压缩失败"));
                continue;
            }

            var outBytes = converted.Bytes;
            if (outBytes.Length >= bytes.Length)
            {
                // 变大或相等：保留原图，不写出，避免帮倒忙。
                items.Add(new ImageCompressItem(path, bytes.Length, outBytes.Length, false,
                    "压缩后未变小，已保留原图"));
                continue;
            }

            var outPath = outputPathFor(path);
            if (string.Equals(Path.GetFullPath(outPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new ImageCompressItem(path, bytes.Length, outBytes.Length, false,
                    "输出路径与源文件相同，已跳过（不覆盖原图）"));
                continue;
            }

            try
            {
                write(outPath, outBytes);
                var alphaNote = converted.AlphaFlattened ? "；透明区已填白" : "";
                items.Add(new ImageCompressItem(path, bytes.Length, outBytes.Length, true,
                    $"→ {Path.GetFileName(outPath)}{alphaNote}"));
            }
            catch (Exception ex)
            {
                items.Add(new ImageCompressItem(path, bytes.Length, outBytes.Length, false,
                    $"写出失败：{ex.Message}"));
            }
        }
        return new ImageCompressResult { Items = items };
    }
}
