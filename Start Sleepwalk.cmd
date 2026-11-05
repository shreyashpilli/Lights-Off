@echo off
if exist "%~dp0Sleepwalk.exe" (
    start "" "%~dp0Sleepwalk.exe"
) else if exist "%~dp0dist\Sleepwalk.exe" (
    start "" "%~dp0dist\Sleepwalk.exe"
) else (
    start "" powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Sleepwalk.ps1"
)
