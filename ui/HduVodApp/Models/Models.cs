using System;
using System.Collections.Generic;
using System.Globalization;

namespace HduVodApp.Models;

public class TermItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public override string ToString() => Name;
}

public class CourseItem
{
    public string TeclId { get; set; } = "";
    public string SubjName { get; set; } = "";
    public string Teachers { get; set; } = "";
    public int CourTimes { get; set; }
    public int PlayCount { get; set; }

    public string Display => $"{SubjName}";
    public string SubTitle => $"{CourTimes} 节 · {Teachers} · 播放 {PlayCount} 次";
}

/// <summary>一节录像（按日期+时间展示）。</summary>
public class SessionItem
{
    public string CourId { get; set; } = "";
    public string SubjName { get; set; } = "";
    public string BeginRaw { get; set; } = "";
    public string EndRaw { get; set; } = "";

    private DateTime? Begin => ParseDt(BeginRaw);
    private DateTime? End => ParseDt(EndRaw);

    /// <summary>日期，如 2026-06-22（周一）。</summary>
    public string DateText
    {
        get
        {
            var b = Begin;
            if (b == null) return BeginRaw;
            string[] wk = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            return $"{b.Value:yyyy-MM-dd}  {wk[(int)b.Value.DayOfWeek]}";
        }
    }

    /// <summary>时间段，如 08:00 – 09:40。</summary>
    public string TimeText
    {
        get
        {
            var b = Begin;
            var e = End;
            if (b == null) return BeginRaw;
            var bt = b.Value.ToString("HH:mm");
            var et = e?.ToString("HH:mm") ?? "";
            return string.IsNullOrEmpty(et) ? bt : $"{bt} – {et}";
        }
    }

    public string Title => string.IsNullOrWhiteSpace(SubjName) ? "录像" : SubjName;

    public DateTime SortKey => Begin ?? DateTime.MinValue;

    private static DateTime? ParseDt(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        string[] fmts = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy/MM/dd HH:mm:ss" };
        if (DateTime.TryParseExact(s, fmts, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
            return dt;
        if (DateTime.TryParse(s, out dt)) return dt;
        return null;
    }
}

public class CameraView
{
    public int ViewNum { get; set; }
    public string VodId { get; set; } = "";
    public string CourId { get; set; } = "";
    public string Url { get; set; } = "";

    public string Label => ViewNum switch
    {
        1 => "后侧",
        3 => "前侧",
        5 => "电脑",
        _ => $"画面 {ViewNum}"
    };
}
