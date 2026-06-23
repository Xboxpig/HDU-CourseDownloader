using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using HduVodApp.Services;

namespace HduVodApp.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var s = SettingsStore.Current;
        ConcurrentBox.Value = s.ClampConcurrent();
        ConnectionsBox.Value = s.ClampConnections();
        DirBox.Text = s.DownloadDir;
        Aria2Status.Text = (Paths.Aria2cAvailable || System.IO.File.Exists(Paths.Aria2c))
            ? $"aria2c：{Paths.Aria2c}"
            : "未检测到 aria2c.exe（请安装 aria2 或放到 drivers\\aria2c.exe）";
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null) DirBox.Text = folder.Path;
        }
        catch (Exception ex)
        {
            ShowInfo(InfoBarSeverity.Error, "选择目录失败：" + ex.Message);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = new AppSettings
        {
            MaxConcurrentDownloads = (int)Math.Round(double.IsNaN(ConcurrentBox.Value) ? 2 : ConcurrentBox.Value),
            ConnectionsPerFile = (int)Math.Round(double.IsNaN(ConnectionsBox.Value) ? 8 : ConnectionsBox.Value),
            DownloadDir = DirBox.Text?.Trim() ?? "",
        };
        SettingsStore.Save(s);
        DownloadManager.Instance.NotifySettingsChanged();
        ShowInfo(InfoBarSeverity.Success,
            $"已保存。最大同时下载 {s.ClampConcurrent()}，单文件线程 {s.ClampConnections()}，目录 {s.EffectiveDownloadDir}");
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        ConcurrentBox.Value = 2;
        ConnectionsBox.Value = 8;
        DirBox.Text = "";
    }

    private void ShowInfo(InfoBarSeverity severity, string message)
    {
        SaveInfo.Severity = severity;
        SaveInfo.Message = message;
        SaveInfo.IsOpen = true;
    }
}
