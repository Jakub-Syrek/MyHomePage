# Setup ngrok locally in the project
# Downloads ngrok.exe to ./ngrok/ folder

$ngrokDir = ".\ngrok"
$ngrokExe = "$ngrokDir\ngrok.exe"

Write-Host "================================================"
Write-Host "Setting up ngrok locally in project folder..."
Write-Host "================================================"
Write-Host ""

# Create ngrok folder
if (!(Test-Path $ngrokDir)) {
    Write-Host "[1/3] Creating ngrok folder..."
    New-Item -ItemType Directory -Path $ngrokDir -Force | Out-Null
    Write-Host "[OK] Folder created: $ngrokDir"
} else {
    Write-Host "[1/3] Folder exists: $ngrokDir"
}

Write-Host ""

# Check if ngrok.exe already exists
if (Test-Path $ngrokExe) {
    Write-Host "[2/3] ngrok.exe already exists"
    & $ngrokExe --version
} else {
    Write-Host "[2/3] Downloading ngrok..."
    Write-Host ""

    # Download ngrok (Windows 64-bit)
    $downloadUrl = "https://bin.equinox.io/c/bNyj1mQVY4c/ngrok-v3-stable-windows-amd64.zip"
    $zipPath = "$ngrokDir\ngrok.zip"

    try {
        # Download with progress
        Write-Host "Downloading from: $downloadUrl"
        Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath -UseBasicParsing
        Write-Host "[OK] Downloaded: $zipPath"

        Write-Host ""
        Write-Host "[3/3] Extracting ngrok.exe..."

        # Extract
        Expand-Archive -Path $zipPath -DestinationPath $ngrokDir -Force

        # Cleanup zip
        Remove-Item -Path $zipPath -Force

        # Verify
        if (Test-Path $ngrokExe) {
            Write-Host "[OK] ngrok.exe extracted successfully"
            Write-Host ""
            & $ngrokExe --version
        } else {
            Write-Host "[ERROR] ngrok.exe not found after extraction!"
            exit 1
        }
    } catch {
        Write-Host "[ERROR] Download failed: $_"
        exit 1
    }
}

Write-Host ""
Write-Host "================================================"
Write-Host "Setup complete! ngrok is ready to use."
Write-Host "Run: .\start-all.bat"
Write-Host "================================================"
