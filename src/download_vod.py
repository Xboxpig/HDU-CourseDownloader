"""HDU 课程点播交互式下载器。

流程：登录/校验 -> 选学期 -> 选课程 -> 选某一节 -> 选机位 -> 下载(支持断点续传)。
"""

import os
import sys
import re
import json
import time

sys.path.append(os.path.dirname(os.path.abspath(__file__)))

import requests
import urllib3

from tokenchecker import check_token_status
from hdu_auth import HduCrawler
from hdu_vod import HduVod

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOWNLOAD_DIR = os.path.join(ROOT, "downloads")

# 机位编号 -> 友好名称（viewNum 一般 1=教师机位 3=屏幕/课件 5=全景，仅供参考）
VIEW_LABEL = {1: "机位1", 2: "机位2", 3: "机位3", 4: "机位4", 5: "机位5", 6: "机位6"}


def load_config():
    path = os.path.join(ROOT, "config.json")
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def ensure_token():
    """返回有效的 (token, cookie)，失效则走 Selenium 补登。"""
    cfg = load_config()
    crawler = HduCrawler(cfg["username"], cfg["password"])
    token, cookie = crawler.load_local_session()
    if token and check_token_status(token, cookie):
        print("[✓] Token 有效。")
        return token, cookie

    print("[*] Token 失效或不存在，启动浏览器补登...")
    for i in range(cfg.get("max_retries", 3)):
        print(f"    第 {i + 1} 次尝试...")
        token, cookie = crawler.get_session_credentials(force_refresh=True)
        if token:
            print("[✓] 登录成功。")
            return token, cookie
        time.sleep(2)
    print("[X] 登录失败，退出。")
    sys.exit(1)


def sanitize(name):
    name = re.sub(r'[\\/:*?"<>|]', "_", str(name)).strip()
    return name[:120] if name else "untitled"


def human_size(n):
    for unit in ["B", "KB", "MB", "GB", "TB"]:
        if n < 1024:
            return f"{n:.1f}{unit}"
        n /= 1024
    return f"{n:.1f}PB"


def ask_int(prompt, lo, hi, allow_blank=False):
    while True:
        s = input(prompt).strip()
        if allow_blank and s == "":
            return None
        if s.isdigit():
            v = int(s)
            if lo <= v <= hi:
                return v
        print(f"  请输入 {lo}-{hi} 之间的数字。")


def download_file(url, dest, referer="https://course.hdu.edu.cn/"):
    """带断点续传与进度显示的下载。"""
    headers = {"User-Agent": "Mozilla/5.0", "Referer": referer}
    tmp = dest + ".part"
    existing = os.path.getsize(tmp) if os.path.exists(tmp) else 0

    # 探测总大小与是否支持断点续传
    with requests.get(url, headers=headers, stream=True, verify=False, timeout=30) as probe:
        probe.raise_for_status()
        total = int(probe.headers.get("Content-Length", 0))
        accept_ranges = probe.headers.get("Accept-Ranges", "") == "bytes"

    mode = "wb"
    if existing and accept_ranges and existing < total:
        headers["Range"] = f"bytes={existing}-"
        mode = "ab"
        print(f"  断点续传：已有 {human_size(existing)} / {human_size(total)}")
    else:
        existing = 0

    downloaded = existing
    start = time.time()
    with requests.get(url, headers=headers, stream=True, verify=False, timeout=60) as r:
        r.raise_for_status()
        with open(tmp, mode) as f:
            for chunk in r.iter_content(chunk_size=1024 * 256):
                if not chunk:
                    continue
                f.write(chunk)
                downloaded += len(chunk)
                if total:
                    pct = downloaded / total * 100
                    elapsed = time.time() - start
                    speed = (downloaded - existing) / elapsed if elapsed > 0 else 0
                    bar_len = 30
                    filled = int(bar_len * downloaded / total)
                    bar = "#" * filled + "-" * (bar_len - filled)
                    sys.stdout.write(
                        f"\r  [{bar}] {pct:5.1f}%  "
                        f"{human_size(downloaded)}/{human_size(total)}  "
                        f"{human_size(speed)}/s   "
                    )
                    sys.stdout.flush()
    print()
    os.replace(tmp, dest)
    print(f"[✓] 已保存: {dest}")


def main():
    print("=" * 60)
    print("  HDU 课程点播下载器")
    print("=" * 60)
    token, cookie = ensure_token()
    vod = HduVod(token, cookie)
    os.makedirs(DOWNLOAD_DIR, exist_ok=True)

    # --- 选学期 ---
    acte_id = 8
    terms = vod.list_terms()
    if terms:
        print("\n可选学期：")
        for t in terms:
            tid = t.get("id") or t.get("acteId")
            tname = t.get("name") or t.get("acteName") or t.get("yearTerm")
            print(f"  acteId={tid}  {tname}")
        v = ask_int(f"输入学期 acteId (回车默认 {acte_id}): ", 0, 10 ** 9, allow_blank=True)
        if v is not None:
            acte_id = v

    while True:
        # --- 课程列表（分页）---
        page = 1
        page_size = 20
        course = None
        while course is None:
            records, page_count, row_count = vod.list_courses(
                acte_id=acte_id, page_index=page, page_size=page_size)
            if not records:
                print("[!] 该学期下没有点播课程。")
                return
            print(f"\n=== 点播课程  第 {page}/{page_count} 页  共 {row_count} 门 ===")
            for i, c in enumerate(records):
                teachers = "/".join(c.get("teacNames") or []) or "未知"
                print(f"  [{i:2}] {c.get('subjName'):<28} {c.get('courTimes', '?')}节  "
                      f"{teachers}  播放{c.get('courPlayCount')}次")
            print("  操作: 数字=选课  n=下一页  p=上一页  q=退出")
            sel = input("> ").strip().lower()
            if sel == "q":
                return
            if sel == "n":
                if page < (page_count or 1):
                    page += 1
                continue
            if sel == "p":
                if page > 1:
                    page -= 1
                continue
            if sel.isdigit() and 0 <= int(sel) < len(records):
                course = records[int(sel)]

        # --- 某课程下各节 ---
        sessions = vod.list_sessions(course["teclId"])
        if not sessions:
            print("[!] 该课程暂无可点播录像。")
            continue
        print(f"\n=== 《{course.get('subjName')}》 共 {len(sessions)} 节 ===")
        for i, s in enumerate(sessions):
            print(f"  [{i:2}] {s.get('courBeginTime')} ~ {s.get('courEndTime', '')[-8:]}  "
                  f"{s.get('subjName')}  (courId={s.get('id')})")
        idx = ask_int("选择第几节 (q 取消请输入 -1): ", -1, len(sessions) - 1)
        if idx == -1:
            continue
        session = sessions[idx]

        # --- 该节多机位直链 ---
        detail = vod.get_vod_urls(session["id"])
        views = detail.get("courseVodViewList") or []
        if not views:
            print("[!] 未获取到该节的视频地址（可能未生成点播或无权限）。")
            continue
        print(f"\n=== 该节共有 {len(views)} 路视频（机位）===")
        for i, v in enumerate(views):
            print(f"  [{i}] {VIEW_LABEL.get(v.get('viewNum'), '机位' + str(v.get('viewNum')))}"
                  f"  vodId={v.get('vodId')}  观看{v.get('viewNum')}")
        print("  输入序号下载单路，输入 a 下载全部机位，回车取消")
        sel = input("> ").strip().lower()
        if sel == "":
            continue
        chosen = list(range(len(views))) if sel == "a" else (
            [int(sel)] if sel.isdigit() and int(sel) < len(views) else [])
        if not chosen:
            print("  无效选择。")
            continue

        # --- 下载 ---
        begin = (session.get("courBeginTime") or "").replace(":", "-")
        base_name = sanitize(f"{course.get('subjName')}_{begin}")
        for ci in chosen:
            v = views[ci]
            label = VIEW_LABEL.get(v.get("viewNum"), f"view{v.get('viewNum')}")
            fname = sanitize(f"{base_name}_{label}_courId{v.get('courId')}_vod{v.get('vodId')}") + ".mp4"
            dest = os.path.join(DOWNLOAD_DIR, fname)
            if os.path.exists(dest):
                print(f"[skip] 已存在: {fname}")
                continue
            print(f"\n[下载] {fname}")
            try:
                download_file(v["url"], dest)
            except Exception as e:
                print(f"[X] 下载失败: {e}")


if __name__ == "__main__":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    try:
        main()
    except KeyboardInterrupt:
        print("\n[!] 已退出。")
