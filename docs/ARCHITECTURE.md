# Architecture

## Stack

- **C# / .NET 10**, WinForms as the tray-app host: an `ApplicationContext` with no main window; the
  only window is Settings.
- **Notifications**: the `Microsoft.Toolkit.Uwp.Notifications` package — Windows Toast for unpackaged
  Win32 apps (Windows 10/11).
- **No administrator rights required.**

## Modules

```
Program.cs                single-instance mutex → Application.Run(TrayAppContext)
App/TrayAppContext.cs     composition: sample timer + render timer → CpuSampler → LoadDetector → Renderer/Notifier
Monitoring/
  CpuSampler.cs           per-core load via PerformanceCounter
  ProcessSampler.cs       process CPU-time deltas → top candidates
  LoadDetector.cs         per-core sliding window, hysteresis, cooldown → Alert event
  ProcessLoadDetector.cs  per-process sustained load (the quiet cooker) → ProcessAlert event
  UsageHistory.cs         session core-time per process (top offenders) + alert log + hottest-core timeline
App/HistoryForm.cs        history window (Top offenders + timeline sparkline + Alerts tabs)
App/SparklineControl.cs   hottest-core load timeline (shelf vs spike)
Tray/
  TrayIconStyle.cs        the five icon styles
  TrayIconRenderer.cs     GDI+ rendering of the live per-core icon + tooltip
Notifications/ToastNotifier.cs
Localization/Localization.cs   8-language string tables, English fallback
Theming/ThemeManager.cs        System/Light/Dark theme via Application.SetColorMode
Update/
  UpdateVersions.cs       разбор тега релиза и сравнение версий
  DistributionInfo.cs     вид сборки, впечатанный CI (source/self-contained/framework)
  GitHubReleases.cs       чтение /releases/latest + SHA256SUMS.txt
  UpdateService.cs        решение об обновлении и загрузка с проверкой хеша
  UpdateInstaller.cs      подмена собственного .exe
tests/CorePulse.Tests/    юнит-тесты чистой логики обновления
Settings/
  AppSettings.cs          JSON in %AppData%\CpuMonitorNotifier\settings.json
  AutoStart.cs            HKCU\Software\Microsoft\Windows\CurrentVersion\Run
  SettingsForm.cs         settings window
```

## Key decisions

### Collecting per-core load

The `Processor Information(*)\% Processor Utility` counters — unlike `% Processor Time` — correctly
account for turbo boost and core parking on modern CPUs (they can read above 100%, so we clamp to
100). Falls back to `% Processor Time` if `% Processor Utility` is unavailable. Sampled once per
second; a PDH counter's first sample is always 0, so we warm it up at startup.

### Attributing a process to a core

Windows does not provide per-process, per-core statistics without ETW (and ETW CPU sampling requires
admin rights). We use a heuristic that's good enough for the target scenario:

1. Once per second we sample each process's `TotalProcessorTime` and compute the delta over the window —
   each process's load expressed in **cores** (1.0 = one fully-busy core).
2. When N cores alert, we surface the top processes whose consumption ≈ N cores (favoring those close
   to an integer number of cores — typical of a busy-loop).
3. For a hung single-threaded process (the main scenario) the heuristic is effectively exact: one
   process eats roughly one core.

Precise attribution via ETW (`Microsoft.Diagnostics.Tracing.TraceEvent`) is a possible future
enhancement, behind a settings flag and with an elevation prompt.

### Detecting "sustained load"

Two independent detectors, each with a sliding window, hysteresis and per-key cooldown:

- **Per-core** (`LoadDetector`): a core triggers when it averages ≥ `ThresholdPercent` (default 90%)
  continuously for `DurationSeconds` (default 60s); clears below `ThresholdPercent − 10`.
- **Per-process** (`ProcessLoadDetector`): the headline "quiet cooker" detector. Aggregates each
  process's load by name (sum of cores across PIDs) and triggers when a process holds
  ≥ `ThresholdCores` (default 0.25 = 25% of a core) continuously for `DurationSeconds` (default 10 min);
  clears below `ThresholdCores − 0.05`. This is what catches the editor steadily using a quarter-core.
- **cooldown**: a repeat notification for the same core/process is throttled by `CooldownMinutes`.

Both feed the same history log; process alerts are recorded with an empty core list.

### Tray icon

- 64×64 bitmap, GDI+ with anti-aliasing (Windows scales it down for the tray); the headline value is
  the hottest core's load, shown as a large number and color (green/yellow/red).
- Five switchable styles (`TrayIconStyle`): `Ring`, `Segments`, `Speedometer`, `Liquid`, `Dots`. For
  `Segments`/`Dots` with many cores, pairwise aggregation (max of the pair).
- **Color = duration, not level**: the number/arc/level still show the current load of the focused
  core, but the fill **color** is driven by each core's *heat* (`LoadDetector.Heat`, 0..1 = how long
  it has held above threshold relative to the alert duration). A brief spike stays green; a core that
  keeps holding warms green→yellow→red and pulses at alert. This is what makes the icon react to
  sustained load rather than momentary noise.
- **Liveliness**: rendering runs on a separate ~125 ms timer (≈8 fps) with an animation phase from a
  `Stopwatch`, while data sampling stays at once per second. This gives the `Liquid` wave and the
  alert ring pulse without loading the metrics collection.
- Tooltip (≤127 chars, a NotifyIcon limit): `Core 5: 98% | CPU 43% | ffmpeg.exe`.
- `Icon.FromHandle(bitmap.GetHicon())` must be followed by `DestroyIcon` — otherwise GDI handles leak
  (verified: at 8 fps the GDI object count stays flat).

### Theming

`AppTheme` is `System` (default), `Light` or `Dark`. `ThemeManager.Apply` maps it onto WinForms'
`Application.SetColorMode` (`System` / `Classic` / `Dark`), which themes the windows and their title
bars. `System` resolves from `HKCU\...\Themes\Personalize\AppsUseLightTheme` (0 = dark) — used by
`SparklineControl`, which paints its own canvas and so picks its background/grid from
`ThemeManager.IsDarkNow`. The color mode is applied at startup before any window is created and
re-applied when settings change; since Settings/History are created on demand, a theme change takes
effect the next time a window is opened.

The tray icon is intentionally *not* themed by this setting: it lives on the taskbar (whose color
follows the Windows theme, not the app's), and its dark rounded backer is verified legible on both
light and dark taskbars.

### Distribution and updates

CI publishes two single-file win-x64 binaries per tag: `CorePulse.exe` (self-contained, ~58 MB, needs
no runtime) and `CorePulse-net10.exe` (framework-dependent, ~24 MB — the Windows Runtime projection it needs for toasts ships with it). WinForms does not support trimming,
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

### Localization

A lightweight `Loc` layer: one string dictionary per language (English/Russian/German/Spanish/French/
Portuguese/Chinese/Japanese) with English as the fallback. Keys are flat (e.g. `menu.settings`);
placeholders are filled via `string.Format`. Language is chosen from the `Language` setting, with
`Auto` mapping from `CultureInfo.CurrentUICulture`.

### Settings

JSON (`System.Text.Json`) in `%AppData%\CpuMonitorNotifier\settings.json`: threshold, duration,
cooldown, notifications on/off, poll interval, icon style, language. Enums are stored as strings.
Autostart is a value in `HKCU\...\Run` (no scheduled task, no admin).
