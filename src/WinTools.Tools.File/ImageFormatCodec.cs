using System.Drawing;
using System.Drawing.Imaging;

namespace WinTools.Tools.Files;

/// <summary>一种可输出的图片格式：id / 显示名 / 扩展名 / GDI+ ImageFormat / 是否保留透明通道。</summary>
public sealed record ImageFormatSpec(string Id, string Display, string Extension, ImageFormat Format, bool KeepsAlpha);

/// <summary>图片格式转换结果。</summary>
public sealed class ImageConvertResult
{
    public bool Success { get; private init; }
    public byte[]? Bytes { get; private init; }
    public string? ErrorMessage { get; private init; }
    public bool AlphaFlattened { get; private init; }  // 源带透明、目标不支持，已填白
    public int Width { get; private init; }
    public int Height { get; private init; }
    public string? DetectedFormat { get; private init; }

    public static ImageConvertResult Fail(string message) => new() { ErrorMessage = message };
    public static ImageConvertResult Ok(byte[] bytes, bool alphaFlattened, int width, int height, string? detected)
        => new() { Success = true, Bytes = bytes, AlphaFlattened = alphaFlattened, Width = width, Height = height, DetectedFormat = detected };
}

/// <summary>
/// I1 图片格式转换的纯逻辑：PNG / JPG / BMP / GIF / TIFF 互转，JPG 可调质量。
///
/// 只依赖系统 GDI+ 内置编解码器，无第三方库、无网络。经实测本机 GDI+ 编码器为
/// PNG/JPEG/BMP/GIF/TIFF，不含 WebP，因此不支持 WebP——界面需明确说明。
/// 目标不支持透明（JPG/BMP）而源图带 alpha 时，先合成到白底再编码，避免透明区变黑。
/// </summary>
public static class ImageFormatCodec
{
    public static readonly IReadOnlyList<ImageFormatSpec> Formats =
    [
        new("png",  "PNG",  ".png",  ImageFormat.Png,  true),
        new("jpg",  "JPG",  ".jpg",  ImageFormat.Jpeg, false),
        new("bmp",  "BMP",  ".bmp",  ImageFormat.Bmp,  false),
        new("gif",  "GIF",  ".gif",  ImageFormat.Gif,  true),
        new("tiff", "TIFF", ".tiff", ImageFormat.Tiff, true),
    ];

    public static ImageFormatSpec? Find(string id) => Formats.FirstOrDefault(f => f.Id == id);

    /// <summary>按文件扩展名猜测来源格式名（仅用于界面提示，实际解码交给 GDI+）。</summary>
    public static string? DetectByExtension(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "jpg" or "jpeg" or "jpe" => "JPG",
            "png" => "PNG",
            "bmp" => "BMP",
            "gif" => "GIF",
            "tif" or "tiff" => "TIFF",
            _ => null
        };
    }

    /// <summary>把源格式描述归一到支持的 id；扩展名不可识别时返回 null（GDI+ 仍可能解码成功）。</summary>
    public static string? IdFromExtension(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "jpg" or "jpeg" or "jpe" => "jpg",
            "png" => "png",
            "bmp" => "bmp",
            "gif" => "gif",
            "tif" or "tiff" => "tiff",
            _ => null
        };
    }

    /// <summary>转换图片字节。quality 仅对 JPG 生效（1-100），其它格式忽略。</summary>
    public static ImageConvertResult Convert(byte[] source, ImageFormatSpec target, long quality = 90)
    {
        if (source is null || source.Length == 0)
            return ImageConvertResult.Fail("源图片为空。");

        try
        {
            using var input = new MemoryStream(source, writable: false);
            using var image = Image.FromStream(input);
            var hasAlpha = ((ImageFlags)image.Flags & ImageFlags.HasAlpha) != 0;
            var flatten = hasAlpha && !target.KeepsAlpha;
            using var output = new MemoryStream();

            if (flatten)
            {
                // 透明区合成到白底：GDI+ 直接把 alpha 图存成 JPG/BMP 时透明像素的 RGB 多为 0（黑）。
                using var flat = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
                using var g = Graphics.FromImage(flat);
                g.Clear(Color.White);
                g.DrawImage(image, 0, 0, image.Width, image.Height);
                Encode(flat, output, target, quality);
            }
            else
            {
                Encode(image, output, target, quality);
            }

            return ImageConvertResult.Ok(output.ToArray(), flatten, image.Width, image.Height,
                DescribeSource(image.PixelFormat));
        }
        catch (ArgumentException)
        {
            return ImageConvertResult.Fail("无法识别的图片格式，或文件已损坏。");
        }
        catch (OutOfMemoryException)
        {
            return ImageConvertResult.Fail("图片过大或格式不受支持。");
        }
        catch (Exception ex)
        {
            return ImageConvertResult.Fail($"转换失败：{ex.Message}");
        }
    }

    private static void Encode(Image image, Stream output, ImageFormatSpec target, long quality)
    {
        if (target.Id == "jpg")
        {
            using var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,
                Math.Clamp(quality, 1, 100));
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
            image.Save(output, codec, encoderParams);
        }
        else
        {
            image.Save(output, target.Format);
        }
    }

    private static string DescribeSource(PixelFormat format) => format switch
    {
        PixelFormat.Format32bppArgb or PixelFormat.Format32bppPArgb => "32 位带透明",
        PixelFormat.Format24bppRgb => "24 位真彩色",
        PixelFormat.Format8bppIndexed => "8 位索引色",
        PixelFormat.Format1bppIndexed => "1 位黑白",
        _ => format.ToString()
    };

    /// <summary>人类可读的字节数。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
