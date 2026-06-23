using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HduVodApp.Services;

public class AppSettings
{
    public int MaxConcurrentDownloads { get; set; } = 2;   // 同时最大下载数
    public int ConnectionsPerFile { get; set; } = 8;       // 同一文件并发线程（aria2 split），上限 8
    public string DownloadDir { get; set; } = "";          // 空 = 默认 downloads 目录

    public int ClampConnections() => Math.Clamp(ConnectionsPerFile, 1, 8);
    public int ClampConcurrent() => Math.Clamp(MaxConcurrentDownloads, 1, 10);

    public string EffectiveDownloadDir =>
        string.IsNullOrWhiteSpace(DownloadDir) ? Paths.DownloadsDir : DownloadDir;
}

/// <summary>应用设置（保存到项目根 app_settings.json）。</summary>
public static class SettingsStore
{
    private static AppSettings? _current;
    public static AppSettings Current => _current ??= Load();

    private static string FilePath => Path.Combine(Paths.Root, "app_settings.json");

    private static AppSettings Load()
    {
        var s = new AppSettings();
        try
        {
            if (File.Exists(FilePath))
            {
                var node = JsonNode.Parse(File.ReadAllText(FilePath));
                if (node != null)
                {
                    s.MaxConcurrentDownloads = TryInt(node["maxConcurrentDownloads"], 2);
                    s.ConnectionsPerFile = TryInt(node["connectionsPerFile"], 8);
                    s.DownloadDir = node["downloadDir"]?.GetValue<string>() ?? "";
                }
            }
        }
        catch { /* ignore */ }
        return s;
    }

    public static void Save(AppSettings s)
    {
        _current = s;
        try
        {
            var obj = new JsonObject
            {
                ["maxConcurrentDownloads"] = s.ClampConcurrent(),
                ["connectionsPerFile"] = s.ClampConnections(),
                ["downloadDir"] = s.DownloadDir ?? "",
            };
            File.WriteAllText(FilePath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* ignore */ }
    }

    private static int TryInt(JsonNode? n, int dflt)
    {
        if (n == null) return dflt;
        try { return n.GetValue<int>(); } catch { }
        if (int.TryParse(n.ToString(), out var v)) return v;
        return dflt;
    }
}
