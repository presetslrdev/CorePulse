# Distribution and in-app updates

**Date:** 2026-07-16
**Status:** approved, ready for planning
**Target version:** 1.5.0

## Context

CorePulse is feature-complete for its purpose but effectively undistributed. The only way to obtain it
is `git clone` plus `dotnet run`, which limits the audience to developers who already have the .NET 10
SDK. Two barriers stand between a curious visitor and a running tray icon:

1. **No binary.** There is no `.exe` in any release.
2. **No runtime.** A plain build needs the .NET 10 Desktop Runtime installed.

A third problem appears once people do install it: they have no way to learn that a new version exists,
so they stay on whatever version they first downloaded forever.

This spec covers all three: publish ready-to-run binaries from CI, and let the app update itself.

## Goals

- A visitor downloads one file from GitHub Releases and it runs. No SDK, no runtime, no installer.
- An installed copy notices a new release and can update itself with one click.
- The update check is honest and optional: no telemetry, and it can be turned off.

## Non-goals

Explicitly out of scope, to keep this shippable:

- Delta updates, and a rollback UI.
- winget / Microsoft Store submission (a possible follow-up; it depends on this spec's artifacts).
- Code signing (see Risks — it costs money and is a separate decision).
- Pre-release channels, skip-this-version, download progress bars.
- Any telemetry, analytics, or usage reporting.

## Deliverable 1: release artifacts

A tag push of `v*` triggers `.github/workflows/release.yml` on `windows-latest`, which publishes two
single-file, win-x64 binaries and attaches them to the GitHub release:

| Asset | Build | Size | For |
|---|---|---|---|
| `CorePulse.exe` | self-contained | ~58 MB | Everyone. Just runs. **Recommended.** |
| `CorePulse-net10.exe` | framework-dependent | ~24 MB | People who already have the .NET 10 Desktop Runtime. |
| `SHA256SUMS.txt` | — | — | Integrity check for both. |

Both use `PublishSingleFile` with `IncludeNativeLibrariesForSelfExtract`; the self-contained build adds
`EnableCompressionInSingleFile`. WinForms does not support trimming, so ~58 MB is the floor for the
self-contained build — the size buys the removal of the runtime prerequisite, which is the point.

Asset names are a contract: the updater resolves them by exact name. They must not change without
changing the updater.

The workflow generates `SHA256SUMS.txt` from the published files and creates the release with `gh`.

## Deliverable 2: distribution stamping

The updater must download the same *kind* of build that is currently running, and must never try to
update a developer's local build. Rather than infer this at runtime, the build states it explicitly.

The csproj declares a `DistributionKind` property defaulting to `source`, surfaced as assembly metadata:

```xml
<PropertyGroup>
  <DistributionKind Condition="'$(DistributionKind)' == ''">source</DistributionKind>
</PropertyGroup>
<ItemGroup>
  <AssemblyMetadata Include="DistributionKind" Value="$(DistributionKind)" />
</ItemGroup>
```

CI overrides it per artifact: `-p:DistributionKind=self-contained` and `-p:DistributionKind=framework`.

At runtime `UpdateService` reads the attribute and maps it to an asset name:

| `DistributionKind` | Asset to download | Update behavior |
|---|---|---|
| `self-contained` | `CorePulse.exe` | Full update flow. |
| `framework` | `CorePulse-net10.exe` | Full update flow. |
| `source` (default) | — | Update checking disabled entirely. |

## Deliverable 3: the `Update/` module

Three units with one job each, so each can be understood and exercised in isolation.

### `Update/GitHubReleases.cs` — network only

```csharp
internal sealed record ReleaseInfo(Version Version, string AssetUrl, string Sha256, string PageUrl);

internal static class GitHubReleases
{
    Task<ReleaseInfo?> FetchLatestAsync(string assetName, CancellationToken ct);
}
```

- HTTPS GET `https://api.github.com/repos/presetslrdev/CorePulse/releases/latest`.
- A `User-Agent` header is mandatory — GitHub answers 403 without one.
- Parses `tag_name` (`v1.5.0` → `Version 1.5.0`), locates the asset by exact name, and reads its
  expected hash from the `SHA256SUMS.txt` asset.
- Returns `null` on any failure (offline, rate limit, malformed release). A failed check is a non-event:
  it is never surfaced to the user during an automatic check, and never blocks or slows the app.
- Unauthenticated GitHub API allows 60 requests/hour per IP. At one check per day this is a non-issue.

### `Update/UpdateService.cs` — decide and fetch

```csharp
Task<ReleaseInfo?> CheckAsync(CancellationToken ct);              // null = nothing newer
Task<string> DownloadAsync(ReleaseInfo r, CancellationToken ct);  // → verified temp file path
```

- Compares against `Assembly.GetExecutingAssembly().GetName().Version`. Updates only when the release is
  **strictly greater**, which also prevents a downgrade if a release is ever mis-tagged.
- Both versions are normalized to `Major.Minor.Build` before comparing. This matters: the assembly
  version is `1.4.0.0` while a tag parses as `1.4.0`, whose `Revision` is `-1`, so an unnormalized
  comparison would report the identical version as *older*. Normalizing makes equal versions compare
  equal.
- Downloads to a temp file, computes SHA256, and **throws on mismatch, deleting the temp file**.
  Verification happens before anything on disk is touched.

### `Update/UpdateInstaller.cs` — swap

```csharp
bool CanSwap();                        // is the directory holding the .exe writable?
void ApplyAndRestart(string newFile);  // swap, relaunch, exit
static void CleanupOldFile();          // called at startup
```

## Deliverable 4: the swap procedure

This rests on verified Windows behavior (tested on Windows 11 26200 against a running process):

| Operation on a running `.exe` | Result |
|---|---|
| Rename it | **Allowed** |
| Write a new file at the freed path | **Allowed** |
| Delete it while the process lives | Denied (`UnauthorizedAccessException`) |
| Delete it after the process exits | **Allowed** |

Because renaming a running executable is allowed, **no separate updater process is needed** — the app
swaps itself. Because deleting it is not, cleanup is deferred to the next startup.

```
0. CanSwap() is false          → do not attempt; show a toast linking to the release page instead
1. Download + verify SHA256    → any failure here leaves the installation untouched
2. CorePulse.exe → CorePulse.old.exe
3. temp file     → CorePulse.exe        (on failure: rename .old.exe back, abort)
4. Start the new exe with --updated <pid>, then exit
5. New process waits for <pid> to exit, takes the single-instance mutex, deletes .old.exe
```

Step 4's `--updated <pid>` argument exists because of the single-instance mutex in `Program.Main`: the
old process is still alive when the new one starts, so without the handoff the new instance would see
the mutex held and silently exit, leaving the user with nothing running. The new process waits for that
PID to exit (bounded, ~10 s) before the normal mutex check.

`CleanupOldFile()` deletes `CorePulse.old.exe` best-effort at every startup; a leftover file is harmless.

Settings, history, and the autostart registry entry all live outside the executable and are unaffected.
The autostart path does not change, because the swap happens in place.

### Failure handling

The genuinely dangerous window — a failure between steps 2 and 3, leaving no `CorePulse.exe` — is
milliseconds wide and has an explicit rollback. Every other failure mode degrades to "nothing happened,
here is the download page". `CanSwap()` is what keeps a copy in `Program Files` (no write access) from
being a broken experience rather than merely a manual one.

## Deliverable 5: settings and UI

Two new fields in `AppSettings`:

```csharp
public bool UpdateCheckEnabled { get; set; } = true;
public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
```

- **When:** one minute after startup (so the check never delays the tray icon appearing), then every
  24 hours, gated on `LastUpdateCheckUtc`. Skipped entirely when `DistributionKind` is `source`.
- **Settings window:** a "Check for updates" checkbox. Hidden when `DistributionKind` is `source`,
  since it would do nothing.
- **Tray menu:** a "Check for updates…" item for a manual check, hidden when `DistributionKind` is
  `source` for the same reason as the checkbox. Unlike the automatic check, this one reports its result
  either way — including "you are up to date" and any error — because the user asked. It runs regardless
  of the `UpdateCheckEnabled` setting and of `LastUpdateCheckUtc`: an explicit request is not throttled.
- **Toast:** "CorePulse 1.5.0 is available" with two buttons: **Update** (download → verify → swap →
  restart) and **What's new** (opens the release page in the browser).
- If a download or swap fails, a toast says so and offers the release page.

## Deliverable 6: localization

New keys across all 8 languages, following the existing flat-key convention:
`settings.updates`, `menu.checkUpdates`, `update.available`, `update.button.update`,
`update.button.notes`, `update.upToDate`, `update.failed`.

## Deliverable 7: documentation

- **README:** a Download section at the top — the direct link, which file to pick, the SmartScreen note
  (below), and how to verify the SHA256. Replace the hardcoded `version-1.0.0` badge (already stale at
  1.4.0) with a dynamic shields.io release badge so it cannot go stale again. Add the update check to
  the feature list and state plainly what leaves the machine: one GET to `api.github.com`, no telemetry,
  and how to turn it off.
- **ARCHITECTURE:** the `Update/` module, the swap procedure and its verified Windows behavior, and the
  trust discussion below.

## Risks and honest limitations

**SmartScreen.** An unsigned binary triggers "Windows protected your PC", and some people will stop
there. An OV code-signing certificate costs roughly $200–400/year and is a separate decision, out of
scope here. Mitigation is honesty: document the "More info → Run anyway" step and publish hashes.

**What SHA256 actually protects.** `SHA256SUMS.txt` is served from the same release as the binary, so it
guards against a corrupted or truncated download — **not** against a compromised GitHub account, which
could replace both. The real trust anchor is HTTPS plus the security of the publishing account. The docs
must say this rather than imply the hash makes updates tamper-proof.

**Auto-update is a code-execution path.** The app downloads a binary and runs it. This is mitigated by
pinning the repository, HTTPS only, requiring a strictly greater version, and verifying the hash before
the swap — but it remains inherently more dangerous than the check-and-notify alternative, which is why
the degradation path to "just open the page" exists and why the whole feature can be switched off.

**~58 MB per update.** Every self-contained update is a full re-download. Acceptable at CorePulse's
release cadence; delta updates are what would fix it, and they are out of scope.

## Verification

1. `dotnet build --no-incremental` — 0 errors, 0 warnings.
2. Local build reports `DistributionKind = source`: no update check runs, the settings checkbox is
   hidden.
3. The CI workflow produces both assets plus `SHA256SUMS.txt`; both binaries launch on a machine
   **without** the .NET 10 runtime installed (self-contained runs, framework-dependent fails cleanly).
4. End-to-end swap against a real release: install `v1.4.x`, publish `v1.5.0`, confirm the toast, click
   Update, and verify the app restarts on 1.5.0 with settings and history intact and `.old.exe` cleaned
   up on the next start.
5. Hash mismatch: point the updater at a corrupted file and confirm it refuses, deletes the temp file,
   and leaves the installation untouched.
6. `CanSwap()` false: run from a read-only directory and confirm the toast offers the release page
   instead of attempting a swap.
7. Offline: confirm the automatic check fails silently and the manual check reports an error.
