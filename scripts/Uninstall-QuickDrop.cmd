@echo off
setlocal
title QuickDrop Uninstaller
cd /d "%~dp0"

echo ============================================================
echo QuickDrop Uninstaller
echo ============================================================
echo.
echo Install folder:
echo   %~dp0
echo.
echo Windows may show a UAC prompt for firewall cleanup.
echo.
echo Running uninstaller...
echo.

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
echo QuickDrop uninstall completed.
echo.
pause
exit /b 0
