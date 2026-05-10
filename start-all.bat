@echo off
REM Master startup script - starts both apps, then ngrok tunnels
REM Kills existing processes and waits for ngrok cloud endpoints to disconnect

setlocal enabledelayedexpansion

echo ===========================================
echo   STARTUP - MyHomePage + DuneChess + ngrok
echo ===========================================
echo.

REM ===== STAGE 1: KILL EXISTING PROCESSES =====
echo [CLEANUP 1/3] Killing target processes only (safe mode)...

REM Kill cmd.exe windows by title (parents that hosted ngrok/dotnet/node)
taskkill /F /FI "WINDOWTITLE eq ngrok - MyHomePage*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq ngrok - DuneChess*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq MyHomePage - ASP.NET Core*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq 3DimensionalChess - Node.js*" >nul 2>&1

REM Kill ngrok and MyHomePage.exe (safe - these are unique to our project)
taskkill /F /IM ngrok.exe >nul 2>&1
taskkill /F /IM MyHomePage.exe >nul 2>&1

REM Kill ONLY dotnet processes running MyHomePage (NOT all dotnet processes - that would kill PowerShell/VS!)
powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | Where-Object { $_.CommandLine -match 'MyHomePage' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"

REM Kill ONLY node processes running 3DimensionalChess or http-server on port 8080
powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='node.exe'\" | Where-Object { $_.CommandLine -match '3DimensionalChess|http-server|esbuild' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"

timeout /t 2 /nobreak >nul

REM ===== STAGE 2: CLEAN BUILD ARTIFACTS =====
echo [CLEANUP 2/3] Cleaning build artifacts...
cd /d "C:\Users\jaqbs\source\repos\MyHomePage"
if exist bin rmdir /s /q bin >nul 2>&1
if exist obj rmdir /s /q obj >nul 2>&1

REM ===== STAGE 3: VERIFY NGROK =====
echo [CLEANUP 3/3] Verifying ngrok installation...
set "NGROK_EXE=C:\Users\jaqbs\source\repos\MyHomePage\ngrok\ngrok.exe"

if not exist "%NGROK_EXE%" (
    echo.
    echo [ERROR] ngrok.exe not found at: %NGROK_EXE%
    echo Please run: .\setup-ngrok.ps1 first to download ngrok
    echo.
    pause
    exit /b 1
)
echo [OK] ngrok found
echo.

REM ===== START APPS =====
echo ===========================================
echo   [1/3] Starting MyHomePage (port 5132)
echo ===========================================
cd /d "C:\Users\jaqbs\source\repos\MyHomePage"
start "MyHomePage - ASP.NET Core" cmd /k "dotnet run"
echo Waiting 8 seconds for ASP.NET Core to start...
timeout /t 8 /nobreak >nul

echo.
echo ===========================================
echo   [2/3] Starting 3DimensionalChess (port 8080)
echo ===========================================
cd /d "C:\Users\jaqbs\source\repos\3DimensionalChess"
start "3DimensionalChess - Node.js" cmd /k "npm run dev"
echo Waiting 5 seconds for Node.js to start...
timeout /t 5 /nobreak >nul

echo.
echo ===========================================
echo   [3/3] Starting ngrok tunnels
echo ===========================================
echo.
echo [INFO] ngrok cloud endpoints need ~30s to disconnect from previous session.
echo [INFO] Waiting 30 seconds before starting tunnels...
echo.

REM Countdown so user knows it's working
for /L %%i in (30,-5,5) do (
    echo    %%i seconds remaining...
    timeout /t 5 /nobreak >nul
)

echo.
echo [1/2] Starting MyHomePage tunnel (port 5132 -^> cruxbeta.com.ngrok.dev)...
start "ngrok - MyHomePage" cmd /k "%NGROK_EXE% http --url=cruxbeta.com.ngrok.dev 5132"

timeout /t 3 /nobreak >nul

echo [2/2] Starting 3DimensionalChess tunnel (port 8080 -^> dunechess.com.ngrok.dev)...
start "ngrok - DuneChess" cmd /k "%NGROK_EXE% http --url=dunechess.com.ngrok.dev 8080"

echo.
echo ===========================================
echo   ALL SERVICES STARTED
echo ===========================================
echo.
echo MyHomePage:     http://localhost:5132
echo                 https://cruxbeta.com.ngrok.dev
echo.
echo DuneChess:      http://localhost:8080
echo                 https://dunechess.com.ngrok.dev
echo.
echo ngrok dashboard: http://localhost:4040
echo.
echo [TIP] If a tunnel shows "endpoint already online" error:
echo       1. Wait 30 more seconds
echo       2. Close that window
echo       3. Re-run: %NGROK_EXE% http --url=^<domain^> ^<port^>
echo.
echo All services running. Close individual windows to stop.
pause

endlocal
