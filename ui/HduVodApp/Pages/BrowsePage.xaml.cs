using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using HduVodApp.Models;
using HduVodApp.Services;

namespace HduVodApp.Pages;

public sealed partial class BrowsePage : Page
{
    private readonly ObservableCollection<CourseItem> _courses = new();
    private readonly ObservableCollection<SessionItem> _sessions = new();
    private readonly SemaphoreSlim _courseLoadLock = new(1, 1);
    private readonly SemaphoreSlim _sessionLoadLock = new(1, 1);

    private string _acteId = "8";
    private int _page = 1;
    private int _pageCount = 1;
    private const int PageSize = 20;

    private CourseItem? _selectedCourse;
    private bool _viewGuard;
    private bool _selectAllGuard;
    private bool _termChanging;
    private int _sessionLoadGen;

    public BrowsePage()
    {
        AppLog.Info("BrowsePage", "ctor begin");
        this.InitializeComponent();
        AppLog.Info("BrowsePage", "ctor after InitializeComponent");
        _viewGuard = true;
        ViewAll.IsChecked = true;
        _viewGuard = false;
        CourseList.ItemsSource = _courses;
        SessionList.ItemsSource = _sessions;
        Loaded += BrowsePage_Loaded;
    }

    private bool _initialized;

    private async void BrowsePage_Loaded(object sender, RoutedEventArgs e)
    {
        AppLog.Info("BrowsePage", $"Loaded init={_initialized} apiNull={AppState.Api == null}");
        if (_initialized) return;
        if (AppState.Api == null)
        {
            ShowInfo(InfoBarSeverity.Error, "未检测到登录凭证，请关闭后重新打开应用登录。");
            return;
        }
        await LoadTermsAsync();
        AppLog.Info("BrowsePage", "terms loaded");
        await LoadCoursesAsync();
        AppLog.Info("BrowsePage", $"courses loaded count={_courses.Count}");
        _initialized = true;
    }

    private HduApiClient Api => AppState.Api!;

    private async Task LoadTermsAsync()
    {
        _termChanging = true;
        try
        {
            var terms = await Api.ListTermsAsync();
            TermCombo.Items.Clear();
            TermCombo.Items.Add(new TermItem { Id = "8", Name = "默认 (acteId=8)" });
            foreach (var t in terms) TermCombo.Items.Add(t);
            TermCombo.SelectedIndex = 0;
        }
        catch
        {
            TermCombo.Items.Clear();
            TermCombo.Items.Add(new TermItem { Id = "8", Name = "默认 (acteId=8)" });
            TermCombo.SelectedIndex = 0;
        }
        finally { _termChanging = false; }
    }

    private void TermCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_termChanging) return;
        if (TermCombo.SelectedItem is TermItem t)
        {
            _acteId = string.IsNullOrEmpty(t.Id) ? "8" : t.Id;
            _page = 1;
            if (_initialized) _ = LoadCoursesAsync();
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadCoursesAsync();
    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_page > 1) { _page--; _ = LoadCoursesAsync(); }
    }
    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page < _pageCount) { _page++; _ = LoadCoursesAsync(); }
    }

    private async Task LoadCoursesAsync()
    {
        await _courseLoadLock.WaitAsync();
        SetBusy(true);
        try
        {
            var (courses, pageCount, rowCount) =
                await Api.ListCoursesAsync(_acteId, _page, PageSize);
            _pageCount = pageCount;
            _courses.Clear();
            foreach (var c in courses) _courses.Add(c);
            PageText.Text = $"{_page}/{_pageCount}  (共{rowCount})";
            PrevBtn.IsEnabled = _page > 1;
            NextBtn.IsEnabled = _page < _pageCount;
        }
        catch (Exception ex)
        {
            ShowInfo(InfoBarSeverity.Error, "加载课程失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
            _courseLoadLock.Release();
        }
    }

    private async void CourseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var course = CourseList.SelectedItem as CourseItem;
        _selectedCourse = course;
        _sessions.Clear();
        SelectAllBox.IsChecked = false;
        UpdateBatchButtons();
        if (course == null) return;

        var gen = ++_sessionLoadGen;
        SessionHeader.Text = $"《{course.SubjName}》 录像加载中...";
        SetBusy(true);
        await _sessionLoadLock.WaitAsync();
        try
        {
            if (gen != _sessionLoadGen) return;
            var sessions = await Api.ListSessionsAsync(course.TeclId);
            if (gen != _sessionLoadGen) return;
            foreach (var s in sessions.OrderBy(x => x.SortKey))
                _sessions.Add(s);
            SessionHeader.Text = $"《{course.SubjName}》 共 {_sessions.Count} 节";
            if (_sessions.Count == 0)
                ShowInfo(InfoBarSeverity.Informational, "该课程暂无可点播录像。");
        }
        catch (Exception ex)
        {
            if (gen != _sessionLoadGen) return;
            SessionHeader.Text = $"《{course.SubjName}》";
            ShowInfo(InfoBarSeverity.Error, "加载录像失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
            _sessionLoadLock.Release();
        }
    }

    private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBatchButtons();
        if (_selectAllGuard) return;
        // 同步全选框状态
        _selectAllGuard = true;
        SelectAllBox.IsChecked = _sessions.Count > 0 && SessionList.SelectedItems.Count == _sessions.Count;
        _selectAllGuard = false;
    }

    private void SelectAll_Changed(object sender, RoutedEventArgs e)
    {
        if (_selectAllGuard) return;
        _selectAllGuard = true;
        if (SelectAllBox.IsChecked == true) SessionList.SelectAll();
        else if (_sessions.Count > 0)
            SessionList.DeselectRange(new Microsoft.UI.Xaml.Data.ItemIndexRange(0, (uint)_sessions.Count));
        _selectAllGuard = false;
        UpdateBatchButtons();
    }

    private void UpdateBatchButtons()
    {
        int n = SessionList.SelectedItems.Count;
        BatchDownloadBtn.IsEnabled = n > 0;
        BatchCopyBtn.IsEnabled = n > 0;
        BatchHint.Text = n > 0 ? $"已勾选 {n} 节" : "勾选上方录像后点「批量下载」";
    }

    private void ViewAll_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewGuard) return;
        _viewGuard = true;
        if (ViewAll.IsChecked == true)
            foreach (var cb in ViewBoxes()) cb.IsChecked = false;
        _viewGuard = false;
    }

    private void ViewNum_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewGuard) return;
        _viewGuard = true;
        if (ViewBoxes().Any(c => c.IsChecked == true))
            ViewAll.IsChecked = false;
        else
            ViewAll.IsChecked = true;
        _viewGuard = false;
    }

    private IEnumerable<CheckBox> ViewBoxes() => new[] { View1, View3, View5 };

    /// <summary>返回要下载的画面编号（1=后侧 3=前侧 5=电脑）；null 表示全部。</summary>
    private HashSet<int>? GetWantedViewNums()
    {
        if (ViewAll.IsChecked == true) return null;
        var set = new HashSet<int>();
        if (View1.IsChecked == true) set.Add(1);
        if (View3.IsChecked == true) set.Add(3);
        if (View5.IsChecked == true) set.Add(5);
        return set.Count == 0 ? null : set;
    }

    private async void BatchDownload_Click(object sender, RoutedEventArgs e)
    {
        var sessions = SessionList.SelectedItems.Cast<SessionItem>().ToList();
        if (_selectedCourse == null || sessions.Count == 0)
        {
            ShowInfo(InfoBarSeverity.Warning, "请先勾选至少一节录像。");
            return;
        }
        var wanted = GetWantedViewNums();

        SetBatchBusy(true, $"正在解析 {sessions.Count} 节的视频地址...");
        int queued = 0, failed = 0;
        foreach (var s in sessions)
        {
            try
            {
                var views = await Api.GetVodUrlsAsync(s.CourId);
                foreach (var v in views)
                {
                    if (wanted != null && !wanted.Contains(v.ViewNum)) continue;
                    QueueDownload(s, v);
                    queued++;
                }
            }
            catch { failed++; }
        }
        SetBatchBusy(false);
        ShowInfo(failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
            $"已加入 {queued} 个下载任务（{sessions.Count} 节"
            + (failed > 0 ? $"，{failed} 节解析失败" : "") + "）。可在「下载任务」查看。");
    }

    private async void BatchCopy_Click(object sender, RoutedEventArgs e)
    {
        var sessions = SessionList.SelectedItems.Cast<SessionItem>().ToList();
        if (sessions.Count == 0) { ShowInfo(InfoBarSeverity.Warning, "请先勾选录像。"); return; }
        var wanted = GetWantedViewNums();

        SetBatchBusy(true, "正在解析直链...");
        var urls = new List<string>();
        foreach (var s in sessions)
        {
            try
            {
                var views = await Api.GetVodUrlsAsync(s.CourId);
                foreach (var v in views)
                {
                    if (wanted != null && !wanted.Contains(v.ViewNum)) continue;
                    urls.Add(v.Url);
                }
            }
            catch { }
        }
        SetBatchBusy(false);

        if (urls.Count == 0) { ShowInfo(InfoBarSeverity.Warning, "未解析到符合条件的直链。"); return; }
        var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
        pkg.SetText(string.Join(Environment.NewLine, urls));
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        ShowInfo(InfoBarSeverity.Success, $"已复制 {urls.Count} 条直链到剪贴板（每行一条）。");
    }

    private void QueueDownload(SessionItem s, CameraView v)
    {
        if (_selectedCourse == null) return;
        var fname = BuildFileName(_selectedCourse, s, v);
        DownloadManager.Instance.Enqueue(new DownloadRequest
        {
            Url = v.Url,
            FileName = fname,
            Course = _selectedCourse.SubjName,
            SessionDate = s.DateText,
            SessionTime = s.TimeText,
            ViewNum = v.ViewNum,
        });
    }

    private static string BuildFileName(CourseItem c, SessionItem s, CameraView v)
    {
        var begin = (s.BeginRaw ?? "").Replace(":", "-").Replace(" ", "_");
        var viewTag = ViewLabel(v.ViewNum);
        var name = $"{c.SubjName}_{begin}_{viewTag}_vod{v.VodId}";
        return Sanitize(name) + ".mp4";
    }

    private static string ViewLabel(int viewNum) => viewNum switch
    {
        1 => "后侧",
        3 => "前侧",
        5 => "电脑",
        _ => $"画面{viewNum}"
    };

    private static string Sanitize(string name)
    {
        name = Regex.Replace(name, "[\\\\/:*?\"<>|]", "_").Trim();
        return name.Length > 120 ? name.Substring(0, 120) : (name.Length == 0 ? "untitled" : name);
    }

    private void SetBusy(bool busy) => TopBusy.IsActive = busy;

    private void SetBatchBusy(bool busy, string? text = null)
    {
        BatchBusy.IsActive = busy;
        BatchDownloadBtn.IsEnabled = !busy && SessionList.SelectedItems.Count > 0;
        BatchCopyBtn.IsEnabled = !busy && SessionList.SelectedItems.Count > 0;
        if (text != null) BatchHint.Text = text;
        else UpdateBatchButtons();
    }

    private void ShowInfo(InfoBarSeverity severity, string message)
    {
        BrowseInfo.Severity = severity;
        BrowseInfo.Message = message;
        BrowseInfo.IsOpen = true;
    }
}
