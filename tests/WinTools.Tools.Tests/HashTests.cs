using System.Security.Cryptography;
using System.Text;
using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// FileHasher 单元测试，实现阶段二 F1-AC1~AC4 与阶段七 F1 单测清单。
/// 全部自包含：不依赖 certutil，用公认标准向量 + .NET 内置 API 交叉验证。
/// </summary>
public sealed class HashTests : IDisposable
{
    private readonly string _tempDir;

    public HashTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wintools-hash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    // 阶段七 Hash_KnownVectors_MatchExpected + F1-AC1
    // 空文件与 abc 的 MD5/SHA1/SHA256 公认值（密码学标准测试向量）
    [Theory]
    // 注意：Convert.ToHexString 输出大写，与 certutil -hashfile 一致（F1-AC1），
    // 所以期望值用大写；下面统一 ToUpperInvariant 比较，避免因大小写写法不同而误报。
    [InlineData("", "d41d8cd98f00b204e9800998ecf8427e",
                     "da39a3ee5e6b4b0d3255bfef95601890afd80709",
                     "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "900150983cd24fb0d6963f7d28e17f72",
                       "a9993e364706816aba3e25717850c26c9cd0d89d",
                       "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public async Task Hash_KnownVectors_MatchExpected(string content, string md5, string sha1, string sha256)
    {
        var path = WriteFile("vector.bin", Encoding.ASCII.GetBytes(content));

        var result = await FileHasher.ComputeAsync(path);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(md5.ToUpperInvariant(), result.Md5);
        Assert.Equal(sha1.ToUpperInvariant(), result.Sha1);
        Assert.Equal(sha256.ToUpperInvariant(), result.Sha256);
    }

    // 阶段七 Hash_LargeFile_Streaming：生成跨越多个 1MB 缓冲区边界的文件，
    // 验证流式分块结果与 .NET 内置一次性哈希一致（既测分块正确性，又不依赖记忆长哈希值）。
    [Fact]
    public async Task Hash_LargeFile_Streaming_MatchesOneShotHash()
    {
        // 2.5MB > 2 个缓冲区边界，能暴露"漏读最后一块"这类分块 bug。
        var content = new byte[2_500_000];
        new Random(42).NextBytes(content); // 固定种子，测试可重复
        var path = WriteFile("large.bin", content);

        var result = await FileHasher.ComputeAsync(path);

        Assert.True(result.Success, result.ErrorMessage);
        // 用 .NET 内置一次性 API 算期望值做交叉验证
        Assert.Equal(Convert.ToHexString(MD5.HashData(content)), result.Md5);
        Assert.Equal(Convert.ToHexString(SHA1.HashData(content)), result.Sha1);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)), result.Sha256);
    }

    // F1-AC3：进度回调会随读取递增，且最终到达 100
    [Fact]
    public async Task Hash_LargeFile_ReportsProgressToHundred()
    {
        var content = new byte[2_500_000];
        new Random(7).NextBytes(content);
        var path = WriteFile("progress.bin", content);
        var reports = new List<int>();

        var result = await FileHasher.ComputeAsync(path, new Progress<int>(reports.Add));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotEmpty(reports);
        Assert.Equal(100, reports[^1]);
        Assert.All(reports, p => Assert.InRange(p, 0, 100));
    }

    // F1-AC2：三个文件分别计算，哈希互不串扰
    [Fact]
    public async Task Hash_MultipleFiles_NoCrossContamination()
    {
        var p1 = WriteFile("a.txt", Encoding.ASCII.GetBytes("abc"));
        var p2 = WriteFile("b.txt", Encoding.ASCII.GetBytes("hello"));
        var p3 = WriteFile("c.txt", Encoding.ASCII.GetBytes("world"));

        var r1 = await FileHasher.ComputeAsync(p1);
        var r2 = await FileHasher.ComputeAsync(p2);
        var r3 = await FileHasher.ComputeAsync(p3);

        Assert.True(r1.Success && r2.Success && r3.Success);
        Assert.Equal("900150983CD24FB0D6963F7D28E17F72", r1.Md5); // abc（大写，与 certutil 一致）
        Assert.NotEqual(r1.Md5, r2.Md5);
        Assert.NotEqual(r2.Md5, r3.Md5);
    }

    // 阶段七 Hash_LockedFile_ReportsError + F1-AC4：不存在的文件返回错误结果，不抛异常
    [Fact]
    public async Task Hash_MissingFile_ReportsErrorNotThrows()
    {
        var path = Path.Combine(_tempDir, "not-exist.bin");

        var result = await FileHasher.ComputeAsync(path);

        Assert.False(result.Success);
        Assert.Contains("不存在", result.ErrorMessage);
        Assert.Equal(string.Empty, result.Md5); // 不输出半成品
    }

    // F1-AC4 补充：传入目录路径也应安全失败
    [Fact]
    public async Task Hash_DirectoryPath_ReportsErrorNotThrows()
    {
        var result = await FileHasher.ComputeAsync(_tempDir);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }
}
