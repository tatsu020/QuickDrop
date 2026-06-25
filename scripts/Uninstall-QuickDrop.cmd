@echo off
setlocal
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-QuickDrop.ps1" -RemoveFirewallRules -RestartExplorer %*
set "exitCode=%ERRORLEVEL%"

if not "%exitCode%"=="0" (
  echo.
  echo QuickDrop uninstall failed. Exit code: %exitCode%
  echo.
  pause
  exit /b %exitCode%
)

echo.
echo QuickDrop uninstalled.
timeout /t 2 /nobreak >nul
exit /b 0
