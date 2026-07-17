using System.Security.Cryptography;

namespace CpuMonitorNotifier.Update;

/// <summary>Хеширование файлов для проверки загруженного обновления.</summary>
internal static class FileHash
{
    /// <summary>SHA256 файла в виде строчного hex.</summary>
    public static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
