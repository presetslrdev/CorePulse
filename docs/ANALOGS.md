# Ревью аналогов

Обзор существующих инструментов мониторинга CPU для Windows — что они умеют и чего не хватает
для нашего сценария: *«заметить, что отдельное ядро долго под нагрузкой, и назвать виновника»*.

## Сравнительная таблица

| # | Аналог | Per-core нагрузка | Информативность трея | Алерты о длительной нагрузке | Показ процесса-виновника | Цена |
|---|--------|-------------------|----------------------|------------------------------|--------------------------|------|
| 1 | Диспетчер задач Windows (иконка в трее) | нет (общий %) | минимальная — одна зелёная шкала | нет | нет (только вручную) | встроен |
| 2 | [XMeters](https://entropy6.com/xmeters/) | **да** (бары в taskbar) | высокая — per-core бары прямо в панели задач | нет | нет | free (personal) |
| 3 | [SysStatsTray](https://apps.microsoft.com/detail/9nd57x1thnzm) | нет | средняя — динамические иконки, цвет зависит от нагрузки | нет | нет | free |
| 4 | [Process Lasso](https://bitsum.com/) (ProBalance) | нет | средняя — график в трее | **да** — реагирует на процессы, злоупотребляющие CPU | **да** | freemium |
| 5 | [HWiNFO](https://www.hwinfo.com/) | **да** (сенсоры каждого ядра) | средняя — можно вывести несколько значений отдельными tray-иконками | **да** — пороги по любому сенсору | нет | free |
| 6 | [Libre Hardware Monitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | **да** | средняя — выбранные сенсоры цифрами в трее | нет | нет | open source |
| 7 | [Core Temp](https://www.alcpu.com/CoreTemp/) | per-core (температура) | средняя — температуры ядер в трее | да (перегрев) | нет | free |
| 8 | [TrafficMonitor](https://github.com/zhongyang219/TrafficMonitor) | нет (общий CPU) | высокая — плавающее окно / встройка в taskbar | нет | нет | open source |
| 9 | [tray-monitor](https://github.com/strayge/tray-monitor) | нет | высокая — графики-иконки в трее | нет | top-процессы по клику | open source |
| 10 | Performance Monitor / perfmon (встроенный) | **да** (счётчики `Processor Information`) | нет | **да** — Data Collector Sets + алерты в журнал событий | нет | встроен |

## Выводы

1. **Per-core визуализацию** дают XMeters, HWiNFO, Libre Hardware Monitor — но ни один из них
   не умеет *алертить* именно по длительной нагрузке отдельного ядра.
2. **Алерты** есть у Process Lasso (по процессам, не по ядрам), HWiNFO (по порогам сенсоров,
   без длительности и без виновника) и perfmon (мощно, но это инструмент администратора:
   без уведомлений в UI, настройка через Data Collector Sets).
3. **Виновника** называет только Process Lasso, но его модель — «процесс превысил общую квоту CPU»,
   а не «ядро №5 занято на 100% уже минуту».
4. Сценарий «одно залипшее ядро при низкой общей загрузке» **не покрыт никем**: на 16-ядерном CPU
   зависший однопоточный процесс даёт ~6% общей загрузки и не триггерит ни один из инструментов выше.

**Ниша CPU Monitor Notifier** — совместить три вещи, которые по отдельности уже существуют:
per-core иконка в трее (как XMeters) + алерты с условием длительности (как ProBalance/perfmon) +
указание процесса-виновника (как Process Lasso), в одном лёгком приложении без прав администратора.

## Источники

- [How to Keep the Task Manager's CPU Stats In Your System Tray (How-To Geek)](https://www.howtogeek.com/685697/how-to-keep-the-task-managers-cpu-stats-in-your-system-tray/)
- [How to Set Up Monitoring to Alert on Windows High System Usage (How-To Geek)](https://www.howtogeek.com/devops/how-to-set-up-monitoring-to-alert-on-windows-high-system-usage/)
- [SysStatsTray в Microsoft Store](https://apps.microsoft.com/detail/9nd57x1thnzm)
- [tray-monitor на GitHub](https://github.com/strayge/tray-monitor)
- [Display CPU usage in systray (TenForums)](https://www.tenforums.com/software-apps/197164-display-cpu-usage-systray.html)
