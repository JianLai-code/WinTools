using System.Security.Cryptography;
using System.Text;
using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

public class DuplicateFinderTests
{
    private static string TempFile(long bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"wt-dup-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void Find_GroupsByIdenticalSizeAndHash()
    {
        var a = TempFile(64);
        var b = TempFile(64);
        var c = TempFile(128);
        try
        {
            var files = new[] { a, b, c };
            // 假哈希：同名前缀视为相同内容，c 大小唯一根本不该参与哈希。
            var hashed = 0;
            var groups = DuplicateFinder.Find(files, path =>
            {
                hashed++;
                return path == c ? throw new InvalidOperationException("大小唯一的文件不应被哈希") : "ABC";
            });

            var group = Assert.Single(groups);
            Assert.Equal(64, group.Size);
            Assert.Equal(2, group.Paths.Count);
            Assert.Equal(2, hashed);
        }
        finally { foreach (var f in new[] { a, b, c }) if (File.Exists(f)) File.Delete(f); }
    }

    [Fact]
    public void Find_SameSizeDifferentHash_AreNotDuplicates()
    {
        var a = TempFile(32);
        var b = TempFile(32);
        try
        {
            var groups = DuplicateFinder.Find(new[] { a, b }, p => p == a ? "A" : "B");
            Assert.Empty(groups);
        }
        finally { foreach (var f in new[] { a, b }) if (File.Exists(f)) File.Delete(f); }
    }

    [Fact]
    public void Find_MinBytes_FiltersSmallFiles()
    {
        var a = TempFile(10);
        var b = TempFile(10);
        try
        {
            Assert.Empty(DuplicateFinder.Find(new[] { a, b }, _ => "X", minBytes: 100));
            Assert.Single(DuplicateFinder.Find(new[] { a, b }, _ => "X", minBytes: 10));
        }
        finally { foreach (var f in new[] { a, b }) if (File.Exists(f)) File.Delete(f); }
    }

    [Fact]
    public void Find_PreCanceledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            DuplicateFinder.Find(new[] { "x" }, _ => "X", cancellationToken: cts.Token));
    }

    [Fact]
    public void ComputeSha256_MatchesKnownVector()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wt-hash-{Guid.NewGuid():N}.txt");
        try
        {
            var bytes = Encoding.UTF8.GetBytes("WinTools");
            File.WriteAllBytes(path, bytes);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), DuplicateFinder.ComputeSha256(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void BuildReport_ContainsAllPathsAndReclaimableSpace()
    {
        var group = new DuplicateGroup("ABC", 2048, new[] { @"C:\a\same.txt", @"D:\b\same.txt" });
        var report = DuplicateFinder.BuildReport(new[] { group });
        Assert.Contains("ABC", report);
        Assert.Contains(@"C:\a\same.txt", report);
        Assert.Contains(@"D:\b\same.txt", report);
        Assert.Contains("2.0 KB", report);
        Assert.Contains("2 份", report);
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2.0 KB")]
    [InlineData(5 * 1024 * 1024, "5.0 MB")]
    public void FormatSize_UsesHumanUnits(long bytes, string expected) =>
        Assert.Equal(expected, DuplicateFinder.FormatSize(bytes));
}
