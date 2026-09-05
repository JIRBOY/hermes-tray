@echo off
:: 启动 GUI Assistant（Session 1 常驻助手）
:: 与 HermesTray 配合，接收并执行 Hermes 派发的任务
cd /d "%~dp0"
title Hermes GUI Assistant
"D:\Program\Hermes\hermes-agent\venv\Scripts\python.exe" gui_assistant.py
pause
