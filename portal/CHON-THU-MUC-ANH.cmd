@echo off
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Start-Portal.ps1" -ChooseFolder
if errorlevel 1 pause
