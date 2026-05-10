@echo off
REM Master startup script - starts both apps, then ngrok tunnels
REM Kills existing ngrok and app processes first

setlocal enabledelayedexpansion

echo ===========================================
echo   STARTUP - MyHomePage + DuneChess + ngrok
echo ===========================================
echo.

REM Kill existing processes - aggressive cleanup
echo [CLEANUP] Killing existing processes...

REM First attempt - standard taskkill
taskkill /F /IM ngrok.exe >nul 2>&1
taskkill /F /IM dotnet.exe >nul 2>&1
taskkill /F /IM MyHomePage.exe >nul 2>&1
taskkill /F /IM node.exe >nul 2>&1

timeout /t 1 /nobreak

REM Second attempt - using wmic for more reliability
wmic process where name="dotnet.exe" delete /nointeractive >nul 2>&1
wmic process where name="MyHomePage.exe" delete /nointeractive >nul 2>&1
wmic process where name="node.exe" delete /nointeractive >nul 2>&1
wmic process where name="ngrok.exe" delete /nointeractive >nul 2>&1

timeout /t 2 /nobreak

REM Clean build artifacts
echo [CLEANUP] Cleaning build artifacts...
cd /d "C:\Users\jaqbs\source\repos\MyHomePage"
if exist bin rmdir /s /q bin >nul 2>&1
if exist obj rmdir /s /q obj >nul 2>&1
timeout /t 1 /nobreak

echo.
echo ===========================================
echo   [1/3] Starting MyHomePage (port 5132)
echo ===========================================
echo.

cd /d "C:\Users\jaqbs\source\repos\MyHomePage"
start "MyHomePage - ASP.NET Core" cmd /k "dotnet run"

timeout /t 5 /nobreak

echo.
echo ===========================================
echo   [2/3] Starting 3DimensionalChess (port 8080)
echo ===========================================
echo.

cd /d "C:\Users\jaqbs\source\repos\3DimensionalChess"
start "3DimensionalChess - Node.js" cmd /k "npm run dev"

timeout /t 5 /nobreak

echo.
echo ===========================================
echo   [3/3] Starting ngrok tunnels
echo ===========================================
echo.

echo [CLEANUP] Killing any existing ngrok processes...
taskkill /F /IM ngrok.exe >nul 2>&1
wmic process where name="ngrok.exe" delete /nointeractive >nul 2>&1

echo [CLEANUP] Waiting for tunnels to disconnect (5 seconds)...
timeout /t 5 /nobreak

REM Set ngrok path (absolute) - check if exists
set "NGROK_EXE=C:\Users\jaqbs\source\repos\MyHomePage\ngrok\ngrok.exe"

if not exist "%NGROK_EXE%" (
    echo.
    echo [ERROR] ngrok.exe not found at: %NGROK_EXE%
    echo Please run: .\setup-ngrok.ps1 first to download ngrok
    echo.
    pause
    exit /b 1
)

echo [1/2] Starting MyHomePage tunnel (port 5132 -> cruxbeta.com.ngrok.dev)...
start "ngrok - MyHomePage" cmd /k "%NGROK_EXE% http --url=cruxbeta.com.ngrok.dev 5132"

timeout /t 2 /nobreak

echo [2/2] Starting 3DimensionalChess tunnel (port 8080 -> dunechess.com.ngrok.dev)...
start "ngrok - DuneChess" cmd /k "%NGROK_EXE% http --url=dunechess.com.ngrok.dev 8080"

echo.
echo ===========================================
echo   ALL SERVICES STARTED
echo ===========================================
echo.
echo MyHomePage:     http://localhost:5132
echo                 https://cruxbeta.com.ngrok.dev
echo.
echo 3DimensionalChess: http://localhost:8080
echo                    https://dunechess.com.ngrok.dev
echo.
echo All services running. Close individual windows to stop.
pause

endlocal
