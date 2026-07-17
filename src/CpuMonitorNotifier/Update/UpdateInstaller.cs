using System.Diagnostics;

namespace CpuMonitorNotifier.Update;

/// <summary>
/// Подмена собственного .exe. Опирается на поведение Windows: работающий файл разрешено
/// переименовать, но запрещено удалять — поэтому отдельный процесс-апдейтер не нужен,
/// а старый файл убирается при следующем запуске.
/// </summary>
internal static class UpdateInstaller
{
    private const string OldSuffix = ".old.exe";
    private const int RollbackAttempts = 3;
    private const int RollbackDelayMs = 150;

    public static string CurrentExePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

    /// <summary>Подменять можно, только если каталог с .exe доступен на запись (иначе — Program Files и т.п.).</summary>
    public static bool CanSwap()
    {
        string? dir = Path.GetDirectoryName(CurrentExePath);
        // без каталога проверять нечего: пустая строка увела бы проверку на текущий рабочий каталог
        return dir is not null && IsDirectoryWritable(dir);
    }

    public static bool IsDirectoryWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".corepulse-write-probe-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Путь, куда уезжает прежний .exe: CorePulse.exe → CorePulse.old.exe.</summary>
    public static string OldPathFor(string exePath) => Path.ChangeExtension(exePath, null) + OldSuffix;

    /// <summary>
    /// Переименовывает текущий .exe в *.old.exe и ставит на его место новый файл.
    /// Если второй шаг не удался — возвращает прежнее имя, установка остаётся рабочей.
    /// </summary>
    public static void SwapFiles(string exePath, string newFile)
    {
        string old = OldPathFor(exePath);
        TryDelete(old); // остаток прошлого обновления, если его не удалось убрать при старте

        File.Move(exePath, old);
        try
        {
            File.Move(newFile, exePath);
        }
        catch (Exception swapFailure)
        {
            Rollback(old, exePath, swapFailure);
            throw; // откат удался — наружу уходит исходная причина
        }
    }

    /// <summary>Подменяет .exe и перезапускает приложение. Вызывать из UI-потока.</summary>
    public static void ApplyAndRestart(string newFile)
    {
        string exe = CurrentExePath;
        SwapFiles(exe, newFile);

        // новый экземпляр стартует, пока этот ещё жив, — он подождёт выхода по --updated
        Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Arguments = $"--updated {Environment.ProcessId}",
        });

        Application.Exit();
    }

    /// <summary>
    /// Возвращает прежний .exe на место. Единственный момент, когда пользователь может остаться
    /// вообще без файла, поэтому промах здесь важнее исходной ошибки: типовая причина —
    /// антивирус, открывший только что переименованный файл, и он обычно отпускает. Если не
    /// отпустил — говорим прямо, где лежит рабочий файл, вместо молчаливого исчезновения.
    /// </summary>
    internal static void Rollback(string old, string exePath, Exception cause)
    {
        for (int attempt = 1; attempt <= RollbackAttempts; attempt++)
        {
            try
            {
                File.Move(old, exePath);
                return;
            }
            catch (Exception rollbackFailure)
            {
                if (attempt == RollbackAttempts)
                    throw new IOException(
                        $"Update failed and the original could not be restored automatically. " +
                        $"Your working CorePulse is at '{old}' — rename it back to '{exePath}'.",
                        new AggregateException(cause, rollbackFailure));

                Thread.Sleep(RollbackDelayMs);
            }
        }
    }

    /// <summary>Убирает остаток прошлого обновления. Раньше этого момента удалить файл было нельзя.</summary>
    public static void CleanupOldFile() => TryDelete(OldPathFor(CurrentExePath));

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* файл ещё занят — попробуем при следующем запуске */ }
        catch (UnauthorizedAccessException) { /* нет прав на удаление — не критично */ }
    }
}
