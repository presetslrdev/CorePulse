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
  LoadDetector.cs         sliding window, hysteresis, cooldown → Alert event
Tray/
  TrayIconStyle.cs        the five icon styles
  TrayIconRenderer.cs     GDI+ rendering of the live per-core icon + tooltip
Notifications/ToastNotifier.cs
Localization/Localization.cs   8-language string tables, English fallback
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

For each logical core, a sliding window of samples:

- **trigger**: average ≥ `ThresholdPercent` (default 90%) continuously for `DurationSeconds` (default 60s);
- **release hysteresis**: the alert clears when load drops below `ThresholdPercent − 10`;
- **cooldown**: a repeat notification for the same core happens no more often than `CooldownMinutes`
  (default 5 min).

### Tray icon

- 64×64 bitmap, GDI+ with anti-aliasing (Windows scales it down for the tray); the headline value is
  the hottest core's load, shown as a large number and color (green/yellow/red).
- Five switchable styles (`TrayIconStyle`): `Ring`, `Segments`, `Speedometer`, `Liquid`, `Dots`. For
  `Segments`/`Dots` with many cores, pairwise aggregation (max of the pair).
- **Liveliness**: rendering runs on a separate ~125 ms timer (≈8 fps) with an animation phase from a
  `Stopwatch`, while data sampling stays at once per second. This gives the `Liquid` wave and the
  alert ring pulse without loading the metrics collection.
- Tooltip (≤127 chars, a NotifyIcon limit): `Core 5: 98% | CPU 43% | ffmpeg.exe`.
- `Icon.FromHandle(bitmap.GetHicon())` must be followed by `DestroyIcon` — otherwise GDI handles leak
  (verified: at 8 fps the GDI object count stays flat).

### Localization

A lightweight `Loc` layer: one string dictionary per language (English/Russian/German/Spanish/French/
Portuguese/Chinese/Japanese) with English as the fallback. Keys are flat (e.g. `menu.settings`);
placeholders are filled via `string.Format`. Language is chosen from the `Language` setting, with
`Auto` mapping from `CultureInfo.CurrentUICulture`.

### Settings

JSON (`System.Text.Json`) in `%AppData%\CpuMonitorNotifier\settings.json`: threshold, duration,
cooldown, notifications on/off, poll interval, icon style, language. Enums are stored as strings.
Autostart is a value in `HKCU\...\Run` (no scheduled task, no admin).
