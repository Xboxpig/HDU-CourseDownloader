import time
import json
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))

from selenium import webdriver
from selenium.webdriver.chrome.service import Service
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC

from hdu_auth import HduCrawler

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def load_cfg():
    with open(os.path.join(ROOT, "config.json"), "r", encoding="utf-8") as f:
        return json.load(f)


def collect_api_calls(driver, store):
    """从 performance 日志里收集所有发往 course.hdu.edu.cn 的请求 URL。"""
    try:
        logs = driver.get_log("performance")
    except Exception:
        return
    for entry in logs:
        try:
            msg = json.loads(entry["message"])["message"]
        except Exception:
            continue
        method = msg.get("method")
        params = msg.get("params", {})
        if method == "Network.requestWillBeSent":
            url = params.get("request", {}).get("url", "")
            if "course.hdu.edu.cn" in url and ("/v1/" in url or "/jy-application" in url or "/api" in url):
                store.setdefault(url.split("?")[0], url)


def main():
    cfg = load_cfg()
    crawler = HduCrawler(cfg["username"], cfg["password"])

    opts = Options()
    opts.set_capability("goog:loggingPrefs", {"performance": "ALL"})
    opts.add_argument("--disable-blink-features=AutomationControlled")
    opts.add_argument("--no-sandbox")
    opts.add_argument("--disable-dev-shm-usage")
    opts.add_argument("--disable-gpu")
    opts.add_argument("--remote-allow-origins=*")
    import tempfile
    opts.add_argument(f"--user-data-dir={tempfile.mkdtemp(prefix='hduexp_')}")
    opts.binary_location = crawler._resolve_chrome_binary()

    driver = webdriver.Chrome(service=crawler._resolve_chromedriver(), options=opts)
    api_store = {}
    try:
        driver.get("https://course.hdu.edu.cn")
        wait = WebDriverWait(driver, 25)
        user_el = wait.until(EC.visibility_of_element_located((By.CSS_SELECTOR, crawler.SELECTORS["user"])))
        pass_el = driver.find_element(By.CSS_SELECTOR, crawler.SELECTORS["pass"])
        btn_el = driver.find_element(By.CSS_SELECTOR, crawler.SELECTORS["btn"])
        driver.execute_script(
            """
            const fill = (el, val) => { el.value = val; el.dispatchEvent(new Event('input', { bubbles: true })); };
            fill(arguments[0], arguments[2]); fill(arguments[1], arguments[3]);
            """,
            user_el, pass_el, cfg["username"], cfg["password"],
        )
        time.sleep(0.5)
        btn_el.click()
        print("[*] 登录已提交，等待主页加载...")

        # 收集登录后初始加载的接口
        for _ in range(15):
            time.sleep(1)
            collect_api_calls(driver, api_store)

        print("\n=== 登录后页面 URL ===")
        print(driver.current_url)

        # 打印当前页面上所有可见的导航/菜单文本，便于找到“点播”入口
        print("\n=== 页面可点击文本(含 点播/录播/视频/课程 关键字) ===")
        texts = driver.execute_script(
            """
            const out = [];
            document.querySelectorAll('a, button, span, li, div').forEach(el => {
                const t = (el.innerText || '').trim();
                if (t && t.length < 20 && /点播|录播|视频|回放|课程|我的/.test(t)) {
                    out.push(t);
                }
            });
            return Array.from(new Set(out));
            """
        )
        for t in texts:
            print("  -", t)

        # 记录点击前已知接口，便于 diff 出点播页新接口
        before = set(api_store.keys())

        # 尝试点击“课程点播”
        clicked = driver.execute_script(
            """
            const els = Array.from(document.querySelectorAll('a, button, span, li, div'));
            const target = els.find(el => (el.innerText || '').trim() === '课程点播');
            if (target) { target.click(); return true; }
            return false;
            """
        )
        print(f"\n[*] 点击“课程点播”: {clicked}")
        for _ in range(10):
            time.sleep(1)
            collect_api_calls(driver, api_store)
        print("\n=== 进入点播页后 URL ===")
        print(driver.current_url)
        print("\n=== 点播页新出现的接口 ===")
        for base in sorted(set(api_store.keys()) - before):
            print("  +", api_store[base])

        print("\n=== 捕获到的所有 API 请求 (去重) ===")
        for base, full in sorted(api_store.items()):
            print(base)

        # 保存完整 URL 列表到文件
        out_file = os.path.join(ROOT, "captured_api.json")
        with open(out_file, "w", encoding="utf-8") as f:
            json.dump(api_store, f, indent=2, ensure_ascii=False)
        print(f"\n[+] 完整 URL 已保存到 {out_file}")

        print("\n[*] 浏览器保持打开 8 秒继续抓取...")
        for _ in range(8):
            time.sleep(1)
            collect_api_calls(driver, api_store)
        with open(out_file, "w", encoding="utf-8") as f:
            json.dump(api_store, f, indent=2, ensure_ascii=False)
        print("\n=== 最终 API 列表 ===")
        for base in sorted(api_store.keys()):
            print(base)
    finally:
        driver.quit()


if __name__ == "__main__":
    main()
