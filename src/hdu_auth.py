import time
import json
import os
import tempfile
from selenium import webdriver
from selenium.webdriver.chrome.service import Service
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC
from webdriver_manager.chrome import ChromeDriverManager


class HduCrawler:
    def __init__(self, username, password, session_file="session.json"):
        self.username = username
        self.password = password
        
        # --- 路径兼容逻辑改进 ---
        # 1. 获取当前文件 (hdu_auth.py) 所在的绝对路径
        current_dir = os.path.dirname(os.path.abspath(__file__))
        
        # 2. 如果当前在 src 目录下，回退一级到项目根目录
        if os.path.basename(current_dir) == 'src':
            root_dir = os.path.dirname(current_dir)
        else:
            root_dir = current_dir
            
        # 3. 将 session 文件保存在根目录，而不是 src 内部
        self.session_file = os.path.join(root_dir, session_file)
        
        # 打印一下，方便 Debug 确认路径对不对
        print(f"[DEBUG] Session file path: {self.session_file}")

        self.SELECTORS = {
            "user": "html > body > app-root > app-right-root > rg-page-container > div > div:nth-of-type(2) > div > div:nth-of-type(2) > div:nth-of-type(2) > div > app-login-auth-panel > div > div > app-login-normal > div > div:nth-of-type(2) > form > div > nz-input-group > input",
            "pass": "html > body > app-root > app-right-root > rg-page-container > div > div:nth-of-type(2) > div > div:nth-of-type(2) > div:nth-of-type(2) > div > app-login-auth-panel > div > div > app-login-normal > div > div:nth-of-type(2) > form > div:nth-of-type(2) > nz-input-group > input",
            "btn":  "html > body > app-root > app-right-root > rg-page-container > div > div:nth-of-type(2) > div > div:nth-of-type(2) > div:nth-of-type(2) > div > app-login-auth-panel > div > div > app-login-normal > div > div:nth-of-type(2) > form > div:nth-of-type(6) > div > button"
        }

    def debug_log(self, msg):
        print(f"[{time.strftime('%H:%M:%S')}] [DEBUG] {msg}")

    def load_local_session(self):
        """尝试从本地加载缓存，核心关注 jwt_token"""
        if not os.path.exists(self.session_file):
            self.debug_log(f"Session 文件不存在: {self.session_file}")
            return None, None
        
        try:
            with open(self.session_file, 'r', encoding='utf-8') as f:
                data = json.load(f)
                token = data.get("jwt_token")
                cookie = data.get("cookie_str") or "" # 如果没有 cookie 则赋予空字符串
                
                # 只要 token 存在且非空，就判定为读取成功
                if token and len(token) > 50:
                    self.debug_log(f"本地缓存读取成功 (Token 长度: {len(token)})")
                    return token, cookie
        except Exception as e:
            self.debug_log(f"读取 session.json 出错: {e}")
        return None, None

    def save_session(self, jwt_token, cookie_str):
        """保存截获的凭证到本地"""
        try:
            data = {
                "jwt_token": jwt_token,
                "cookie_str": cookie_str if cookie_str else "",
                "update_time": time.strftime("%Y-%m-%d %H:%M:%S")
            }
            with open(self.session_file, 'w', encoding='utf-8') as f:
                json.dump(data, f, indent=4)
            self.debug_log(">>> 凭证已同步至本地存储。")
        except Exception as e:
            self.debug_log(f"保存凭证至文件时出错: {e}")

    def get_session_credentials(self, force_refresh=False):
        """
        获取鉴权信息的主入口
        :param force_refresh: 是否忽略缓存直接启动浏览器
        """
        if not force_refresh:
            self.debug_log("检查本地缓存...")
            token, cookie = self.load_local_session()
            if token:
                self.debug_log(">>> [命中缓存] 跳过自动化登录流程。")
                return token, cookie
            else:
                self.debug_log(">>> [未命中] 缓存无效或不存在。")
                # 按照要求，若 get_session_credentials 被普通调用且未命中的逻辑
                # 如果你想让它此时自动调 Selenium，可以取消下面 _run_selenium_auth 的注释
                # return self._run_selenium_auth()
                return None, None
        
        return self._run_selenium_auth()

    def _resolve_chromedriver(self):
        """优先使用项目内置的本地 chromedriver，避免联网下载被墙。"""
        current_dir = os.path.dirname(os.path.abspath(__file__))
        root_dir = os.path.dirname(current_dir) if os.path.basename(current_dir) == 'src' else current_dir
        local_driver = os.path.join(root_dir, "drivers", "chromedriver-win64", "chromedriver.exe")
        if os.path.exists(local_driver):
            self.debug_log(f"使用本地 chromedriver: {local_driver}")
            return Service(local_driver)
        self.debug_log("未找到本地 chromedriver，回退到 webdriver_manager 在线下载...")
        return Service(ChromeDriverManager().install())

    def _resolve_chrome_binary(self):
        """定位 Chrome 可执行文件（兼容安装在用户目录的情况）。"""
        candidates = [
            os.path.join(os.environ.get("LOCALAPPDATA", ""), r"Google\Chrome\Application\chrome.exe"),
            r"C:\Program Files\Google\Chrome\Application\chrome.exe",
            r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        ]
        for c in candidates:
            if c and os.path.exists(c):
                return c
        return None

    def _run_selenium_auth(self):
        """执行 Selenium 自动化登录并拦截流量"""
        self.debug_log("正在启动 Chrome 浏览器执行授权...")
        opts = Options()
        opts.set_capability('goog:loggingPrefs', {'performance': 'ALL'})
        opts.add_argument("--disable-blink-features=AutomationControlled")
        opts.add_argument("--no-sandbox")
        opts.add_argument("--disable-dev-shm-usage")
        opts.add_argument("--disable-gpu")
        opts.add_argument("--remote-allow-origins=*")
        # 使用独立临时用户数据目录，避免与已打开的 Chrome 默认配置冲突
        self._user_data_dir = tempfile.mkdtemp(prefix="hduchrome_")
        opts.add_argument(f"--user-data-dir={self._user_data_dir}")

        chrome_bin = self._resolve_chrome_binary()
        if chrome_bin:
            opts.binary_location = chrome_bin
            self.debug_log(f"使用 Chrome 二进制: {chrome_bin}")

        driver = webdriver.Chrome(service=self._resolve_chromedriver(), options=opts)
        try:
            driver.get("https://course.hdu.edu.cn")
            wait = WebDriverWait(driver, 25)
            
            # 定位并填入
            user_el = wait.until(EC.visibility_of_element_located((By.CSS_SELECTOR, self.SELECTORS["user"])))
            pass_el = driver.find_element(By.CSS_SELECTOR, self.SELECTORS["pass"])
            btn_el = driver.find_element(By.CSS_SELECTOR, self.SELECTORS["btn"])

            self.debug_log("正在自动填充账号密码...")
            driver.execute_script("""
                const fill = (el, val) => {
                    el.value = val;
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                };
                fill(arguments[0], arguments[2]);
                fill(arguments[1], arguments[3]);
            """, user_el, pass_el, self.username, self.password)
            
            time.sleep(0.5)
            btn_el.click()
            self.debug_log("登录已提交，等待 API 响应...")

            # 拦截流量
            start_time = time.time()
            while time.time() - start_time < 30:
                logs = driver.get_log('performance')
                for entry in logs:
                    log = json.loads(entry['message'])['message']
                    if log.get('method') == 'Network.requestWillBeSent':
                        headers = log['params']['request'].get('headers', {})
                        if 'jwt-token' in headers:
                            token = headers['jwt-token']
                            # 尝试获取 Header 里的 Cookie
                            cookie = headers.get('Cookie', headers.get('cookie', ''))
                            
                            # 如果 Header 里没 Cookie，尝试从驱动获取整个域名的 Cookie
                            if not cookie:
                                self.debug_log("API Header 无 Cookie，尝试获取浏览器全量 Cookie...")
                                selenium_cookies = driver.get_cookies()
                                cookie = "; ".join([f"{c['name']}={c['value']}" for c in selenium_cookies])
                                
                            self.save_session(token, cookie)
                            return token, cookie
                time.sleep(1)
            
            self.debug_log("[X] 拦截 Token 超时。")
            return None, None
        except Exception as e:
            self.debug_log(f"[X] Selenium 流程发生异常: {e}")
            return None, None
        finally:
            driver.quit()