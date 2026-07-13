<div align="center">

<img src="assets/logo.png" width="140" alt="CorePulse logo" />

# CorePulse

### Find the process that's quietly cooking your CPU. CorePulse watches per-core load over time and names the app behind the sustained usage that heats your machine and spins up your fans.

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6?logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-3fb950)](LICENSE)
[![Version](https://img.shields.io/badge/version-1.0.0-blue)](#)
[![Languages](https://img.shields.io/badge/i18n-8%20languages-ff9f43)](#-languages)

</div>

---

## Why CorePulse?

Every CPU monitor shows you the **overall** load. But overall load hides the problem that actually
heats your machine: **a single process quietly holding a core busy for a long time.**

Here's the real story that inspired CorePulse: an open editor was using just **20–30% of one core** —
nothing alarming on any usual monitor — yet it kept the CPU warm enough that the liquid-cooling fans
spun up and stayed loud. The overall load looked fine. Task Manager, sorted by momentary CPU, never
pointed at it. The culprit was hiding in plain sight because it was *steady*, not *spiky*.

**CorePulse looks at load over time, per core, and attributes it to a process.** It surfaces the
quiet, sustained CPU consumers — the ones that don't spike but never let go — and names them. So when
your fans won't calm down, you open CorePulse and immediately see *what* is keeping your cores warm.

It also raises a notification when a core stays under heavy load for too long, again naming the
responsible process — so acute spikes get your attention too.

## Tray icon styles

The tray icon is **live** (redrawn ~8×/second) and always leads with the load of your **hottest core**
as a large number. Pick the look you like:

<div align="center">
<img src="assets/tray-styles.png" width="760" alt="Five tray icon styles: ring, segmented ring, speedometer, liquid, dots grid" />
</div>

### The color means *duration*, not just level

The number is the current load, but the **color reflects how long a core has stayed hot** — so a brief
spike doesn't cry wolf. A momentary jump to 100% stays green; a core that *keeps* holding warms from
green → yellow → red and finally pulses when it crosses your alert duration. That's the whole point:
CorePulse reacts to *sustained* load, not noise.

<div align="center">
<img src="assets/tray-sustained.png" width="620" alt="A 100% spike stays green; 95% sustained warms to yellow then red with a pulse" />
</div>

| Style | What it shows |
|-------|---------------|
| **Ring + %** | Ring gauge of the hottest core + big number. Most legible at tiny tray sizes. *(default)* |
| **Segmented ring** | One segment per core (see them all at a glance), hottest highlighted, its % in the center. |
| **Speedometer** | 270° gauge — the familiar dashboard metaphor. |
| **Liquid + %** | A container that fills to the load level with an animated wave. |
| **Dots grid** | A dot per core; each dot fills and colors by its load. |

## Features

- 🎯 **Per-core monitoring** — tracks every logical core, not just the overall average.
- 📜 **Usage history** — a **Top offenders (this session)** ranking by accumulated *core-time* surfaces
  the quiet, steady consumers (the editor at 25% that never lets go), plus a saved log of past alerts.
- 🔔 **Sustained-load alerts** — fires only when a core stays above your threshold for a set duration,
  with hysteresis and a per-core cooldown to avoid spam. Threshold goes as low as 10% to catch
  moderate-but-constant load.
- 🕵️ **Culprit detection** — every alert names the top processes likely responsible, with their CPU share.
- 📊 **Informative live tray icon** — five modern styles, hottest-core load front and center, with
  **color driven by duration** so brief spikes stay calm and only sustained load warms to red.
- 🌍 **8 languages** — auto-detected from your system, switchable in settings.
- 🚀 **Lightweight & no admin rights** — a single tray app, no drivers, no elevation.
- ⚙️ **Configurable** — threshold, duration, cooldown, poll interval, notifications on/off, autostart.
- 🖱️ **One-click Task Manager** — jump straight to the culprit from the notification.

## Usage history — find the quiet offender

Right-click the tray icon → **History**. The **Top offenders** tab ranks every process by the CPU
**core-time** it has accumulated this session — so a process steadily using a fraction of a core
climbs the list over time and gives itself away, even though it never spikes. Above it, a **timeline
of the hottest core** makes the difference obvious: a sustained offender shows a flat *shelf*, a real
spike is just a thin blip. The **Alerts** tab keeps a saved log of past sustained-load events and
their culprits.

<div align="center">
<img src="assets/history.png" width="560" alt="History window: Top offenders ranked by core-minutes, with a steady 27%-peak process visible" />
</div>

## Installation

**Requirements:** Windows 10 or 11, [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
(the Desktop Runtime).

### Build from source

```powershell
git clone <your-fork-url> CorePulse
cd CorePulse
dotnet run --project src/CpuMonitorNotifier
```

### Publish a single executable

```powershell
dotnet publish src/CpuMonitorNotifier -c Release -r win-x64 --self-contained false
```

## Usage

- Look at the tray icon: the number is your hottest core's load; the color tells you how hot.
- Hover for a tooltip: hottest core, overall CPU, and the greediest process.
- **Right-click** the icon (or double-click) for **Settings** — choose the icon style, language,
  alert threshold/duration/cooldown, poll interval, notifications, and autostart.
- **History** in the menu opens the offenders ranking and alert log (see above).
- **Test notification** in the menu fires a sample toast right away — handy to confirm notifications
  aren't being swallowed by Windows **Focus Assist / Do Not Disturb**.

## How culprit detection works

Windows doesn't expose per-process, per-core CPU statistics without ETW (which needs administrator
rights). CorePulse uses a heuristic that's accurate for the case that matters most:

1. Every second it samples each process's total CPU time and computes the delta — each process's load
   expressed in **cores** (1.0 = one fully-busy core).
2. When a core alerts, it surfaces the processes whose consumption matches the number of saturated cores.
3. For the classic scenario — a hung single-threaded process holding one core at 100% — the guess is
   effectively exact.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full design, and
[docs/ANALOGS.md](docs/ANALOGS.md) for how CorePulse compares to existing tools.

## Languages

Auto-detected from your system locale, or pick one explicitly in Settings:

🇬🇧 English · 🇷🇺 Русский · 🇩🇪 Deutsch · 🇪🇸 Español · 🇫🇷 Français · 🇧🇷 Português · 🇨🇳 中文 · 🇯🇵 日本語

Adding a language is a single dictionary in [Localization.cs](src/CpuMonitorNotifier/Localization/Localization.cs) —
pull requests welcome.

## How it compares

No existing tool combines all three of per-core visualization, sustained-load alerting, and culprit
naming in one lightweight app:

| | Per-core | Live tray icon | Sustained-load alerts | Names the culprit |
|---|:---:|:---:|:---:|:---:|
| Task Manager tray icon | ✗ | minimal | ✗ | ✗ |
| XMeters | ✓ | ✓ | ✗ | ✗ |
| Process Lasso | ✗ | ✓ | ✓ (by process) | ✓ |
| HWiNFO | ✓ | ✓ | ✓ (sensor thresholds) | ✗ |
| **CorePulse** | **✓** | **✓** | **✓ (per core)** | **✓** |

Full breakdown in [docs/ANALOGS.md](docs/ANALOGS.md).

## Roadmap

- Precise per-core → per-process attribution via ETW CPU sampling (opt-in, requires elevation).
- Optional history graph / mini-sparkline in the tooltip.
- Portable single-file self-contained build.

## Tech stack

C# · .NET 10 · WinForms tray host · GDI+ rendering · Windows Toast notifications
(`Microsoft.Toolkit.Uwp.Notifications`) · PDH performance counters (`Processor Information`).

## License

[MIT](LICENSE) © 2026 Denis Esis
