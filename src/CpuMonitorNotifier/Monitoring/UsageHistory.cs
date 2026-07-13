using System.Text.Json;

namespace CpuMonitorNotifier.Monitoring;

/// <summary>Запись об алерте: когда, какие ядра, кто виновник, средняя нагрузка виновника и длительность.</summary>
internal sealed record AlertRecord(
    DateTime Time, int[] Cores, string Culprit, double CulpritCores, double DurationSeconds);

/// <summary>Накопленная нагрузка процесса за сессию: имя, суммарное core-время, пик, когда последний раз виден.</summary>
internal sealed record OffenderStat(string Name, double CoreSeconds, double PeakCores, DateTime LastSeen);

/// <summary>
/// История нагрузки. Две части:
/// 1) «Главные нагрузчики за сессию» — накопленное core-время по каждому процессу (ловит «тихого
///    пожирателя», который долго держит ядро даже на 20–30% и греет CPU); живёт в памяти сессии.
/// 2) Журнал алертов — сохраняется в %AppData%\CpuMonitorNotifier\history.json (последние N событий).
/// </summary>
internal sealed class UsageHistory
{
    private const int MaxAlerts = 100;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CpuMonitorNotifier", "history.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const int SeriesCap = 600; // ~10 минут при опросе раз в секунду

    // имя процесса -> накопленные core-секунды, пик (в ядрах), время последнего появления
    private readonly Dictionary<string, (double CoreSeconds, double Peak, DateTime Last)> _offenders = new();
    private readonly List<AlertRecord> _alerts;
    private readonly Queue<float> _series = new(); // нагрузка самого горячего ядра по времени (для графика)

    public UsageHistory() => _alerts = LoadAlerts();

    /// <summary>Добавляет сэмпл нагрузки самого горячего ядра в кольцевой буфер для графика.</summary>
    public void AddSample(float hottestLoad)
    {
        _series.Enqueue(hottestLoad);
        while (_series.Count > SeriesCap)
            _series.Dequeue();
    }

    /// <summary>Снимок таймлайна нагрузки самого горячего ядра (хронологически).</summary>
    public float[] Series() => _series.ToArray();

    /// <summary>Накапливает core-время процессов за прошедший интервал.</summary>
    public void Accumulate(IReadOnlyList<ProcessLoad> loads, int intervalSeconds, DateTime now)
    {
        foreach (var l in loads)
        {
            _offenders.TryGetValue(l.Name, out var cur);
            _offenders[l.Name] = (
                cur.CoreSeconds + l.Cores * intervalSeconds,
                Math.Max(cur.Peak, l.Cores),
                now);
        }
    }

    /// <summary>Топ процессов по накопленному core-времени за сессию.</summary>
    public IReadOnlyList<OffenderStat> TopOffenders(int count) =>
        _offenders
            .Select(kv => new OffenderStat(kv.Key, kv.Value.CoreSeconds, kv.Value.Peak, kv.Value.Last))
            .OrderByDescending(o => o.CoreSeconds)
            .Take(count)
            .ToList();

    /// <summary>Добавляет событие алерта и сохраняет журнал.</summary>
    public void AddAlert(AlertRecord record)
    {
        _alerts.Insert(0, record);
        if (_alerts.Count > MaxAlerts)
            _alerts.RemoveRange(MaxAlerts, _alerts.Count - MaxAlerts);
        SaveAlerts();
    }

    /// <summary>Журнал алертов, новые сверху.</summary>
    public IReadOnlyList<AlertRecord> Alerts => _alerts;

    private static List<AlertRecord> LoadAlerts()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<AlertRecord>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch
        {
            // повреждённый файл — начинаем с пустого журнала
        }
        return new();
    }

    private void SaveAlerts()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_alerts, JsonOptions));
        }
        catch
        {
            // не критично, если не удалось записать журнал
        }
    }
}
