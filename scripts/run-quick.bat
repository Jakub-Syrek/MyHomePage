@echo off
REM Quick run - skip restore and build, just run the application
REM Use this if you've already built and only made code/UI changes

echo.
echo Starting MyHomePage...
echo Website: http://localhost:5000
echo Press Ctrl+C to stop
echo.

timeout /t 1 /nobreak
start http://localhost:5000 >nul 2>&1

dotnet run
