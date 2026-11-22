using QRCoder;

namespace WinTools.Tools.Text;

/// <summary>二维码生成结果：PNG 字节可直接显示或另存，不依赖 System.Drawing。</summary>
public sealed record QrResult(bool Success, byte[]? Png, string? ErrorMessage, int ModuleCount)
{
    public static QrResult Ok(byte[] png, int modules) => new(true, png, null, modules);
    public static QrResult Fail(string msg) => new(false, null, msg, 0);
}

/// <summary>
/// 二维码编码：文本 → PNG 字节。纯本地计算，无网络、无 System.Drawing 依赖，
/// 用 PngByteQRCode 直接产出字节数组，便于单测与跨平台。
/// </summary>
public static class QrCodec
{
    /// <summary>内容字符上限。超出后二维码密度过高，普通摄像头难以识别。</summary>
    public const int MaxLength = 1200;
    public const int DefaultPixelPerModule = 12;

    public static QrResult Generate(string text, int pixelPerModule = DefaultPixelPerModule)
    {
        if (string.IsNullOrWhiteSpace(text))
            return QrResult.Fail("请输入要生成二维码的文本或链接");

        if (text.Length > MaxLength)
            return QrResult.Fail($"内容过长（{text.Length} 字符），请控制在 {MaxLength} 字符以内，否则二维码难以识别");

        var px = Math.Clamp(pixelPerModule, 4, 40);
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(px, new byte[] { 17, 24, 39 }, new byte[] { 255, 255, 255 }, false);
            return QrResult.Ok(png, data.ModuleMatrix.Count);
        }
        catch (Exception ex)
        {
            // 内容含大量中文/emoji 时 UTF-8 字节数可能超容量，给出可操作的提示。
            return QrResult.Fail($"二维码生成失败：{ex.Message}。请缩短内容后重试");
        }
    }
}
