# Hermes Tray

A lightweight Windows system tray agent for remote GUI automation and process execution. Runs in **Session 1** (user desktop) and exposes an HTTP API on `127.0.0.1:51234` for control by agents running in Session 0 or other contexts.

## Features

- **GUI Automation** — screenshot, click, type, key combos, scroll, window management
- **Remote Process Execution** — run commands with stdout/stderr capture, background or blocking mode
- **Token Auth** — X-Agent-Token header authentication (auto-generated)
- **Zero Dependencies** — pure .NET 8, no external packages

## Architecture

```
Session 0 (Agent/Service)          Session 1 (User Desktop)
┌─────────────────┐               ┌─────────────────────┐
│  Python CLI     │──HTTP/JSON──→│  HermesTray.exe      │
│  hermes_tray.py │               │  ├─ /screenshot      │
│  gui_assistant  │               │  ├─ /click /type     │
└─────────────────┘               │  ├─ /run (exec)      │
                                  │  ├─ /process (status)│
                                  │  └─ /window /app     │
                                  └─────────────────────┘
```

## Quick Start

### Build
```bash
dotnet publish HermesTray.csproj -c Release -o publish/
```

### Run
```bash
# Start tray (double-click or run from terminal)
scripts/start_gui_agent.bat

# Or run the GUI Assistant (task queue worker)
scripts/start_gui_assistant.bat
```

### CLI Usage
```bash
python scripts/hermes_tray.py health
python scripts/hermes_tray.py screenshot
python scripts/hermes_tray.py click 100 200
python scripts/hermes_tray.py type "hello"
python scripts/hermes_tray.py run "python" "script.py" --wait --workdir "D:/Projects"
python scripts/hermes_tray.py process <pid>
```

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/screenshot` | POST | Capture screenshot (full or by window title) |
| `/click` | POST | Click at coordinates |
| `/type` | POST | Type text (supports CJK via clipboard) |
| `/key` | POST | Key combo (ctrl+s, alt+tab, etc.) |
| `/scroll` | POST | Mouse scroll |
| `/window` | POST | Window management (activate/close/minimize) |
| `/app` | POST | Start application |
| `/run` | POST | **Execute command** with stdout/stderr capture |
| `/process` | POST | Query process status by PID |
| `/process/list` | GET | List all tracked processes |
| `/process/kill` | POST | Terminate process |
| `/shutdown` | POST | Shutdown tray |

### `/run` Example
```json
POST /run
{
  "cmd": "C:/SIMULIA/Commands/abaqus.bat",
  "args": "job=test input=model.inp",
  "workdir": "D:/temp/abaqus",
  "wait": true,
  "timeout_ms": 300000
}
```

## GUI Assistant

`gui_assistant.py` is a task queue worker that polls for JSON task files and executes them via HermesTray's API.

```python
from gui_assistant_dispatch import dispatch_task, run_remote

# Run a command via Session 1
output = run_remote("python", "analysis.py", workdir="D:/Projects")

# Multi-step workflow
result = dispatch_task([
    {"cmd": "python", "args": "run.py", "wait": True, "timeout_ms": 120000},
    {"screenshot": True, "title": "Results"},
])
```

## Security

- Binds to `127.0.0.1` only (no external access)
- X-Agent-Token authentication (auto-generated in `token.txt`)
- No arbitrary shell execution; `/run` starts processes directly
- All operations logged to `hermes_tray.log`

## Requirements

- Windows 10/11
- .NET 8.0 SDK (for building)
- Python 3.10+ (for CLI/scripts)

## License

MIT
