using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HduVodApp.Services;

public class Credentials
{
    public string Token { get; set; } = "";
    public string Cookie { get; set; } = "";
}

public class AppConfig
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public int MaxRetries { get; set; } = 3;
    public int DaysOffset { get; set; } = 7;
}

/// <summary>读写与 Python 端共享的 session.json / config.json。</summary>
public static class SessionStore
{
    public static Credentials? LoadSession()
    {
        try
        {
            if (!File.Exists(Paths.SessionJson)) return null;
            var node = JsonNode.Parse(File.ReadAllText(Paths.SessionJson));
            if (node == null) return null;
            var token = node["jwt_token"]?.GetValue<string>() ?? "";
            var cookie = node["cookie_str"]?.GetValue<string>() ?? "";
            if (token.Length > 50)
                return new Credentials { Token = token, Cookie = cookie };
        }
        catch { /* ignore */ }
        return null;
    }

    public static AppConfig LoadConfig()
    {
        var cfg = new AppConfig();
        try
        {
            if (File.Exists(Paths.ConfigJson))
            {
                var node = JsonNode.Parse(File.ReadAllText(Paths.ConfigJson));
                if (node != null)
                {
                    cfg.Username = node["username"]?.GetValue<string>() ?? "";
                    cfg.Password = node["password"]?.GetValue<string>() ?? "";
                    cfg.MaxRetries = TryInt(node["max_retries"], 3);
                    cfg.DaysOffset = TryInt(node["days_offset"], 7);
                }
            }
        }
        catch { /* ignore */ }
        return cfg;
    }

    public static void SaveConfig(AppConfig cfg)
    {
        var obj = new JsonObject
        {
            ["username"] = cfg.Username,
            ["password"] = cfg.Password,
            ["max_retries"] = cfg.MaxRetries,
            ["days_offset"] = cfg.DaysOffset,
        };
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Paths.ConfigJson, obj.ToJsonString(opts));
    }

    private static int TryInt(JsonNode? n, int dflt)
    {
        if (n == null) return dflt;
        try { return n.GetValue<int>(); } catch { }
        try { return int.Parse(n.GetValue<string>()); } catch { }
        return dflt;
    }
}
