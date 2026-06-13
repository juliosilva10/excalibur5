using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace Excalibur5.Services;

public static class AppLogger
{
    private static readonly string LogPath;
    private static readonly string PrevLogPath;
    private static readonly BlockingCollection<string> _queue = new(boundedCapacity: 2048);
    private const long MaxBytes = 5 * 1024 * 1024;

    public static event Action<string>? LogEntryAdded;

    static AppLogger()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Excalibur5", "logs");
        Directory.CreateDirectory(dir);
        LogPath = Path.Combine(dir, "excalibur5.log");
        PrevLogPath = Path.Combine(dir, "excalibur5.prev.log");

        // Roll the previous session's log aside instead of wiping it — the prior log
        // is exactly what's needed to diagnose an unexpected exit.
        Rotate();

        var thread = new Thread(WriteLoop) { IsBackground = true, Name = "AppLogger" };
        thread.Start();

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _queue.CompleteAdding();
            thread.Join(millisecondsTimeout: 2000);
        };
    }

    // Moves the current log to excalibur5.prev.log (overwriting any older prev), leaving
    // a fresh empty current log. Best-effort: failures are swallowed.
    private static void Rotate()
    {
        try
        {
            if (File.Exists(LogPath))
                File.Move(LogPath, PrevLogPath, overwrite: true);
        }
        catch { }
    }

    public static void Info(string source, string message)  => Enqueue("INFO ", source, message);
    public static void Warn(string source, string message)  => Enqueue("WARN ", source, message);
    public static void Error(string source, string message, Exception? ex = null)
    {
        Enqueue("ERROR", source, ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");
        if (ex?.InnerException is not null)
            Enqueue("ERROR", source, $"  inner: {ex.InnerException.Message}");
    }

    public static string GetLogPath() => LogPath;

    private static void Enqueue(string level, string source, string message)
    {
        if (_queue.IsAddingCompleted) return;
        var entry = $"{DateTime.UtcNow:HH:mm:ss.fff} [{level}] [{source}] {message}";
        _queue.TryAdd(entry);
        LogEntryAdded?.Invoke(entry);
    }

    private static void WriteLoop()
    {
        try
        {
            var writer = OpenWriter();
            try
            {
                foreach (var line in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        writer.WriteLine(line);
                        if (_queue.Count == 0)
                        {
                            writer.Flush();
                            if (writer.BaseStream.Length > MaxBytes)
                            {
                                // Real rotation: close current, move it to .prev, reopen fresh.
                                writer.Dispose();
                                Rotate();
                                writer = OpenWriter();
                            }
                        }
                    }
                    catch { }
                }
            }
            finally
            {
                writer.Dispose();
            }
        }
        catch { }
    }

    private static StreamWriter OpenWriter() =>
        new(LogPath, append: true, Encoding.UTF8, bufferSize: 65536) { AutoFlush = false };
}
