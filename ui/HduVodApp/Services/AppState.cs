namespace HduVodApp.Services;

/// <summary>登录后全局共享的 API 客户端与凭证。</summary>
public static class AppState
{
    public static Credentials? Credentials { get; set; }
    public static HduApiClient? Api { get; set; }
    public static string Username { get; set; } = "";

    public static void SetCredentials(Credentials creds)
    {
        Credentials = creds;
        Api = new HduApiClient(creds.Token, creds.Cookie);
        var u = SessionStore.LoadConfig().Username;
        if (!string.IsNullOrWhiteSpace(u)) Username = u;
    }
}
