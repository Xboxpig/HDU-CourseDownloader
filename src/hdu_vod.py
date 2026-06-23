"""HDU 课程点播 (on-demand) API 封装。

三段式接口链路：
1. group_subject_vod_list  -> 点播课程(讲座)列表，每条含 teclId
2. subject_vod_list        -> 某课程下各节录像，每节含 id(=courId)
3. course_vod_urls         -> 某节录像的多机位 MP4 直链(带 auth_key)
"""

import requests
import urllib3

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)


class HduVod:
    def __init__(self, jwt_token, cookie_str=""):
        self.base_url = "https://course.hdu.edu.cn/jy-application-vod-he-hdu"
        self.headers = {
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
            "(KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36",
            "jwt-token": jwt_token,
            "Cookie": cookie_str or "",
            "Accept": "application/json, text/plain, */*",
            "Referer": "https://course.hdu.edu.cn/",
            "Origin": "https://course.hdu.edu.cn",
        }

    def _get(self, path, params=None):
        url = f"{self.base_url}{path}"
        res = requests.get(url, headers=self.headers, params=params,
                           verify=False, timeout=20)
        res.raise_for_status()
        return res.json()

    def list_courses(self, acte_id=8, page_index=1, page_size=50,
                     order_field="courPlayCount", asc=False):
        """点播课程(讲座)列表。返回 (records, page_count, row_count)。"""
        params = {
            "page.pageIndex": page_index,
            "page.pageSize": page_size,
            "page.orders[0].asc": "true" if asc else "false",
            "page.orders[0].field": order_field,
            "acteId": acte_id,
        }
        data = self._get("/v1/group_subject_vod_list", params).get("data", {}) or {}
        return (data.get("records", []) or [],
                data.get("pageCount"), data.get("rowCount"))

    def list_sessions(self, tecl_id, page_size=1000):
        """某课程(teclId)下的各节录像。返回 records 列表。"""
        params = {
            "page.pageIndex": 1,
            "page.pageSize": page_size,
            "teclIds": tecl_id,
            "page.orders[0].asc": "true",
            "page.orders[0].field": "courBeginTime",
        }
        data = self._get("/v1/subject_vod_list", params).get("data", {}) or {}
        return data.get("records", []) or []

    def get_vod_urls(self, course_id):
        """某节录像(courId)的多机位 MP4 直链。返回 data 字典。"""
        data = self._get("/v1/course_vod_urls", {"courseId": course_id})
        return data.get("data", {}) or {}

    def list_terms(self):
        """学期/学年列表（acteId 映射）。"""
        try:
            data = self._get("/v1/list/termYear")
            return data.get("data", []) or []
        except Exception:
            return []
