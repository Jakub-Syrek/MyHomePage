@echo off
REM Start ngrok tunnels for MyHomePage and DuneChess
REM Kills any existing ngrok processes first

echo ===========================================
echo Killing existing ngrok processes...
echo ===========================================

taskkill /F /IM ngrok.exe >nul 2>&1
if errorlevel 0 (
    echo [OK] Existing ngrok processes terminated
) else (
    echo [INFO] No existing ngrok processes found
)

timeout /t 1 /nobreak

echo.
echo ===========================================
echo Starting all ngrok tunnels
echo ===========================================
echo.

echo [1/2] Starting MyHomePage tunnel (port 5132 -> cruxbeta.com.ngrok.dev)...
start "ngrok - MyHomePage" cmd /k "ngrok http --url=cruxbeta.com.ngrok.dev 5132"

timeout /t 2 /nobreak

echo [2/2] Starting 3DimensionalChess tunnel (port 8080 -> dunechess.com.ngrok.dev)...
start "ngrok - DuneChess" cmd /k "ngrok http --url=dunechess.com.ngrok.dev 8080"

echo.
echo ===========================================
echo Both tunnels running in separate windows
echo ===========================================
echo.
echo MyHomePage:     https://cruxbeta.com.ngrok.dev
echo DuneChess:      https://dunechess.com.ngrok.dev
echo.
echo Close the windows to stop a tunnel.
pause
