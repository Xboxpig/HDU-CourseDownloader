using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HduVodApp.Models;

namespace HduVodApp.Services;

/// <summary>
/// HDU 点播 API 的 C# 封装，镜像 src/hdu_vod.py 的三段链路：
/// group_subject_vod_list -> subject_vod_list -> course_vod_urls。
/// </summary>
public class HduApiClient
{
    private const string Base = "https://course.hdu.edu.cn/jy-application-vod-he-hdu";
    private readonly HttpClient _http;
    private readonly string _token;
    private readonly string _cookie;

    public HduApiClient(string token, string cookie)
    {
        _token = token;
        _cookie = cookie ?? "";
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
    }

    private void ApplyHeaders(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation("jwt-token", _token);
        if (!string.IsNullOrEmpty(_cookie))
            req.Headers.TryAddWithoutValidation("Cookie", _cookie);
        req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        req.Headers.TryAddWithoutValidation("Referer", "https://course.hdu.edu.cn/");
        req.Headers.TryAddWithoutValidation("Origin", "https://course.hdu.edu.cn");
    }

    private async Task<JsonNode?> GetAsync(string path, string query)
    {
        var url = $"{Base}{path}{(string.IsNullOrEmpty(query) ? "" : "?" + query)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(req);
        using var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var text = await res.Content.ReadAsStringAsync();
        return JsonNode.Parse(text);
    }

    /// <summary>校验 token 是否有效（10013 = 签名失效）。</summary>
    public async Task<bool> CheckTokenAsync()
    {
        try
        {
            var node = await GetAsync("/v1/vod_live", "page.pageIndex=1&page.pageSize=1");
            if (node == null) return false;
            var status = node["status"]?.ToString();
            var code = node["code"]?.ToString();
            if (status == "10013" || code == "-1") return false;
            return node["data"] != null;
        }
        catch { return false; }
    }

    public async Task<List<TermItem>> ListTermsAsync()
    {
        var result = new List<TermItem>();
        try
        {
            var node = await GetAsync("/v1/list/termYear", "");
            System.Text.Json.Nodes.JsonArray? arr = node as System.Text.Json.Nodes.JsonArray;
            if (arr == null && node?["data"] is System.Text.Json.Nodes.JsonArray dataArr)
                arr = dataArr;
            if (arr != null)
            {
                foreach (var t in arr)
                {
                    if (t == null) continue;
                    var id = t["id"]?.ToString() ?? t["acteId"]?.ToString() ?? "";
                    var name = t["name"]?.ToString() ?? t["acteName"]?.ToString()
                               ?? t["yearTerm"]?.ToString() ?? t["acyeCode"]?.ToString() ?? id;
                    if (!string.IsNullOrEmpty(id))
                        result.Add(new TermItem { Id = id, Name = name });
                }
            }
        }
        catch { /* ignore */ }
        return result;
    }

    /// <summary>点播课程（讲座）列表。返回 (课程, 总页数, 总条数)。</summary>
    public async Task<(List<CourseItem> Courses, int PageCount, int RowCount)> ListCoursesAsync(
        string acteId, int pageIndex, int pageSize)
    {
        var q = $"page.pageIndex={pageIndex}&page.pageSize={pageSize}" +
                "&page.orders%5B0%5D.asc=false&page.orders%5B0%5D.field=courPlayCount" +
                $"&acteId={Uri.EscapeDataString(acteId)}";
        var node = await GetAsync("/v1/group_subject_vod_list", q);
        var data = node?["data"];
        var list = new List<CourseItem>();
        if (data?["records"] is JsonArray arr)
        {
            foreach (var c in arr)
            {
                if (c == null) continue;
                var teachers = "";
                if (c["teacNames"] is JsonArray tn)
                {
                    var parts = new List<string>();
                    foreach (var x in tn) parts.Add(x?.ToString() ?? "");
                    teachers = string.Join("/", parts);
                }
                list.Add(new CourseItem
                {
                    TeclId = c["teclId"]?.ToString() ?? "",
                    SubjName = c["subjName"]?.ToString() ?? "(未命名)",
                    Teachers = string.IsNullOrEmpty(teachers) ? "未知" : teachers,
                    CourTimes = TryInt(c["courTimes"]),
                    PlayCount = TryInt(c["courPlayCount"]),
                });
            }
        }
        int pc = TryInt(data?["pageCount"], 1);
        int rc = TryInt(data?["rowCount"]);
        return (list, pc < 1 ? 1 : pc, rc);
    }

    /// <summary>某课程（teclId）下各节录像。</summary>
    public async Task<List<SessionItem>> ListSessionsAsync(string teclId)
    {
        var q = "page.pageIndex=1&page.pageSize=1000" +
                $"&teclIds={Uri.EscapeDataString(teclId)}" +
                "&page.orders%5B0%5D.asc=true&page.orders%5B0%5D.field=courBeginTime";
        var node = await GetAsync("/v1/subject_vod_list", q);
        var list = new List<SessionItem>();
        if (node?["data"]?["records"] is JsonArray arr)
        {
            foreach (var s in arr)
            {
                if (s == null) continue;
                list.Add(new SessionItem
                {
                    CourId = s["id"]?.ToString() ?? "",
                    SubjName = s["subjName"]?.ToString() ?? "",
                    BeginRaw = s["courBeginTime"]?.ToString() ?? "",
                    EndRaw = s["courEndTime"]?.ToString() ?? "",
                });
            }
        }
        return list;
    }

    /// <summary>某节录像（courId）的多机位直链。</summary>
    public async Task<List<CameraView>> GetVodUrlsAsync(string courseId)
    {
        var node = await GetAsync("/v1/course_vod_urls", $"courseId={Uri.EscapeDataString(courseId)}");
        var list = new List<CameraView>();
        if (node?["data"]?["courseVodViewList"] is JsonArray arr)
        {
            foreach (var v in arr)
            {
                if (v == null) continue;
                var url = v["url"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(url)) continue;
                list.Add(new CameraView
                {
                    ViewNum = TryInt(v["viewNum"]),
                    VodId = v["vodId"]?.ToString() ?? "",
                    CourId = v["courId"]?.ToString() ?? "",
                    Url = url,
                });
            }
        }
        return list;
    }

    private static int TryInt(JsonNode? n, int dflt = 0)
    {
        if (n == null) return dflt;
        try { return n.GetValue<int>(); } catch { }
        if (int.TryParse(n.ToString(), out var v)) return v;
        return dflt;
    }
}
