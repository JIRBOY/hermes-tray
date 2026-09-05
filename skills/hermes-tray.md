# Hermes Tray 指挥协议（内置工具）

> Hermes 本体住 Session 0（服务会话，无桌面）。HermesTray.exe 住 Session 1（用户桌面），是 Hermes 的"GUI 小弟"。两者通过 `127.0.0.1:51234` 通信。

## 部署位置

| 项 | 路径 |
|----|------|
| 程序(发布产物) | `D:\Manager\AppData\Hermes\scripts\hermes-tray\HermesTray.exe` |
| CLI 封装(推荐调用方式) | `D:\Manager\AppData\Hermes\scripts\hermes_tray.py` |
| 启动器(用户双击) | `D:\Manager\AppData\Hermes\gui_helper\start_gui_agent.bat` |
| Token | `D:\Manager\AppData\Hermes\gui_helper\token.txt`（首次运行自动生成） |
| 截图目录 | `D:\Manager\AppData\Hermes\gui_helper\shots\` |
| 日志 | `D:\Manager\AppData\Hermes\gui_helper\hermes_tray.log` |
| 源码工程 | `D:\Manager\AppData\Hermes\gui_helper\HermesTray\`（dotnet build 后重新 publish） |
| 自启 | 计划任务 `HermesGuiHelper`(ONLOGON) + Startup 文件夹双保险 |

## CLI 用法（Hermes 内直接执行）

```bash
PY="D:/Program/Hermes/hermes-agent/venv/Scripts/python.exe"
CLI="D:/Manager/AppData/Hermes/scripts/hermes_tray.py"
$PY $CLI health
$PY $CLI windows                          # 列用户桌面可见窗口
$PY $CLI screenshot                        # 全屏截图 → shots/
$PY $CLI screenshot "SAP2000"             # 仅截某窗口(自动激活)
$PY $CLI click 1200 800                    # 点击屏幕坐标
$PY $CLI rightclick 100 100
$PY $CLI type "中文文本测试"              # 中文走剪贴板
$PY $CLI key ctrl+s                        # ^s；支持 alt+tab/win+d/enter/f5...
$PY $CLI scroll 3                          # 正值向上滚
$PY $CLI window activate "记事本"
$PY $CLI window close "计算器"
$PY $CLI app start "C:/Windows/notepad.exe"
```

## 坐标策略

1. 先 `screenshot` 拿图 → 用视觉模型看图确定目标坐标（多显示器注意主屏原点）
2. `windows` 返回窗口 rect(left/top/width/height)，可计算相对坐标：点击窗口内(x,y) = (left+offset_x, top+offset_y)
3. 点击前若需激活窗口：`window activate <标题>`

## 常见故障

| 症状 | 原因 | 处理 |
|------|------|------|
| CLI 返回"连接拒绝"(10061) | HermesTray 未运行 | 请用户双击 start_gui_agent.bat；或等下次登录自启 |
| 401 unauthorized | token 文件被改/不一致 | 删 token.txt 后重启 tray 重新生成 |
| screenshot 报"句柄无效" | 进程跑在 Session 0 | HermesTray 必须由用户会话启动，勿在 Session 0 手动跑 |
| windows 返回 count=0 | 同上（Session 0 枚举不到桌面窗口） | 同上 |
| 端口冲突 51234 | 残留进程占端口 | 任务管理器结束 HermesTray 后重启 |

## 安全边界

- 仅绑定 127.0.0.1，X-Agent-Token 认证（token 与 Hermes 共享于 gui_helper\token.txt）
- 无任意 shell 执行；/app 仅 start 进程
- 所有操作写 hermes_tray.log
- 重新编译：`D:/Program/dotnet-sdk/dotnet.exe publish HermesTray.csproj -c Release -o D:/Manager/AppData/Hermes/scripts/hermes-tray`
