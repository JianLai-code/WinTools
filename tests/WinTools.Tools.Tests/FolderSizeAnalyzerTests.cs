using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

public class FolderSizeAnalyzerTests : IDisposable
{
    private readonly string _root;

    public FolderSizeAnalyzerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"wintools-fs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void WriteFile(string relPath, int bytes)
    {
        var full = Path.Combine(_root, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, new byte[bytes]);
    }

    [Fact]
    public void Analyze_MissingDirectory_Throws()
        => Assert.Throws<DirectoryNotFoundException>(
            () => FolderSizeAnalyzer.Analyze(Path.Combine(_root, "nope")));

    [Fact]
    public void Analyze_EmptyDirectory_ReturnsZero()
    {
        var result = FolderSizeAnalyzer.Analyze(_root);
        Assert.Equal(0, result.TotalBytes);
        Assert.Equal(0, result.TotalFiles);
        Assert.Empty(result.Children);
    }

    [Fact]
    public void Analyze_RecursiveSumAndSorting()
    {
        WriteFile("root.txt", 10);
        WriteFile(Path.Combine("big", "a.bin"), 1000);
        WriteFile(Path.Combine("big", "nested", "b.bin"), 500);
        WriteFile(Path.Combine("small", "c.bin"), 100);

        var result = FolderSizeAnalyzer.Analyze(_root);

        // 根直属 10 + big(1000+500) + small(100) = 1610
        Assert.Equal(1610, result.TotalBytes);
        Assert.Equal(4, result.TotalFiles);
        Assert.Equal(2, result.TotalDirs);
        Assert.Equal(2, result.Children.Count);
        // 按大小降序：big 在前
        Assert.Equal("big", result.Children[0].Name);
        Assert.Equal(1500, result.Children[0].TotalBytes);
        Assert.Equal(2, result.Children[0].TotalFiles);
        Assert.Equal("small", result.Children[1].Name);
        Assert.Equal(100, result.Children[1].TotalBytes);
        Assert.Equal(1, result.Children[1].DirectFiles);
    }

    [Fact]
    public void SumDirectory_WorksOnSubtree()
    {
        WriteFile(Path.Combine("sub", "x.bin"), 250);
        var (bytes, files, denied) = FolderSizeAnalyzer.SumDirectory(Path.Combine(_root, "sub"));
        Assert.Equal(250, bytes);
        Assert.Equal(1, files);
        Assert.Equal(0, denied);
    }

    [Fact]
    public void Analyze_PreCanceledToken_Throws()
    {
        WriteFile("a.bin", 10);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // 根目录直属文件循环第一步就会抛取消
        Assert.Throws<OperationCanceledException>(() => FolderSizeAnalyzer.Analyze(_root, cts.Token));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1536, "1.5 KB")]
    public void FormatSize_Works(long bytes, string expected)
        => Assert.Equal(expected, FolderSizeAnalyzer.FormatSize(bytes));
}
