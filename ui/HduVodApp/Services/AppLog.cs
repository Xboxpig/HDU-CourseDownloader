using System;
using System.IO;
using System.Threading.Tasks;

namespace HduVodApp.Services;

/// <summary>写入项目根目录 hduvod_debug.log，便于复现闪退后离线分析。</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static string LogPath
    {
        get
        {
            if (_path != null) return _path;
            _path = Path.Combine(Paths.Root, "hduvod_debug.log");
            return _path;
        }
    }

    public static void Init()
    {
        try
        {
            var p = LogPath;
            File.WriteAllText(p,
                $"===== HDU Vod debug log {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====={Environment.NewLine}" +
                $"exe={Environment.ProcessPath}{Environment.NewLine}" +
                $"base={AppContext.BaseDirectory}{Environment.NewLine}" +
                $"root={Paths.Root}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* ignore */ }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("FATAL", "AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("ERROR", "TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    public static void Info(string tag, string message) => Write("INFO", tag, message);

    public static void Error(string tag, string message, Exception? ex = null) =>
        Write("ERROR", tag, message, ex);

    private static void Write(string level, string tag, string message, Exception? ex = null)
    {
        try
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {tag}: {message}";
            if (ex != null)
                line += $"{Environment.NewLine}  {ex}{Environment.NewLine}  HRESULT=0x{ex.HResult:X8}";
            line += Environment.NewLine;
            lock (Gate) File.AppendAllText(LogPath, line);
        }
        catch { /* ignore */ }
    }

    private static void Write(string level, string tag, Exception? ex)
    {
        Write(level, tag, ex?.Message ?? "(null)", ex);
    }
}
