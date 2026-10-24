@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title MadNegar - Build Windows Installer

where powershell.exe >nul 2>nul
if errorlevel 1 goto missing_powershell

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Installer.ps1"
set "BUILD_EXIT_CODE=%ERRORLEVEL%"
if not "%BUILD_EXIT_CODE%"=="0" (
  echo.
  echo MadNegar installer build failed. Review the diagnostic above.
  pause
)
exit /b %BUILD_EXIT_CODE%

:missing_powershell
echo ERROR: Windows PowerShell was not found. This script requires powershell.exe.
pause
exit /b 2
