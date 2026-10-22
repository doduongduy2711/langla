@echo off
cd /d "%~dp0"
if exist "%~dp0QLTK.exe" (
  start "" "%~dp0QLTK.exe"
  exit /b
)
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0QLTK\AccountManager.ps1"
if errorlevel 1 pause
