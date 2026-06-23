"""供 WinUI 前端调用的登录辅助 CLI。

读取 config.json 的账号密码，走 Selenium 自动登录（hdu_auth.HduCrawler），
成功后回写 session.json，并在 stdout 打印一行 `RESULT_JSON:{...}`，
供 C# 端解析。普通日志直接打到 stdout/stderr 即可（前端会作为进度展示）。
"""

import os
import sys
import json

sys.path.append(os.path.dirname(os.path.abspath(__file__)))

from hdu_auth import HduCrawler  # noqa: E402


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    cfg_path = os.path.join(root, "config.json")

    try:
        with open(cfg_path, "r", encoding="utf-8") as f:
            cfg = json.load(f)
    except Exception as e:
        print("RESULT_JSON:" + json.dumps(
            {"ok": False, "token": "", "cookie": "", "message": f"读取 config.json 失败: {e}"}))
        return

    username = cfg.get("username", "")
    password = cfg.get("password", "")
    if not username or not password:
        print("RESULT_JSON:" + json.dumps(
            {"ok": False, "token": "", "cookie": "", "message": "config.json 缺少账号或密码"}))
        return

    print(f"[*] 启动浏览器自动登录（账号 {username}）...")
    crawler = HduCrawler(username, password)
    try:
        token, cookie = crawler.get_session_credentials(force_refresh=True)
    except Exception as e:
        print("RESULT_JSON:" + json.dumps(
            {"ok": False, "token": "", "cookie": "", "message": f"登录异常: {e}"}))
        return

    ok = bool(token)
    print("RESULT_JSON:" + json.dumps({
        "ok": ok,
        "token": token or "",
        "cookie": cookie or "",
        "message": "登录成功" if ok else "未能拦截到 token",
    }))


if __name__ == "__main__":
    main()
