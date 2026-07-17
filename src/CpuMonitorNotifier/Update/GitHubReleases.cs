using System.Text.Json;

namespace CpuMonitorNotifier.Update;

/// <summary>Чтение последнего релиза с GitHub. Разбор отделён от сети, чтобы его можно было тестировать.</summary>
internal static class GitHubReleases
{
    private const string LatestUrl = "https://api.github.com/repos/presetslrdev/CorePulse/releases/latest";
    private const string SumsAsset = "SHA256SUMS.txt";
    private const int Sha256HexLength = 64;

    /// <summary>
    /// Запрашивает последний релиз. null — офлайн, лимит запросов, релиз без нужных ассетов:
    /// неудачная проверка не является событием и никогда не показывается пользователю сама по себе.
    /// </summary>
    public static async Task<ReleaseInfo?> FetchLatestAsync(string assetName, CancellationToken ct)
    {
        try
        {
            using var http = CreateClient();
            var parsed = ParseLatest(await http.GetStringAsync(LatestUrl, ct), assetName);
            if (parsed is null) return null;

            string? hash = ParseSha256Sums(await http.GetStringAsync(parsed.SumsUrl, ct), assetName);
            return hash is null ? null : new ReleaseInfo(parsed.Version, parsed.AssetUrl, hash, parsed.PageUrl);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // нас отменили — это не сбой проверки, глушить нельзя
        }
        catch (Exception)
        {
            // офлайн, лимит запросов, битый релиз, таймаут HttpClient (тоже TaskCanceledException,
            // но без запроса отмены) — всё это сбои: проверка молча не состоялась
            return null;
        }
    }

    public static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) }; // self-contained ассет ~58 МБ
        // без User-Agent GitHub отвечает 403
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"CorePulse/{UpdateVersions.Current}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>Разбирает ответ /releases/latest. null — если тег не версия или нужных ассетов нет.</summary>
    public static ParsedRelease? ParseLatest(string json, string assetName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var version = UpdateVersions.ParseTag(GetString(root, "tag_name"));
            if (version is null) return null;

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            string? assetUrl = null, sumsUrl = null;
            foreach (var a in assets.EnumerateArray())
            {
                string? name = GetString(a, "name");
                string? url = GetString(a, "browser_download_url");
                if (name is null || url is null) continue;
                if (name == assetName) assetUrl = url;
                else if (name == SumsAsset) sumsUrl = url;
            }

            // без файла хешей обновляться нечем проверить — считаем релиз непригодным
            if (assetUrl is null || sumsUrl is null) return null;

            return new ParsedRelease(version, assetUrl, sumsUrl, GetString(root, "html_url") ?? "");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Достаёт хеш файла из SHA256SUMS.txt (формат coreutils: «&lt;hex&gt;␠␠&lt;имя&gt;»).</summary>
    public static string? ParseSha256Sums(string text, string fileName)
    {
        foreach (string line in text.Split('\n'))
        {
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !IsSha256Hex(parts[0])) continue;

            string name = parts[^1].TrimStart('*'); // coreutils помечает двоичный режим звёздочкой
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                return parts[0].ToLowerInvariant();
        }
        return null;
    }

    /// <summary>Ровно 64 шестнадцатеричных символа — иначе это не строка хеша, а что-то ещё.</summary>
    private static bool IsSha256Hex(string s)
    {
        if (s.Length != Sha256HexLength) return false;
        foreach (char c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    private static string? GetString(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
