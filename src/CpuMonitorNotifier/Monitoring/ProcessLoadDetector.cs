namespace CpuMonitorNotifier.Monitoring;

/// <summary>Сработавший алерт по процессу: имя, текущее потребление в «ядрах», как долго держится.</summary>
internal sealed record ProcessAlert(string Name, double Cores, TimeSpan Duration);

/// <summary>
/// Ловит «тихого пожирателя»: процесс, который непрерывно держит не менее <see cref="ThresholdCores"/>
/// ядра дольше <see cref="DurationSeconds"/>. Именно такой процесс (условный Sublime на 20–30% одного
/// ядра) греет CPU и раскручивает вентиляторы, не давая пиков. Нагрузка агрегируется по имени процесса
/// (сумма по всем PID). Гистерезис снятия и cooldown повторов — как в <see cref="LoadDetector"/>.
/// </summary>
internal sealed class ProcessLoadDetector
{
    private const double HysteresisCores = 0.05;

    private sealed class State
    {
        public double HotSeconds;
        public bool Active;
        public DateTime LastNotified;
    }

    private readonly Dictionary<string, State> _state = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Порог в «ядрах» (1.0 = одно ядро целиком). 0.25 = 25% одного ядра.</summary>
    public double ThresholdCores { get; set; } = 0.25;
    public int DurationSeconds { get; set; } = 600;
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Поднимается один раз, когда процесс переходит в состояние длительной нагрузки.</summary>
    public event Action<ProcessAlert>? Alert;

    /// <summary>Обновляет состояние по свежему срезу нагрузки процессов. Вызывается раз в intervalSeconds.</summary>
    public void Update(IReadOnlyList<ProcessLoad> loads, int intervalSeconds, DateTime now)
    {
        // агрегируем «ядра» по имени процесса (у одного имени может быть много PID)
        var byName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in loads)
            byName[l.Name] = byName.TryGetValue(l.Name, out var c) ? c + l.Cores : l.Cores;

        foreach (var name in byName.Keys)
            if (!_state.ContainsKey(name))
                _state[name] = new State();

        var stale = new List<string>();
        foreach (var (name, st) in _state)
        {
            double cores = byName.TryGetValue(name, out var c) ? c : 0;

            if (cores >= ThresholdCores)
            {
                st.HotSeconds += intervalSeconds;
                if (!st.Active && st.HotSeconds >= DurationSeconds)
                {
                    st.Active = true;
                    if (now - st.LastNotified >= Cooldown)
                    {
                        st.LastNotified = now;
                        Alert?.Invoke(new ProcessAlert(name, cores, TimeSpan.FromSeconds(st.HotSeconds)));
                    }
                }
            }
            else if (cores < ThresholdCores - HysteresisCores)
            {
                st.HotSeconds = 0;
                st.Active = false;
                if (cores <= 0)
                    stale.Add(name); // процесс исчез/остыл — не держим запись
            }
        }

        foreach (var name in stale)
            _state.Remove(name);
    }
}
