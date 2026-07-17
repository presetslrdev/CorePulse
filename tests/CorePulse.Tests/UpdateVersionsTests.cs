using CpuMonitorNotifier.Update;
using Xunit;

namespace CorePulse.Tests;

public class UpdateVersionsTests
{
    [Theory]
    [InlineData("v1.5.0", "1.5.0")]
    [InlineData("1.5.0", "1.5.0")]
    [InlineData("V1.5.0", "1.5.0")]
    [InlineData(" v1.5.0 ", "1.5.0")]
    [InlineData("v1.5", "1.5.0")]
    [InlineData("v1.5.0.7", "1.5.0")]
    public void ParseTag_ReadsReleaseTags(string tag, string expected)
        => Assert.Equal(Version.Parse(expected), UpdateVersions.ParseTag(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nightly")]
    [InlineData("v")]
    public void ParseTag_ReturnsNullForJunk(string? tag)
        => Assert.Null(UpdateVersions.ParseTag(tag));

    [Fact]
    public void Normalize_TreatsThreeAndFourComponentVersionsAsEqual()
        => Assert.Equal(UpdateVersions.Normalize(new Version(1, 4, 0)),
                        UpdateVersions.Normalize(new Version(1, 4, 0, 0)));

    [Fact]
    public void IsNewer_TrueForHigherVersion()
        => Assert.True(UpdateVersions.IsNewer(new Version(1, 4, 0, 0), new Version(1, 5, 0)));

    [Fact]
    public void IsNewer_FalseForSameVersionAcrossComponentCounts()
        => Assert.False(UpdateVersions.IsNewer(new Version(1, 4, 0, 0), new Version(1, 4, 0)));

    [Fact]
    public void IsNewer_FalseForOlderVersion_NoDowngrade()
        => Assert.False(UpdateVersions.IsNewer(new Version(1, 5, 0, 0), new Version(1, 4, 0)));

    [Fact]
    public void Current_ReportsTheRunningAssemblyVersion()
        => Assert.True(UpdateVersions.Current >= new Version(1, 4, 0));
}
