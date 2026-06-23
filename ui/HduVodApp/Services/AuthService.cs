using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace HduVodApp.Services;

public class LoginResult
{
    public bool Ok { get; set; }
    public string Token { get; set; } = "";
    public string Cookie { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>
/// 登录服务：优先复用 session.json 已有 token；失效时调用 Python Selenium
/// 流程（src/login_cli.py）补登并回写 session.json。
/// </summary>
public static class AuthService
{
    /// <summary>校验本地缓存的凭证是否仍然有效。</summary>
    public static async Task<Credentials?> TryCachedAsync()
    {
        var creds = SessionStore.LoadSession();
        if (creds == null) return null;
        var client = new HduApiClient(creds.Token, creds.Cookie);
        return await client.CheckTokenAsync() ? creds : null;
    }

    /// <summary>
    /// 保存账号密码到 config.json，并启动 Python Selenium 自动登录。
    /// 进度通过 onLog 回调输出。
    /// </summary>
    public static async Task<LoginResult> RefreshLoginAsync(
        string username, string password, Action<string>? onLog = null)
    {
        var cfg = SessionStore.LoadConfig();
        cfg.Username = username;
        cfg.Password = password;
        SessionStore.SaveConfig(cfg);

        var py = Paths.PythonExe;
        var cli = Paths.LoginCli;

        if (!System.IO.File.Exists(py))
            return new LoginResult { Ok = false, Message = $"未找到 Python 解释器：{py}" };
        if (!System.IO.File.Exists(cli))
            return new LoginResult { Ok = false, Message = $"未找到登录脚本：{cli}" };

        var psi = new ProcessStartInfo
        {
            FileName = py,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Paths.Root,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add(cli);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";

        var resultLine = "";
        try
        {
            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                if (e.Data.StartsWith("RESULT_JSON:"))
                    resultLine = e.Data.Substring("RESULT_JSON:".Length);
                else
                    onLog?.Invoke(e.Data);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) onLog?.Invoke(e.Data);
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            return new LoginResult { Ok = false, Message = "启动登录进程失败：" + ex.Message };
        }

        if (string.IsNullOrWhiteSpace(resultLine))
            return new LoginResult { Ok = false, Message = "登录脚本未返回结果（可能 Token 拦截超时）。" };

        try
        {
            var node = JsonNode.Parse(resultLine);
            var ok = node?["ok"]?.GetValue<bool>() ?? false;
            return new LoginResult
            {
                Ok = ok,
                Token = node?["token"]?.ToString() ?? "",
                Cookie = node?["cookie"]?.ToString() ?? "",
                Message = ok ? "登录成功" : (node?["message"]?.ToString() ?? "登录失败"),
            };
        }
        catch (Exception ex)
        {
            return new LoginResult { Ok = false, Message = "解析登录结果失败：" + ex.Message };
        }
    }
}
