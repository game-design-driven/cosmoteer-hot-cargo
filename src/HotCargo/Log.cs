namespace HotCargo;

/// <summary>
/// The mod log. Logging never throws: it runs inside the catch of every Errors.Guard, and a throw there ends the game.
/// A line that the disk refuses (full disk, locked file) is lost; the next line that writes says how many were lost.
/// </summary>
public static class Log
{
    static string? _path;
    static readonly Lock Gate = new();
    static int _lost;

    public static void Init(string modDir)
    {
        _path = Path.Combine(modDir, "hot_cargo_log.txt");
        try { File.WriteAllText(_path, $"Hot Cargo log {DateTime.Now:O}\n"); }
        catch (Exception) { _lost++; }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}\n{ex}");

    /// <summary>Runs after each written line, with the level and the message. The telemetry area copies the lines into its events.</summary>
    public static event Action<string, string>? Written;

    static void Write(string level, string message)
    {
        if (_path is null) return;
        lock (Gate)
        {
            try
            {
                string lost = _lost > 0 ? $"({_lost} log lines lost before this one) " : "";
                File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss.fff} {level} {lost}{message}\n");
                _lost = 0;
            }
            catch (Exception) { _lost++; }
        }
        try { Written?.Invoke(level, message); }
        catch (Exception) { /* A listener (telemetry) that fails loses this line; the log file still has it. */ }
    }
}
