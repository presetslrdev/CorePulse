namespace CpuMonitorNotifier.Update;

internal enum UpdateCheckStatus
{
    /// <summary>Установлена последняя версия.</summary>
    UpToDate,
    UpdateAvailable,
    /// <summary>Проверить не удалось (офлайн, лимит запросов, битый релиз).</summary>
    Failed,
    /// <summary>Сборка из исходников — обновление не поддерживается.</summary>
    NotSupported,
}

/// <summary>«Нет обновления» и «проверка не удалась» — разные исходы: ручная проверка сообщает оба, автоматическая — ни одного.</summary>
internal sealed record UpdateCheckResult(UpdateCheckStatus Status, ReleaseInfo? Release);

/// <summary>Проверка обновлений и загрузка нового файла с проверкой хеша.</summary>
internal sealed class UpdateService
{
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        string? asset = DistributionInfo.CurrentAssetName;
        if (asset is null)
            return new UpdateCheckResult(UpdateCheckStatus.NotSupported, null);

        var release = await GitHubReleases.FetchLatestAsync(asset, ct);
        if (release is null)
            return new UpdateCheckResult(UpdateCheckStatus.Failed, null);

        return UpdateVersions.IsNewer(UpdateVersions.Current, release.Version)
            ? new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, release)
            : new UpdateCheckResult(UpdateCheckStatus.UpToDate, null);
    }

    /// <summary>
    /// Качает ассет во временный файл и сверяет SHA256. Несовпадение — исключение, временный файл удалён.
    /// Проверка выполняется до того, как что-либо на диске будет тронуто.
    /// </summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken ct = default)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"CorePulse-{release.Version}-{Guid.NewGuid():N}.exe");
        try
        {
            await using (var src = await OpenAssetAsync(release.AssetUrl, ct))
            await using (var dst = File.Create(temp))
                await src.CopyToAsync(dst, ct);

            string actual = await FileHash.Sha256Async(temp, ct);
            if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA256 mismatch: expected {release.Sha256}, got {actual}");

            return temp;
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>HttpClient не умеет file://; локальный путь читаем напрямую.</summary>
    private static async Task<Stream> OpenAssetAsync(string url, CancellationToken ct)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile)
            return File.OpenRead(uri.LocalPath);

        var http = GitHubReleases.CreateClient();
        HttpResponseMessage? response = null;
        try
        {
            response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            return new HttpOwnedStream(await response.Content.ReadAsStreamAsync(ct), http, response);
        }
        catch
        {
            // EnsureSuccessStatusCode бросает уже после того, как ответ получен: освободить надо оба,
            // Dispose у HttpClient не закрывает выданные им ответы
            response?.Dispose();
            http.Dispose();
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* временный файл — не критично, ОС уберёт */ }
    }

    /// <summary>Держит HttpClient и ответ живыми, пока читается поток тела.</summary>
    private sealed class HttpOwnedStream(Stream inner, HttpClient client, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        // поток ответа последовательный: длина неизвестна при chunked-передаче, врать про неё нельзя
        public override long Length => throw new NotSupportedException();
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => inner.ReadAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
                client.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
