@echo off
setlocal
title MadNegar - Test Launcher
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 goto missing_dotnet

dotnet --list-sdks | findstr /B /C:"10." >nul
if errorlevel 1 goto missing_sdk

echo Starting MadNegar in test mode...
echo The first run may restore NuGet packages and compile the application.
echo.
dotnet run --project "%~dp0MadNegar.csproj" --configuration Debug
set "APP_EXIT_CODE=%ERRORLEVEL%"
if not "%APP_EXIT_CODE%"=="0" (
  echo.
  echo MadNegar exited with an error. Review the message above.
  pause
)
exit /b %APP_EXIT_CODE%

:missing_dotnet
echo .NET SDK was not found.
echo Install the .NET 10 SDK, then double-click Run-MadNegar.cmd again.
echo https://dotnet.microsoft.com/download/dotnet/10.0
pause
exit /b 1

:missing_sdk
echo The .NET 10 SDK is required to build and run this test project.
echo Install it from https://dotnet.microsoft.com/download/dotnet/10.0
pause
exit /b 1
