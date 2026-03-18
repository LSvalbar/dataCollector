@echo off
setlocal
set "PROJECT_ROOT=%~dp0"
cd /d "%PROJECT_ROOT%"
powershell -ExecutionPolicy Bypass -File "%PROJECT_ROOT%scripts\Start-FanucCollectorGui.ps1" %*
endlocal
