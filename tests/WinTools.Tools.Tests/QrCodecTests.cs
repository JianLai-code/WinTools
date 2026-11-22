using WinTools.Tools.Text;
using Xunit;

namespace WinTools.Tools.Tests;

public class QrCodecTests
{
    [Fact]
    public void Generate_EmptyText_Fails()
    {
        var result = QrCodec.Generate("   ");
        Assert.False(result.Success);
        Assert.Contains("请输入", result.ErrorMessage);
        Assert.Null(result.Png);
    }

    [Fact]
    public void Generate_TooLong_Fails()
    {
        var result = QrCodec.Generate(new string('a', QrCodec.MaxLength + 1));
        Assert.False(result.Success);
        Assert.Contains("内容过长", result.ErrorMessage);
    }

    [Fact]
    public void Generate_Url_ProducesPngWithHeader()
    {
        var result = QrCodec.Generate("https://example.com/path?q=1");
        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Png);
        Assert.True(result.ModuleCount >= 21);
        // PNG 文件签名：89 50 4E 47 0D 0A 1A 0A
        var sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Assert.Equal(sig, result.Png![..8]);
    }

    [Fact]
    public void Generate_ChineseText_Succeeds()
    {
        var result = QrCodec.Generate("WinTools 工具箱 · 中文二维码测试");
        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Png);
        Assert.True(result.Png!.Length > 100);
    }

    [Fact]
    public void Generate_SameInput_ProducesSameBytes()
    {
        var a = QrCodec.Generate("deterministic-qr");
        var b = QrCodec.Generate("deterministic-qr");
        Assert.True(a.Success && b.Success);
        Assert.Equal(a.Png, b.Png);
    }

    [Fact]
    public void Generate_DifferentInput_ProducesDifferentBytes()
    {
        var a = QrCodec.Generate("first-content");
        var b = QrCodec.Generate("second-content-different");
        Assert.NotEqual(a.Png, b.Png);
    }

    [Fact]
    public void Generate_PixelClamped_ToSafeRange()
    {
        // 极小 / 极大像素都应被夹紧，不抛异常且仍产出 PNG
        var tiny = QrCodec.Generate("clamp-test", 0);
        var huge = QrCodec.Generate("clamp-test", 1000);
        Assert.True(tiny.Success);
        Assert.True(huge.Success);
        Assert.True(huge.Png!.Length > tiny.Png!.Length);
    }
}
