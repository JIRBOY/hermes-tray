"""
Hermes GUI Assistant — Session 1 常驻助手
功能：从 Hermes 接收任务队列，自主执行多步骤工作流，汇报结果。
通过 HermesTray HTTP API 执行命令（/run /screenshot /process 等）。

架构：
  Session 0 (Hermes) → 写任务到任务文件
  Session 1 (本助手) → 轮询任务文件 → 执行 → 写结果文件
  Session 0 (Hermes) → 读结果文件 → 验证 → 汇报

启动：python gui_assistant.py
停止：Ctrl+C 或删除 gui_assistant.stop
"""
import json
import os
import sys
import time
import traceback
import urllib.request
from datetime import datetime
from pathlib import Path

# === 配置 ===
TRAY_BASE = "http://127.0.0.1:51234"
TOKEN_FILE = Path(r"D:\Manager\AppData\Hermes\gui_helper\token.txt")
TASK_DIR = Path(r"D:\Manager\AppData\Hermes\gui_helper\tasks")
RESULT_DIR = Path(r"D:\Manager\AppData\Hermes\gui_helper\results")
LOG_FILE = Path(r"D:\Manager\AppData\Hermes\gui_helper\gui_assistant.log")
POLL_INTERVAL = 3  # 秒
MAX_RUNTIME = 600  # 单任务最长10分钟


def log(msg):
    line = f"[{datetime.now().strftime('%H:%M:%S')}] {msg}"
    print(line, flush=True)
    with open(LOG_FILE, "a", encoding="utf-8") as f:
        f.write(line + "\n")


def token():
    try:
        return TOKEN_FILE.read_text(encoding="utf-8").strip()
    except:
        return ""


def tray_call(method, path, body=None):
    """调用 HermesTray HTTP API"""
    url = TRAY_BASE + path
    headers = {"X-Agent-Token": token(), "Content-Type": "application/json"}
    data = json.dumps(body).encode() if body else None
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return json.loads(resp.read().decode())
    except Exception as e:
        return {"ok": False, "error": str(e)}


def run_cmd(cmd, args="", workdir=None, wait=True, timeout_ms=300000, shell=None):
    """通过 /run 执行命令"""
    body = {"cmd": cmd, "args": args, "wait": wait, "timeout_ms": timeout_ms}
    if workdir:
        body["workdir"] = workdir
    if shell:
        body["shell"] = shell
    return tray_call("POST", "/run", body)


def screenshot(title=None):
    """截图"""
    body = {}
    if title:
        body["title"] = title
    return tray_call("POST", "/screenshot", body)


def check_health():
    """检查 HermesTray 是否存活"""
    r = tray_call("GET", "/health")
    return r.get("ok", False)


# === 任务执行器 ===
def execute_task(task):
    """
    执行一个任务。任务格式：
    {
        "id": "task-xxx",
        "type": "run" | "workflow" | "screenshot",
        "steps": [
            {"cmd": "...", "args": "...", "workdir": "...", "wait": true, "timeout_ms": 300000},
            {"screenshot": true, "title": "窗口标题"},
            {"cmd": "...", "args": "..."}
        ]
    }
    """
    task_id = task.get("id", "unknown")
    task_type = task.get("type", "run")
    steps = task.get("steps", [])
    results = []
    start = time.time()

    log(f"[TASK {task_id}] 开始执行，{len(steps)} 步")

    for i, step in enumerate(steps):
        elapsed = time.time() - start
        if elapsed > MAX_RUNTIME:
            log(f"[TASK {task_id}] 超时 ({elapsed:.0f}s > {MAX_RUNTIME}s)，中止")
            results.append({"step": i, "error": "timeout", "elapsed": elapsed})
            break

        log(f"[TASK {task_id}] 步骤 {i+1}/{len(steps)}: {step.get('cmd', 'screenshot')}")

        if step.get("screenshot"):
            # 截图步骤
            title = step.get("title")
            r = screenshot(title)
            results.append({"step": i, "type": "screenshot", "result": r})
        else:
            # 命令执行步骤
            r = run_cmd(
                cmd=step.get("cmd", ""),
                args=step.get("args", ""),
                workdir=step.get("workdir"),
                wait=step.get("wait", True),
                timeout_ms=step.get("timeout_ms", 300000),
                shell=step.get("shell"),
            )
            results.append({"step": i, "type": "run", "result": r})

            # 如果步骤失败且标记为关键，中止
            if not r.get("ok") and step.get("critical", False):
                log(f"[TASK {task_id}] 关键步骤 {i+1} 失败，中止")
                break

    total_time = time.time() - start
    log(f"[TASK {task_id}] 完成，耗时 {total_time:.1f}s")

    return {
        "task_id": task_id,
        "status": "completed",
        "steps": results,
        "total_time": total_time,
        "timestamp": datetime.now().isoformat(),
    }


# === 主循环 ===
def main():
    TASK_DIR.mkdir(parents=True, exist_ok=True)
    RESULT_DIR.mkdir(parents=True, exist_ok=True)

    log("=== GUI Assistant 启动 ===")
    log(f"任务目录: {TASK_DIR}")
    log(f"结果目录: {RESULT_DIR}")
    log(f"轮询间隔: {POLL_INTERVAL}s")

    if not check_health():
        log("警告: HermesTray 不可达，等待...")
        for _ in range(30):
            time.sleep(2)
            if check_health():
                log("HermesTray 已连接")
                break
        else:
            log("HermesTray 持续不可达，退出")
            return

    log("就绪，等待任务...")

    while True:
        # 检查停止信号
        if (TASK_DIR / "gui_assistant.stop").exists():
            log("收到停止信号，退出")
            (TASK_DIR / "gui_assistant.stop").unlink(missing_ok=True)
            break

        # 扫描任务文件
        task_files = sorted(TASK_DIR.glob("*.json"))
        for tf in task_files:
            try:
                task = json.loads(tf.read_text(encoding="utf-8"))
                result = execute_task(task)

                # 写结果
                result_file = RESULT_DIR / tf.name
                result_file.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")

                # 删除已处理的任务文件
                tf.unlink(missing_ok=True)

            except Exception as e:
                log(f"任务处理异常: {e}\n{traceback.format_exc()}")
                # 写错误结果
                error_result = {
                    "task_id": tf.stem,
                    "status": "error",
                    "error": str(e),
                    "timestamp": datetime.now().isoformat(),
                }
                (RESULT_DIR / tf.name).write_text(
                    json.dumps(error_result, ensure_ascii=False, indent=2), encoding="utf-8"
                )
                tf.unlink(missing_ok=True)

        time.sleep(POLL_INTERVAL)

    log("=== GUI Assistant 已停止 ===")


if __name__ == "__main__":
    main()
