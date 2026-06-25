@echo off
setlocal
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-QuickDrop.ps1" -AddFirewallRules -RestartExplorer %*
set "exitCode=%ERRORLEVEL%"

if not "%exitCode%"=="0" (
  echo.
  echo QuickDrop install failed. Exit code: %exitCode%
  echo.
  pause
  exit /b %exitCode%
)

echo.
echo QuickDrop installed.
timeout /t 2 /nobreak >nul
exit /b 0
