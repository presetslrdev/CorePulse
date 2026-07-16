using CpuMonitorNotifier.Update;
using Xunit;

namespace CorePulse.Tests;

public class UpdateServiceTests
{
    // Тесты собираются локально → метка source → сеть не трогается вовсе.
    [Fact]
    public async Task CheckAsync_NotSupportedForSourceBuilds()
    {
        var result = await new UpdateService().CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateCheckStatus.NotSupported, result.Status);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task DownloadAsync_RejectsHashMismatchAndLeavesNoTempFile()
    {
        // file:// — та же логика загрузки, но без сети
        string source = Path.GetTempFileName();
        await File.WriteAllTextAsync(source, "pretend this is CorePulse.exe");
        var release = new ReleaseInfo(
            new Version(9, 9, 9),
            new Uri(source).AbsoluteUri,
            new string('0', 64), // заведомо неверный хеш
            "https://example.invalid");

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => new UpdateService().DownloadAsync(release, CancellationToken.None));
            Assert.Contains("SHA256", ex.Message);
            Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "CorePulse-9.9.9-*.exe"));
        }
        finally { File.Delete(source); }
    }

    [Fact]
    public async Task DownloadAsync_KeepsFileWhenHashMatches()
    {
        string source = Path.GetTempFileName();
        await File.WriteAllTextAsync(source, "abc");
        var release = new ReleaseInfo(
            new Version(9, 9, 8),
            new Uri(source).AbsoluteUri,
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "https://example.invalid");

        string? downloaded = null;
        try
        {
            downloaded = await new UpdateService().DownloadAsync(release, CancellationToken.None);
            Assert.True(File.Exists(downloaded));
            Assert.Equal("abc", await File.ReadAllTextAsync(downloaded));
        }
        finally
        {
            File.Delete(source);
            if (downloaded is not null) File.Delete(downloaded);
        }
    }
}
