namespace CpuMonitorNotifier.Update;

/// <summary>Готовый к использованию релиз: что качать, чем проверить, куда отправить пользователя.</summary>
internal sealed record ReleaseInfo(Version Version, string AssetUrl, string Sha256, string PageUrl);

/// <summary>Промежуточный разбор ответа GitHub: хеш ещё не загружен, известна лишь ссылка на SHA256SUMS.txt.</summary>
internal sealed record ParsedRelease(Version Version, string AssetUrl, string SumsUrl, string PageUrl);
