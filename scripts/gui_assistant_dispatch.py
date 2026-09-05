"""
Hermes → GUI Assistant 任务调度器
在 Session 0 调用，写任务文件到 tasks/，轮询 results/ 等待完成。

用法：
  from gui_assistant_dispatch import dispatch_task, wait_result

  # 派发单条命令
  result = dispatch_task([
      {"cmd": "C:/SIMULIA/Commands/abaqus.bat", "args": "job=test input=model.inp", "workdir": "D:/temp", "wait": True}
  ])

  # 派发多步工作流（运行+截图）
  result = dispatch_task([
      {"cmd": "python", "args": "run_analysis.py", "workdir": "D:/Projects", "wait": True, "timeout_ms": 120000},
      {"screenshot": True, "title": "ABAQUS"},
  ])
"""
import json
import time
import uuid
from pathlib import Path

TASK_DIR = Path(r"D:\Manager\AppData\Hermes\gui_helper\tasks")
RESULT_DIR = Path(r"D:\Manager\AppData\Hermes\gui_helper\results")


def dispatch_task(steps, task_id=None, timeout=300):
    """
    派发任务并等待结果。
    steps: [{"cmd": "...", "args": "...", "workdir": "...", "wait": true}, ...]
           或 [{"screenshot": true, "title": "..."}]
    返回: {"task_id": "...", "status": "completed", "steps": [...], ...}
    """
    TASK_DIR.mkdir(parents=True, exist_ok=True)
    RESULT_DIR.mkdir(parents=True, exist_ok=True)

    task_id = task_id or f"task-{uuid.uuid4().hex[:8]}"
    task = {"id": task_id, "type": "workflow", "steps": steps}

    task_file = TASK_DIR / f"{task_id}.json"
    result_file = RESULT_DIR / f"{task_id}.json"

    # 清理旧结果
    result_file.unlink(missing_ok=True)

    # 写任务
    task_file.write_text(json.dumps(task, ensure_ascii=False, indent=2), encoding="utf-8")

    # 等待结果
    deadline = time.time() + timeout
    while time.time() < deadline:
        if result_file.exists():
            try:
                result = json.loads(result_file.read_text(encoding="utf-8"))
                result_file.unlink(missing_ok=True)
                return result
            except json.JSONDecodeError:
                time.sleep(0.5)
                continue
        time.sleep(1)

    return {"task_id": task_id, "status": "timeout", "error": f"等待超时 ({timeout}s)"}


def run_remote(cmd, args="", workdir=None, timeout=300, shell=None):
    """
    快捷方法：远程执行单条命令并等待结果。
    返回 stdout 字符串，失败抛异常。
    """
    step = {"cmd": cmd, "args": args, "wait": True, "timeout_ms": timeout * 1000}
    if workdir:
        step["workdir"] = workdir
    if shell:
        step["shell"] = shell

    result = dispatch_task([step], timeout=timeout + 30)
    if result.get("status") != "completed":
        raise RuntimeError(f"任务失败: {result}")

    step_result = result.get("steps", [{}])[0].get("result", {})
    if not step_result.get("ok"):
        raise RuntimeError(f"命令失败: exit={step_result.get('exitCode')} stderr={step_result.get('stderr', '')[:500]}")

    return step_result.get("stdout", "")


def check_assistant():
    """检查 GUI Assistant 是否在运行（通过检查任务目录和最近的日志）"""
    log_file = Path(r"D:\Manager\AppData\Hermes\gui_helper\gui_assistant.log")
    if not log_file.exists():
        return False, "日志文件不存在，助手未启动"

    # 检查日志最后修改时间
    mtime = log_file.stat().st_mtime
    age = time.time() - mtime
    if age > 60:
        return False, f"日志超过 {age:.0f}s 未更新，助手可能已停止"

    return True, "助手运行中"
