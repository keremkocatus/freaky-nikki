using System.IO;

namespace FreakyNikki.Diagnostics;

/// <summary>
/// Tiny, dependency-free rolling file log at %AppData%\FreakyNikki\log.txt.
/// Best-effort: logging must never throw into the app. When the file passes the
/// size cap it is rolled once to log.old.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Sync = new();
    private static string? _path;

    public static void Init(string directory)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "log.txt");
        }
        catch
        {
            _path = null;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            lock (Sync)
            {
                Roll();
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Never let logging break the app.
        }
    }

    private static void Roll()
    {
        try
        {
            var info = new FileInfo(_path!);
            if (info.Exists && info.Length > MaxBytes)
            {
                string old = _path + ".old";
                File.Delete(old);
                File.Move(_path!, old);
            }
        }
        catch
        {
            // If rolling fails, keep appending to the current file.
        }
    }
}
