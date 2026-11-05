@echo off
if exist "%~dp0Sleepwalk.exe" (
    start "" "%~dp0Sleepwalk.exe"
) else (
    start "" powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Sleepwalk.ps1"
)
