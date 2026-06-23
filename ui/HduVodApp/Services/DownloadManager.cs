using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace HduVodApp.Services;

public enum DownloadState { Queued, Running, Paused, Completed, Failed, Stopped }

/// <summary>下载请求（含写入历史所需的元数据）。</summary>
public class DownloadRequest
{
    public string Url { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Course { get; init; } = "";
    public string SessionDate { get; init; } = "";
    public string SessionTime { get; init; } = "";
    public int ViewNum { get; init; }
}

public class DownloadItem : INotifyPropertyChanged
{
    public string Url { get; init; } = "";
    public string FileName { get; init; } = "";
    public string DestPath { get; init; } = "";
    public string Course { get; init; } = "";
    public string SessionDate { get; init; } = "";
    public string SessionTime { get; init; } = "";
    public int ViewNum { get; init; }
    public long HistoryId { get; set; }

    private double _progress;
    public double Progress { get => _progress; set => Set(ref _progress, value, nameof(Progress)); }

    private string _sizeText = "";
    public string SizeText { get => _sizeText; set => Set(ref _sizeText, value, nameof(SizeText)); }

    private string _speedText = "";
    public string SpeedText { get => _speedText; set => Set(ref _speedText, value, nameof(SpeedText)); }

    private DownloadState _state = DownloadState.Queued;
    public DownloadState State
    {
        get => _state;
        set
        {
            Set(ref _state, value, nameof(State));
            Raise(nameof(StateText));
            Raise(nameof(CanPause));
            Raise(nameof(CanResume));
            Raise(nameof(CanStop));
            Raise(nameof(ToggleGlyph));
            Raise(nameof(ToggleTip));
            Raise(nameof(IsActive));
        }
    }

    private string _error = "";
    public string Error { get => _error; set => Set(ref _error, value, nameof(Error)); }

    public string StateText => State switch
    {
        DownloadState.Queued => "排队中",
        DownloadState.Running => "下载中",
        DownloadState.Paused => "已暂停",
        DownloadState.Completed => "已完成",
        DownloadState.Failed => "失败",
        DownloadState.Stopped => "已停止",
        _ => ""
    };
    public bool CanPause => State == DownloadState.Running || State == DownloadState.Queued;
    public bool CanResume => State == DownloadState.Paused || State == DownloadState.Failed || State == DownloadState.Stopped;
    public bool CanStop => State != DownloadState.Completed && State != DownloadState.Stopped;

    /// <summary>是否处于活动态（下载中/排队中），用于决定进度条是否高亮显示。</summary>
    public bool IsActive => State == DownloadState.Running || State == DownloadState.Queued;

    /// <summary>播放/暂停二合一按钮的可变图标：下载中显示「暂停」，其余显示「播放/继续」。</summary>
    public string ToggleGlyph =>
        (State == DownloadState.Running || State == DownloadState.Queued) ? "\uE769" : "\uE768";

    /// <summary>播放/暂停二合一按钮的悬浮提示。</summary>
    public string ToggleTip => State switch
    {
        DownloadState.Running => "暂停",
        DownloadState.Queued => "暂停",
        DownloadState.Completed => "播放",
        DownloadState.Paused => "继续",
        DownloadState.Failed => "重试",
        DownloadState.Stopped => "重新下载",
        _ => "继续"
    };

    /// <summary>课程 + 上课时间的副标题。</summary>
    public string MetaText
    {
        get
        {
            var dt = string.IsNullOrWhiteSpace(SessionDate) ? "" : $"{SessionDate} {SessionTime}".Trim();
            if (!string.IsNullOrWhiteSpace(Course) && !string.IsNullOrWhiteSpace(dt))
                return $"{Course}  ·  {dt}";
            return string.IsNullOrWhiteSpace(Course) ? dt : Course;
        }
    }

    // 运行期状态（非绑定）
    public Process? Proc { get; set; }
    public bool PauseRequested { get; set; }
    public bool StopRequested { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, string name)
    {
        if (Equals(field, value)) return;
        field = value;
        Raise(name);
    }
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>基于 aria2c 的下载任务管理器（单例）。每个任务一个 aria2c 进程，并写入 SQLite 历史。</summary>
public class DownloadManager
{
    public static DownloadManager Instance { get; } = new();

    public ObservableCollection<DownloadItem> Items { get; } = new();

    // 调度：_inflight 仅在 UI 线程访问。并发上限来自设置，可运行时调整。
    private readonly System.Collections.Generic.HashSet<DownloadItem> _inflight = new();

    private DispatcherQueue? _dispatcher;
    public void AttachDispatcher(DispatcherQueue dq) => _dispatcher = dq;

    /// <summary>设置变更后调用，按新的并发上限补充启动排队任务。</summary>
    public void NotifySettingsChanged() => Pump();

    private void OnUi(Action a)
    {
        if (_dispatcher != null) _dispatcher.TryEnqueue(() => a());
        else a();
    }

    private static string MapStatus(DownloadState s) => s switch
    {
        DownloadState.Running => "downloading",
        DownloadState.Paused => "paused",
        DownloadState.Completed => "completed",
        DownloadState.Failed => "failed",
        DownloadState.Stopped => "stopped",
        _ => "downloading"
    };

    private void SetState(DownloadItem item, DownloadState st)
    {
        OnUi(() => item.State = st);
        if (item.HistoryId > 0)
        {
            var status = MapStatus(st);
            var size = item.SizeText;
            _ = Task.Run(() => HistoryStore.Update(item.HistoryId, status, size));
        }
    }

    public DownloadItem Enqueue(DownloadRequest req)
    {
        var dir = SettingsStore.Current.EffectiveDownloadDir;
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, req.FileName);
        var item = new DownloadItem
        {
            Url = req.Url,
            FileName = req.FileName,
            DestPath = dest,
            Course = req.Course,
            SessionDate = req.SessionDate,
            SessionTime = req.SessionTime,
            ViewNum = req.ViewNum,
        };

        bool already = File.Exists(dest);
        item.State = already ? DownloadState.Completed : DownloadState.Queued;
        if (already) item.Progress = 100;

        // 写入历史
        var rec = new HistoryRecord
        {
            Username = AppState.Username,
            Course = req.Course,
            SessionDate = req.SessionDate,
            SessionTime = req.SessionTime,
            ViewNum = req.ViewNum,
            FileName = req.FileName,
            Url = req.Url,
            DestPath = dest,
            SizeText = "",
            Status = already ? "completed" : "downloading",
        };
        var url = req.Url;
        _ = Task.Run(() =>
        {
            var id = HistoryStore.Insert(rec);
            item.HistoryId = id;
        });

        OnUi(() =>
        {
            Items.Insert(0, item);
            if (!already) Pump();
        });
        return item;
    }

    private bool _restored;

    /// <summary>
    /// 启动时把历史库里的全部记录载入统一的下载列表（最新在上）：
    /// - 已完成 → 显示「已完成」，可点播放打开文件；
    /// - 未完成（上次关闭时仍在下载/暂停）→ 置为「已暂停」，点继续即可断点续传（aria2 --continue + .aria2 控制文件）；
    /// - 失败/已停止 → 保留对应状态，可重试/重新下载。
    /// 同一目标文件只保留最新一条，避免重复。
    /// </summary>
    public void RestoreAll()
    {
        if (_restored) return;
        _restored = true;
        var username = AppState.Username;

        _ = Task.Run(() =>
        {
            List<HistoryRecord> recs;
            try { recs = HistoryStore.ListByUser(username); }
            catch { return; }

            // ListByUser 按 id 倒序（最新在前）。同一目标文件只保留最新一条。
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var toAdd = new List<DownloadItem>();
            foreach (var r in recs)
            {
                if (!string.IsNullOrWhiteSpace(r.DestPath) && !seen.Add(r.DestPath)) continue;

                bool fileExists = !string.IsNullOrWhiteSpace(r.DestPath) && File.Exists(r.DestPath);
                bool ctrlExists = !string.IsNullOrWhiteSpace(r.DestPath) && File.Exists(r.DestPath + ".aria2");

                long partial = 0;
                try { if (fileExists) partial = new FileInfo(r.DestPath).Length; } catch { }

                DownloadState st;
                double progress = 0;
                bool pauseReq = false;
                string sizeText = r.SizeText;

                if (r.Status == "completed")
                {
                    st = DownloadState.Completed;
                    progress = 100;
                }
                else if (fileExists && !ctrlExists && (r.Status == "downloading" || r.Status == "paused"))
                {
                    // 文件已存在且无 .aria2 控制文件 —— 视为已下载完成，纠正历史状态。
                    st = DownloadState.Completed;
                    progress = 100;
                    try { HistoryStore.Update(r.Id, "completed", r.SizeText); } catch { }
                }
                else if (r.Status == "downloading" || r.Status == "paused")
                {
                    st = DownloadState.Paused;
                    pauseReq = true;
                    sizeText = partial > 0
                        ? $"已下载 {FormatSize(partial)}，点继续断点续传"
                        : "可断点续传，点继续开始";
                    try { HistoryStore.Update(r.Id, "paused", r.SizeText); } catch { }
                }
                else if (r.Status == "failed")
                {
                    st = DownloadState.Failed;
                    if (partial > 0) sizeText = $"已下载 {FormatSize(partial)}";
                }
                else // stopped 或未知
                {
                    st = DownloadState.Stopped;
                }

                var item = new DownloadItem
                {
                    Url = r.Url,
                    FileName = r.FileName,
                    DestPath = r.DestPath,
                    Course = r.Course,
                    SessionDate = r.SessionDate,
                    SessionTime = r.SessionTime,
                    ViewNum = r.ViewNum,
                    HistoryId = r.Id,
                };
                item.PauseRequested = pauseReq;
                item.State = st;
                item.Progress = progress;
                item.SizeText = sizeText;
                toAdd.Add(item);
            }

            if (toAdd.Count == 0) return;

            OnUi(() =>
            {
                foreach (var item in toAdd)
                {
                    if (Items.Any(x => x.HistoryId == item.HistoryId)) continue;
                    Items.Add(item); // recs 已按最新在前，依序 Add 即最新在上
                }
            });
        });
    }

    /// <summary>
    /// 程序退出时调用：把仍在进行/排队的任务在历史库标记为「已暂停」，并结束 aria2 进程。
    /// 配合 aria2 的 --stop-with-process / --auto-save-interval，保证下次可断点续传。
    /// </summary>
    public void ShutdownAll()
    {
        foreach (var item in Items.ToList())
        {
            if (item.State == DownloadState.Running || item.State == DownloadState.Queued)
            {
                item.PauseRequested = true;
                TryKill(item);
                if (item.HistoryId > 0)
                {
                    try { HistoryStore.Update(item.HistoryId, "paused", item.SizeText); } catch { }
                }
            }
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double s = bytes;
        int i = 0;
        while (s >= 1024 && i < units.Length - 1) { s /= 1024; i++; }
        return $"{s:0.#}{units[i]}";
    }

    public void Pause(DownloadItem item)
    {
        if (!item.CanPause) return;
        item.PauseRequested = true;
        SetState(item, DownloadState.Paused);
        TryKill(item);
    }

    public void Resume(DownloadItem item)
    {
        if (!item.CanResume) return;
        item.PauseRequested = false;
        item.StopRequested = false;
        OnUi(() =>
        {
            item.State = DownloadState.Queued;
            Pump();
        });
    }

    private void Pump() => OnUi(PumpCore);

    private void PumpCore()
    {
        int max = SettingsStore.Current.ClampConcurrent();
        if (_inflight.Count >= max) return;
        for (int i = Items.Count - 1; i >= 0 && _inflight.Count < max; i--)
        {
            var it = Items[i];
            if (it.State == DownloadState.Queued && !_inflight.Contains(it))
            {
                _inflight.Add(it);
                _ = RunOneAsync(it);
            }
        }
    }

    /// <summary>停止：终止下载、删除半成品文件、标记为已停止（终态）。</summary>
    public void Stop(DownloadItem item)
    {
        if (!item.CanStop) return;
        item.StopRequested = true;
        item.PauseRequested = false;
        TryKill(item);
        SetState(item, DownloadState.Stopped);
        OnUi(() =>
        {
            item.SpeedText = "";
            item.Error = "";
        });
        _ = Task.Run(() =>
        {
            Thread.Sleep(300); // 等 aria2c 释放文件句柄
            TryDelete(item.DestPath);
            TryDelete(item.DestPath + ".aria2");
        });
    }

    public void Remove(DownloadItem item)
    {
        TryKill(item);
        OnUi(() => Items.Remove(item));
    }

    /// <summary>删除：从列表移除并删除历史记录（保留已下载的本地文件）。</summary>
    public void Delete(DownloadItem item)
    {
        TryKill(item);
        if (item.HistoryId > 0)
        {
            var id = item.HistoryId;
            _ = Task.Run(() => HistoryStore.Delete(id));
        }
        OnUi(() => Items.Remove(item));
    }

    /// <summary>播放：用系统默认播放器打开已下载完成的视频文件。返回是否成功打开。</summary>
    public bool OpenFile(DownloadItem item)
    {
        try
        {
            if (File.Exists(item.DestPath))
            {
                Process.Start(new ProcessStartInfo { FileName = item.DestPath, UseShellExecute = true });
                return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }

    private static void TryKill(DownloadItem item)
    {
        try { if (item.Proc is { HasExited: false } p) p.Kill(true); }
        catch { /* ignore */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private async Task RunOneAsync(DownloadItem item)
    {
        try
        {
            if (item.PauseRequested) { SetState(item, DownloadState.Paused); return; }
            if (item.StopRequested) { return; }

            if (!Paths.Aria2cAvailable && !File.Exists(Paths.Aria2c))
            {
                OnUi(() => item.Error = "未找到 aria2c.exe，请安装 aria2 或放到 drivers\\aria2c.exe。");
                SetState(item, DownloadState.Failed);
                return;
            }

            await RunAria2Async(item);
        }
        catch (Exception ex)
        {
            OnUi(() => item.Error = ex.Message);
            SetState(item, DownloadState.Failed);
        }
        finally
        {
            OnUi(() =>
            {
                _inflight.Remove(item);
                PumpCore();
            });
        }
    }

    private static readonly Regex ReadoutRx = new(
        @"(?<done>\S+?)/(?<total>\S+?)\((?<pct>\d+)%\).*?DL:(?<dl>\S+)",
        RegexOptions.Compiled);

    private async Task RunAria2Async(DownloadItem item)
    {
        SetState(item, DownloadState.Running);

        var dir = Path.GetDirectoryName(item.DestPath) ?? SettingsStore.Current.EffectiveDownloadDir;
        Directory.CreateDirectory(dir);
        int conns = SettingsStore.Current.ClampConnections();

        var psi = new ProcessStartInfo
        {
            FileName = Paths.Aria2c,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = dir,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add($"--dir={dir}");
        psi.ArgumentList.Add($"--out={item.FileName}");
        psi.ArgumentList.Add("--continue=true");
        psi.ArgumentList.Add("--auto-file-renaming=false");
        psi.ArgumentList.Add("--allow-overwrite=true");
        // 程序退出时让 aria2 优雅停止并保存 .aria2 控制文件（断点续传 + 避免孤儿进程）
        psi.ArgumentList.Add($"--stop-with-process={Environment.ProcessId}");
        psi.ArgumentList.Add("--auto-save-interval=20");
        psi.ArgumentList.Add("--check-certificate=false");
        psi.ArgumentList.Add($"--max-connection-per-server={conns}");
        psi.ArgumentList.Add($"--split={conns}");
        psi.ArgumentList.Add("--min-split-size=4M");
        psi.ArgumentList.Add("--max-tries=10");
        psi.ArgumentList.Add("--retry-wait=3");
        psi.ArgumentList.Add("--connect-timeout=60");
        psi.ArgumentList.Add("--timeout=60");
        psi.ArgumentList.Add("--console-log-level=warn");
        psi.ArgumentList.Add("--summary-interval=1");
        psi.ArgumentList.Add("--show-console-readout=true");
        psi.ArgumentList.Add("--user-agent=Mozilla/5.0");
        psi.ArgumentList.Add("--referer=https://course.hdu.edu.cn/");
        psi.ArgumentList.Add(item.Url);

        var proc = new Process { StartInfo = psi };
        item.Proc = proc;
        var lastErr = "";

        proc.Start();

        var outTask = PumpAsync(proc.StandardOutput, line => HandleLine(item, line));
        var errTask = PumpAsync(proc.StandardError, line =>
        {
            if (!string.IsNullOrWhiteSpace(line)) lastErr = line.Trim();
        });

        await proc.WaitForExitAsync();
        await Task.WhenAll(outTask, errTask);

        int code = proc.ExitCode;
        item.Proc = null;

        if (item.StopRequested) return;
        if (item.PauseRequested) { SetState(item, DownloadState.Paused); return; }

        if (code == 0 && File.Exists(item.DestPath))
        {
            OnUi(() =>
            {
                item.Progress = 100;
                item.SpeedText = "";
                item.Error = "";
            });
            SetState(item, DownloadState.Completed);
        }
        else
        {
            OnUi(() =>
            {
                item.SpeedText = "";
                item.Error = string.IsNullOrEmpty(lastErr) ? $"aria2c 退出码 {code}" : lastErr;
            });
            SetState(item, DownloadState.Failed);
        }
    }

    private void HandleLine(DownloadItem item, string line)
    {
        var m = ReadoutRx.Match(line);
        if (!m.Success) return;
        var done = m.Groups["done"].Value;
        var total = m.Groups["total"].Value;
        var dl = m.Groups["dl"].Value;
        double pct = double.TryParse(m.Groups["pct"].Value, out var p) ? p : 0;

        OnUi(() =>
        {
            item.Progress = pct;
            item.SizeText = $"{done} / {total}";
            item.SpeedText = $"{dl}/s";
            if (item.State != DownloadState.Running && !item.PauseRequested && !item.StopRequested)
                item.State = DownloadState.Running;
        });
    }

    /// <summary>逐字符读取，按 \r 或 \n 切行（aria2 用 \r 刷新进度）。</summary>
    private static async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        var sb = new StringBuilder();
        var buf = new char[1024];
        int n;
        while ((n = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i < n; i++)
            {
                char c = buf[i];
                if (c == '\r' || c == '\n')
                {
                    if (sb.Length > 0) { onLine(sb.ToString()); sb.Clear(); }
                }
                else sb.Append(c);
            }
        }
        if (sb.Length > 0) onLine(sb.ToString());
    }
}
