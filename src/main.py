# main.py
import sys
import time
import json
import os

# 确保在 src 目录下运行时能正确导入同级模块
sys.path.append(os.path.dirname(os.path.abspath(__file__)))

from tokenchecker import check_token_status
from hdu_auth import HduCrawler
from hdu_api import HduApi

def load_config(config_file="config.json"):
    """
    智能路径加载：
    如果当前文件在 src 目录下，则自动去上一级（根目录）查找配置文件
    """
    # 获取当前 main.py 的绝对路径
    current_dir = os.path.dirname(os.path.abspath(__file__))
    
    # 判断是否在 src 目录下
    if os.path.basename(current_dir) == 'src':
        root_path = os.path.dirname(current_dir)
    else:
        root_path = current_dir
        
    config_path = os.path.join(root_path, config_file)
    
    if not os.path.exists(config_path):
        print(f"[X] 错误: 找不到配置文件 {config_path}")
        print(f"请确保 {config_file} 放在项目根目录下。")
        sys.exit(1)
    
    try:
        with open(config_path, 'r', encoding='utf-8') as f:
            return json.load(f)
    except Exception as e:
        print(f"[X] 解析配置文件失败: {e}")
        sys.exit(1)

def main():
    # --- 1. 从项目根目录加载凭据 ---
    config = load_config()
    USER = config.get("username")
    PWD = config.get("password")
    MAX_RETRIES = config.get("max_retries", 3)
    DAYS = config.get("days_offset", 7)
    
    # 传入 session_file 名称，HduCrawler 内部会处理路径将其放在根目录
    crawler = HduCrawler(USER, PWD)

    # --- 2. 检查本地缓存 (session.json) ---
    print("\n[1/3] 正在读取本地缓存...")
    token, cookie = crawler.load_local_session()

    # --- 3. 验证与重试逻辑 ---
    is_valid = False
    if token:
        print("[2/3] 正在验证 Token 有效性...")
        is_valid = check_token_status(token, cookie)
    else:
        print("[2/3] 本地无缓存，准备初始化登录。")
    
    if not is_valid:
        print(">>> 结论: Token 失效或不存在，启动 Selenium 补登流程...")
        success = False
        for i in range(MAX_RETRIES):
            print(f"[*] 第 {i+1}/{MAX_RETRIES} 次尝试...")
            token, cookie = crawler.get_session_credentials(force_refresh=True)
            if token:
                success = True
                print(f"[✓] 第 {i+1} 次尝试登录成功！")
                break
            
            if i < MAX_RETRIES - 1:
                print(f"[!] 登录卡住或失败，2秒后进行重试...")
                time.sleep(2)
        
        if not success:
            print(f"\n[X] 严重错误: 连续 {MAX_RETRIES} 次重试失败，流程终止。")
            return
    else:
        print(">>> 结论: Token 依然有效，跳过浏览器，直接起飞！")

    # --- 4. 业务逻辑 ---
    print(f"\n[3/3] 正在获取未来 {DAYS} 天的课程列表...")
    api = HduApi(token, cookie)
    courses = api.fetch_formatted_courses(days_offset=DAYS)

    if courses:
        print(f"\n" + "="*60)
        print(f"{'序号':<5} | {'课程名称':<25} | {'开始时间':<18}")
        print("-" * 60)
        for i, c in enumerate(courses):
            print(f"{i:<6} | {c['name']:<25} | {c['time_start']}")
        print("="*60)
    elif courses == []:
        print("\n[-] 鉴权成功，但当前时间段内未发现直播课程。")
    else:
        # 这里对应 api 返回 None 的情况（虽然上面已经 check 过了，但在获取数据瞬间失效的情况）
        print("\n[X] 获取数据时 Token 异常失效。")

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n[!] 用户手动退出。")