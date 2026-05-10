using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace Excalibur5.Services;

/// <summary>
/// Non-blocking async logger. Enqueues entries to a background thread so logging
/// never stalls the UI or WebSocket threads. Rotates at 5 MB.
/// </summary>
public static class AppLogger
{
    private static readonly string LogPath;
    private static readonly BlockingCollection<string> _queue = new(boundedCapacity: 2048);
    private const long MaxBytes = 5 * 1024 * 1024;

    static AppLogger()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Excalibur5", "logs");
        Directory.CreateDirectory(dir);
        LogPath = Path.Combine(dir, "excalibur5.log");

        var thread = new Thread(WriteLoop) { IsBackground = true, Name = "AppLogger" };
        thread.Start();

        // Flush remaining entries on process exit
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _queue.CompleteAdding();
            thread.Join(millisecondsTimeout: 2000);
        };
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
        // TryAdd never blocks — drops entry only if queue is full (prevents memory pressure)
        _queue.TryAdd($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z [{level}] [{source}] {message}");
    }

    private static void WriteLoop()
    {
        try
        {
            using var writer = new StreamWriter(LogPath, append: true, Encoding.UTF8, bufferSize: 65536)
            {
                AutoFlush = false
            };
            foreach (var line in _queue.GetConsumingEnumerable())
            {
                try
                {
                    writer.WriteLine(line);
                    // Batch writes — flush only when queue drains
                    if (_queue.Count == 0)
                    {
                        writer.Flush();
                        // Rotate after flush so BaseStream.Length is accurate
                        if (writer.BaseStream.Length > MaxBytes)
                        {
                            writer.BaseStream.SetLength(0);
                            writer.BaseStream.Seek(0, SeekOrigin.Begin);
                        }
                    }
                }
                catch { /* never crash the logger */ }
            }
        }
        catch { /* never crash the app */ }
    }
}
