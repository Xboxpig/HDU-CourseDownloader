import requests
import urllib3
import json
from datetime import datetime, timedelta

# 禁用不安全请求警告
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

class HduApi:
    def __init__(self, jwt_token, cookie_str):
        self.base_url = "https://course.hdu.edu.cn/jy-application-vod-he-hdu"
        self.headers = {
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "jwt-token": jwt_token,
            "Cookie": cookie_str,
            "Accept": "application/json, text/plain, */*",
            "Referer": "https://course.hdu.edu.cn/",
            "Origin": "https://course.hdu.edu.cn"
        }

    def fetch_formatted_courses(self, begin_time=None, end_time=None, days_offset=7):
        """
        获取格式化课程列表
        :return: 
            - 成功: 返回 list (包含课程字典)
            - Token失效 (如 10013 错误): 返回 None -> 触发 main.py 自动重新鉴权
            - 其他网络错误或空数据: 返回 []
        """
        url = f"{self.base_url}/v1/vod_live"
        
        # --- 动态时间处理 ---
        if not begin_time:
            begin_time = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        
        if not end_time:
            dt_begin = datetime.strptime(begin_time, "%Y-%m-%d %H:%M:%S")
            end_time = (dt_begin + timedelta(days=days_offset)).strftime("%Y-%m-%d %H:%M:%S")
        
        params = {
            "page.pageIndex": 1,
            "page.pageSize": 50,  # 提高单页容量
            "courBeginTime": begin_time,
            "courEndTime": end_time,
            "page.orders[0].asc": 1,
            "page.orders[0].field": "courBeginTime"
        }
        
        try:
            res = requests.get(url, headers=self.headers, params=params, verify=False, timeout=10)
            
            # 尝试将响应解析为 JSON
            try:
                data_json = res.json()
            except Exception:
                print(f"[!] 响应格式非 JSON。状态码: {res.status_code}")
                return []

            # --- 核心鉴权失效判定 (针对探测到的 10013 错误) ---
            # 即使 status_code 是 200，只要 body 里的状态码不对，也视为失效
            res_status = data_json.get("status")
            res_code = data_json.get("code")
            res_msg = data_json.get("message", "")

            if res_status == 10013 or res_code == "-1" or res.status_code == 401:
                # 只有确认是 Token 相关错误才返回 None
                if "token" in res_msg.lower() or "signature error" in res_msg.lower():
                    print(f"[!] 鉴权检测失效: {res_msg} (Status: {res_status})")
                    return None  # 重要：返回 None 以触发 main.py 的强制刷新逻辑

            # 如果不是 200 且也没命中上面的 Token 错误，按普通错误处理
            if res.status_code != 200:
                print(f"[!] 接口响应异常，HTTP状态码: {res.status_code}")
                return []

            # --- 正常数据解析 ---
            raw_records = data_json.get("data", {}).get("records", [])
            if not raw_records:
                return []

            formatted_list = []
            for record in raw_records:
                formatted_list.append({
                    "id": record.get("id"),
                    "name": record.get("subjName") or record.get("courName"),
                    "time_start": record.get('courBeginTime'),
                    "time_end": record.get('courEndTime'),
                    "location": record.get("roomName") or "远程/未知地点",
                    "teacher": record.get("teacNames", ["未知"])[0] if record.get("teacNames") else "未知"
                })
            
            return formatted_list

        except requests.exceptions.RequestException as e:
            print(f"[X] API 网络请求发生异常: {e}")
            return []
        except Exception as e:
            print(f"[X] 解析过程中发生未知错误: {e}")
            return []