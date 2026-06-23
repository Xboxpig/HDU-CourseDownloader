using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using HduVodApp.Services;

namespace HduVodApp.Pages;

public sealed partial class LoginPage : Page
{
    private Credentials? _cached;

    public LoginPage()
    {
        this.InitializeComponent();
        Loaded += LoginPage_Loaded;
    }

    private async void LoginPage_Loaded(object sender, RoutedEventArgs e)
    {
        AppLog.Info("LoginPage", "Loaded");
        var cfg = SessionStore.LoadConfig();
        UserBox.Text = cfg.Username;
        PassBox.Password = cfg.Password;

        SetBusy(true, "正在校验本地凭证...");
        try
        {
            _cached = await AuthService.TryCachedAsync();
        }
        catch { _cached = null; }
        SetBusy(false);

        if (_cached != null)
        {
            // 有已保存凭证：蓝色「使用已保存的凭证进入」排第一，灰色「重新登录」排第二。
            ShowStatus(InfoBarSeverity.Success, "检测到有效的已保存凭证，可直接进入。");
            UseCacheBtn.Visibility = Visibility.Visible;
            ReloginBtn.Visibility = Visibility.Visible;
            LoginBtn.Visibility = Visibility.Collapsed;
            AppLog.Info("LoginPage", "cached credentials ok");
        }
        else
        {
            // 无已保存凭证：仅显示蓝色「登录 / 刷新凭证」。
            ShowStatus(InfoBarSeverity.Informational, "未检测到有效凭证，请登录刷新。");
            UseCacheBtn.Visibility = Visibility.Collapsed;
            ReloginBtn.Visibility = Visibility.Collapsed;
            LoginBtn.Visibility = Visibility.Visible;
        }
    }

    private void OnUseCacheClick(object sender, RoutedEventArgs e)
    {
        AppLog.Info("LoginPage", "UseCacheClick");
        if (_cached != null)
        {
            SetBusy(true, "正在进入，加载课程列表...");
            GoToShell(_cached);
        }
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        var user = UserBox.Text.Trim();
        var pass = PassBox.Password;
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            ShowStatus(InfoBarSeverity.Warning, "请输入账号和密码。");
            return;
        }

        SetBusy(true, "正在启动浏览器自动登录（请稍候，可能弹出 Chrome 窗口）...");
        LogText.Text = "";
        StatusBar.IsOpen = false;

        var result = await AuthService.RefreshLoginAsync(user, pass, line =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LogText.Text = (LogText.Text + "\n" + line).TrimStart('\n');
            });
        });

        SetBusy(false);

        if (result.Ok)
        {
            ShowStatus(InfoBarSeverity.Success, "登录成功，正在进入...");
            SetBusy(true, "正在进入，加载课程列表...");
            GoToShell(new Credentials { Token = result.Token, Cookie = result.Cookie });
        }
        else
        {
            ShowStatus(InfoBarSeverity.Error, "登录失败：" + result.Message);
        }
    }

    private void GoToShell(Credentials creds)
    {
        AppLog.Info("LoginPage", "GoToShell enqueue");
        DispatcherQueue.TryEnqueue(() =>
        {
            AppLog.Info("LoginPage", "GoToShell navigate ShellPage");
            if (Frame == null) return;
            Frame.Navigate(typeof(ShellPage), creds);
        });
    }

    private void SetBusy(bool busy, string? text = null)
    {
        BusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (text != null) BusyText.Text = text;
        LoginBtn.IsEnabled = !busy;
        UseCacheBtn.IsEnabled = !busy;
        ReloginBtn.IsEnabled = !busy;
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
