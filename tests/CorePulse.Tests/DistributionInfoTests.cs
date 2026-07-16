using CpuMonitorNotifier.Update;
using Xunit;

namespace CorePulse.Tests;

public class DistributionInfoTests
{
    // Отдельные Fact, а не Theory с параметром DistributionKind: внутренний тип в сигнатуре
    // публичного метода даёт CS0051, а xunit требует публичный тест-класс. В теле метода — можно.
    [Fact]
    public void Parse_SelfContainedStamp()
        => Assert.Equal(DistributionKind.SelfContained, DistributionInfo.Parse("self-contained"));

    [Fact]
    public void Parse_FrameworkStamp()
        => Assert.Equal(DistributionKind.Framework, DistributionInfo.Parse("framework"));

    [Fact]
    public void Parse_SourceStamp()
        => Assert.Equal(DistributionKind.Source, DistributionInfo.Parse("source"));

    [Fact]
    public void Parse_EmptyStampIsSource()
        => Assert.Equal(DistributionKind.Source, DistributionInfo.Parse(""));

    [Fact]
    public void Parse_MissingStampIsSource()
        => Assert.Equal(DistributionKind.Source, DistributionInfo.Parse(null));

    [Fact]
    public void Parse_UnknownStampIsSource()
        => Assert.Equal(DistributionKind.Source, DistributionInfo.Parse("nonsense"));

    [Fact]
    public void AssetNameFor_SelfContained() =>
        Assert.Equal("CorePulse.exe", DistributionInfo.AssetNameFor(DistributionKind.SelfContained));

    [Fact]
    public void AssetNameFor_Framework() =>
        Assert.Equal("CorePulse-net10.exe", DistributionInfo.AssetNameFor(DistributionKind.Framework));

    [Fact]
    public void AssetNameFor_Source_HasNoAsset() =>
        Assert.Null(DistributionInfo.AssetNameFor(DistributionKind.Source));

    // Тесты собирают приложение локально, без -p:DistributionKind — значит метка source
    // и самообновление под разработчиком выключено.
    [Fact]
    public void LocalBuild_IsStampedAsSource()
        => Assert.Equal(DistributionKind.Source, DistributionInfo.Current);

    [Fact]
    public void LocalBuild_DoesNotSupportUpdates()
        => Assert.False(DistributionInfo.UpdatesSupported);
}
