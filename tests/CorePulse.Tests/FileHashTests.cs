using CpuMonitorNotifier.Update;
using Xunit;

namespace CorePulse.Tests;

public class FileHashTests
{
    // Эталон: SHA256("abc") из FIPS 180-4
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public async Task Sha256Async_MatchesKnownVector()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "abc");
            Assert.Equal(AbcSha256, await FileHash.Sha256Async(path, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Sha256Async_ReturnsLowercaseHex()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "abc");
            string hash = await FileHash.Sha256Async(path, CancellationToken.None);
            Assert.Equal(64, hash.Length);
            Assert.Equal(hash.ToLowerInvariant(), hash);
        }
        finally { File.Delete(path); }
    }
}
