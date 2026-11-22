using System.Text;
using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

public class EncodingConverterTests
{
    static EncodingConverterTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void Detect_Utf8Bom_Identified()
    {
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("中文")).ToArray();
        var d = TextEncodingConverter.Detect(bytes);
        Assert.True(d.HasBom);
        Assert.False(d.IsBinary);
        Assert.NotNull(d.Encoding);
        Assert.Contains("UTF-8", d.Reason);
    }

    [Fact]
    public void Detect_Utf8NoBom_Identified()
    {
        var d = TextEncodingConverter.Detect(Encoding.UTF8.GetBytes("hello 中文"));
        Assert.False(d.HasBom);
        Assert.False(d.IsBinary);
        Assert.NotNull(d.Encoding);
        Assert.Equal(65001, d.Encoding!.CodePage);
    }

    [Fact]
    public void Detect_GbkBytes_Identified()
    {
        var gbk = Encoding.GetEncoding(936);
        var d = TextEncodingConverter.Detect(gbk.GetBytes("中文测试内容"));
        Assert.False(d.HasBom);
        Assert.False(d.IsBinary);
        Assert.NotNull(d.Encoding);
        Assert.Contains("GB18030", d.Reason);
    }

    [Fact]
    public void Detect_BinaryWithNul_Rejected()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x00, 0x03, 0x04 };
        var d = TextEncodingConverter.Detect(bytes);
        Assert.True(d.IsBinary);
        Assert.Contains("NUL", d.Reason);
    }

    [Fact]
    public void Detect_EmptyFile_ReturnsNoEncoding()
    {
        var d = TextEncodingConverter.Detect([]);
        Assert.Null(d.Encoding);
        Assert.False(d.IsBinary);
        Assert.Contains("空", d.Reason);
    }

    [Fact]
    public void Options_ContainsAllSixEncodings()
    {
        Assert.Equal(6, TextEncodingConverter.Options.Count);
        Assert.NotNull(TextEncodingConverter.Find("utf-8"));
        Assert.NotNull(TextEncodingConverter.Find("utf-8-bom"));
        Assert.NotNull(TextEncodingConverter.Find("gbk"));
        Assert.NotNull(TextEncodingConverter.Find("gb18030"));
        Assert.NotNull(TextEncodingConverter.Find("utf-16le"));
        Assert.NotNull(TextEncodingConverter.Find("utf-16be"));
        Assert.Null(TextEncodingConverter.Find("not-exist"));
    }

    [Fact]
    public void Convert_Utf8ToGbk_RoundTrips()
    {
        var utf8 = Encoding.UTF8.GetBytes("中文内容 hello");
        var gbk = Encoding.GetEncoding(936);
        var result = TextEncodingConverter.Convert(utf8, Encoding.UTF8, gbk);
        Assert.True(result.Success, result.ErrorMessage);
        var decoded = gbk.GetString(result.Bytes!);
        Assert.Equal("中文内容 hello", decoded);
    }

    [Fact]
    public void Convert_GbkToUtf8_RoundTrips()
    {
        var gbk = Encoding.GetEncoding(936);
        var source = gbk.GetBytes("简体中文 GBK → UTF-8");
        var result = TextEncodingConverter.Convert(source, gbk, new UTF8Encoding(false));
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("简体中文 GBK → UTF-8", Encoding.UTF8.GetString(result.Bytes!));
    }

    [Fact]
    public void Convert_Utf8ToUtf8Bom_WritesPreamble()
    {
        var result = TextEncodingConverter.Convert(Encoding.UTF8.GetBytes("hello"),
            Encoding.UTF8, new UTF8Encoding(true));
        Assert.True(result.Success);
        Assert.Equal(new UTF8Encoding(true).GetPreamble(), result.Bytes![..3]);
    }

    [Fact]
    public void Convert_BomRemoved_NoDoubleBom()
    {
        var source = new UTF8Encoding(true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("content")).ToArray();
        var result = TextEncodingConverter.Convert(source, new UTF8Encoding(true), new UTF8Encoding(true));
        Assert.True(result.Success);
        Assert.Equal(new UTF8Encoding(true).GetPreamble(), result.Bytes![..3]);
        Assert.Equal("content", Encoding.UTF8.GetString(result.Bytes![3..]));
    }

    [Fact]
    public void Convert_EmptyBytes_Fails()
    {
        var result = TextEncodingConverter.Convert([], Encoding.UTF8, Encoding.UTF8);
        Assert.False(result.Success);
        Assert.Contains("空", result.ErrorMessage);
    }
}
