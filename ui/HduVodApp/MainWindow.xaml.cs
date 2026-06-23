using Microsoft.UI.Xaml;
using HduVodApp.Pages;
using HduVodApp.Services;

namespace HduVodApp;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        AppLog.Info("MainWindow", "ctor begin");
        this.InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Title = "HDU 录播下载器";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 780));
        RootFrame.Navigate(typeof(LoginPage));
        this.Closed += MainWindow_Closed;
        AppLog.Info("MainWindow", "ctor done, navigated LoginPage");
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        try { DownloadManager.Instance.ShutdownAll(); }
        catch { /* ignore */ }
    }
}
