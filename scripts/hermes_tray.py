#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Hermes Tray CLI — 指挥 Session 1 的 GUI 小弟（HermesTray.exe）
==============================================================
Hermes 本体在 Session 0 无法碰桌面；HermesTray 常驻用户桌面会话(Session 1),
暴露 http://127.0.0.1:51234。本 CLI 自动读 token 并发起 JSON 请求。

用法:
  python hermes_tray.py health                     # 健康检查
  python hermes_tray.py status                     # 详细状态
  python hermes_tray.py windows                    # 列出可见窗口
  python hermes_tray.py screenshot [title]         # 截图（全屏/指定窗口）
  python hermes_tray.py screenshot-b64 [title]     # 截图返回base64
  python hermes_tray.py click X Y                  # 点击坐标
  python hermes_tray.py rightclick X Y             # 右键点击
  python hermes_tray.py type "文本"                # 输入（中文走剪贴板）
  python hermes_tray.py key ctrl+s                 # 按键组合
  python hermes_tray.py scroll 3                   # 滚轮（正上负下）
  python hermes_tray.py window activate "标题"     # 激活窗口
  python hermes_tray.py window close "标题"        # 关闭窗口
  python hermes_tray.py focus "标题"               # 聚焦窗口
  python hermes_tray.py exists "标题"              # 检查窗口是否存在
  python hermes_tray.py wait "标题" [timeout_ms]   # 等待窗口出现
  python hermes_tray.py mouse-pos                  # 获取鼠标位置
  python hermes_tray.py move-mouse X Y             # 移动鼠标
  python hermes_tray.py screen-size                # 获取屏幕尺寸
  python hermes_tray.py app start "路径" [参数]    # 启动应用
  python hermes_tray.py run "cmd" [args] [--wait] [--workdir DIR] [--timeout MS] [--shell]
  python hermes_tray.py run "abaqus.bat" "job=test input=test.inp" --wait --workdir "D:/temp"
  python hermes_tray.py run "python" "script.py" --wait --workdir "D:/Projects"
  python hermes_tray.py run "dir" "" --shell --wait           # shell模式
  python hermes_tray.py process <pid>                          # 查询进程状态
  python hermes_tray.py process-list                           # 列出所有进程
  python hermes_tray.py process-kill <pid>                     # 终止进程
  python hermes_tray.py shutdown                               # 关闭Hermes Tray
  python hermes_tray.py shot-dir                   # 打印截图目录
"""
import json
import sys
import urllib.request

BASE = "http://127.0.0.1:51234"
TOKEN_FILE = r"D:\Manager\AppData\Hermes\gui_helper\token.txt"
SHOTS = r"D:\Manager\AppData\Hermes\gui_helper\shots"


def token():
    try:
        with open(TOKEN_FILE, "r", encoding="utf-8") as f:
            t = f.read().strip()
        if len(t) >= 16:
            return t
    except FileNotFoundError:
        pass
    return ""


def call(method, path, payload=None):
    t = token()
    if not t:
        return {"ok": False, "error": "token 缺失——HermesTray 尚未运行"}
    body = json.dumps(payload).encode("utf-8") if payload is not None else None
    req = urllib.request.Request(BASE + path, data=body, method=method,
                                 headers={"X-Agent-Token": t,
                                          "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            return json.loads(e.read().decode("utf-8"))
        except Exception:
            return {"ok": False, "error": f"HTTP {e.code}"}
    except Exception as e:
        return {"ok": False, "error": f"{type(e).__name__}: {e}"}


def main():
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        return
    cmd = args[0]
    if cmd == "shot-dir":
        print(SHOTS)
        return
    if cmd == "health":
        r = call("GET", "/health")
    elif cmd == "status":
        r = call("GET", "/status")
    elif cmd == "windows":
        r = call("GET", "/windows")
    elif cmd == "screenshot":
        title = args[1] if len(args) > 1 else None
        q = f"?title={urllib.request.quote(title)}" if title else ""
        r = call("GET", "/screenshot" + q)
    elif cmd == "screenshot-b64":
        title = args[1] if len(args) > 1 else None
        q = f"?title={urllib.request.quote(title)}" if title else ""
        r = call("GET", "/screenshot-b64" + q)
    elif cmd == "click":
        r = call("POST", "/click", {"x": int(args[1]), "y": int(args[2])})
    elif cmd == "rightclick":
        r = call("POST", "/rightclick", {"x": int(args[1]), "y": int(args[2])})
    elif cmd == "type":
        r = call("POST", "/type", {"text": args[1]})
    elif cmd == "key":
        r = call("POST", "/key", {"combo": args[1]})
    elif cmd == "scroll":
        r = call("POST", "/scroll", {"clicks": int(args[1])})
    elif cmd == "window":
        action = args[1]
        title = " ".join(args[2:]) if len(args) > 2 else ""
        r = call("POST", "/window", {"action": action, "title": title})
    elif cmd == "focus":
        r = call("POST", "/focus", {"title": " ".join(args[1:])})
    elif cmd == "exists":
        r = call("POST", "/exists", {"title": " ".join(args[1:])})
    elif cmd == "wait":
        title = args[1] if len(args) > 1 else ""
        timeout = int(args[2]) if len(args) > 2 else 5000
        r = call("POST", "/wait", {"title": title, "timeout_ms": timeout})
    elif cmd == "mouse-pos":
        r = call("GET", "/mouse-pos")
    elif cmd == "move-mouse":
        r = call("POST", "/move-mouse", {"x": int(args[1]), "y": int(args[2])})
    elif cmd == "screen-size":
        r = call("GET", "/screen-size")
    elif cmd == "app":
        if len(args) > 1 and args[1] == "start":
            if len(args) < 3:
                r = {"ok": False, "error": "app start 需要路径参数"}
            else:
                path_ = args[2]
                extra = " ".join(args[3:]) if len(args) > 3 else ""
                r = call("POST", "/app", {"action": "start", "path": path_, "args": extra})
        else:
            r = {"ok": False, "error": "用法: app start <path> [args]"}
    elif cmd == "shutdown":
        r = call("POST", "/shutdown")
    elif cmd == "run":
        # run "cmd" [args] [--wait] [--workdir DIR] [--timeout MS] [--shell]
        if len(args) < 2:
            r = {"ok": False, "error": "用法: run <cmd> [args] [--wait] [--workdir DIR] [--timeout MS] [--shell]"}
        else:
            run_cmd = args[1]
            run_args = ""
            wait = False
            workdir = None
            timeout = 300000
            shell = None
            i = 2
            positional_done = False
            while i < len(args):
                a = args[i]
                if a == "--wait":
                    wait = True
                elif a == "--workdir" and i + 1 < len(args):
                    workdir = args[i + 1]; i += 1
                elif a == "--timeout" and i + 1 < len(args):
                    timeout = int(args[i + 1]); i += 1
                elif a == "--shell":
                    shell = "cmd"
                elif not positional_done and not a.startswith("--"):
                    run_args = a
                i += 1
            body = {"cmd": run_cmd, "args": run_args, "wait": wait, "timeout_ms": timeout}
            if workdir: body["workdir"] = workdir
            if shell: body["shell"] = shell
            r = call("POST", "/run", body)
    elif cmd == "process":
        if len(args) < 2:
            r = {"ok": False, "error": "用法: process <pid>"}
        else:
            r = call("POST", "/process", {"pid": int(args[1])})
    elif cmd == "process-list":
        r = call("GET", "/process/list")
    elif cmd == "process-kill":
        if len(args) < 2:
            r = {"ok": False, "error": "用法: process-kill <pid>"}
        else:
            r = call("POST", "/process/kill", {"pid": int(args[1])})
    else:
        print("未知命令:", cmd)
        print(__doc__)
        return

    print(json.dumps(r, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main()
