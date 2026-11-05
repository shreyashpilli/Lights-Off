@echo off
if exist "%~dp0QuietRun.exe" (
    start "" "%~dp0QuietRun.exe"
) else (
    start "" powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0QuietRun.ps1"
)
