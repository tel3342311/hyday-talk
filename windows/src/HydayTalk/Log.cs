namespace HydayTalk;

/// Troubleshooting log, written to %APPDATA%\HydayTalk\debug.log only when HYDAYTALK_LOG=1.
/// It records events and phases, never dictated text.
internal static class Log
{
    private static readonly object Gate = new();
    private static readonly string? Path = Environment.GetEnvironmentVariable("HYDAYTALK_LOG") == "1"
        ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HydayTalk", "debug.log") : null;

    public static void Write(string message)
    {
        if (Path == null) return;
        lock (Gate)
        {
            try { File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"); } catch { }
        }
    }
}
