# ================================================================================
# RJAutoMover Manual Deployment Instructions - Step 4
# ================================================================================
# This step is MANUAL - you must deploy the test config and run the service yourself
# This script provides instructions and validation only
# Saves backup to the versioned test folder created in Step 1
#
# Usage:
#   .\4-Deploy-And-Wait.ps1 -TestRoot "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"
#
# Output (in TestRoot):
#   - config-backup.yaml (backup of production config)
# ================================================================================

param(
    [Parameter(Mandatory=$true)]
    [string]$TestRoot
)

# Color output functions
function Write-Success { param([string]$Message) Write-Host "[OK] $Message" -ForegroundColor Green }
function Write-Info { param([string]$Message) Write-Host " -> $Message" -ForegroundColor Cyan }
function Write-Warning { param([string]$Message) Write-Host "[!!] $Message" -ForegroundColor Yellow }
function Write-Error { param([string]$Message) Write-Host "[ERROR] $Message" -ForegroundColor Red }
function Write-Section { param([string]$Title)
    Write-Host ""
    Write-Host "============================================================================" -ForegroundColor Yellow
    Write-Host " $Title" -ForegroundColor Yellow
    Write-Host "============================================================================" -ForegroundColor Yellow
}

Write-Section "Step 4: Manual Deployment (Human Interaction Required)"

# Validate TestRoot
if (-not (Test-Path $TestRoot)) {
    Write-Warning "Test root folder not found: $TestRoot"
    Write-Info "Run 1-Generate-TestFiles.ps1 first to create the test folder"
    exit 1
}

$TestConfig = Join-Path $TestRoot "test-config.yaml"
$BackupConfigPath = Join-Path $TestRoot "config-backup.yaml"
$ProductionConfigPath = "C:\Program Files\RJAutoMover\config.yaml"

Write-Info "Test Root: $TestRoot"
Write-Info "Test Config: $TestConfig"
Write-Info "Production Path: $ProductionConfigPath"
Write-Info "Backup Path: $BackupConfigPath"
Write-Info ""

# ====================================================================================
# Pre-Flight Checks
# ====================================================================================

Write-Section "Pre-Flight Checks"

# Check if test config exists
if (-not (Test-Path $TestConfig)) {
    Write-Error "Test config not found: $TestConfig"
    Write-Info "Run 2-Generate-TestConfig.ps1 and 3-Generate-Predictions.ps1 first"
    exit 1
}
Write-Success "Test config found: $TestConfig"

# Check if production config path exists (directory)
$prodConfigDir = Split-Path $ProductionConfigPath -Parent
if (-not (Test-Path $prodConfigDir)) {
    Write-Error "Production config directory not found: $prodConfigDir"
    Write-Info "RJAutoMover may not be installed at expected location"
    exit 1
}
Write-Success "Production config directory exists"

# ====================================================================================
# Manual Deployment Instructions
# ====================================================================================

Write-Section "Manual Deployment Instructions"

Write-Info "You must complete the following steps manually:"
Write-Info ""
Write-Info "1. BACKUP your current production config (if it exists):"
Write-Host "   Copy-Item '$ProductionConfigPath' '$BackupConfigPath' -Force" -ForegroundColor White
Write-Info ""
Write-Info "2. COPY the test config to production location:"
Write-Host "   Copy-Item '$TestConfig' '$ProductionConfigPath' -Force" -ForegroundColor White
Write-Info ""
Write-Info "3. START the RJAutoMoverService executable:"
Write-Info "   - Navigate to: C:\Program Files\RJAutoMover"
Write-Info "   - Run as Administrator: RJAutoMoverService.exe"
Write-Info "   OR start via services.msc (Win+R -> services.msc)"
Write-Info ""
Write-Info "4. START the RJAutoMoverTray executable (optional but recommended):"
Write-Info "   - Navigate to: C:\Program Files\RJAutoMover"
Write-Info "   - Run: RJAutoMoverTray.exe"
Write-Info "   - Tray icon provides real-time monitoring"
Write-Info ""
Write-Info "5. VERIFY service is running:"
Write-Host "   Get-Service -Name 'RJAutoMoverService'" -ForegroundColor White
Write-Info "   OR check tray icon status"
Write-Info ""
Write-Info "6. MONITOR the service logs:"
Write-Host "   Get-Content 'C:\ProgramData\RJAutoMover\Logs\*RJAutoMoverService*.log' -Wait" -ForegroundColor White
Write-Info "   OR view logs in tray application (Logs tab)"
Write-Info ""
Write-Info "7. WAIT for the service to process all test files:"
Write-Info "   - Watch the logs for file move operations"
Write-Info "   - Allow at least 2-3 minutes for all scan cycles to complete"
Write-Info "   - Recommended: Wait 5-10 minutes for thorough processing"
Write-Info "   - Service should stop actively moving files"
Write-Info ""

# ====================================================================================
# Wait for User Confirmation
# ====================================================================================

Write-Section "Ready to Continue?"

Write-Warning "Before proceeding to Step 5 (Analysis), ensure:"
Write-Info "  [x] Test config copied to: $ProductionConfigPath"
Write-Info "  [x] RJAutoMoverService.exe started and running"
Write-Info "  [x] RJAutoMoverTray.exe started (optional)"
Write-Info "  [x] Service has processed files (check logs or tray)"
Write-Info "  [x] Service is no longer actively moving files"
Write-Info ""

$continue = Read-Host "Have you completed deployment and waited for processing? (y/n)"

if ($continue -ne 'y') {
    Write-Warning "Deployment not complete - stopping here"
    Write-Info "Complete the manual steps above, then re-run this script"
    exit 1
}

Write-Success "Deployment confirmed - ready for analysis"

# ====================================================================================
# Deployment Summary
# ====================================================================================

Write-Section "Deployment Summary"

# Check if production config was actually replaced
if (Test-Path $ProductionConfigPath) {
    $prodConfigContent = Get-Content $ProductionConfigPath -Raw
    $testConfigContent = Get-Content $TestConfig -Raw

    if ($prodConfigContent -eq $testConfigContent) {
        Write-Success "Test config is active in production location"
    } else {
        Write-Warning "Production config does not match test config"
        Write-Info "You may need to redeploy the test config"
    }
}

# Check service status
$service = Get-Service -Name "RJAutoMoverService" -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -eq "Running") {
        Write-Success "RJAutoMoverService is running"
    } else {
        Write-Warning "RJAutoMoverService is not running (Status: $($service.Status))"
        Write-Info "Start the service before proceeding to Step 5"
    }
} else {
    Write-Warning "RJAutoMoverService not found"
}

# Check if backup exists
if (Test-Path $BackupConfigPath) {
    Write-Success "Backup config exists: $BackupConfigPath"
} else {
    Write-Warning "Backup config not found: $BackupConfigPath"
    Write-Info "Create backup manually if you want to restore later"
}

# ====================================================================================
# Next Steps
# ====================================================================================

Write-Section "Next Steps"

Write-Success "Step 4 Complete (Manual Deployment)"
Write-Info ""
Write-Info "Test Root: $TestRoot"
Write-Info ""
Write-Info "Next: Run Step 5 to analyze results"
Write-Host "   .\5-Analyze-Results.ps1 -TestRoot '$TestRoot' -GenerateHTML" -ForegroundColor White
Write-Info ""
Write-Warning "IMPORTANT: After testing, restore your production config:"
Write-Info "   1. Stop RJAutoMoverService.exe (close or stop via services.msc)"
Write-Info "   2. Stop RJAutoMoverTray.exe (close from system tray)"
Write-Host "   3. Copy-Item '$BackupConfigPath' '$ProductionConfigPath' -Force" -ForegroundColor White
Write-Info "   4. Restart RJAutoMoverService.exe"
Write-Info "   5. Restart RJAutoMoverTray.exe"
Write-Info ""
