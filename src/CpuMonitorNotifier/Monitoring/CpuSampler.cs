using System.Diagnostics;

namespace CpuMonitorNotifier.Monitoring;

/// <summary>
/// Снимает загрузку каждого логического ядра через счётчики "Processor Information".
/// Предпочитает "% Processor Utility" (корректен при turbo boost / parking),
/// при его отсутствии откатывается на "% Processor Time".
/// </summary>
internal sealed class CpuSampler : IDisposable
{
    private const string Category = "Processor Information";

    private readonly PerformanceCounter[] _coreCounters;
    private readonly PerformanceCounter _totalCounter;

    public int CoreCount => _coreCounters.Length;

    /// <summary>Последний снятый сэмпл: % загрузки по ядрам (0–100).</summary>
    public float[] CoreLoads { get; }

    /// <summary>Последняя общая загрузка CPU, %.</summary>
    public float TotalLoad { get; private set; }

    public CpuSampler()
    {
        var category = new PerformanceCounterCategory(Category);
        string counterName = category.CounterExists("% Processor Utility")
            ? "% Processor Utility"
            : "% Processor Time";

        // Инстансы вида "0,0", "0,1", ..., "0,_Total" (группа процессоров, номер ядра)
        var instances = category.GetInstanceNames();
        var coreInstances = instances
            .Where(n => !n.EndsWith("_Total", StringComparison.Ordinal))
            .OrderBy(ParseGroup).ThenBy(ParseIndex)
            .ToArray();
        string totalInstance = instances.First(n => n.EndsWith("_Total", StringComparison.Ordinal));

        _coreCounters = coreInstances
            .Select(n => new PerformanceCounter(Category, counterName, n, readOnly: true))
            .ToArray();
        _totalCounter = new PerformanceCounter(Category, counterName, totalInstance, readOnly: true);

        CoreLoads = new float[_coreCounters.Length];
        Sample(); // прогрев: первый NextValue() у PDH-счётчика всегда 0
    }

    /// <summary>Снимает свежие значения по всем ядрам. Вызывать не чаще раза в секунду.</summary>
    public void Sample()
    {
        try
        {
            for (int i = 0; i < _coreCounters.Length; i++)
                CoreLoads[i] = Math.Clamp(_coreCounters[i].NextValue(), 0f, 100f);
            TotalLoad = Math.Clamp(_totalCounter.NextValue(), 0f, 100f);
        }
        catch (InvalidOperationException)
        {
            // счётчик мог пропасть (например, после сна) — оставляем предыдущие значения
        }
    }

    private static int ParseGroup(string instance) => ParsePart(instance, 0);
    private static int ParseIndex(string instance) => ParsePart(instance, 1);

    private static int ParsePart(string instance, int part)
    {
        var parts = instance.Split(',');
        return parts.Length > part && int.TryParse(parts[part], out int v) ? v : 0;
    }

    public void Dispose()
    {
        foreach (var c in _coreCounters)
            c.Dispose();
        _totalCounter.Dispose();
    }
}
