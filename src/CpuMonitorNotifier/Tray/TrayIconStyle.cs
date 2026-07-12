namespace CpuMonitorNotifier.Tray;

/// <summary>Стиль отрисовки иконки в трее.</summary>
internal enum TrayIconStyle
{
    /// <summary>Кольцо-индикатор нагрузки макс. ядра + число в центре.</summary>
    Ring,

    /// <summary>Сегментное кольцо: сегмент на ядро, самое горячее подсвечено + число в центре.</summary>
    Segments,

    /// <summary>Шкала-тахометр на 270° + число.</summary>
    Speedometer,

    /// <summary>«Жидкость»: контейнер наливается до уровня нагрузки, анимированная волна + число.</summary>
    Liquid,

    /// <summary>Сетка кругов, по одному на ядро (обзор всех ядер).</summary>
    Dots,
}
