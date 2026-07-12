# Архитектура

## Стек

- **C# / .NET 10**, WinForms как хост tray-приложения: `ApplicationContext` без главного окна,
  единственное окно — настройки.
- **Уведомления**: пакет `Microsoft.Toolkit.Uwp.Notifications` — Windows Toast для
  unpackaged Win32-приложений (Windows 10/11).
- Права администратора **не требуются**.

## Модули

```
Program.cs               single-instance mutex → Application.Run(TrayAppContext)
App/TrayAppContext.cs    композиция: таймер 1 c → CpuSampler → LoadDetector → Renderer/Notifier
Monitoring/
  CpuSampler.cs          per-core загрузка через PerformanceCounter
  ProcessSampler.cs      дельты CPU-времени процессов → top-кандидаты
  LoadDetector.cs        скользящее окно, гистерезис, cooldown → событие Alert
Tray/TrayIconRenderer.cs GDI+ рендер per-core иконки + tooltip
Notifications/ToastNotifier.cs
Settings/
  AppSettings.cs         JSON в %AppData%\CpuMonitorNotifier\settings.json
  AutoStart.cs           HKCU\Software\Microsoft\Windows\CurrentVersion\Run
  SettingsForm.cs        окно настроек
```

## Ключевые решения

### Сбор per-core нагрузки

Счётчики `Processor Information(*)\% Processor Utility` — в отличие от `% Processor Time`
корректно учитывают turbo boost и parking на современных CPU (могут показывать >100%,
клампим до 100). Fallback на `% Processor Time`, если `% Processor Utility` недоступен.
Опрос раз в 1 секунду; первый снятый сэмпл PDH-счётчика всегда 0 — прогреваем при старте.

### Атрибуция процесса к ядру

Windows не даёт per-process-per-core статистику без ETW (а ETW CPU sampling требует админа).
Используем эвристику, достаточную для целевого сценария:

1. Раз в секунду снимаем `Process.TotalProcessorTime` всех процессов, считаем дельту за окно —
   получаем нагрузку каждого процесса в «ядрах» (1.0 = одно полностью занятое ядро).
2. При алерте по N нагруженным ядрам показываем top-процессы, чья нагрузка ≈ N ядер
   (в первую очередь тех, у кого она кратна целому числу ядер — типично для busy-loop).
3. Для зависшего однопоточного процесса (главный сценарий) эвристика практически точна:
   один процесс ест ровно ~1 ядро.

Точная привязка через ETW (`Microsoft.Diagnostics.Tracing.TraceEvent`) — возможное развитие,
за флагом в настройках и с запросом elevation.

### Детекция «длительной нагрузки»

Для каждого логического ядра — скользящее окно сэмплов:

- **срабатывание**: среднее ≥ `ThresholdPercent` (по умолчанию 90%) непрерывно в течение
  `DurationSeconds` (по умолчанию 60 с);
- **гистерезис снятия**: алерт снимается при падении ниже `ThresholdPercent − 10`;
- **cooldown**: повторное уведомление по тому же ядру не чаще `CooldownMinutes` (по умолчанию 5 мин).

### Иконка в трее

- 64×64 битмап, GDI+ с антиалиасингом (Windows масштабирует под трей); главная величина —
  нагрузка самого горячего ядра крупным числом и цветом (зелёный/жёлтый/красный).
- Пять переключаемых стилей (`TrayIconStyle`): `Ring`, `Segments`, `Speedometer`, `Liquid`, `Dots`.
  Для `Segments`/`Dots` при большом числе ядер — попарная агрегация (максимум пары).
- **«Живость»**: рендер идёт отдельным таймером ~125 мс (≈8 кадров/с) с фазой анимации от
  `Stopwatch`, тогда как сбор данных остаётся раз в секунду. Это даёт плавную волну у `Liquid`
  и пульсацию кольца в алерте, не нагружая сбор метрик.
- Tooltip (≤127 символов, ограничение NotifyIcon): `Ядро 5: 98% | CPU 43% | ffmpeg.exe`.
- После `Icon.FromHandle(bitmap.GetHicon())` обязателен `DestroyIcon` — иначе утечка
  GDI-хендлов (проверено: при 8 кадрах/с число GDI-объектов стабильно).

### Настройки

JSON (`System.Text.Json`) в `%AppData%\CpuMonitorNotifier\settings.json`:
порог, длительность, cooldown, вкл/выкл уведомлений, интервал опроса, автозапуск.
Автозапуск — значение в `HKCU\...\Run` (без задач планировщика и без админа).
