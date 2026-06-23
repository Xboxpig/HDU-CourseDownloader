using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using HduVodApp.Services;

namespace HduVodApp.Pages;

public sealed partial class DownloadsPage : Page
{
    public DownloadsPage()
    {
        this.InitializeComponent();
        TaskList.ItemsSource = DownloadManager.Instance.Items;
        DownloadManager.Instance.Items.CollectionChanged += Items_CollectionChanged;
        Loaded += (_, _) => UpdateEmpty();
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => DispatcherQueue.TryEnqueue(UpdateEmpty);

    private void UpdateEmpty()
    {
        bool any = DownloadManager.Instance.Items.Count > 0;
        EmptyHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        TaskList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>播放/暂停二合一：下载中→暂停；已完成→播放文件；其余→继续/重试下载。</summary>
    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DownloadItem item) return;
        switch (item.State)
        {
            case DownloadState.Running:
            case DownloadState.Queued:
                DownloadManager.Instance.Pause(item);
                break;
            case DownloadState.Completed:
                if (!DownloadManager.Instance.OpenFile(item))
                    item.Error = "文件不存在，可能已被移动或删除。";
                break;
            default: // Paused / Failed / Stopped
                DownloadManager.Instance.Resume(item);
                break;
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DownloadItem item)
            DownloadManager.Instance.Delete(item);
    }
}
