# Deployment script for ngrok server
# Run this on your ngrok server to deploy the latest Facebook/Open Graph fixes

Write-Host "=== MyHomePage Deployment Script ===" -ForegroundColor Cyan
Write-Host "This will pull the latest code and restart the application" -ForegroundColor Yellow
Write-Host ""

# Navigate to project directory
$projectPath = "C:\path\to\MyHomePage"  # CHANGE THIS TO YOUR ACTUAL PATH
if (!(Test-Path $projectPath)) {
    Write-Host "ERROR: Project path not found: $projectPath" -ForegroundColor Red
    Write-Host "Please edit this script and set the correct projectPath" -ForegroundColor Yellow
    exit 1
}

cd $projectPath
Write-Host "✓ Working directory: $(Get-Location)" -ForegroundColor Green
Write-Host ""

# Step 1: Check current commits
Write-Host "1. Checking latest commits..." -ForegroundColor Cyan
git log --oneline | Select-Object -First 2
Write-Host ""

# Step 2: Pull latest code
Write-Host "2. Pulling latest code from origin/master..." -ForegroundColor Cyan
git pull origin master
Write-Host ""

# Step 3: Verify [AllowAnonymous] was added
Write-Host "3. Verifying [AllowAnonymous] attribute..." -ForegroundColor Cyan
$hasAllowAnonymous = Select-String -Path "Pages/_Host.cshtml.cs" -Pattern "AllowAnonymous"
if ($hasAllowAnonymous) {
    Write-Host "✓ [AllowAnonymous] attribute found" -ForegroundColor Green
} else {
    Write-Host "✗ WARNING: [AllowAnonymous] not found!" -ForegroundColor Red
}
Write-Host ""

# Step 4: Verify robots.txt
Write-Host "4. Verifying robots.txt..." -ForegroundColor Cyan
if (Test-Path "wwwroot/robots.txt") {
    Write-Host "✓ robots.txt exists" -ForegroundColor Green
} else {
    Write-Host "✗ WARNING: robots.txt not found!" -ForegroundColor Red
}
Write-Host ""

# Step 5: Kill running dotnet processes
Write-Host "5. Stopping any running instances..." -ForegroundColor Cyan
$runningProcess = Get-Process | Where-Object {$_.ProcessName -eq "dotnet" -or $_.ProcessName -eq "MyHomePage"}
if ($runningProcess) {
    Write-Host "Found running process(es): $($runningProcess.Id)" -ForegroundColor Yellow
    try {
        $runningProcess | Stop-Process -Force -ErrorAction Stop
        Write-Host "✓ Process stopped" -ForegroundColor Green
        Start-Sleep -Seconds 3
    } catch {
        Write-Host "✗ Could not stop process - you may need to stop it manually" -ForegroundColor Red
    }
} else {
    Write-Host "✓ No running instances found" -ForegroundColor Green
}
Write-Host ""

# Step 6: Clean and build
Write-Host "6. Cleaning and building project..." -ForegroundColor Cyan
dotnet clean
dotnet build
if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Build successful" -ForegroundColor Green
} else {
    Write-Host "✗ Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Step 7: Restart application
Write-Host "7. Starting application..." -ForegroundColor Cyan
Write-Host "The application will start running. You can Ctrl+C to stop it." -ForegroundColor Yellow
Write-Host ""
dotnet run
