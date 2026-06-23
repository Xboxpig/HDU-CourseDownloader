using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace HduVodApp.Services;

/// <summary>一条下载历史记录（按用户分割存储）。</summary>
public class HistoryRecord
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Course { get; set; } = "";
    public string SessionDate { get; set; } = "";
    public string SessionTime { get; set; } = "";
    public int ViewNum { get; set; }
    public string FileName { get; set; } = "";
    public string Url { get; set; } = "";
    public string DestPath { get; set; } = "";
    public string SizeText { get; set; } = "";
    public string Status { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";

    // 展示用
    public string DateTimeText =>
        string.IsNullOrWhiteSpace(SessionDate) ? CreatedAt : $"{SessionDate} {SessionTime}";
    public string StatusText => Status switch
    {
        "completed" => "已完成",
        "downloading" => "下载中",
        "paused" => "已暂停",
        "failed" => "失败",
        "stopped" => "已停止",
        _ => Status
    };
}

/// <summary>基于 SQLite 的下载历史表，按 username 分割（每个用户查询自己的记录）。</summary>
public static class HistoryStore
{
    private static string Conn => $"Data Source={Paths.HistoryDb}";
    private static bool _inited;

    public static void Init()
    {
        if (_inited) return;
        using var con = new SqliteConnection(Conn);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS downloads (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    username     TEXT NOT NULL,
    course       TEXT,
    session_date TEXT,
    session_time TEXT,
    view_num     INTEGER,
    file_name    TEXT,
    url          TEXT,
    dest_path    TEXT,
    size_text    TEXT,
    status       TEXT,
    created_at   TEXT,
    updated_at   TEXT
);
CREATE INDEX IF NOT EXISTS idx_downloads_user ON downloads(username);";
        cmd.ExecuteNonQuery();
        _inited = true;
    }

    public static long Insert(HistoryRecord r)
    {
        try
        {
            Init();
            using var con = new SqliteConnection(Conn);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO downloads
 (username, course, session_date, session_time, view_num, file_name, url, dest_path, size_text, status, created_at, updated_at)
VALUES
 ($u, $c, $sd, $st, $vn, $fn, $url, $dp, $sz, $status, $now, $now);
SELECT last_insert_rowid();";
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            cmd.Parameters.AddWithValue("$u", r.Username);
            cmd.Parameters.AddWithValue("$c", r.Course);
            cmd.Parameters.AddWithValue("$sd", r.SessionDate);
            cmd.Parameters.AddWithValue("$st", r.SessionTime);
            cmd.Parameters.AddWithValue("$vn", r.ViewNum);
            cmd.Parameters.AddWithValue("$fn", r.FileName);
            cmd.Parameters.AddWithValue("$url", r.Url);
            cmd.Parameters.AddWithValue("$dp", r.DestPath);
            cmd.Parameters.AddWithValue("$sz", r.SizeText);
            cmd.Parameters.AddWithValue("$status", r.Status);
            cmd.Parameters.AddWithValue("$now", now);
            var id = (long)(cmd.ExecuteScalar() ?? 0L);
            return id;
        }
        catch { return 0; }
    }

    public static void Update(long id, string status, string sizeText)
    {
        if (id <= 0) return;
        try
        {
            Init();
            using var con = new SqliteConnection(Conn);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
UPDATE downloads
   SET status = $status,
       size_text = CASE WHEN $sz <> '' THEN $sz ELSE size_text END,
       updated_at = $now
 WHERE id = $id;";
            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$sz", sizeText ?? "");
            cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        catch { /* ignore */ }
    }

    public static List<HistoryRecord> ListByUser(string username)
    {
        var list = new List<HistoryRecord>();
        try
        {
            Init();
            using var con = new SqliteConnection(Conn);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT id, username, course, session_date, session_time, view_num, file_name, url,
       dest_path, size_text, status, created_at, updated_at
  FROM downloads
 WHERE username = $u
 ORDER BY id DESC;";
            cmd.Parameters.AddWithValue("$u", username ?? "");
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new HistoryRecord
                {
                    Id = rd.GetInt64(0),
                    Username = rd.GetString(1),
                    Course = rd.IsDBNull(2) ? "" : rd.GetString(2),
                    SessionDate = rd.IsDBNull(3) ? "" : rd.GetString(3),
                    SessionTime = rd.IsDBNull(4) ? "" : rd.GetString(4),
                    ViewNum = rd.IsDBNull(5) ? 0 : rd.GetInt32(5),
                    FileName = rd.IsDBNull(6) ? "" : rd.GetString(6),
                    Url = rd.IsDBNull(7) ? "" : rd.GetString(7),
                    DestPath = rd.IsDBNull(8) ? "" : rd.GetString(8),
                    SizeText = rd.IsDBNull(9) ? "" : rd.GetString(9),
                    Status = rd.IsDBNull(10) ? "" : rd.GetString(10),
                    CreatedAt = rd.IsDBNull(11) ? "" : rd.GetString(11),
                    UpdatedAt = rd.IsDBNull(12) ? "" : rd.GetString(12),
                });
            }
        }
        catch { /* ignore */ }
        return list;
    }

    public static void Delete(long id)
    {
        if (id <= 0) return;
        try
        {
            Init();
            using var con = new SqliteConnection(Conn);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM downloads WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        catch { /* ignore */ }
    }
}
