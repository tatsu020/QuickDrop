@echo off
setlocal
title QuickDrop Installer
cd /d "%~dp0"

echo ============================================================
echo QuickDrop Installer
echo ============================================================
echo.
echo Install folder:
echo   %~dp0
echo.
echo Windows may show a UAC prompt for firewall setup.
echo Please approve it if you want QuickDrop to receive files.
echo.
echo Running installer...
echo.

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
echo QuickDrop install completed.
echo.
echo The QuickDrop tray icon should now be running.
echo If the Explorer menu is not visible yet, sign out and back in or restart Explorer once.
echo.
pause
exit /b 0
