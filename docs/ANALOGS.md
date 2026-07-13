# Competitive review

An overview of existing Windows CPU-monitoring tools — what they do and what's missing for our
scenario: *"notice that a single core has been under load for a while, and name the culprit."*

## Comparison table

| # | Tool | Per-core load | Tray informativeness | Sustained-load alerts | Names the culprit | Price |
|---|------|---------------|----------------------|-----------------------|-------------------|-------|
| 1 | Windows Task Manager (tray icon) | ✗ (overall %) | minimal — a single green meter | ✗ | ✗ (manual only) | built-in |
| 2 | [XMeters](https://entropy6.com/xmeters/) | **✓** (bars in taskbar) | high — per-core bars right in the taskbar | ✗ | ✗ | free (personal) |
| 3 | [SysStatsTray](https://apps.microsoft.com/detail/9nd57x1thnzm) | ✗ | medium — dynamic icons, color by load | ✗ | ✗ | free |
| 4 | [Process Lasso](https://bitsum.com/) (ProBalance) | ✗ | medium — graph in tray | **✓** — reacts to CPU-hogging processes | **✓** | freemium |
| 5 | [HWiNFO](https://www.hwinfo.com/) | **✓** (per-core sensors) | medium — several values as separate tray icons | **✓** — thresholds on any sensor | ✗ | free |
| 6 | [Libre Hardware Monitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | **✓** | medium — chosen sensors as numbers in tray | ✗ | ✗ | open source |
| 7 | [Core Temp](https://www.alcpu.com/CoreTemp/) | per-core (temperature) | medium — per-core temps in tray | yes (overheating) | ✗ | free |
| 8 | [TrafficMonitor](https://github.com/zhongyang219/TrafficMonitor) | ✗ (overall CPU) | high — floating window / taskbar embed | ✗ | ✗ | open source |
| 9 | [tray-monitor](https://github.com/strayge/tray-monitor) | ✗ | high — graph icons in tray | ✗ | top processes on click | open source |
| 10 | Performance Monitor / perfmon (built-in) | **✓** (`Processor Information` counters) | ✗ | **✓** — Data Collector Sets + event-log alerts | ✗ | built-in |

## Conclusions

1. **Per-core visualization** is offered by XMeters, HWiNFO and Libre Hardware Monitor — but none of
   them can *alert* specifically on a single core being under sustained load.
2. **Alerts** exist in Process Lasso (by process, not by core), HWiNFO (by sensor thresholds, without
   a duration condition and without a culprit) and perfmon (powerful, but an admin tool: no UI
   notifications, configured through Data Collector Sets).
3. **The culprit** is named only by Process Lasso, but its model is "a process exceeded its overall
   CPU quota" — not "core #5 has been busy at 100% for a minute."
4. The scenario *"one stuck core at low overall load"* is **covered by nobody**: on a 16-core CPU a
   hung single-threaded process is ~6% overall and triggers none of the tools above.

**CorePulse's niche** is to combine three things that already exist separately: a per-core tray icon
(like XMeters) + alerts with a duration condition (like ProBalance/perfmon) + naming the responsible
process (like Process Lasso), in one lightweight app that needs no administrator rights.

## Sources

- [How to Keep the Task Manager's CPU Stats In Your System Tray (How-To Geek)](https://www.howtogeek.com/685697/how-to-keep-the-task-managers-cpu-stats-in-your-system-tray/)
- [How to Set Up Monitoring to Alert on Windows High System Usage (How-To Geek)](https://www.howtogeek.com/devops/how-to-set-up-monitoring-to-alert-on-windows-high-system-usage/)
- [SysStatsTray on the Microsoft Store](https://apps.microsoft.com/detail/9nd57x1thnzm)
- [tray-monitor on GitHub](https://github.com/strayge/tray-monitor)
- [Display CPU usage in systray (TenForums)](https://www.tenforums.com/software-apps/197164-display-cpu-usage-systray.html)
