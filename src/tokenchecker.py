import requests
import json
import urllib3

# 禁用 SSL 警告
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

def check_token_status(token, cookie=""):
    """
    核心二分判断函数
    返回 True: Token 有效，可以继续
    返回 False: Token 失效 (10013)，需要重连
    """
    url = "https://course.hdu.edu.cn/jy-application-vod-he-hdu/v1/vod_live"
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
        "jwt-token": token,
        "Cookie": cookie
    }
    # 极简请求，只拿 1 条数据验证身份
    params = {"page.pageIndex": 1, "page.pageSize": 1}

    try:
        res = requests.get(url, headers=headers, params=params, verify=False, timeout=5)
        data = res.json()
        
        # --- 二分判断核心 ---
        # 10013 是你抓包确认的签名错误码
        if data.get("status") == 10013 or data.get("code") == "-1":
            print(f"[RECONNECT] 原因: {data.get('message', 'Token 签名错误')}")
            return False 
        
        # 只要没有命中上面的错误码，且能解析出 data，就认为有效
        if "data" in data:
            print("[KEEP] Token 有效，无需重连。")
            return True
            
        return False
    except Exception as e:
        print(f"[ERROR] 网络或格式错误: {e}")
        return False

# --- 模拟测试逻辑 ---
if __name__ == "__main__":
    # 模拟从 session.json 读取
    try:
        with open("session.json", "r") as f:
            session = json.load(f)
            my_token = session.get("jwt_token")
    except:
        my_token = "wrong_token_test"

    # 执行二分判断
    needs_reconnect = not check_token_status(my_token)

    if needs_reconnect:
        print(">>> 结论: 执行 Selenium 补登流程")
    else:
        print(">>> 结论: 直接执行业务 API")