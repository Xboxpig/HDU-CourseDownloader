using System;
using System.IO;

namespace HduVodApp.Services;

/// <summary>
/// 定位应用数据目录。源码运行时复用项目根目录，发布包运行时使用本机用户数据目录。
/// </summary>
public static class Paths
{
    private static string? _root;
    private static bool _sourceRootResolved;
    private static string? _sourceRoot;

    public static string Root
    {
        get
        {
            if (_root != null) return _root;

            var sourceRoot = SourceRoot;
            if (sourceRoot != null)
            {
                _root = sourceRoot;
                return _root;
            }

            _root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HDU-CourseDownloader");
            Directory.CreateDirectory(_root);
            return _root;
        }
    }

    private static string? SourceRoot
    {
        get
        {
            if (_sourceRootResolved) return _sourceRoot;
            _sourceRootResolved = true;

            var overrideRoot = Environment.GetEnvironmentVariable("HDU_COURSE_DOWNLOADER_ROOT");
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                var full = Path.GetFullPath(overrideRoot);
                if (File.Exists(Path.Combine(full, "src", "hdu_auth.py")))
                {
                    _sourceRoot = full;
                    return _sourceRoot;
                }
            }

            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var probe = Path.Combine(dir, "src", "hdu_auth.py");
                if (File.Exists(probe))
                {
                    _sourceRoot = dir;
                    return _sourceRoot;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }

            return null;
        }
    }

    private static string ToolRoot => SourceRoot ?? Root;

    public static string ConfigJson => Path.Combine(Root, "config.json");
    public static string SessionJson => Path.Combine(Root, "session.json");
    public static string DownloadsDir => Path.Combine(Root, "downloads");
    public static string HistoryDb => Path.Combine(Root, "download_history.db");
    public static string PythonExe => Path.Combine(ToolRoot, "env", "Scripts", "python.exe");
    public static string LoginCli => Path.Combine(ToolRoot, "src", "login_cli.py");

    private static string? _aria2;

    /// <summary>定位 aria2c.exe：项目内置 -> 常见安装位置 -> PATH。</summary>
    public static string Aria2c
    {
        get
        {
            if (_aria2 != null) return _aria2;

            var candidates = new[]
            {
                Path.Combine(Root, "drivers", "aria2c.exe"),
                Path.Combine(Root, "drivers", "aria2", "aria2c.exe"),
                @"C:\scoop\shims\aria2c.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    @"scoop\shims\aria2c.exe"),
                @"C:\ProgramData\chocolatey\bin\aria2c.exe",
            };
            foreach (var c in candidates)
                if (File.Exists(c)) { _aria2 = c; return _aria2; }

            // 回退：依赖 PATH 解析
            _aria2 = "aria2c.exe";
            return _aria2;
        }
    }

    public static bool Aria2cAvailable
    {
        get
        {
            if (Aria2c != "aria2c.exe") return true;
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathVar.Split(Path.PathSeparator))
            {
                try { if (File.Exists(Path.Combine(dir.Trim(), "aria2c.exe"))) return true; }
                catch { }
            }
            return false;
        }
    }
}
