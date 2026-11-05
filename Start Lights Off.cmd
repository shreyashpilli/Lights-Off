@echo off
if exist "%~dp0LightsOff.exe" (
    start "" "%~dp0LightsOff.exe"
) else if exist "%~dp0dist\LightsOff.exe" (
    start "" "%~dp0dist\LightsOff.exe"
) else (
    start "" powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0LightsOff.ps1"
)
