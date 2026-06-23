import json
import os
import requests
import urllib3

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
with open(os.path.join(ROOT, "session.json"), "r", encoding="utf-8") as f:
    s = json.load(f)

TOKEN = s["jwt_token"]
COOKIE = s.get("cookie_str", "")

BASE = "https://course.hdu.edu.cn/jy-application-vod-he-hdu"
HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
    "jwt-token": TOKEN,
    "Cookie": COOKIE,
    "Accept": "application/json, text/plain, */*",
    "Referer": "https://course.hdu.edu.cn/",
    "Origin": "https://course.hdu.edu.cn",
}

# 候选点播(on-demand)端点
candidates = [
    "/v1/vod",
    "/v1/vod_record",
    "/v1/vod_play",
    "/v1/vod_list",
    "/v1/vod_on_demand",
    "/v1/record",
    "/v1/records",
    "/v1/vod_demand",
    "/v1/vod_video",
    "/v1/vod_resource",
    "/v1/my_vod",
    "/v1/vod_my",
    "/v1/course",
    "/v1/courses",
    "/v1/vod_course",
    "/v1/vod_replay",
    "/v1/replay",
]

params = {"page.pageIndex": 1, "page.pageSize": 5}

for path in candidates:
    url = BASE + path
    try:
        r = requests.get(url, headers=HEADERS, params=params, verify=False, timeout=8)
        body = r.text
        snippet = body[:300].replace("\n", " ")
        print(f"[{r.status_code}] {path} -> {snippet}")
    except Exception as e:
        print(f"[ERR] {path} -> {e}")
