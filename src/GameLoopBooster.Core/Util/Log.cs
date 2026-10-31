namespace GameLoopBooster.Core.Util;

public enum LogLevel { Info, Success, Warning, Error }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}

/// <summary>Application-wide log: kept in memory for the UI and appended to a file under ProgramData.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly List<LogEntry> Entries = new();

    public static event Action<LogEntry>? Written;

    public static IReadOnlyList<LogEntry> Snapshot()
    {
        lock (Gate) return Entries.ToList();
    }

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Success(string message) => Write(LogLevel.Success, message);
    public static void Warn(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        lock (Gate)
        {
            Entries.Add(entry);
            if (Entries.Count > 2000) Entries.RemoveRange(0, 500);
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.AppendAllText(AppPaths.LogFile, $"{entry.Time:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
            catch { /* logging must never crash the app */ }
        }
        Written?.Invoke(entry);
    }
}
