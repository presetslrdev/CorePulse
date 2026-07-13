namespace CpuMonitorNotifier.Monitoring;

/// <summary>Данные сработавшего алерта.</summary>
internal sealed record LoadAlert(int[] Cores, TimeSpan Duration);

/// <summary>
/// Следит за каждым ядром: если нагрузка держится не ниже порога заданное время —
/// поднимает событие Alert. Гистерезис снятия и cooldown повторных срабатываний.
/// </summary>
internal sealed class LoadDetector
{
    private const float HysteresisPercent = 10f;

    private readonly int[] _hotSeconds;        // сколько секунд подряд ядро выше порога
    private readonly bool[] _alertActive;      // ядро в состоянии алерта
    private readonly double[] _heat;           // 0..1 — доля от порога длительности (для цвета иконки)
    private readonly DateTime[] _lastNotified; // для cooldown по каждому ядру

    public float ThresholdPercent { get; set; } = 90f;
    public int DurationSeconds { get; set; } = 60;
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Текущие состояния алертов по ядрам (для отрисовки иконки).</summary>
    public IReadOnlyList<bool> ActiveAlerts => _alertActive;

    /// <summary>
    /// «Нагретость» каждого ядра 0..1 = как долго оно под нагрузкой относительно порога длительности.
    /// Цвет иконки строится по ней, а не по мгновенному значению — короткий спайк остаётся «холодным».
    /// </summary>
    public IReadOnlyList<double> Heat => _heat;

    /// <summary>Поднимается один раз при переходе ядер в состояние длительной нагрузки.</summary>
    public event Action<LoadAlert>? Alert;

    public LoadDetector(int coreCount)
    {
        _hotSeconds = new int[coreCount];
        _alertActive = new bool[coreCount];
        _heat = new double[coreCount];
        _lastNotified = new DateTime[coreCount];
    }

    /// <summary>Обновляет состояние по свежему сэмплу. Вызывается раз в intervalSeconds.</summary>
    public void Update(IReadOnlyList<float> coreLoads, int intervalSeconds, DateTime now)
    {
        var triggered = new List<int>();

        for (int i = 0; i < coreLoads.Count; i++)
        {
            if (coreLoads[i] >= ThresholdPercent)
            {
                _hotSeconds[i] += intervalSeconds;
                if (!_alertActive[i] && _hotSeconds[i] >= DurationSeconds)
                {
                    _alertActive[i] = true;
                    if (now - _lastNotified[i] >= Cooldown)
                    {
                        triggered.Add(i);
                        _lastNotified[i] = now;
                    }
                }
            }
            else if (coreLoads[i] < ThresholdPercent - HysteresisPercent)
            {
                _hotSeconds[i] = 0;
                _alertActive[i] = false;
            }
            // между (порог−гистерезис) и порогом — состояние не меняем

            _heat[i] = Math.Clamp((double)_hotSeconds[i] / Math.Max(1, DurationSeconds), 0, 1);
        }

        if (triggered.Count > 0)
        {
            // в уведомление включаем и ядра, уже находящиеся в алерте (общая картина)
            var cores = Enumerable.Range(0, coreLoads.Count).Where(i => _alertActive[i]).ToArray();
            int maxSeconds = cores.Max(i => _hotSeconds[i]);
            Alert?.Invoke(new LoadAlert(cores, TimeSpan.FromSeconds(maxSeconds)));
        }
    }
}
