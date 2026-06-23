# HDU 录播下载器

杭州电子科技大学（HDU）课程平台点播录像（VOD）下载工具。包含一个 **Python 抓取/鉴权后端** 和一个 **WinUI 3 桌面客户端**：自动登录课程平台、浏览课程与录像、选择机位，并用 aria2c 多线程下载，支持断点续传与重启恢复。

> 仅供本人学习、备份个人课程录像使用，请遵守学校与平台的相关规定，勿用于侵权或商业用途。

---

## 功能特性

- **自动登录**：基于 Selenium 自动完成课程平台登录并抓取访问 Token，凭证缓存到本地，下次可直接进入。
- **课程浏览**：按学期 / 日期浏览课程与录像，支持机位选择（后侧 / 前侧 / 电脑 / 全部画面），并配教室俯视示意图。
- **统一下载列表**：下载任务与历史二合一，最新在最上；已完成项标注「已完成」。
- **播放 / 暂停二合一**：单个可变图标按钮——下载中显示暂停、已暂停/失败显示继续、已完成显示播放（用系统默认播放器打开），旁边一个 ✕ 删除。
- **多线程下载**：基于 [aria2c](https://aria2.github.io/)，可配置并发数与单任务连接数。
- **断点续传 + 重启恢复**：程序退出时未完成的任务会被保存，下次启动恢复为「已暂停」，点继续即从断点接着下。

---

## 快速开始（QuickStart）

### 方式一：下载安装包（推荐普通用户）

1. 前往 [Releases](https://github.com/Xboxpig/HDU-CourseDownloader/releases) 下载最新的 `HDU-CourseDownloader-v1.0.0.exe`。
2. 双击运行（7-Zip 自解压，自带 .NET 8 运行时与 Windows App SDK，**免安装**）。
3. 在登录页：
   - 若已有有效凭证 → 点蓝色 **「使用已保存的凭证进入」**；
   - 否则输入 **学号 / 密码** → 点 **「登录 / 刷新凭证」**（首次会弹出 Chrome 自动登录窗口）。
4. **课程浏览** → 选课程、勾选录像、选机位 → 加入下载。
5. **下载列表** 查看进度，用 ▶/⏸ 暂停继续、用 ✕ 删除。

> 注意：发布包不会内置任何个人账号、密码、Token 或 Cookie。发布包运行时的 `config.json` / `session.json` / 下载历史默认保存在 `%LOCALAPPDATA%\HDU-CourseDownloader`。**首次登录（刷新凭证）依赖本项目的 Python 环境与 chromedriver**（见下方依赖说明）；如需让发布包调用源码目录的登录脚本，可设置环境变量 `HDU_COURSE_DOWNLOADER_ROOT` 指向项目根目录。

### 方式二：从源码运行（开发者）

```powershell
# 1) 克隆
git clone git@github.com:Xboxpig/HDU-CourseDownloader.git
cd HDU-CourseDownloader

# 2) 准备 Python 环境（用于 Selenium 自动登录）
python -m venv env
.\env\Scripts\pip install selenium requests
#   并把对应版本的 chromedriver.exe 放到 drivers\ 下

# 3) 准备配置（复制示例后填写自己的账号密码）
copy config.json.example config.json

# 4) 准备下载器：把 aria2c.exe 放到 drivers\，或用 scoop/choco 安装并加入 PATH
#   scoop install aria2

# 5) 运行桌面端（需 .NET 8 SDK + Windows App SDK 工作负载）
cd ui\HduVodApp
dotnet run -c Debug
```

---

## 依赖说明

| 依赖 | 用途 | 获取方式 |
|------|------|----------|
| .NET 8 SDK + Windows App SDK | 构建/运行 WinUI 3 客户端 | [dotnet.microsoft.com](https://dotnet.microsoft.com/) |
| Python 3.x + selenium | 自动登录抓取 Token（`src/login_cli.py`） | `pip install selenium requests` |
| Chrome + chromedriver | Selenium 驱动浏览器登录 | 放到 `drivers\chromedriver.exe`（版本需与本机 Chrome 匹配） |
| aria2c | 多线程下载 / 断点续传 | 放到 `drivers\aria2c.exe`，或 `scoop install aria2` |
| 7-Zip | 打包 SFX 单 exe（仅发布时需要） | `scoop install 7zip` |

> `drivers/`、`env/`、构建产物（`bin/`、`obj/`、`dist-release/`）及个人数据（`config.json`、`session.json`、`download_history.db`、日志）均已在 `.gitignore` 中忽略，不会进入仓库。源码运行时默认使用项目根目录保存这些文件；发布包运行时默认使用 `%LOCALAPPDATA%\HDU-CourseDownloader`，不会回退读取开发机绝对路径。

---

## 项目结构

```
.
├─ src/                     # Python 后端（登录 / 抓取 / API）
│  ├─ hdu_auth.py           #   Selenium 自动登录核心
│  ├─ hdu_api.py            #   课程 / 录像 / 流地址 API
│  └─ login_cli.py          #   供客户端调用的登录入口（输出 RESULT_JSON）
├─ ui/HduVodApp/            # WinUI 3 桌面客户端（.NET 8）
│  ├─ Pages/                #   登录 / 课程浏览 / 下载列表 / 设置
│  └─ Services/             #   下载管理 / 鉴权 / 历史库 / 设置
├─ ui/publish.ps1           # 打包成单个 SFX exe 的脚本
├─ config.json.example      # 配置示例（账号密码占位）
└─ .gitignore
```

---

## 构建发布包

```powershell
powershell -ExecutionPolicy Bypass -File .\ui\publish.ps1
# 产物：dist-release\HDU-CourseDownloader-v1.0.0.exe（自包含单 exe）
```

---

## 版本

当前版本：**v1.0.0**

- 课程浏览 + 机位选择（含示意图）
- 下载任务 / 历史 二合一的统一下载列表
- 断点续传 + 重启恢复
- 登录页凭证缓存与按钮逻辑优化

---

## 免责声明

本项目仅用于个人学习与课程录像的合法备份。使用者需对自身行为负责，作者不对任何滥用承担责任。
