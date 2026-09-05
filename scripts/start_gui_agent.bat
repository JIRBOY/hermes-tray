@echo off
rem Start Hermes Tray (GUI helper for Hegel, must run in USER session not Session 0)
cd /d "D:\Manager\AppData\Hermes\scripts\hermes-tray"
if not exist HermesTray.exe (
  echo [ERROR] HermesTray.exe not found.
  pause
  exit /b 1
)
start "" HermesTray.exe
exit /b 0
