using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using HduVodApp.Services;

namespace HduVodApp.Pages;

public sealed partial class ShellPage : Page
{
    private bool _syncingSelection;
    private bool _shellReady;

    public ShellPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        AppLog.Info("ShellPage", $"OnNavigatedTo ready={_shellReady}");
        if (e.Parameter is Credentials creds)
            AppState.SetCredentials(creds);

        DownloadManager.Instance.AttachDispatcher(DispatcherQueue);
        HistoryStore.Init();
        DownloadManager.Instance.RestoreAll();

        if (_shellReady) return;
        Loaded += ShellPage_FirstLoaded;
    }

    private void ShellPage_FirstLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Loaded -= ShellPage_FirstLoaded;
        if (_shellReady) return;
        _shellReady = true;

        AppLog.Info("ShellPage", "FirstLoaded -> navigate BrowsePage");
        ContentFrame.Navigated += ContentFrame_Navigated;
        _syncingSelection = true;
        var browse = FindNavItem("browse");
        if (browse != null) Nav.SelectedItem = browse;
        ContentFrame.Navigate(typeof(BrowsePage));
        ReleaseSelectionGuard();
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingSelection || !_shellReady) return;
        AppLog.Info("ShellPage", $"Nav_SelectionChanged tag={(args.SelectedItem as NavigationViewItem)?.Tag}");
        if (args.SelectedItem is not NavigationViewItem item) return;

        var target = (item.Tag as string) switch
        {
            "browse" => typeof(BrowsePage),
            "downloads" => typeof(DownloadsPage),
            "settings" => typeof(SettingsPage),
            _ => null
        };
        if (target == null || ContentFrame.CurrentSourcePageType == target) return;

        _syncingSelection = true;
        ContentFrame.Navigate(target);
        ReleaseSelectionGuard();
    }

    private void Nav_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack)
            ContentFrame.GoBack();
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        AppLog.Info("ShellPage", $"ContentFrame_Navigated {e.SourcePageType?.Name}");
        Nav.IsBackEnabled = ContentFrame.CanGoBack;

        string? tag = e.SourcePageType == typeof(DownloadsPage) ? "downloads"
                     : e.SourcePageType == typeof(SettingsPage) ? "settings"
                     : e.SourcePageType == typeof(BrowsePage) ? "browse" : null;
        if (tag == null) return;

        var match = FindNavItem(tag);
        if (match == null || ReferenceEquals(Nav.SelectedItem, match)) return;

        _syncingSelection = true;
        Nav.SelectedItem = match;
        ReleaseSelectionGuard();
    }

    private void ReleaseSelectionGuard()
    {
        DispatcherQueue.TryEnqueue(() => _syncingSelection = false);
    }

    private NavigationViewItem? FindNavItem(string tag)
    {
        foreach (var mi in Nav.MenuItems)
            if (mi is NavigationViewItem nvi && (nvi.Tag as string) == tag) return nvi;
        foreach (var mi in Nav.FooterMenuItems)
            if (mi is NavigationViewItem nvi && (nvi.Tag as string) == tag) return nvi;
        return null;
    }

    private void OpenFolder_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            var dir = SettingsStore.Current.EffectiveDownloadDir;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch { /* ignore */ }
    }
}
