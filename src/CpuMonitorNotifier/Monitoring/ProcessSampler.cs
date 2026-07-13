using System.Diagnostics;

namespace CpuMonitorNotifier.Monitoring;

/// <summary>Процесс-кандидат: имя и потребление в «ядрах» (1.0 = одно полностью занятое ядро).</summary>
internal sealed record ProcessLoad(int Pid, string Name, double Cores);

/// <summary>
/// Считает потребление CPU процессами по дельтам TotalProcessorTime между вызовами Sample().
/// Windows не даёт per-process-per-core без ETW, поэтому виновников определяем эвристикой:
/// процесс, стабильно потребляющий ~N ядер при N нагруженных ядрах, — вероятный кандидат.
/// </summary>
internal sealed class ProcessSampler
{
    private Dictionary<int, (string Name, TimeSpan Cpu)> _previous = new();
    private DateTime _previousAt = DateTime.MinValue;
    private List<ProcessLoad> _lastLoads = new();

    /// <summary>Снимает срез CPU-времени всех процессов и пересчитывает дельты.</summary>
    public void Sample()
    {
        var now = DateTime.UtcNow;
        var current = new Dictionary<int, (string, TimeSpan)>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                current[p.Id] = (p.ProcessName, p.TotalProcessorTime);
            }
            catch
            {
                // Idle/System/защищённые процессы — недоступны без прав, пропускаем
            }
            finally
            {
                p.Dispose();
            }
        }

        double elapsed = (now - _previousAt).TotalSeconds;
        if (_previousAt != DateTime.MinValue && elapsed > 0.1)
        {
            var loads = new List<ProcessLoad>();
            foreach (var (pid, (name, cpu)) in current)
            {
                if (!_previous.TryGetValue(pid, out var prev))
                    continue; // новый процесс — дельты ещё нет
                double cores = (cpu - prev.Cpu).TotalSeconds / elapsed;
                if (cores > 0.05)
                    loads.Add(new ProcessLoad(pid, name, cores));
            }
            _lastLoads = loads.OrderByDescending(l => l.Cores).ToList();
        }

        _previous = current;
        _previousAt = now;
    }

    /// <summary>Top-N процессов по потреблению CPU за последний интервал.</summary>
    public IReadOnlyList<ProcessLoad> GetTopConsumers(int count = 3) =>
        _lastLoads.Take(count).ToList();

    /// <summary>Все процессы с заметной нагрузкой за последний интервал (для накопления истории).</summary>
    public IReadOnlyList<ProcessLoad> LastLoads => _lastLoads;
}
