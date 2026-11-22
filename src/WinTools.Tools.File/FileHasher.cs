using System.Buffers;
using System.IO;
using System.Security.Cryptography;

namespace WinTools.Tools.Files;

/// <summary>
/// 文件哈希计算的纯逻辑类，不依赖 UI，便于单元测试。
/// 对应验收用例 F1-AC1 ~ AC4（见阶段二文档）。
///
/// 核心设计：一次流式读取，同时喂给 MD5/SHA1/SHA256 三个 IncrementalHash。
/// 这样大文件只读一遍磁盘（而不是三遍），且内存恒定（1MB 缓冲），不会随文件大小暴涨。
/// </summary>
public static class FileHasher
{
    // 1MB 缓冲区：足够大以减少 IO 次数，又不会占用过多内存。
    private const int BufferSize = 1024 * 1024;

    /// <summary>
    /// 异步计算单个文件的三个哈希值，通过 progress 报告 0~100 的百分比。
    /// 任何读取失败都转为 FileHashResult.Fail，绝不把异常抛给调用方（F1-AC4）。
    /// </summary>
    public static async Task<FileHashResult> ComputeAsync(
        string filePath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // F1-AC4：文件不存在、是目录、无权限等，统一转为失败结果。
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            return FileHashResult.Fail(filePath ?? string.Empty, "文件不存在或无法访问");
        }

        try
        {
            // FileShare.ReadWrite：允许其他程序正打开的文件也能读取（如日志、文档）。
            // 这是 F1-AC4"被占用文件"的关键——用默认 FileShare.Read 会被占用文件拒绝。
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var totalBytes = stream.Length;

            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            // 用 ArrayPool 复用缓冲区，避免大文件计算时反复分配 1MB 数组造成 GC 压力。
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                long readTotal = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
                {
                    var span = buffer.AsSpan(0, read);
                    md5.AppendData(span);
                    sha1.AppendData(span);
                    sha256.AppendData(span);

                    readTotal += read;
                    // 空文件 totalBytes=0 时直接报 100，避免除零。
                    var percent = totalBytes == 0 ? 100 : (int)(readTotal * 100 / totalBytes);
                    progress?.Report(Math.Min(percent, 100));
                }

                // 校验：读取量与文件长度不符（文件中途被改动）时明确报错，不给错误哈希。
                if (readTotal != totalBytes)
                {
                    return FileHashResult.Fail(filePath, "文件在计算过程中被修改，结果不可靠");
                }

                progress?.Report(100);

                return FileHashResult.Ok(
                    filePath,
                    Convert.ToHexString(md5.GetHashAndReset()),
                    Convert.ToHexString(sha1.GetHashAndReset()),
                    Convert.ToHexString(sha256.GetHashAndReset()));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException)
        {
            return FileHashResult.Fail(filePath, "计算已取消");
        }
        catch (UnauthorizedAccessException)
        {
            return FileHashResult.Fail(filePath, "没有读取权限");
        }
        catch (IOException ex)
        {
            return FileHashResult.Fail(filePath, $"读取失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            // 兜底：任何意外异常都转成结果，保证"一个文件失败不影响其他文件"。
            return FileHashResult.Fail(filePath, $"计算失败：{ex.Message}");
        }
    }
}
