# Running MyHomePage

## Quick Start

### `run.bat` — Full Setup & Run
**Use this for initial setup or after updating dependencies**

```cmd
run.bat
```

This script:
1. ✅ Verifies .NET 8.0 SDK is installed
2. ✅ Restores NuGet packages
3. ✅ Builds the project
4. ✅ Automatically opens browser at http://localhost:5000
5. ✅ Starts the development server

**Duration:** ~30-60 seconds (first run longer due to package restore)

---

### `run-quick.bat` — Fast Run
**Use this for development when you've only changed code/UI**

```cmd
run-quick.bat
```

This script:
1. ✅ Skips restore and build (assumes already done)
2. ✅ Opens browser immediately
3. ✅ Starts the development server

**Duration:** ~2 seconds

---

## Manual Commands

If you prefer to run manually in PowerShell:

```powershell
# Full setup
dotnet restore
dotnet build
dotnet run

# Quick run (after initial build)
dotnet run
```

---

## Accessing the Website

Once the server starts, visit:
```
http://localhost:5000
```

The browser opens automatically, but you can always navigate manually to the URL above.

---

## Stopping the Server

Press `Ctrl+C` in the terminal window to stop the development server.

---

## Troubleshooting

### "dotnet is not recognized..."
- .NET 8.0 SDK is not installed
- Download from: https://dotnet.microsoft.com/download
- Restart your terminal after installation

### Port 5000 already in use
- Another application is using port 5000
- Kill the process: `netstat -ano | findstr :5000` then `taskkill /PID <PID> /F`
- Or change the port in `appsettings.json` under `Kestrel:Endpoints:Http:Url`

### Build fails
- Delete `bin/` and `obj/` folders manually
- Run `run.bat` again

---

## Project Structure

```
MyHomePage/
├── run.bat              ← Use for full setup
├── run-quick.bat        ← Use for quick development runs
├── appsettings.json     ← Configuration (port 5000)
├── Program.cs           ← Startup configuration
├── wwwroot/videos/      ← Stored videos (persisted)
└── logs/                ← Application logs
```
