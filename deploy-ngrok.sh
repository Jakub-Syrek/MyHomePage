#!/bin/bash
# Deployment script for ngrok server (Linux/macOS)
# Run this on your ngrok server to deploy the latest Facebook/Open Graph fixes

echo "=== MyHomePage Deployment Script ==="
echo "This will pull the latest code and restart the application"
echo ""

# CHANGE THIS TO YOUR ACTUAL PATH
PROJECT_PATH="/home/user/MyHomePage"

if [ ! -d "$PROJECT_PATH" ]; then
    echo "ERROR: Project path not found: $PROJECT_PATH"
    echo "Please edit this script and set the correct PROJECT_PATH"
    exit 1
fi

cd "$PROJECT_PATH"
echo "✓ Working directory: $(pwd)"
echo ""

# Step 1: Check current commits
echo "1. Checking latest commits..."
git log --oneline | head -2
echo ""

# Step 2: Pull latest code
echo "2. Pulling latest code from origin/master..."
git pull origin master
echo ""

# Step 3: Verify [AllowAnonymous] was added
echo "3. Verifying [AllowAnonymous] attribute..."
if grep -q "AllowAnonymous" Pages/_Host.cshtml.cs; then
    echo "✓ [AllowAnonymous] attribute found"
else
    echo "✗ WARNING: [AllowAnonymous] not found!"
fi
echo ""

# Step 4: Verify robots.txt
echo "4. Verifying robots.txt..."
if [ -f "wwwroot/robots.txt" ]; then
    echo "✓ robots.txt exists"
else
    echo "✗ WARNING: robots.txt not found!"
fi
echo ""

# Step 5: Kill running dotnet processes
echo "5. Stopping any running instances..."
pkill -f "dotnet run" || pkill -f "dotnet" || echo "✓ No running instances found"
sleep 3
echo ""

# Step 6: Clean and build
echo "6. Cleaning and building project..."
dotnet clean
dotnet build
if [ $? -eq 0 ]; then
    echo "✓ Build successful"
else
    echo "✗ Build failed!"
    exit 1
fi
echo ""

# Step 7: Restart application
echo "7. Starting application..."
echo "The application will start running. Press Ctrl+C to stop it."
echo ""
dotnet run
