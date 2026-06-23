import json
import os
import requests
import urllib3

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
with open(os.path.join(ROOT, "session.json"), "r", encoding="utf-8") as f:
    s = json.load(f)

HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
    "jwt-token": s["jwt_token"],
    "Cookie": s.get("cookie_str", ""),
    "Accept": "application/json, text/plain, */*",
    "Referer": "https://course.hdu.edu.cn/",
    "Origin": "https://course.hdu.edu.cn",
}

BASE = "https://course.hdu.edu.cn/jy-application-vod-he-hdu"

targets = [
    ("watchRecord", f"{BASE}/v1/list/vod/watchRecord", {"page.pageIndex": 1, "page.pageSize": 9}),
    ("myself_curriculum", f"{BASE}/v1/myself/curriculum",
     {"acteId": 8, "courBeginTime": "2026-02-01 00:00:00", "courEndTime": "2026-06-28 23:59:59",
      "page.pageIndex": 1, "page.pageSize": 20}),
    ("termYear", f"{BASE}/v1/list/termYear", {}),
    ("labels", f"{BASE}/v1/list/labels", {}),
    ("myselsf", f"{BASE}/v1/myselsf", {}),
]

for name, url, params in targets:
    print("\n" + "=" * 70)
    print(f"### {name}: {url}")
    print(f"### params: {params}")
    try:
        r = requests.get(url, headers=HEADERS, params=params, verify=False, timeout=10)
        print(f"HTTP {r.status_code}")
        try:
            data = r.json()
            print(json.dumps(data, ensure_ascii=False, indent=2)[:4000])
        except Exception:
            print(r.text[:1500])
    except Exception as e:
        print("ERR", e)
