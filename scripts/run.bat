@echo off
REM Run MyHomePage - ASP.NET Core 8.0 Blazor Server application
REM This script restores dependencies, builds, and runs the website locally

setlocal enabledelayedexpansion

REM Colors for output (Windows 10+)
for /F %%A in ('echo prompt $H ^| cmd') do set "BS=%%A"

echo.
echo ==========================================
echo   MyHomePage - Local Development Server
echo ==========================================
echo.

REM Check if .NET SDK is installed
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo %BS%[ERROR] .NET SDK is not installed or not in PATH
    echo Please download .NET 8.0 SDK from: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

REM Display .NET version
echo [INFO] Using:
dotnet --version

echo.
echo [INFO] Restoring NuGet packages...
dotnet restore
if errorlevel 1 (
    echo %BS%[ERROR] Package restore failed
    pause
    exit /b 1
)

echo.
echo [INFO] Building project...
dotnet build
if errorlevel 1 (
    echo %BS%[ERROR] Build failed
    pause
    exit /b 1
)

echo.
echo ==========================================
echo   Starting MyHomePage...
echo ==========================================
echo.
echo [INFO] The website will open in your browser at:
echo        http://localhost:5000
echo.
echo Press Ctrl+C to stop the server
echo.

REM Open browser after a short delay (server needs to start)
timeout /t 3 /nobreak

REM Try to open browser (Windows 10+)
start http://localhost:5000 >nul 2>&1

REM Run the application
dotnet run

endlocal
