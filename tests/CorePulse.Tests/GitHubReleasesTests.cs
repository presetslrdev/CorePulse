using CpuMonitorNotifier.Update;
using Xunit;

namespace CorePulse.Tests;

public class GitHubReleasesTests
{
    // Форма ответа GitHub /releases/latest, обрезанная до используемых полей
    private const string LatestJson = """
    {
      "tag_name": "v1.5.0",
      "html_url": "https://github.com/presetslrdev/CorePulse/releases/tag/v1.5.0",
      "assets": [
        { "name": "CorePulse.exe",
          "browser_download_url": "https://github.com/presetslrdev/CorePulse/releases/download/v1.5.0/CorePulse.exe" },
        { "name": "CorePulse-net10.exe",
          "browser_download_url": "https://github.com/presetslrdev/CorePulse/releases/download/v1.5.0/CorePulse-net10.exe" },
        { "name": "SHA256SUMS.txt",
          "browser_download_url": "https://github.com/presetslrdev/CorePulse/releases/download/v1.5.0/SHA256SUMS.txt" }
      ]
    }
    """;

    [Fact]
    public void ParseLatest_ReadsVersionAndPage()
    {
        var r = GitHubReleases.ParseLatest(LatestJson, "CorePulse.exe");
        Assert.NotNull(r);
        Assert.Equal(new Version(1, 5, 0), r!.Version);
        Assert.Equal("https://github.com/presetslrdev/CorePulse/releases/tag/v1.5.0", r.PageUrl);
    }

    [Fact]
    public void ParseLatest_PicksTheRequestedAsset()
    {
        var self = GitHubReleases.ParseLatest(LatestJson, "CorePulse.exe");
        var fx = GitHubReleases.ParseLatest(LatestJson, "CorePulse-net10.exe");
        Assert.EndsWith("/CorePulse.exe", self!.AssetUrl);
        Assert.EndsWith("/CorePulse-net10.exe", fx!.AssetUrl);
        Assert.EndsWith("/SHA256SUMS.txt", self.SumsUrl);
    }

    [Fact]
    public void ParseLatest_NullWhenAssetMissing()
        => Assert.Null(GitHubReleases.ParseLatest(LatestJson, "CorePulse-arm64.exe"));

    [Fact]
    public void ParseLatest_NullWhenSumsMissing()
    {
        const string noSums = """
        { "tag_name": "v1.5.0", "html_url": "x",
          "assets": [ { "name": "CorePulse.exe", "browser_download_url": "u" } ] }
        """;
        Assert.Null(GitHubReleases.ParseLatest(noSums, "CorePulse.exe"));
    }

    [Fact]
    public void ParseLatest_NullWhenTagIsNotAVersion()
    {
        const string badTag = """
        { "tag_name": "nightly", "html_url": "x", "assets": [] }
        """;
        Assert.Null(GitHubReleases.ParseLatest(badTag, "CorePulse.exe"));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("{}")]
    public void ParseLatest_NullOnMalformedInput(string json)
        => Assert.Null(GitHubReleases.ParseLatest(json, "CorePulse.exe"));

    [Fact]
    public void ParseSha256Sums_FindsTheRequestedFile()
    {
        string sums =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  CorePulse.exe\n" +
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  CorePulse-net10.exe\n";
        Assert.Equal(new string('a', 64), GitHubReleases.ParseSha256Sums(sums, "CorePulse.exe"));
        Assert.Equal(new string('b', 64), GitHubReleases.ParseSha256Sums(sums, "CorePulse-net10.exe"));
    }

    [Fact]
    public void ParseSha256Sums_HandlesCrlfAndBinaryMarkerAndCase()
    {
        string sums = new string('A', 64) + " *CorePulse.exe\r\n";
        Assert.Equal(new string('a', 64), GitHubReleases.ParseSha256Sums(sums, "CorePulse.exe"));
    }

    [Fact]
    public void ParseSha256Sums_NullWhenFileAbsent()
        => Assert.Null(GitHubReleases.ParseSha256Sums(new string('a', 64) + "  other.exe\n", "CorePulse.exe"));

    [Fact]
    public void ParseSha256Sums_IgnoresLinesThatAreNotHashes()
        => Assert.Null(GitHubReleases.ParseSha256Sums("deadbeef  CorePulse.exe\n", "CorePulse.exe"));
}
