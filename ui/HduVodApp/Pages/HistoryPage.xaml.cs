using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using HduVodApp.Services;

namespace HduVodApp.Pages;

public sealed partial class HistoryPage : Page
{
    private readonly ObservableCollection<HistoryRecord> _rows = new();

    public HistoryPage()
    {
        this.InitializeComponent();
        HistoryList.ItemsSource = _rows;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Load();
    }

    private void Load()
    {
        _rows.Clear();
        var user = AppState.Username;
        var list = HistoryStore.ListByUser(user);
        foreach (var r in list) _rows.Add(r);

        SubText.Text = $"账号 {(string.IsNullOrEmpty(user) ? "(未知)" : user)} · 共 {_rows.Count} 条记录";
        bool any = _rows.Count > 0;
        EmptyHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        HistoryList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is HistoryRecord r)
        {
            var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText(r.Url);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        }
    }

    private void Redownload_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is HistoryRecord r)
        {
            DownloadManager.Instance.Enqueue(new DownloadRequest
            {
                Url = r.Url,
                FileName = r.FileName,
                Course = r.Course,
                SessionDate = r.SessionDate,
                SessionTime = r.SessionTime,
                ViewNum = r.ViewNum,
            });
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is HistoryRecord r)
        {
            HistoryStore.Delete(r.Id);
            _rows.Remove(r);
            Load();
        }
    }
}
