# Distribution and In-App Updates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship ready-to-run `.exe` files from CI and let an installed CorePulse update itself.

**Architecture:** CI publishes two single-file binaries and stamps the build kind into the assembly, so the app knows which release asset matches it (and that a local source build must never self-update). The app checks the GitHub Releases API once a day, and on request downloads the matching asset, verifies its SHA256, and swaps its own `.exe` — possible because Windows permits renaming a running executable. The old file is deleted at the next startup, because deleting a running one is not permitted.

**Tech Stack:** C# / .NET 10, WinForms, `System.Text.Json`, `HttpClient`, GitHub Actions, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-16-distribution-auto-update-design.md`

## Global Constraints

- Target framework is `net10.0-windows10.0.17763.0`. Do not change it.
- **No new NuGet dependencies in the app project.** It has exactly one (`Microsoft.Toolkit.Uwp.Notifications`) and must keep exactly one. Test-project packages are fine.
- Code comments are in **Russian**; documentation, README, and UI English strings are in **English**. Match the surrounding file.
- Every user-facing string goes through `Loc.T(key)` and must exist in **all 8 languages**: English, Russian, German, Spanish, French, Portuguese, Chinese, Japanese.
- The repository is pinned: `https://api.github.com/repos/presetslrdev/CorePulse/releases/latest`. HTTPS only.
- Release asset names are a contract with the updater: `CorePulse.exe`, `CorePulse-net10.exe`, `SHA256SUMS.txt`.
- `DistributionKind` values are exactly `source`, `self-contained`, `framework`.
- No telemetry of any kind.
- The app must run with **no administrator rights**.
- **Kill any running `CpuMonitorNotifier.exe` before building** — a running instance locks the output file and the build fails with MSB3027.

---

### Task 1: Test project and version comparison

The repo has no tests. This task creates the test project and the first pure unit under test.

**Why version normalization matters:** the assembly version is `1.4.0.0` but a release tag parses as `1.4.0`, whose `Revision` is `-1`. `new Version(1,4,0) < new Version(1,4,0,0)` is **true**, so an unnormalized comparison reports the identical version as older and the app would offer an "update" to itself forever.

**Files:**
- Create: `tests/CorePulse.Tests/CorePulse.Tests.csproj`
- Create: `tests/CorePulse.Tests/UpdateVersionsTests.cs`
- Create: `src/CpuMonitorNotifier/Update/UpdateVersions.cs`
- Modify: `src/CpuMonitorNotifier/CpuMonitorNotifier.csproj`

**Interfaces:**
- Produces: `CpuMonitorNotifier.Update.UpdateVersions` with `static Version Normalize(Version)`, `static Version? ParseTag(string?)`, `static bool IsNewer(Version current, Version candidate)`, `static Version Current { get; }`

- [ ] **Step 1: Create the test project**

Create `tests/CorePulse.Tests/CorePulse.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.17763.0</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CpuMonitorNotifier\CpuMonitorNotifier.csproj" />
  </ItemGroup>

</Project>
```

`UseWindowsForms` is required: the referenced project is a WinForms app, and the test project must target the same framework flavour to reference it.

- [ ] **Step 2: Grant the test project access to internals**

Every type in the app is `internal`. In `src/CpuMonitorNotifier/CpuMonitorNotifier.csproj`, add a new `ItemGroup` after the existing `PackageReference` one:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="CorePulse.Tests" />
  </ItemGroup>
```

- [ ] **Step 3: Write the failing tests**

Create `tests/CorePulse.Tests/UpdateVersionsTests.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/CorePulse.Tests`
Expected: FAIL — `error CS0234: The type or namespace name 'Update' does not exist in the namespace 'CpuMonitorNotifier'`

- [ ] **Step 5: Write the implementation**

Create `src/CpuMonitorNotifier/Update/UpdateVersions.cs`:

```csharp
using System.Reflection;

namespace CpuMonitorNotifier.Update;

/// <summary>
/// Версии для обновления. Тег релиза («1.5.0») и версия сборки («1.5.0.0») должны сравниваться
/// как равные, поэтому обе приводятся к Major.Minor.Build.
/// </summary>
internal static class UpdateVersions
{
    /// <summary>Версия текущей сборки.</summary>
    public static Version Current { get; } =
        Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    /// <summary>Приводит версию к Major.Minor.Build; неопределённые компоненты (-1) становятся нулями.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>Разбирает тег релиза вида «v1.5.0» или «1.5.0». null — если это не версия.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        string s = tag.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];

        return Version.TryParse(s, out var v) ? Normalize(v) : null;
    }

    /// <summary>Обновляемся только на строго большую версию — это же защищает от отката при ошибочном теге.</summary>
    public static bool IsNewer(Version current, Version candidate) =>
        Normalize(candidate) > Normalize(current);
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/CorePulse.Tests`
Expected: PASS — 16 tests passed (11 of them are `[InlineData]` cases)

- [ ] **Step 7: Commit**

```bash
git add tests/CorePulse.Tests src/CpuMonitorNotifier/Update/UpdateVersions.cs src/CpuMonitorNotifier/CpuMonitorNotifier.csproj
git commit -m "test: add test project; feat: version comparison for updates"
```

---

### Task 2: Distribution stamping

The updater must fetch the same *kind* of build that is running, and must never update a developer's local build. The build states its kind rather than the app guessing.

**Files:**
- Create: `src/CpuMonitorNotifier/Update/DistributionInfo.cs`
- Create: `tests/CorePulse.Tests/DistributionInfoTests.cs`
- Modify: `src/CpuMonitorNotifier/CpuMonitorNotifier.csproj`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `CpuMonitorNotifier.Update.DistributionKind` (enum `Source`, `SelfContained`, `Framework`); `DistributionInfo` with `static DistributionKind Current { get; }`, `static bool UpdatesSupported { get; }`, `static string? CurrentAssetName { get; }`, `static string? AssetNameFor(DistributionKind)`, `static DistributionKind Parse(string?)`, consts `SelfContainedAsset`, `FrameworkAsset`.

- [ ] **Step 1: Add the build stamp**

In `src/CpuMonitorNotifier/CpuMonitorNotifier.csproj`, add to the existing `PropertyGroup` (after `<Version>`):

```xml
    <!-- CI переопределяет: -p:DistributionKind=self-contained|framework -->
    <DistributionKind Condition="'$(DistributionKind)' == ''">source</DistributionKind>
```

And add a new `ItemGroup`:

```xml
  <ItemGroup>
    <AssemblyMetadata Include="DistributionKind" Value="$(DistributionKind)" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing tests**

Create `tests/CorePulse.Tests/DistributionInfoTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/CorePulse.Tests --filter DistributionInfoTests`
Expected: FAIL — `error CS0246: The type or namespace name 'DistributionKind' could not be found`

- [ ] **Step 4: Write the implementation**

Create `src/CpuMonitorNotifier/Update/DistributionInfo.cs`:

```csharp
using System.Reflection;

namespace CpuMonitorNotifier.Update;

/// <summary>Вид сборки. Задаётся при публикации через MSBuild-свойство DistributionKind.</summary>
internal enum DistributionKind
{
    /// <summary>Локальная сборка из исходников — не обновляется.</summary>
    Source,
    SelfContained,
    Framework,
}

/// <summary>
/// Вид сборки впечатывается в саму сборку через AssemblyMetadata, поэтому апдейтеру не нужно
/// угадывать, какой из двух ассетов релиза сейчас запущен.
/// </summary>
internal static class DistributionInfo
{
    public const string SelfContainedAsset = "CorePulse.exe";
    public const string FrameworkAsset = "CorePulse-net10.exe";

    public static DistributionKind Current { get; } = Parse(ReadStamp(Assembly.GetExecutingAssembly()));

    /// <summary>Сборку из исходников подменять нельзя: под разработчиком её собирает msbuild.</summary>
    public static bool UpdatesSupported => Current != DistributionKind.Source;

    /// <summary>Имя ассета в релизе для текущей сборки; null — обновление не поддерживается.</summary>
    public static string? CurrentAssetName => AssetNameFor(Current);

    public static string? AssetNameFor(DistributionKind kind) => kind switch
    {
        DistributionKind.SelfContained => SelfContainedAsset,
        DistributionKind.Framework => FrameworkAsset,
        _ => null,
    };

    /// <summary>Неизвестная или отсутствующая метка трактуется как source — безопасный вариант.</summary>
    public static DistributionKind Parse(string? stamp) => stamp switch
    {
        "self-contained" => DistributionKind.SelfContained,
        "framework" => DistributionKind.Framework,
        _ => DistributionKind.Source,
    };

    private static string? ReadStamp(Assembly asm) => asm
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "DistributionKind")?.Value;
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/CorePulse.Tests --filter DistributionInfoTests`
Expected: PASS — 11 tests passed

> **C# constraint, learned the hard way.** Internal types must not appear in the *signature* of a
> public test method — `[Theory] public void T(DistributionKind k)` fails with **CS0051** even with
> `InternalsVisibleTo`, because the attribute grants access without raising the type's accessibility,
> and xUnit requires public test classes. Using the internal type inside a method *body* is fine.
> This applies to every later task that tests an internal enum or record.

- [ ] **Step 6: Verify the CI override works**

Run: `dotnet build src/CpuMonitorNotifier -p:DistributionKind=self-contained`
Expected: build succeeds. This proves the property is accepted; the stamped value is exercised end-to-end in Task 9.

- [ ] **Step 7: Commit**

```bash
git add src/CpuMonitorNotifier/Update/DistributionInfo.cs tests/CorePulse.Tests/DistributionInfoTests.cs src/CpuMonitorNotifier/CpuMonitorNotifier.csproj
git commit -m "feat: stamp distribution kind into the assembly"
```

---

### Task 3: GitHub release parsing and hashing

Parsing is separated from the network call so it can be tested against real-shaped JSON without touching the internet.

**Files:**
- Create: `src/CpuMonitorNotifier/Update/ReleaseInfo.cs`
- Create: `src/CpuMonitorNotifier/Update/FileHash.cs`
- Create: `src/CpuMonitorNotifier/Update/GitHubReleases.cs`
- Create: `tests/CorePulse.Tests/GitHubReleasesTests.cs`
- Create: `tests/CorePulse.Tests/FileHashTests.cs`

**Interfaces:**
- Consumes: `UpdateVersions.ParseTag`, `UpdateVersions.Current` (Task 1).
- Produces: `ReleaseInfo(Version Version, string AssetUrl, string Sha256, string PageUrl)`; `ParsedRelease(Version Version, string AssetUrl, string SumsUrl, string PageUrl)`; `GitHubReleases.ParseLatest(string json, string assetName) → ParsedRelease?`; `GitHubReleases.ParseSha256Sums(string text, string fileName) → string?`; `GitHubReleases.FetchLatestAsync(string assetName, CancellationToken) → Task<ReleaseInfo?>`; `GitHubReleases.CreateClient() → HttpClient`; `FileHash.Sha256Async(string path, CancellationToken) → Task<string>`.

- [ ] **Step 1: Write the failing tests**

Create `tests/CorePulse.Tests/FileHashTests.cs`:

```csharp
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
```

Create `tests/CorePulse.Tests/GitHubReleasesTests.cs`:

```csharp
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

    [Fact]
    public void ParseSha256Sums_IgnoresRightLengthButNonHexToken()
        => Assert.Null(GitHubReleases.ParseSha256Sums(new string('z', 64) + "  CorePulse.exe\n", "CorePulse.exe"));

    [Fact]
    public async Task FetchLatestAsync_LetsRealCancellationThrough()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        // отмена — команда вызывающего, а не сбой проверки: она обязана дойти наружу
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => GitHubReleases.FetchLatestAsync("CorePulse.exe", cts.Token));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CorePulse.Tests --filter "GitHubReleasesTests|FileHashTests"`
Expected: FAIL — `error CS0103: The name 'GitHubReleases' does not exist`

- [ ] **Step 3: Write ReleaseInfo**

Create `src/CpuMonitorNotifier/Update/ReleaseInfo.cs`:

```csharp
namespace CpuMonitorNotifier.Update;

/// <summary>Готовый к использованию релиз: что качать, чем проверить, куда отправить пользователя.</summary>
internal sealed record ReleaseInfo(Version Version, string AssetUrl, string Sha256, string PageUrl);

/// <summary>Промежуточный разбор ответа GitHub: хеш ещё не загружен, известна лишь ссылка на SHA256SUMS.txt.</summary>
internal sealed record ParsedRelease(Version Version, string AssetUrl, string SumsUrl, string PageUrl);
```

- [ ] **Step 4: Write FileHash**

Create `src/CpuMonitorNotifier/Update/FileHash.cs`:

```csharp
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
```

- [ ] **Step 5: Write GitHubReleases**

Create `src/CpuMonitorNotifier/Update/GitHubReleases.cs`:

```csharp
using System.Text.Json;

namespace CpuMonitorNotifier.Update;

/// <summary>Чтение последнего релиза с GitHub. Разбор отделён от сети, чтобы его можно было тестировать.</summary>
internal static class GitHubReleases
{
    private const string LatestUrl = "https://api.github.com/repos/presetslrdev/CorePulse/releases/latest";
    private const string SumsAsset = "SHA256SUMS.txt";
    private const int Sha256HexLength = 64;

    /// <summary>
    /// Запрашивает последний релиз. null — офлайн, лимит запросов, релиз без нужных ассетов:
    /// неудачная проверка не является событием и никогда не показывается пользователю сама по себе.
    /// </summary>
    public static async Task<ReleaseInfo?> FetchLatestAsync(string assetName, CancellationToken ct)
    {
        try
        {
            using var http = CreateClient();
            var parsed = ParseLatest(await http.GetStringAsync(LatestUrl, ct), assetName);
            if (parsed is null) return null;

            string? hash = ParseSha256Sums(await http.GetStringAsync(parsed.SumsUrl, ct), assetName);
            return hash is null ? null : new ReleaseInfo(parsed.Version, parsed.AssetUrl, hash, parsed.PageUrl);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // нас отменили — это не сбой проверки, глушить нельзя
        }
        catch (Exception)
        {
            // офлайн, лимит запросов, битый релиз, таймаут HttpClient (тоже TaskCanceledException,
            // но без запроса отмены) — всё это сбои: проверка молча не состоялась
            return null;
        }
    }

    public static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) }; // self-contained ассет ~70 МБ
        // без User-Agent GitHub отвечает 403
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"CorePulse/{UpdateVersions.Current}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>Разбирает ответ /releases/latest. null — если тег не версия или нужных ассетов нет.</summary>
    public static ParsedRelease? ParseLatest(string json, string assetName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var version = UpdateVersions.ParseTag(GetString(root, "tag_name"));
            if (version is null) return null;

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            string? assetUrl = null, sumsUrl = null;
            foreach (var a in assets.EnumerateArray())
            {
                string? name = GetString(a, "name");
                string? url = GetString(a, "browser_download_url");
                if (name is null || url is null) continue;
                if (name == assetName) assetUrl = url;
                else if (name == SumsAsset) sumsUrl = url;
            }

            // без файла хешей обновляться нечем проверить — считаем релиз непригодным
            if (assetUrl is null || sumsUrl is null) return null;

            return new ParsedRelease(version, assetUrl, sumsUrl, GetString(root, "html_url") ?? "");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Достаёт хеш файла из SHA256SUMS.txt (формат coreutils: «&lt;hex&gt;␠␠&lt;имя&gt;»).</summary>
    public static string? ParseSha256Sums(string text, string fileName)
    {
        foreach (string line in text.Split('\n'))
        {
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !IsSha256Hex(parts[0])) continue;

            string name = parts[^1].TrimStart('*'); // coreutils помечает двоичный режим звёздочкой
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                return parts[0].ToLowerInvariant();
        }
        return null;
    }

    /// <summary>Ровно 64 шестнадцатеричных символа — иначе это не строка хеша, а что-то ещё.</summary>
    private static bool IsSha256Hex(string s)
    {
        if (s.Length != Sha256HexLength) return false;
        foreach (char c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    private static string? GetString(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/CorePulse.Tests --filter "GitHubReleasesTests|FileHashTests"`
Expected: PASS — 16 tests passed

> **Why `FetchLatestAsync` does not swallow cancellation.** The null-on-failure contract covers
> *failures* — offline, rate limit, a malformed release. Caller-initiated cancellation is not a
> failure, and reporting it as "no update available" would hide that the check never ran. The subtlety
> is that `HttpClient`'s own **timeout** also surfaces as `TaskCanceledException`, and that one *is* a
> failure — so the filter is `when (ct.IsCancellationRequested)`, which is true only for real
> cancellation. `FetchLatestAsync_LetsRealCancellationThrough` pins this; it makes no network call
> because a pre-cancelled token throws before the request is sent.

- [ ] **Step 7: Commit**

```bash
git add src/CpuMonitorNotifier/Update tests/CorePulse.Tests
git commit -m "feat: parse GitHub releases and hash files for update verification"
```

---

### Task 4: UpdateService — check and download

**Design note:** `CheckAsync` returns a status rather than a nullable release, because "up to date" and "the check failed" must be distinguishable: a manual check reports both, an automatic one reports neither.

**Files:**
- Create: `src/CpuMonitorNotifier/Update/UpdateService.cs`
- Create: `tests/CorePulse.Tests/UpdateServiceTests.cs`

**Interfaces:**
- Consumes: `DistributionInfo.CurrentAssetName` (Task 2); `GitHubReleases.FetchLatestAsync`, `GitHubReleases.CreateClient`, `FileHash.Sha256Async`, `ReleaseInfo` (Task 3); `UpdateVersions.IsNewer`, `UpdateVersions.Current` (Task 1).
- Produces: `UpdateCheckStatus` (enum `UpToDate`, `UpdateAvailable`, `Failed`, `NotSupported`); `UpdateCheckResult(UpdateCheckStatus Status, ReleaseInfo? Release)`; `UpdateService` with `Task<UpdateCheckResult> CheckAsync(CancellationToken)`, `Task<string> DownloadAsync(ReleaseInfo, CancellationToken)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/CorePulse.Tests/UpdateServiceTests.cs`:

```csharp
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
```

> `HttpClient` does not support `file://`. `UpdateService.DownloadAsync` must therefore open the asset through a small seam that handles both — see the implementation's `OpenAssetAsync`. This is not test-only scaffolding: it keeps the download path exercised by tests without a network.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CorePulse.Tests --filter UpdateServiceTests`
Expected: FAIL — `error CS0246: The type or namespace name 'UpdateService' could not be found`

- [ ] **Step 3: Write the implementation**

Create `src/CpuMonitorNotifier/Update/UpdateService.cs`:

```csharp
namespace CpuMonitorNotifier.Update;

internal enum UpdateCheckStatus
{
    /// <summary>Установлена последняя версия.</summary>
    UpToDate,
    UpdateAvailable,
    /// <summary>Проверить не удалось (офлайн, лимит запросов, битый релиз).</summary>
    Failed,
    /// <summary>Сборка из исходников — обновление не поддерживается.</summary>
    NotSupported,
}

/// <summary>«Нет обновления» и «проверка не удалась» — разные исходы: ручная проверка сообщает оба, автоматическая — ни одного.</summary>
internal sealed record UpdateCheckResult(UpdateCheckStatus Status, ReleaseInfo? Release);

/// <summary>Проверка обновлений и загрузка нового файла с проверкой хеша.</summary>
internal sealed class UpdateService
{
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        string? asset = DistributionInfo.CurrentAssetName;
        if (asset is null)
            return new UpdateCheckResult(UpdateCheckStatus.NotSupported, null);

        var release = await GitHubReleases.FetchLatestAsync(asset, ct);
        if (release is null)
            return new UpdateCheckResult(UpdateCheckStatus.Failed, null);

        return UpdateVersions.IsNewer(UpdateVersions.Current, release.Version)
            ? new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, release)
            : new UpdateCheckResult(UpdateCheckStatus.UpToDate, null);
    }

    /// <summary>
    /// Качает ассет во временный файл и сверяет SHA256. Несовпадение — исключение, временный файл удалён.
    /// Проверка выполняется до того, как что-либо на диске будет тронуто.
    /// </summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken ct = default)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"CorePulse-{release.Version}-{Guid.NewGuid():N}.exe");
        try
        {
            await using (var src = await OpenAssetAsync(release.AssetUrl, ct))
            await using (var dst = File.Create(temp))
                await src.CopyToAsync(dst, ct);

            string actual = await FileHash.Sha256Async(temp, ct);
            if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA256 mismatch: expected {release.Sha256}, got {actual}");

            return temp;
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>HttpClient не умеет file://; локальный путь читаем напрямую.</summary>
    private static async Task<Stream> OpenAssetAsync(string url, CancellationToken ct)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile)
            return File.OpenRead(uri.LocalPath);

        var http = GitHubReleases.CreateClient();
        HttpResponseMessage? response = null;
        try
        {
            response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            return new HttpOwnedStream(await response.Content.ReadAsStreamAsync(ct), http, response);
        }
        catch
        {
            // EnsureSuccessStatusCode бросает уже после того, как ответ получен: освободить надо оба,
            // Dispose у HttpClient не закрывает выданные им ответы
            response?.Dispose();
            http.Dispose();
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* временный файл — не критично, ОС уберёт */ }
    }

    /// <summary>Держит HttpClient и ответ живыми, пока читается поток тела.</summary>
    private sealed class HttpOwnedStream(Stream inner, HttpClient client, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        // поток ответа последовательный: длина неизвестна при chunked-передаче, врать про неё нельзя
        public override long Length => throw new NotSupportedException();
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => inner.ReadAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
                client.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/CorePulse.Tests --filter UpdateServiceTests`
Expected: PASS — 3 tests passed

- [ ] **Step 5: Commit**

```bash
git add src/CpuMonitorNotifier/Update/UpdateService.cs tests/CorePulse.Tests/UpdateServiceTests.cs
git commit -m "feat: update check with distinct statuses and verified download"
```

---

### Task 5: UpdateInstaller — the swap

**Verified Windows behavior this rests on** (tested against a running process on Windows 11 26200):

| Operation on a running `.exe` | Result |
|---|---|
| Rename it | Allowed |
| Write a new file at the freed path | Allowed |
| Delete it while the process lives | Denied (`UnauthorizedAccessException`) |
| Delete it after the process exits | Allowed |

Renaming being allowed is why no separate updater process is needed. Deleting being denied is why cleanup is deferred to the next startup.

`SwapFiles` takes explicit paths so it can be tested with plain files — no real executable needed.

**Files:**
- Create: `src/CpuMonitorNotifier/Update/UpdateInstaller.cs`
- Create: `tests/CorePulse.Tests/UpdateInstallerTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `UpdateInstaller` with `static string CurrentExePath { get; }`, `static bool CanSwap()`, `static bool IsDirectoryWritable(string)`, `static string OldPathFor(string)`, `static void SwapFiles(string exePath, string newFile)`, `static void ApplyAndRestart(string newFile)`, `static void CleanupOldFile()`.

- [ ] **Step 1: Write the failing tests**

Create `tests/CorePulse.Tests/UpdateInstallerTests.cs`:

```csharp
using CpuMonitorNotifier.Update;
using Xunit;

namespace CorePulse.Tests;

public class UpdateInstallerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("corepulse-swap").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void OldPathFor_SitsNextToTheExe()
        => Assert.Equal(@"C:\apps\CorePulse.old.exe", UpdateInstaller.OldPathFor(@"C:\apps\CorePulse.exe"));

    [Fact]
    public void IsDirectoryWritable_TrueForWritableDirectory()
        => Assert.True(UpdateInstaller.IsDirectoryWritable(_dir));

    [Fact]
    public void IsDirectoryWritable_FalseForMissingDirectory()
        => Assert.False(UpdateInstaller.IsDirectoryWritable(Path.Combine(_dir, "does-not-exist")));

    [Fact]
    public void IsDirectoryWritable_LeavesNoProbeFileBehind()
    {
        UpdateInstaller.IsDirectoryWritable(_dir);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void SwapFiles_PutsNewBinaryInPlaceAndKeepsTheOldOne()
    {
        string exe = Path.Combine(_dir, "CorePulse.exe");
        string incoming = Path.Combine(_dir, "incoming.tmp");
        File.WriteAllText(exe, "old");
        File.WriteAllText(incoming, "new");

        UpdateInstaller.SwapFiles(exe, incoming);

        Assert.Equal("new", File.ReadAllText(exe));
        Assert.Equal("old", File.ReadAllText(UpdateInstaller.OldPathFor(exe)));
        Assert.False(File.Exists(incoming));
    }

    [Fact]
    public void SwapFiles_RollsBackWhenTheNewFileCannotBeMoved()
    {
        string exe = Path.Combine(_dir, "CorePulse.exe");
        File.WriteAllText(exe, "old");
        string missing = Path.Combine(_dir, "never-downloaded.tmp");

        Assert.ThrowsAny<IOException>(() => UpdateInstaller.SwapFiles(exe, missing));

        // установка цела: .exe на месте и с прежним содержимым, следов подмены нет
        Assert.True(File.Exists(exe));
        Assert.Equal("old", File.ReadAllText(exe));
        Assert.False(File.Exists(UpdateInstaller.OldPathFor(exe)));
    }

    [Fact]
    public void SwapFiles_ReplacesLeftoverOldFileFromAPreviousUpdate()
    {
        string exe = Path.Combine(_dir, "CorePulse.exe");
        string incoming = Path.Combine(_dir, "incoming.tmp");
        File.WriteAllText(exe, "v2");
        File.WriteAllText(incoming, "v3");
        File.WriteAllText(UpdateInstaller.OldPathFor(exe), "v1");

        UpdateInstaller.SwapFiles(exe, incoming);

        Assert.Equal("v3", File.ReadAllText(exe));
        Assert.Equal("v2", File.ReadAllText(UpdateInstaller.OldPathFor(exe)));
    }

    [Fact]
    public void CurrentExePath_IsAnExistingFile()
        => Assert.True(File.Exists(UpdateInstaller.CurrentExePath));

    [Fact]
    public void Rollback_SucceedsAndRestoresTheOriginal()
    {
        string exe = Path.Combine(_dir, "CorePulse.exe");
        string old = UpdateInstaller.OldPathFor(exe);
        File.WriteAllText(old, "original");

        UpdateInstaller.Rollback(old, exe, new IOException("swap failed"));

        Assert.Equal("original", File.ReadAllText(exe));
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void Rollback_WhenItCannotRestore_SaysWhereTheWorkingBinaryIs()
    {
        string exe = Path.Combine(_dir, "CorePulse.exe");
        string old = UpdateInstaller.OldPathFor(exe);
        File.WriteAllText(old, "original");
        Directory.CreateDirectory(exe); // каталог на месте .exe — переименовать поверх него нельзя

        var cause = new IOException("swap failed");
        var ex = Assert.Throws<IOException>(() => UpdateInstaller.Rollback(old, exe, cause));

        // пользователь не должен остаться наедине с пустой папкой: путь к рабочему файлу — в сообщении
        Assert.Contains(old, ex.Message);
        Assert.Contains(cause, Assert.IsType<AggregateException>(ex.InnerException).InnerExceptions);
        Assert.True(File.Exists(old), "the working binary must still exist for the user to restore");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CorePulse.Tests --filter UpdateInstallerTests`
Expected: FAIL — `error CS0246: The type or namespace name 'UpdateInstaller' could not be found`

- [ ] **Step 3: Write the implementation**

Create `src/CpuMonitorNotifier/Update/UpdateInstaller.cs`:

```csharp
using System.Diagnostics;

namespace CpuMonitorNotifier.Update;

/// <summary>
/// Подмена собственного .exe. Опирается на поведение Windows: работающий файл разрешено
/// переименовать, но запрещено удалять — поэтому отдельный процесс-апдейтер не нужен,
/// а старый файл убирается при следующем запуске.
/// </summary>
internal static class UpdateInstaller
{
    private const string OldSuffix = ".old.exe";
    private const int RollbackAttempts = 3;
    private const int RollbackDelayMs = 150;

    public static string CurrentExePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

    /// <summary>Подменять можно, только если каталог с .exe доступен на запись (иначе — Program Files и т.п.).</summary>
    public static bool CanSwap()
    {
        string? dir = Path.GetDirectoryName(CurrentExePath);
        // без каталога проверять нечего: пустая строка увела бы проверку на текущий рабочий каталог
        return dir is not null && IsDirectoryWritable(dir);
    }

    public static bool IsDirectoryWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".corepulse-write-probe-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Путь, куда уезжает прежний .exe: CorePulse.exe → CorePulse.old.exe.</summary>
    public static string OldPathFor(string exePath) => Path.ChangeExtension(exePath, null) + OldSuffix;

    /// <summary>
    /// Переименовывает текущий .exe в *.old.exe и ставит на его место новый файл.
    /// Если второй шаг не удался — возвращает прежнее имя, установка остаётся рабочей.
    /// </summary>
    public static void SwapFiles(string exePath, string newFile)
    {
        string old = OldPathFor(exePath);
        TryDelete(old); // остаток прошлого обновления, если его не удалось убрать при старте

        File.Move(exePath, old);
        try
        {
            File.Move(newFile, exePath);
        }
        catch (Exception swapFailure)
        {
            Rollback(old, exePath, swapFailure);
            throw; // откат удался — наружу уходит исходная причина
        }
    }

    /// <summary>
    /// Возвращает прежний .exe на место. Единственный момент, когда пользователь может остаться
    /// вообще без файла, поэтому промах здесь важнее исходной ошибки: типовая причина —
    /// антивирус, открывший только что переименованный файл, и он обычно отпускает. Если не
    /// отпустил — говорим прямо, где лежит рабочий файл, вместо молчаливого исчезновения.
    /// </summary>
    internal static void Rollback(string old, string exePath, Exception cause)
    {
        for (int attempt = 1; attempt <= RollbackAttempts; attempt++)
        {
            try
            {
                File.Move(old, exePath);
                return;
            }
            catch (Exception rollbackFailure)
            {
                if (attempt == RollbackAttempts)
                    throw new IOException(
                        $"Update failed and the original could not be restored automatically. " +
                        $"Your working CorePulse is at '{old}' — rename it back to '{exePath}'.",
                        new AggregateException(cause, rollbackFailure));

                Thread.Sleep(RollbackDelayMs);
            }
        }
    }

    /// <summary>Подменяет .exe и перезапускает приложение. Вызывать из UI-потока.</summary>
    public static void ApplyAndRestart(string newFile)
    {
        string exe = CurrentExePath;
        SwapFiles(exe, newFile);

        // новый экземпляр стартует, пока этот ещё жив, — он подождёт выхода по --updated
        Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Arguments = $"--updated {Environment.ProcessId}",
        });

        Application.Exit();
    }

    /// <summary>Убирает остаток прошлого обновления. Раньше этого момента удалить файл было нельзя.</summary>
    public static void CleanupOldFile() => TryDelete(OldPathFor(CurrentExePath));

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* файл ещё занят — попробуем при следующем запуске */ }
        catch (UnauthorizedAccessException) { /* нет прав на удаление — не критично */ }
    }
}
```

> **Why the rollback needs its own retry.** The first draft of this task did `catch { File.Move(old, exePath); throw; }`. If *that* move failed too, the exception escaped with **no file at the original path** — the exact outcome this whole design exists to prevent, reachable through a second, independent failure. The realistic cause is transient (an antivirus opening the just-renamed file), so a few short retries clear it; and if they don't, the user is told where their working binary is rather than being left to discover an empty folder.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/CorePulse.Tests --filter UpdateInstallerTests`
Expected: PASS — 10 tests passed (the failing-rollback test takes ~300 ms: it exhausts the retries)

- [ ] **Step 5: Commit**

```bash
git add src/CpuMonitorNotifier/Update/UpdateInstaller.cs tests/CorePulse.Tests/UpdateInstallerTests.cs
git commit -m "feat: swap the running exe in place, with rollback"
```

---

### Task 6: Startup handoff after an update

**Why this exists:** `Program.Main` holds a single-instance mutex. After `ApplyAndRestart`, the new process starts while the old one is still alive, so without a handoff the new instance sees the mutex held and returns silently — leaving the user with **nothing running** after clicking Update.

**Files:**
- Modify: `src/CpuMonitorNotifier/Program.cs`

**Interfaces:**
- Consumes: `UpdateInstaller.CleanupOldFile()` (Task 5).
- Produces: `Main(string[] args)` honouring `--updated <pid>`.

- [ ] **Step 1: Rewrite Program.cs**

Replace the entire contents of `src/CpuMonitorNotifier/Program.cs`:

```csharp
using System.Diagnostics;
using CpuMonitorNotifier.Localization;
using CpuMonitorNotifier.Settings;
using CpuMonitorNotifier.Theming;
using CpuMonitorNotifier.Update;

namespace CpuMonitorNotifier;

internal static class Program
{
    private const string UpdatedFlag = "--updated";
    private const int OldProcessExitTimeoutMs = 10_000;

    [STAThread]
    private static void Main(string[] args)
    {
        WaitForReplacedProcess(args); // до мьютекса: старый экземпляр ещё держит его

        using var mutex = new Mutex(initiallyOwned: true, "CpuMonitorNotifier_SingleInstance", out bool createdNew);
        if (!createdNew)
            return; // уже запущен — вторую иконку не создаём

        UpdateInstaller.CleanupOldFile(); // остаток прошлого обновления: раньше файл был занят

        var startup = AppSettings.Load();
        Loc.Apply(startup.Language); // локализуем в т.ч. возможное сообщение об ошибке

        ApplicationConfiguration.Initialize();
        ThemeManager.Apply(startup.Theme); // до создания окон
        try
        {
            Application.Run(new App.TrayAppContext());
        }
        catch (Exception ex)
        {
            // типовой случай — отключённые/повреждённые счётчики производительности (lodctr /R лечит)
            MessageBox.Show(
                string.Format(Loc.T("error.startFailed"), ex.Message),
                Loc.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// После самообновления новый экземпляр стартует, пока старый ещё жив. Без ожидания он увидел бы
    /// занятый мьютекс и молча вышел — пользователь остался бы вообще без приложения.
    /// </summary>
    private static void WaitForReplacedProcess(string[] args)
    {
        int i = Array.IndexOf(args, UpdatedFlag);
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out int pid))
            return;

        try
        {
            using var old = Process.GetProcessById(pid);
            old.WaitForExit(OldProcessExitTimeoutMs);
        }
        catch (ArgumentException)
        {
            // процесс уже завершился — ждать нечего
        }
    }
}
```

- [ ] **Step 2: Build and smoke test**

```bash
taskkill //F //IM CpuMonitorNotifier.exe 2>/dev/null || true
dotnet build src/CpuMonitorNotifier --no-incremental
```

Expected: `0 Error(s)`, `0 Warning(s)`

Then run the app once and confirm the tray icon appears and the app still starts normally with no arguments:

```bash
dotnet run --project src/CpuMonitorNotifier &
sleep 5
tasklist //FI "IMAGENAME eq CpuMonitorNotifier.exe" | grep -q CpuMonitorNotifier && echo "ALIVE"
taskkill //F //IM CpuMonitorNotifier.exe
```

Expected: `ALIVE`

- [ ] **Step 3: Commit**

```bash
git add src/CpuMonitorNotifier/Program.cs
git commit -m "feat: hand off the single-instance mutex after a self-update"
```

---

### Task 7: Settings, strings, and the settings checkbox

**Files:**
- Modify: `src/CpuMonitorNotifier/Settings/AppSettings.cs`
- Modify: `src/CpuMonitorNotifier/Localization/Localization.cs`
- Modify: `src/CpuMonitorNotifier/Settings/SettingsForm.cs`

**Interfaces:**
- Consumes: `DistributionInfo.UpdatesSupported` (Task 2).
- Produces: `AppSettings.UpdateCheckEnabled` (bool, default `true`), `AppSettings.LastUpdateCheckUtc` (DateTime, default `DateTime.MinValue`); localization keys `menu.checkUpdates`, `settings.updates`, `update.available`, `update.available.body`, `update.button.update`, `update.button.notes`, `update.downloading`, `update.upToDate`, `update.failed`.

> **Deliberate deviation from the spec.** The spec lists seven keys; this plan adds three. A toast needs
> two lines of text, so `update.available` (title) needs `update.available.body` alongside it.
> `update.downloading` exists because the self-contained asset is ~70 MB: without it, clicking Update
> looks like nothing happened, and the user clicks again. `update.failed.network` (added in Task 8)
> fills the `{0}` of `update.failed` when the check itself could not reach GitHub.

- [ ] **Step 1: Add the settings fields**

In `src/CpuMonitorNotifier/Settings/AppSettings.cs`, after the `ExcludedProcesses` property:

```csharp
    // Обновления: единственный сетевой запрос приложения — GET к api.github.com, без телеметрии.
    public bool UpdateCheckEnabled { get; set; } = true;
    public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
```

- [ ] **Step 2: Add the strings to all 8 languages**

In `src/CpuMonitorNotifier/Localization/Localization.cs`, insert the matching block into **each** dictionary immediately **before** its `["error.startFailed"]` entry. Every dictionary ends with that key, which makes it an unambiguous anchor.

English:

```csharp
        ["menu.checkUpdates"] = "Check for updates…",
        ["settings.updates"] = "Check for updates automatically",
        ["update.available"] = "CorePulse {0} is available",
        ["update.available.body"] = "You have {0}. Updating takes a moment and restarts the app.",
        ["update.button.update"] = "Update",
        ["update.button.notes"] = "What's new",
        ["update.downloading"] = "Downloading CorePulse {0}…",
        ["update.upToDate"] = "You're up to date ({0}).",
        ["update.failed"] = "Update failed: {0}",
```

Russian:

```csharp
        ["menu.checkUpdates"] = "Проверить обновления…",
        ["settings.updates"] = "Проверять обновления автоматически",
        ["update.available"] = "Доступен CorePulse {0}",
        ["update.available.body"] = "У вас {0}. Обновление займёт несколько секунд и перезапустит приложение.",
        ["update.button.update"] = "Обновить",
        ["update.button.notes"] = "Что нового",
        ["update.downloading"] = "Загрузка CorePulse {0}…",
        ["update.upToDate"] = "У вас последняя версия ({0}).",
        ["update.failed"] = "Не удалось обновить: {0}",
```

German:

```csharp
        ["menu.checkUpdates"] = "Nach Updates suchen…",
        ["settings.updates"] = "Automatisch nach Updates suchen",
        ["update.available"] = "CorePulse {0} ist verfügbar",
        ["update.available.body"] = "Sie haben {0}. Das Update dauert einen Moment und startet die App neu.",
        ["update.button.update"] = "Aktualisieren",
        ["update.button.notes"] = "Neuerungen",
        ["update.downloading"] = "CorePulse {0} wird heruntergeladen…",
        ["update.upToDate"] = "Sie sind auf dem neuesten Stand ({0}).",
        ["update.failed"] = "Update fehlgeschlagen: {0}",
```

Spanish:

```csharp
        ["menu.checkUpdates"] = "Buscar actualizaciones…",
        ["settings.updates"] = "Buscar actualizaciones automáticamente",
        ["update.available"] = "CorePulse {0} está disponible",
        ["update.available.body"] = "Tienes {0}. La actualización tarda un momento y reinicia la aplicación.",
        ["update.button.update"] = "Actualizar",
        ["update.button.notes"] = "Novedades",
        ["update.downloading"] = "Descargando CorePulse {0}…",
        ["update.upToDate"] = "Estás al día ({0}).",
        ["update.failed"] = "Error al actualizar: {0}",
```

French:

```csharp
        ["menu.checkUpdates"] = "Rechercher des mises à jour…",
        ["settings.updates"] = "Rechercher les mises à jour automatiquement",
        ["update.available"] = "CorePulse {0} est disponible",
        ["update.available.body"] = "Vous avez {0}. La mise à jour prend un instant et redémarre l'application.",
        ["update.button.update"] = "Mettre à jour",
        ["update.button.notes"] = "Nouveautés",
        ["update.downloading"] = "Téléchargement de CorePulse {0}…",
        ["update.upToDate"] = "Vous êtes à jour ({0}).",
        ["update.failed"] = "Échec de la mise à jour : {0}",
```

Portuguese:

```csharp
        ["menu.checkUpdates"] = "Procurar atualizações…",
        ["settings.updates"] = "Procurar atualizações automaticamente",
        ["update.available"] = "CorePulse {0} está disponível",
        ["update.available.body"] = "Você tem {0}. A atualização leva um momento e reinicia o aplicativo.",
        ["update.button.update"] = "Atualizar",
        ["update.button.notes"] = "Novidades",
        ["update.downloading"] = "Baixando CorePulse {0}…",
        ["update.upToDate"] = "Você está atualizado ({0}).",
        ["update.failed"] = "Falha na atualização: {0}",
```

Chinese:

```csharp
        ["menu.checkUpdates"] = "检查更新…",
        ["settings.updates"] = "自动检查更新",
        ["update.available"] = "CorePulse {0} 可用",
        ["update.available.body"] = "当前版本 {0}。更新只需片刻，应用将重新启动。",
        ["update.button.update"] = "更新",
        ["update.button.notes"] = "更新内容",
        ["update.downloading"] = "正在下载 CorePulse {0}…",
        ["update.upToDate"] = "已是最新版本（{0}）。",
        ["update.failed"] = "更新失败：{0}",
```

Japanese:

```csharp
        ["menu.checkUpdates"] = "更新を確認…",
        ["settings.updates"] = "自動的に更新を確認する",
        ["update.available"] = "CorePulse {0} が利用できます",
        ["update.available.body"] = "現在のバージョンは {0} です。更新はすぐに完了し、アプリが再起動します。",
        ["update.button.update"] = "更新",
        ["update.button.notes"] = "変更点",
        ["update.downloading"] = "CorePulse {0} をダウンロードしています…",
        ["update.upToDate"] = "最新バージョンです（{0}）。",
        ["update.failed"] = "更新に失敗しました: {0}",
```

- [ ] **Step 3: Add the settings checkbox**

In `src/CpuMonitorNotifier/Settings/SettingsForm.cs`:

Add the using:

```csharp
using CpuMonitorNotifier.Update;
```

Change `ClientSize = new Size(440, 482);` to:

```csharp
        ClientSize = new Size(440, 510);
```

Change `RowCount = 14,` to:

```csharp
            RowCount = 15,
```

Add the field next to `_autoStart`:

```csharp
    private readonly CheckBox? _updateCheck; // null для сборок из исходников — обновлять нечего
```

Insert after the `_autoStart` block (after `layout.SetColumnSpan(_autoStart, 2);`) and before the `buttons` block:

```csharp
        if (DistributionInfo.UpdatesSupported)
        {
            _updateCheck = new CheckBox
            {
                Text = Loc.T("settings.updates"),
                Checked = settings.UpdateCheckEnabled,
                AutoSize = true,
            };
            layout.Controls.Add(_updateCheck);
            layout.SetColumnSpan(_updateCheck, 2);
        }
```

Add to `ApplyTo`, before `AutoStart.IsEnabled = _autoStart.Checked;`:

```csharp
        if (_updateCheck is not null)
            settings.UpdateCheckEnabled = _updateCheck.Checked;
```

- [ ] **Step 4: Verify every language has every key**

Create `tests/CorePulse.Tests/LocalizationTests.cs`:

```csharp
using System.Reflection;
using CpuMonitorNotifier.Localization;
using Xunit;

namespace CorePulse.Tests;

public class LocalizationTests
{
    private static IEnumerable<(AppLanguage Lang, Dictionary<string, string> Table)> Tables()
    {
        var field = typeof(Loc).GetField("Tables", BindingFlags.NonPublic | BindingFlags.Static)!;
        var tables = (Dictionary<AppLanguage, Dictionary<string, string>>)field.GetValue(null)!;
        return tables.Select(kv => (kv.Key, kv.Value));
    }

    private static Dictionary<string, string> English()
    {
        var field = typeof(Loc).GetField("English", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Dictionary<string, string>)field.GetValue(null)!;
    }

    [Fact]
    public void EveryLanguageHasEveryEnglishKey()
    {
        var expected = English().Keys.OrderBy(k => k).ToArray();
        foreach (var (lang, table) in Tables())
        {
            string[] missing = expected.Except(table.Keys).OrderBy(k => k).ToArray();
            Assert.True(missing.Length == 0, $"{lang} is missing: {string.Join(", ", missing)}");
        }
    }

    [Theory]
    [InlineData("menu.checkUpdates")]
    [InlineData("settings.updates")]
    [InlineData("update.available")]
    [InlineData("update.available.body")]
    [InlineData("update.button.update")]
    [InlineData("update.button.notes")]
    [InlineData("update.downloading")]
    [InlineData("update.upToDate")]
    [InlineData("update.failed")]
    public void UpdateKeysExistInEnglish(string key) => Assert.Contains(key, English().Keys);
}
```

If `Tables` or `English` are not the exact private field names in `Localization.cs`, fix the test to match the real names rather than renaming the fields.

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/CorePulse.Tests`
Expected: PASS — all tests pass, including `EveryLanguageHasEveryEnglishKey`

- [ ] **Step 6: Commit**

```bash
git add src/CpuMonitorNotifier/Settings src/CpuMonitorNotifier/Localization tests/CorePulse.Tests/LocalizationTests.cs
git commit -m "feat: update-check setting and strings in all 8 languages"
```

---

### Task 8: Toasts and tray wiring

**Threading note:** toast button activations arrive on a **background thread**. `Application.Exit()` and dialogs must run on the UI thread, so `TrayAppContext` marshals through a hidden `Control`. `SynchronizationContext.Current` is **not** usable here: `TrayAppContext` is constructed *before* `Application.Run` installs the WinForms context, so it would be `null`.

**Files:**
- Modify: `src/CpuMonitorNotifier/Notifications/ToastNotifier.cs`
- Modify: `src/CpuMonitorNotifier/App/TrayAppContext.cs`

**Interfaces:**
- Consumes: `UpdateService`, `UpdateCheckResult`, `UpdateCheckStatus`, `ReleaseInfo` (Task 4); `UpdateInstaller.CanSwap/ApplyAndRestart` (Task 5); `DistributionInfo.UpdatesSupported` (Task 2); `UpdateVersions.Current` (Task 1); `AppSettings.UpdateCheckEnabled/LastUpdateCheckUtc` (Task 7).
- Produces: `ToastNotifier.ShowUpdateAvailable(ReleaseInfo)`, `ToastNotifier.ShowDownloading(Version)`, `ToastNotifier.ShowUpdateFailed(string)`, `event Action? UpdateRequested`.

- [ ] **Step 1: Extend ToastNotifier**

In `src/CpuMonitorNotifier/Notifications/ToastNotifier.cs`, add the using:

```csharp
using CpuMonitorNotifier.Update;
```

Add next to `ActionOpenTaskManager`:

```csharp
    private const string ActionUpdate = "installUpdate";
    private const string ActionNotes = "releaseNotes";

    /// <summary>Нажата кнопка «Обновить» в тосте. Приходит из фонового потока.</summary>
    public event Action? UpdateRequested;
```

Add these methods after `ShowProcessAlert`:

```csharp
    public void ShowUpdateAvailable(ReleaseInfo release)
    {
        new ToastContentBuilder()
            .AddText(string.Format(Loc.T("update.available"), release.Version))
            .AddText(string.Format(Loc.T("update.available.body"), UpdateVersions.Current))
            .AddButton(new ToastButton()
                .SetContent(Loc.T("update.button.update"))
                .AddArgument("action", ActionUpdate))
            .AddButton(new ToastButton()
                .SetContent(Loc.T("update.button.notes"))
                .AddArgument("action", ActionNotes)
                .AddArgument("url", release.PageUrl)) // ссылку несём в аргументе, чтобы не хранить состояние
            .Show();
    }

    /// <summary>Ассет — десятки мегабайт; без этого тоста нажатие «Обновить» выглядит как «ничего не произошло».</summary>
    public void ShowDownloading(Version version)
    {
        new ToastContentBuilder()
            .AddText(Loc.AppName)
            .AddText(string.Format(Loc.T("update.downloading"), version))
            .Show();
    }

    public void ShowUpdateFailed(string message)
    {
        new ToastContentBuilder()
            .AddText(Loc.AppName)
            .AddText(string.Format(Loc.T("update.failed"), message))
            .Show();
    }
```

Replace the whole `OnToastActivated` method (note: it stops being `static` — the event needs the instance; the existing `+=` in the constructor and `-=` in `Dispose` keep working unchanged):

```csharp
    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        var args = ToastArguments.Parse(e.Argument);
        if (!args.TryGetValue("action", out string action))
            return;

        switch (action)
        {
            case ActionOpenTaskManager:
                Launch("taskmgr.exe");
                break;
            case ActionUpdate:
                UpdateRequested?.Invoke();
                break;
            case ActionNotes:
                if (args.TryGetValue("url", out string url))
                    Launch(url);
                break;
        }
    }

    private static void Launch(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // диспетчер задач может быть заблокирован политикой, браузера может не быть — молча игнорируем
        }
    }
```

- [ ] **Step 2: Wire it into TrayAppContext**

In `src/CpuMonitorNotifier/App/TrayAppContext.cs`, add the using:

```csharp
using CpuMonitorNotifier.Update;
```

Add the constant next to `RenderIntervalMs`:

```csharp
    private const int FirstUpdateCheckMs = 60_000;      // не тормозим запуск: первая проверка через минуту
    private const int UpdateCheckPollMs = 3_600_000;    // раз в час смотрим, не прошли ли сутки
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);
```

Add fields after `_historyForm`:

```csharp
    private readonly UpdateService _updates = new();
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly ToolStripMenuItem _checkUpdatesItem;
    // тосты активируются в фоновом потоке; через скрытый control возвращаемся в UI-поток.
    // SynchronizationContext.Current здесь непригоден: контекст ставит Application.Run, а он ещё не вызван.
    private readonly Control _marshal = new();
    private ReleaseInfo? _pendingUpdate;
    private bool _updating;
```

In the constructor, add the menu item after `_historyItem` is created and add it to the menu after `menu.Items.Add(_historyItem);`:

```csharp
        _checkUpdatesItem = new ToolStripMenuItem(string.Empty, null, (_, _) => _ = CheckUpdatesManuallyAsync())
        {
            Visible = DistributionInfo.UpdatesSupported, // сборке из исходников обновляться неоткуда
        };
```

```csharp
        menu.Items.Add(_checkUpdatesItem);
```

At the end of the constructor, after `OnSample();`:

```csharp
        _ = _marshal.Handle; // форсируем создание окна, иначе BeginInvoke бросит
        _notifier.UpdateRequested += () => _marshal.BeginInvoke(() => _ = StartUpdateAsync());

        _updateTimer = new System.Windows.Forms.Timer { Interval = FirstUpdateCheckMs };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = UpdateCheckPollMs;
            _ = CheckUpdatesAutomaticallyAsync();
        };
        if (DistributionInfo.UpdatesSupported)
            _updateTimer.Start();
```

Add to `LocalizeMenu`:

```csharp
        _checkUpdatesItem.Text = Loc.T("menu.checkUpdates");
```

Add these methods before `TogglePause`:

```csharp
    /// <summary>Автоматическая проверка: молчит и об ошибке, и об актуальной версии — пользователь её не запрашивал.</summary>
    private async Task CheckUpdatesAutomaticallyAsync()
    {
        if (!_settings.UpdateCheckEnabled || _updating) return;
        if (DateTime.UtcNow - _settings.LastUpdateCheckUtc < UpdateCheckInterval) return;

        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
        _settings.Save();

        var result = await _updates.CheckAsync();
        if (result.Status == UpdateCheckStatus.UpdateAvailable)
            OfferUpdate(result.Release!);
    }

    /// <summary>Ручная проверка: сообщает любой исход и не считается с суточным интервалом — её попросили.</summary>
    private async Task CheckUpdatesManuallyAsync()
    {
        if (_updating) return;

        var result = await _updates.CheckAsync();
        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
        _settings.Save();

        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                OfferUpdate(result.Release!);
                break;
            case UpdateCheckStatus.UpToDate:
                MessageBox.Show(string.Format(Loc.T("update.upToDate"), UpdateVersions.Current),
                    Loc.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
            default:
                MessageBox.Show(string.Format(Loc.T("update.failed"), Loc.T("update.failed.network")),
                    Loc.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
        }
    }

    private void OfferUpdate(ReleaseInfo release)
    {
        _pendingUpdate = release;
        _notifier.ShowUpdateAvailable(release);
    }

    /// <summary>Загружает и подменяет .exe. Если подменять нельзя — отправляем на страницу релиза.</summary>
    private async Task StartUpdateAsync()
    {
        if (_updating || _pendingUpdate is null) return;

        var release = _pendingUpdate;
        if (!UpdateInstaller.CanSwap())
        {
            // каталог недоступен на запись (например, Program Files) — не ломаемся, пусть скачает вручную
            OpenReleasePage(release.PageUrl);
            return;
        }

        _updating = true;
        try
        {
            _notifier.ShowDownloading(release.Version);
            string file = await _updates.DownloadAsync(release);
            UpdateInstaller.ApplyAndRestart(file); // отсюда процесс уже не вернётся
        }
        catch (Exception ex)
        {
            _updating = false;
            _notifier.ShowUpdateFailed(ex.Message);
            OpenReleasePage(release.PageUrl);
        }
    }

    private static void OpenReleasePage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* браузера может не быть — тогда ничего не делаем */ }
    }
```

Add to `ExitApp`, before `Application.Exit();`:

```csharp
        _updateTimer.Stop();
        _marshal.Dispose();
```

- [ ] **Step 3: Add the one missing string**

`CheckUpdatesManuallyAsync` uses `update.failed.network`. Add it to **all 8** dictionaries in `Localization.cs`, next to the other `update.*` keys:

```csharp
        ["update.failed.network"] = "could not reach GitHub",      // English
        ["update.failed.network"] = "не удалось связаться с GitHub", // Russian
        ["update.failed.network"] = "GitHub ist nicht erreichbar",   // German
        ["update.failed.network"] = "no se pudo conectar con GitHub", // Spanish
        ["update.failed.network"] = "impossible de joindre GitHub",  // French
        ["update.failed.network"] = "não foi possível acessar o GitHub", // Portuguese
        ["update.failed.network"] = "无法连接到 GitHub",              // Chinese
        ["update.failed.network"] = "GitHub に接続できませんでした",   // Japanese
```

Add `update.failed.network` to the `[InlineData]` list in `LocalizationTests.UpdateKeysExistInEnglish`.

- [ ] **Step 4: Build, test and smoke test**

```bash
taskkill //F //IM CpuMonitorNotifier.exe 2>/dev/null || true
dotnet build src/CpuMonitorNotifier --no-incremental
dotnet test tests/CorePulse.Tests
```

Expected: `0 Error(s)`, `0 Warning(s)`; all tests pass.

Run the app and open the tray menu. Because this is a `source` build, **"Check for updates…" must not appear** and the Settings window must **not** show the "Check for updates automatically" checkbox. This is the local-build guard working.

- [ ] **Step 5: Commit**

```bash
git add src/CpuMonitorNotifier tests/CorePulse.Tests
git commit -m "feat: update toast, tray menu entry and daily check"
```

---

### Task 9: CI release workflow

**Files:**
- Create: `.github/workflows/release.yml`
- Modify: `src/CpuMonitorNotifier/CpuMonitorNotifier.csproj`

**Interfaces:**
- Consumes: the `DistributionKind` property (Task 2).
- Produces: release assets `CorePulse.exe`, `CorePulse-net10.exe`, `SHA256SUMS.txt`.

- [ ] **Step 1: Bump the version**

In `src/CpuMonitorNotifier/CpuMonitorNotifier.csproj`, change `<Version>1.4.0</Version>` to:

```xml
    <Version>1.5.0</Version>
```

- [ ] **Step 2: Write the workflow**

Create `.github/workflows/release.yml`:

```yaml
name: release

on:
  push:
    tags: [ 'v*' ]

permissions:
  contents: write

jobs:
  release:
    runs-on: windows-latest

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Test
        run: dotnet test tests/CorePulse.Tests -c Release

      # Ассеты собираются с меткой вида сборки: по ней апдейтер выбирает, что качать.
      - name: Publish self-contained
        run: >
          dotnet publish src/CpuMonitorNotifier -c Release -r win-x64
          --self-contained true
          -p:PublishSingleFile=true
          -p:IncludeNativeLibrariesForSelfExtract=true
          -p:EnableCompressionInSingleFile=true
          -p:DistributionKind=self-contained
          -o publish/self-contained

      - name: Publish framework-dependent
        run: >
          dotnet publish src/CpuMonitorNotifier -c Release -r win-x64
          --self-contained false
          -p:PublishSingleFile=true
          -p:IncludeNativeLibrariesForSelfExtract=true
          -p:DistributionKind=framework
          -o publish/framework

      - name: Collect assets
        shell: pwsh
        run: |
          New-Item -ItemType Directory -Force dist | Out-Null
          Copy-Item publish/self-contained/CpuMonitorNotifier.exe dist/CorePulse.exe
          Copy-Item publish/framework/CpuMonitorNotifier.exe dist/CorePulse-net10.exe
          # Формат coreutils: "<hash>  <file>" — его читает GitHubReleases.ParseSha256Sums
          Get-FileHash dist/CorePulse.exe, dist/CorePulse-net10.exe -Algorithm SHA256 |
            ForEach-Object { '{0}  {1}' -f $_.Hash.ToLower(), (Split-Path $_.Path -Leaf) } |
            Set-Content dist/SHA256SUMS.txt -Encoding ascii

      - name: Verify the stamp
        shell: pwsh
        run: |
          $exe = Get-Item dist/CorePulse.exe
          if ($exe.Length -lt 20MB) { throw "self-contained build looks too small: $($exe.Length) bytes" }
          $fx = Get-Item dist/CorePulse-net10.exe
          if ($fx.Length -gt 20MB) { throw "framework-dependent build looks too big: $($fx.Length) bytes" }
          Get-Content dist/SHA256SUMS.txt

      - name: Create release
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: >
          gh release create $env:GITHUB_REF_NAME
          dist/CorePulse.exe dist/CorePulse-net10.exe dist/SHA256SUMS.txt
          --title $env:GITHUB_REF_NAME --generate-notes
```

- [ ] **Step 3: Verify the publish commands locally**

Run exactly what CI will run, to catch failures before tagging:

```bash
taskkill //F //IM CpuMonitorNotifier.exe 2>/dev/null || true
dotnet publish src/CpuMonitorNotifier -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DistributionKind=self-contained -o publish/self-contained
dotnet publish src/CpuMonitorNotifier -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DistributionKind=framework -o publish/framework
ls -la publish/self-contained/CpuMonitorNotifier.exe publish/framework/CpuMonitorNotifier.exe
```

Expected: both succeed; the self-contained exe is tens of MB, the framework-dependent one a few MB.

- [ ] **Step 4: Verify the stamp reaches the binary**

The published exe must be renamed to `CorePulse.exe` first — that is the name users get, and the swap
logic derives `CorePulse.old.exe` from whatever the file is actually called.

```bash
probe="$(mktemp -d)"
cp publish/self-contained/CpuMonitorNotifier.exe "$probe/CorePulse.exe"
"$probe/CorePulse.exe" &
sleep 6
tasklist //FI "IMAGENAME eq CorePulse.exe" | grep -q CorePulse && echo "PUBLISHED BUILD RUNS"
taskkill //F //IM CorePulse.exe
rm -rf "$probe"
```

Expected: `PUBLISHED BUILD RUNS`. Open the tray menu while it runs: **"Check for updates…" must now be visible**, because this build is stamped `self-contained` — the inverse of the Task 8 check.

- [ ] **Step 5: Clean up and commit**

`publish/` is already in `.gitignore`; confirm `git status --short` shows no stray files.

```bash
rm -rf publish
git add .github/workflows/release.yml src/CpuMonitorNotifier/CpuMonitorNotifier.csproj
git commit -m "ci: publish both binaries and a checksum file on tag"
```

---

### Task 10: Documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/ARCHITECTURE.md`

- [ ] **Step 1: Add the Download section to README**

In `README.md`, replace the stale hardcoded version badge:

```markdown
[![Version](https://img.shields.io/badge/version-1.0.0-blue)](#)
```

with a dynamic one that cannot go stale (this is why it is being changed — it already reads 1.0.0 at 1.4.0):

```markdown
[![Release](https://img.shields.io/github/v/release/presetslrdev/CorePulse?color=blue)](https://github.com/presetslrdev/CorePulse/releases/latest)
```

Insert a new section immediately after the closing `</div>` of the badge block and the `---` that follows it, before `## Why CorePulse?`:

````markdown
## Download

**[⬇ Download the latest release](https://github.com/presetslrdev/CorePulse/releases/latest)** — no installer, no setup. Unzip nothing; just run it.

| File | Pick this if | Size |
|---|---|---|
| **`CorePulse.exe`** | You just want it to run. Nothing else needed. | ~70 MB |
| `CorePulse-net10.exe` | You already have the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). | ~3 MB |

CorePulse keeps itself up to date: it checks GitHub for a new release once a day and offers to update
with one click. You can turn that off in Settings.

### "Windows protected your PC"

CorePulse isn't code-signed yet — a certificate costs a few hundred dollars a year — so SmartScreen
shows a warning the first time you run it. Click **More info → Run anyway**.

If you'd rather verify the download first, every release ships a `SHA256SUMS.txt`:

```powershell
Get-FileHash .\CorePulse.exe -Algorithm SHA256
```

Compare the result with the line for `CorePulse.exe` in `SHA256SUMS.txt`. Note what this does and
doesn't prove: it confirms your download isn't corrupted or truncated, but since the checksums are
published alongside the binary, it isn't protection against a compromised GitHub account. The real
trust anchor is HTTPS and the security of the publishing account.
````

- [ ] **Step 2: Update the README feature list and privacy**

In `## Features`, add after the themes bullet:

```markdown
- ⬆️ **Updates itself** — checks GitHub once a day, updates with one click, and restarts. Optional.
```

Change the "Lightweight & no admin rights" bullet to keep the offline claim honest:

```markdown
- 🚀 **Lightweight & no admin rights** — a single tray app, no drivers, no elevation. The only network
  request it ever makes is the update check (one `GET` to `api.github.com`); there is no telemetry of
  any kind, and you can turn the check off in Settings.
```

In `## Usage`, extend the Settings bullet to mention updates:

```markdown
- **Right-click** the icon (or double-click) for **Settings** — choose the icon style, language, theme,
  alert threshold/duration/cooldown, poll interval, notifications, exclusions, update checks, and autostart.
```

In `## Roadmap`, remove the now-shipped "Portable single-file self-contained build." line and replace it with:

```markdown
- Code signing, to get rid of the SmartScreen warning.
- winget package (`winget install CorePulse`).
```

- [ ] **Step 3: Update the Installation section**

Replace the whole `## Installation` section body (keeping the heading) with:

````markdown
**Requirements:** Windows 10 or 11. The self-contained `CorePulse.exe` needs nothing else;
`CorePulse-net10.exe` needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

Most people should just [download a release](#download). To build it yourself:

### Build from source

```powershell
git clone https://github.com/presetslrdev/CorePulse.git
cd CorePulse
dotnet run --project src/CpuMonitorNotifier
```

Builds from source are stamped as such and never self-update.

### Run the tests

```powershell
dotnet test tests/CorePulse.Tests
```

### Publish it the way CI does

```powershell
dotnet publish src/CpuMonitorNotifier -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DistributionKind=self-contained
```
````

- [ ] **Step 4: Document the module in ARCHITECTURE**

In `docs/ARCHITECTURE.md`, add to the module tree after the `Theming/ThemeManager.cs` line:

```
Update/
  UpdateVersions.cs       разбор тега релиза и сравнение версий
  DistributionInfo.cs     вид сборки, впечатанный CI (source/self-contained/framework)
  GitHubReleases.cs       чтение /releases/latest + SHA256SUMS.txt
  UpdateService.cs        решение об обновлении и загрузка с проверкой хеша
  UpdateInstaller.cs      подмена собственного .exe
tests/CorePulse.Tests/    юнит-тесты чистой логики обновления
```

Add a new section after `### Theming`:

```markdown
### Distribution and updates

CI publishes two single-file win-x64 binaries per tag: `CorePulse.exe` (self-contained, ~70 MB, needs
no runtime) and `CorePulse-net10.exe` (framework-dependent, ~3 MB). WinForms does not support trimming,
so the self-contained size is a floor, not an oversight — it buys the removal of the runtime
prerequisite, which is the barrier that actually stops people.

Each build is stamped via `AssemblyMetadata` (`-p:DistributionKind=…`), so `UpdateService` knows which
asset matches the running binary instead of guessing. The default is `source`, which disables updating
entirely — a developer's local build must never swap itself.

**The swap.** The app updates itself with no helper process, which is possible because of an asymmetry
in Windows: a running `.exe` **can be renamed** but **cannot be deleted**. So the app renames itself to
`CorePulse.old.exe`, moves the verified download into place, relaunches with `--updated <pid>`, and
exits; the new instance waits for that PID (the single-instance mutex would otherwise make it exit
silently) and deletes the leftover. If the second move fails, the rename is undone and the installation
survives. If the directory isn't writable — `Program Files`, say — no swap is attempted and the user is
sent to the release page instead.

**Trust.** The updater pins the repository, uses HTTPS, requires a strictly greater version (no
downgrade), and verifies SHA256 before touching anything on disk. Being explicit about the limit:
`SHA256SUMS.txt` ships in the same release as the binary, so it protects against a corrupted download,
**not** against a compromised GitHub account, which could replace both. The trust anchor is HTTPS plus
the publishing account. The binaries are unsigned, so SmartScreen warns on first run; the whole check
can be disabled in Settings, and the app makes no other network request and collects no telemetry.
```

- [ ] **Step 5: Verify the docs match reality**

Re-read both files and confirm every claim is true of the code as built: asset names, the default being
`System` theme, the `source` stamp disabling updates, and the checkbox location.

- [ ] **Step 6: Commit**

```bash
git add README.md docs/ARCHITECTURE.md
git commit -m "docs: download section, update behavior and its trust limits"
```

---

### Task 11: Release v1.5.0

The first real end-to-end proof. Everything before this is unverified against a live release.

- [ ] **Step 1: Full clean verification**

```bash
taskkill //F //IM CpuMonitorNotifier.exe 2>/dev/null || true
dotnet build src/CpuMonitorNotifier --no-incremental
dotnet test tests/CorePulse.Tests
git status --short
```

Expected: `0 Error(s)`, `0 Warning(s)`; all tests pass; working tree clean.

- [ ] **Step 2: Push and tag**

```bash
git push origin master
git tag v1.5.0
git push origin v1.5.0
```

- [ ] **Step 3: Watch the workflow**

```bash
gh run watch
```

Expected: the `release` workflow succeeds and the release appears with all three assets.

```bash
gh release view v1.5.0
```

- [ ] **Step 4: Verify the published binary end-to-end**

Download the real asset the way a user would, run it, and confirm the update path is live:

```bash
cd "$(mktemp -d)"
gh release download v1.5.0 -p CorePulse.exe -p SHA256SUMS.txt
sha256sum -c SHA256SUMS.txt --ignore-missing
./CorePulse.exe &
sleep 6
tasklist //FI "IMAGENAME eq CorePulse.exe" | grep -q CorePulse && echo "RELEASE BINARY RUNS"
```

Expected: the checksum verifies (`CorePulse.exe: OK`) and the binary runs. In the tray menu,
**"Check for updates…" is visible**; clicking it must report **"You're up to date (1.5.0)"** — which
proves the whole chain: the stamp, the API call, the tag parse, and the normalized comparison that
would otherwise claim 1.5.0 is newer than itself.

```bash
taskkill //F //IM CorePulse.exe
```

- [ ] **Step 5: Verify the swap against a real release**

This is the only test of the actual update. Publish a `v1.5.1` (any trivial change, e.g. a README typo),
then with the **1.5.0** binary still installed in a writable folder:

1. Confirm the toast "CorePulse 1.5.1 is available" appears (use "Check for updates…" rather than waiting a day).
2. Click **Update**.
3. Expect: a "Downloading…" toast, the app disappears and comes back within a minute.
4. Verify the new version: the tray menu → "Check for updates…" reports **up to date (1.5.1)**.
5. Verify settings and history survived: `%AppData%\CpuMonitorNotifier\settings.json` and `history.json` are intact.
6. Verify cleanup: `CorePulse.old.exe` is gone from the folder after the restart.

If any step fails, **do not leave a broken release published** — that is the risk this whole design is
built around. `gh release delete` the bad tag and fix forward.

- [ ] **Step 6: Update the GitHub repo metadata**

```bash
gh repo edit --add-topic windows,cpu-monitor,tray,dotnet,winforms,performance
```

Confirm the repo description still matches the csproj `<Description>`.

---

## Verification summary

Mapping back to the spec's verification list:

| Spec check | Where |
|---|---|
| Clean build, 0 warnings | Task 6 Step 2, Task 11 Step 1 |
| Local build = `source`, no check, checkbox hidden | Task 2 Step 2 (tests), Task 8 Step 4 (UI) |
| CI produces both assets + sums; both launch | Task 9 Steps 3–4, Task 11 Steps 3–4 |
| End-to-end swap, settings/history intact, `.old.exe` cleaned | Task 11 Step 5 |
| Hash mismatch refuses and leaves nothing behind | Task 4 Step 1 (`DownloadAsync_RejectsHashMismatchAndLeavesNoTempFile`) |
| `CanSwap()` false → release page instead | Task 5 (`IsDirectoryWritable_FalseForMissingDirectory`), Task 8 Step 2 (`StartUpdateAsync`) |
| Offline: auto check silent, manual reports error | Task 3 (`FetchLatestAsync` returns null), Task 8 Step 2 (both paths) |

**Not covered by automated tests**, by nature — these are the manual gates in Task 11 Step 5: the real
swap of a running executable, the toast round-trip through Windows, and CI itself.
