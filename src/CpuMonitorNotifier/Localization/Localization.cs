using System.Globalization;

namespace CpuMonitorNotifier.Localization;

/// <summary>Язык интерфейса. <see cref="Auto"/> — по языку системы.</summary>
internal enum AppLanguage
{
    Auto,
    English,
    Russian,
    German,
    Spanish,
    French,
    Portuguese,
    Chinese,
    Japanese,
}

/// <summary>
/// Лёгкая локализация: словарь строк на язык, English как основной и запасной.
/// Ключи плоские (например, "menu.settings"); подстановки — через string.Format.
/// </summary>
internal static class Loc
{
    /// <summary>Название продукта (не переводится).</summary>
    public const string AppName = "CorePulse";

    private static Dictionary<string, string> _current = English;

    /// <summary>Родные названия языков для выпадающего списка (Auto подставляется отдельно).</summary>
    public static readonly (AppLanguage Lang, string Native)[] LanguageChoices =
    {
        (AppLanguage.English, "English"),
        (AppLanguage.Russian, "Русский"),
        (AppLanguage.German, "Deutsch"),
        (AppLanguage.Spanish, "Español"),
        (AppLanguage.French, "Français"),
        (AppLanguage.Portuguese, "Português"),
        (AppLanguage.Chinese, "中文"),
        (AppLanguage.Japanese, "日本語"),
    };

    /// <summary>Устанавливает текущий язык (для Auto определяет по CurrentUICulture).</summary>
    public static void Apply(AppLanguage lang)
    {
        if (lang == AppLanguage.Auto)
            lang = Detect();
        _current = Tables.TryGetValue(lang, out var t) ? t : English;
    }

    /// <summary>Возвращает строку по ключу; при отсутствии — English, затем сам ключ.</summary>
    public static string T(string key) =>
        _current.TryGetValue(key, out var v) ? v
        : English.TryGetValue(key, out var e) ? e
        : key;

    private static AppLanguage Detect() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "ru" => AppLanguage.Russian,
        "de" => AppLanguage.German,
        "es" => AppLanguage.Spanish,
        "fr" => AppLanguage.French,
        "pt" => AppLanguage.Portuguese,
        "zh" => AppLanguage.Chinese,
        "ja" => AppLanguage.Japanese,
        _ => AppLanguage.English,
    };

    private static readonly Dictionary<string, string> English = new()
    {
        ["app.paused"] = "{0} — paused",
        ["menu.settings"] = "Settings…",
        ["menu.test"] = "Test notification",
        ["menu.pause"] = "Pause monitoring",
        ["menu.exit"] = "Exit",
        ["settings.title"] = "{0} — Settings",
        ["settings.iconStyle"] = "Tray icon style:",
        ["settings.threshold"] = "Core load threshold, %:",
        ["settings.duration"] = "Time before alert, s:",
        ["settings.cooldown"] = "Cooldown between alerts, min:",
        ["settings.pollInterval"] = "Poll interval, s:",
        ["settings.notifications"] = "Show notifications",
        ["settings.autostart"] = "Start with Windows",
        ["settings.language"] = "Language:",
        ["settings.languageAuto"] = "System default",
        ["settings.ok"] = "OK",
        ["settings.cancel"] = "Cancel",
        ["style.ring"] = "Ring + %",
        ["style.segments"] = "Segmented ring",
        ["style.speedometer"] = "Speedometer",
        ["style.liquid"] = "Liquid + %",
        ["style.dots"] = "Dots grid",
        ["toast.title.one"] = "Core {0} under load for {1}",
        ["toast.title.many"] = "Cores {0} under load for {1}",
        ["toast.culprit"] = "Likely culprit: {0}",
        ["toast.culprit.none"] = "Culprit not identified (possibly a system or protected process)",
        ["toast.button.taskmgr"] = "Task Manager",
        ["toast.core.load"] = "{0} ({1}% of a core)",
        ["tooltip.proc"] = "Core {0}: {1}% | CPU {2}% | {3}",
        ["tooltip.noproc"] = "Core {0}: {1}% | CPU {2}%",
        ["duration.min"] = "{0} min",
        ["duration.sec"] = "{0} s",
        ["error.startFailed"] = "Failed to start monitoring:\n\n{0}",
    };

    private static readonly Dictionary<string, string> Russian = new()
    {
        ["app.paused"] = "{0} — пауза",
        ["menu.settings"] = "Настройки…",
        ["menu.test"] = "Проверить уведомление",
        ["menu.pause"] = "Пауза мониторинга",
        ["menu.exit"] = "Выход",
        ["settings.title"] = "{0} — настройки",
        ["settings.iconStyle"] = "Стиль иконки в трее:",
        ["settings.threshold"] = "Порог нагрузки ядра, %:",
        ["settings.duration"] = "Длительность до алерта, с:",
        ["settings.cooldown"] = "Пауза между уведомлениями, мин:",
        ["settings.pollInterval"] = "Интервал опроса, с:",
        ["settings.notifications"] = "Показывать уведомления",
        ["settings.autostart"] = "Запускать при входе в Windows",
        ["settings.language"] = "Язык:",
        ["settings.languageAuto"] = "Как в системе",
        ["settings.ok"] = "ОК",
        ["settings.cancel"] = "Отмена",
        ["style.ring"] = "Кольцо + %",
        ["style.segments"] = "Сегментное кольцо",
        ["style.speedometer"] = "Спидометр",
        ["style.liquid"] = "Жидкость + %",
        ["style.dots"] = "Сетка кругов",
        ["toast.title.one"] = "Ядро {0} под нагрузкой уже {1}",
        ["toast.title.many"] = "Ядра {0} под нагрузкой уже {1}",
        ["toast.culprit"] = "Вероятный виновник: {0}",
        ["toast.culprit.none"] = "Виновник не определён (возможно, системный или защищённый процесс)",
        ["toast.button.taskmgr"] = "Диспетчер задач",
        ["toast.core.load"] = "{0} ({1}% ядра)",
        ["tooltip.proc"] = "Ядро {0}: {1}% | CPU {2}% | {3}",
        ["tooltip.noproc"] = "Ядро {0}: {1}% | CPU {2}%",
        ["duration.min"] = "{0} мин",
        ["duration.sec"] = "{0} с",
        ["error.startFailed"] = "Не удалось запустить мониторинг:\n\n{0}",
    };

    private static readonly Dictionary<string, string> German = new()
    {
        ["app.paused"] = "{0} — pausiert",
        ["menu.settings"] = "Einstellungen…",
        ["menu.test"] = "Testbenachrichtigung",
        ["menu.pause"] = "Überwachung pausieren",
        ["menu.exit"] = "Beenden",
        ["settings.title"] = "{0} — Einstellungen",
        ["settings.iconStyle"] = "Symbolstil in der Taskleiste:",
        ["settings.threshold"] = "Kernauslastungsschwelle, %:",
        ["settings.duration"] = "Zeit bis zur Warnung, s:",
        ["settings.cooldown"] = "Pause zwischen Warnungen, Min.:",
        ["settings.pollInterval"] = "Abtastintervall, s:",
        ["settings.notifications"] = "Benachrichtigungen anzeigen",
        ["settings.autostart"] = "Mit Windows starten",
        ["settings.language"] = "Sprache:",
        ["settings.languageAuto"] = "Systemstandard",
        ["settings.ok"] = "OK",
        ["settings.cancel"] = "Abbrechen",
        ["style.ring"] = "Ring + %",
        ["style.segments"] = "Segmentring",
        ["style.speedometer"] = "Tacho",
        ["style.liquid"] = "Flüssigkeit + %",
        ["style.dots"] = "Punktraster",
        ["toast.title.one"] = "Kern {0} seit {1} ausgelastet",
        ["toast.title.many"] = "Kerne {0} seit {1} ausgelastet",
        ["toast.culprit"] = "Wahrscheinlicher Verursacher: {0}",
        ["toast.culprit.none"] = "Verursacher nicht ermittelt (evtl. System- oder geschützter Prozess)",
        ["toast.button.taskmgr"] = "Task-Manager",
        ["toast.core.load"] = "{0} ({1}% eines Kerns)",
        ["tooltip.proc"] = "Kern {0}: {1}% | CPU {2}% | {3}",
        ["tooltip.noproc"] = "Kern {0}: {1}% | CPU {2}%",
        ["duration.min"] = "{0} Min.",
        ["duration.sec"] = "{0} s",
        ["error.startFailed"] = "Überwachung konnte nicht gestartet werden:\n\n{0}",
    };

    private static readonly Dictionary<string, string> Spanish = new()
    {
        ["app.paused"] = "{0} — en pausa",
        ["menu.settings"] = "Configuración…",
        ["menu.test"] = "Probar notificación",
        ["menu.pause"] = "Pausar monitorización",
        ["menu.exit"] = "Salir",
        ["settings.title"] = "{0} — Configuración",
        ["settings.iconStyle"] = "Estilo del icono en la bandeja:",
        ["settings.threshold"] = "Umbral de carga del núcleo, %:",
        ["settings.duration"] = "Tiempo antes de la alerta, s:",
        ["settings.cooldown"] = "Espera entre alertas, min:",
        ["settings.pollInterval"] = "Intervalo de sondeo, s:",
        ["settings.notifications"] = "Mostrar notificaciones",
        ["settings.autostart"] = "Iniciar con Windows",
        ["settings.language"] = "Idioma:",
        ["settings.languageAuto"] = "Predeterminado del sistema",
        ["settings.ok"] = "Aceptar",
        ["settings.cancel"] = "Cancelar",
        ["style.ring"] = "Anillo + %",
        ["style.segments"] = "Anillo segmentado",
        ["style.speedometer"] = "Velocímetro",
        ["style.liquid"] = "Líquido + %",
        ["style.dots"] = "Cuadrícula de puntos",
        ["toast.title.one"] = "Núcleo {0} con carga desde hace {1}",
        ["toast.title.many"] = "Núcleos {0} con carga desde hace {1}",
        ["toast.culprit"] = "Probable causante: {0}",
        ["toast.culprit.none"] = "Causante no identificado (posible proceso del sistema o protegido)",
        ["toast.button.taskmgr"] = "Administrador de tareas",
        ["toast.core.load"] = "{0} ({1}% de un núcleo)",
        ["tooltip.proc"] = "Núcleo {0}: {1}% | CPU {2}% | {3}",
        ["tooltip.noproc"] = "Núcleo {0}: {1}% | CPU {2}%",
        ["duration.min"] = "{0} min",
        ["duration.sec"] = "{0} s",
        ["error.startFailed"] = "No se pudo iniciar la monitorización:\n\n{0}",
    };

    private static readonly Dictionary<string, string> French = new()
    {
        ["app.paused"] = "{0} — en pause",
        ["menu.settings"] = "Paramètres…",
        ["menu.test"] = "Tester la notification",
        ["menu.pause"] = "Suspendre la surveillance",
        ["menu.exit"] = "Quitter",
        ["settings.title"] = "{0} — Paramètres",
        ["settings.iconStyle"] = "Style de l'icône dans la barre d'état :",
        ["settings.threshold"] = "Seuil de charge du cœur, % :",
        ["settings.duration"] = "Délai avant alerte, s :",
        ["settings.cooldown"] = "Délai entre alertes, min :",
        ["settings.pollInterval"] = "Intervalle de sondage, s :",
        ["settings.notifications"] = "Afficher les notifications",
        ["settings.autostart"] = "Lancer au démarrage de Windows",
        ["settings.language"] = "Langue :",
        ["settings.languageAuto"] = "Paramètre système",
        ["settings.ok"] = "OK",
        ["settings.cancel"] = "Annuler",
        ["style.ring"] = "Anneau + %",
        ["style.segments"] = "Anneau segmenté",
        ["style.speedometer"] = "Compteur",
        ["style.liquid"] = "Liquide + %",
        ["style.dots"] = "Grille de points",
        ["toast.title.one"] = "Cœur {0} sous charge depuis {1}",
        ["toast.title.many"] = "Cœurs {0} sous charge depuis {1}",
        ["toast.culprit"] = "Coupable probable : {0}",
        ["toast.culprit.none"] = "Coupable non identifié (processus système ou protégé possible)",
        ["toast.button.taskmgr"] = "Gestionnaire des tâches",
        ["toast.core.load"] = "{0} ({1}% d'un cœur)",
        ["tooltip.proc"] = "Cœur {0} : {1}% | CPU {2}% | {3}",
        ["tooltip.noproc"] = "Cœur {0} : {1}% | CPU {2}%",
        ["duration.min"] = "{0} min",
        ["duration.sec"] = "{0} s",
        ["error.startFailed"] = "Impossible de démarrer la surveillance :\n\n{0}",
    };

    private static readonly Dictionary<string, string> Portuguese = new()
    {
        ["app.paused"] = "{0} — pausado",
        ["menu.settings"] = "Configurações…",
        ["menu.test"] = "Testar notificação",
        ["menu.pause"] = "Pausar monitoramento",
        ["menu.exit"] = "Sair",
        ["settings.title"] = "{0} — Configurações",
        ["settings.iconStyle"] = "Estilo do ícone na bandeja:",
        ["settings.threshold"] = "Limite de carga do núcleo, %:",
        ["settings.duration"] = "Tempo até o alerta, s:",
        ["settings.cooldown"] = "Intervalo entre alertas, min:",
        ["settings.pollInterval"] = "Intervalo de sondagem, s:",
        ["settings.notifications"] = "Mostrar notificações",
        ["settings.autostart"] = "Iniciar com o Windows",
        ["settings.language"] = "Idioma:",
        ["settings.languageAuto"] = "Padrão do sistema",
        ["settings.ok"] = "OK",
        ["settings.cancel"] = "Cancelar",
        ["style.ring"] = "Anel + %",
        ["style.segments"] = "Anel segmentado",
        ["style.speedometer"] = "Velocímetro",
        ["style.liquid"] = "Líquido + %",
        ["style.dots"] = "Grade de pontos",
        ["toast.title.one"] = "Núcleo {0} sob carga há {1}",
        ["toast.title.many"] = "Núcleos {0} sob carga há {1}",
        ["toast.culprit"] = "Provável causador: {0}",
        ["toast.culprit.none"] = "Causador não identificado (possível processo do sistema ou protegido)",
        ["toast.button.taskmgr"] = "Gerenciador de Tarefas",
        ["toast.core.load"] = "{0} ({1}% de um núcleo)",
        ["tooltip.proc"] = "Núcleo {0}: {1}% | CPU {2}% | {3}",
        ["tooltip.noproc"] = "Núcleo {0}: {1}% | CPU {2}%",
        ["duration.min"] = "{0} min",
        ["duration.sec"] = "{0} s",
        ["error.startFailed"] = "Não foi possível iniciar o monitoramento:\n\n{0}",
    };

    private static readonly Dictionary<string, string> Chinese = new()
    {
        ["app.paused"] = "{0} — 已暂停",
        ["menu.settings"] = "设置…",
        ["menu.test"] = "测试通知",
        ["menu.pause"] = "暂停监控",
        ["menu.exit"] = "退出",
        ["settings.title"] = "{0} — 设置",
        ["settings.iconStyle"] = "托盘图标样式：",
        ["settings.threshold"] = "单核负载阈值 (%)：",
        ["settings.duration"] = "触发提醒前的持续时间 (秒)：",
        ["settings.cooldown"] = "两次提醒之间的间隔 (分钟)：",
        ["settings.pollInterval"] = "采样间隔 (秒)：",
        ["settings.notifications"] = "显示通知",
        ["settings.autostart"] = "开机时启动",
        ["settings.language"] = "语言：",
        ["settings.languageAuto"] = "跟随系统",
        ["settings.ok"] = "确定",
        ["settings.cancel"] = "取消",
        ["style.ring"] = "圆环 + %",
        ["style.segments"] = "分段圆环",
        ["style.speedometer"] = "仪表盘",
        ["style.liquid"] = "液体 + %",
        ["style.dots"] = "点阵",
        ["toast.title.one"] = "核心 {0} 已高负载 {1}",
        ["toast.title.many"] = "核心 {0} 已高负载 {1}",
        ["toast.culprit"] = "可能的原因：{0}",
        ["toast.culprit.none"] = "无法确定原因（可能是系统或受保护的进程）",
        ["toast.button.taskmgr"] = "任务管理器",
        ["toast.core.load"] = "{0}（占用一个核心的 {1}%）",
        ["tooltip.proc"] = "核心 {0}：{1}% | CPU {2}% | {3}",
        ["tooltip.noproc"] = "核心 {0}：{1}% | CPU {2}%",
        ["duration.min"] = "{0} 分钟",
        ["duration.sec"] = "{0} 秒",
        ["error.startFailed"] = "无法启动监控：\n\n{0}",
    };

    private static readonly Dictionary<string, string> Japanese = new()
    {
        ["app.paused"] = "{0} — 一時停止中",
        ["menu.settings"] = "設定…",
        ["menu.test"] = "通知をテスト",
        ["menu.pause"] = "監視を一時停止",
        ["menu.exit"] = "終了",
        ["settings.title"] = "{0} — 設定",
        ["settings.iconStyle"] = "トレイアイコンのスタイル：",
        ["settings.threshold"] = "コア負荷のしきい値 (%)：",
        ["settings.duration"] = "通知までの時間 (秒)：",
        ["settings.cooldown"] = "通知の間隔 (分)：",
        ["settings.pollInterval"] = "取得間隔 (秒)：",
        ["settings.notifications"] = "通知を表示する",
        ["settings.autostart"] = "Windows 起動時に実行",
        ["settings.language"] = "言語：",
        ["settings.languageAuto"] = "システムの既定",
        ["settings.ok"] = "OK",
        ["settings.cancel"] = "キャンセル",
        ["style.ring"] = "リング + %",
        ["style.segments"] = "セグメントリング",
        ["style.speedometer"] = "スピードメーター",
        ["style.liquid"] = "リキッド + %",
        ["style.dots"] = "ドットグリッド",
        ["toast.title.one"] = "コア {0} が {1} 高負荷です",
        ["toast.title.many"] = "コア {0} が {1} 高負荷です",
        ["toast.culprit"] = "原因の可能性: {0}",
        ["toast.culprit.none"] = "原因を特定できません（システムまたは保護されたプロセスの可能性）",
        ["toast.button.taskmgr"] = "タスク マネージャー",
        ["toast.core.load"] = "{0}（コアの {1}%）",
        ["tooltip.proc"] = "コア {0}: {1}% | CPU {2}% | {3}",
        ["tooltip.noproc"] = "コア {0}: {1}% | CPU {2}%",
        ["duration.min"] = "{0} 分",
        ["duration.sec"] = "{0} 秒",
        ["error.startFailed"] = "監視を開始できませんでした：\n\n{0}",
    };

    private static readonly Dictionary<AppLanguage, Dictionary<string, string>> Tables = new()
    {
        [AppLanguage.English] = English,
        [AppLanguage.Russian] = Russian,
        [AppLanguage.German] = German,
        [AppLanguage.Spanish] = Spanish,
        [AppLanguage.French] = French,
        [AppLanguage.Portuguese] = Portuguese,
        [AppLanguage.Chinese] = Chinese,
        [AppLanguage.Japanese] = Japanese,
    };
}
