# ================================================================================
# RJAutoMover Manual Deployment Instructions - Step 4
# ================================================================================
# This step is MANUAL - you must deploy the test config and run the service yourself
# This script provides instructions and validation only
#
# Usage:
#   .\4-Deploy-And-Wait.ps1
#
# Output:
#   - Instructions for manual deployment
#   - Validation of prerequisites
#   - Reminders for next step
# ================================================================================

param(
    [string]$TestConfig = "test-config.yaml",
    [string]$ProductionConfigPath = "C:\Program Files\RJAutoMover\config.yaml",
    [string]$BackupConfigPath = "config-backup.yaml"
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
Write-Info "Test Config: $TestConfig"
Write-Info "Production Path: $ProductionConfigPath"
Write-Info ""

# ====================================================================================
# Pre-Flight Checks
# ====================================================================================

Write-Section "Pre-Flight Checks"

# Check if test config exists
if (-not (Test-Path $TestConfig)) {
    Write-Error "Test config not found: $TestConfig"
    Write-Info "Run 2-Generate-TestConfig.ps1 first"
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
Write-Info "1. BACKUP your current production config:"
Write-Host "   Copy-Item '$ProductionConfigPath' '$BackupConfigPath' -Force" -ForegroundColor White
Write-Info ""
Write-Info "2. DEPLOY the test config:"
Write-Host "   Copy-Item '$TestConfig' '$ProductionConfigPath' -Force" -ForegroundColor White
Write-Info ""
Write-Info "3. RESTART the RJAutoMoverService:"
Write-Host "   Restart-Service -Name 'RJAutoMoverService' -Force" -ForegroundColor White
Write-Info "   OR use services.msc (Win+R -> services.msc)"
Write-Info ""
Write-Info "4. VERIFY service is running:"
Write-Host "   Get-Service -Name 'RJAutoMoverService'" -ForegroundColor White
Write-Info ""
Write-Info "5. MONITOR the service logs:"
Write-Host "   Get-Content 'C:\ProgramData\RJAutoMover\Logs\*RJAutoMoverService*.log' -Wait" -ForegroundColor White
Write-Info ""
Write-Info "6. WAIT for the service to process all test files"
Write-Info "   - Watch the logs for file move operations"
Write-Info "   - Allow at least 2-3 minutes for all scan cycles to complete"
Write-Info "   - Recommended: Wait 5-10 minutes for thorough processing"
Write-Info ""

# ====================================================================================
# Wait for User Confirmation
# ====================================================================================

Write-Section "Ready to Continue?"

Write-Warning "Before proceeding to Step 5 (Analysis), ensure:"
Write-Info "  [x] Test config deployed to production location"
Write-Info "  [x] RJAutoMoverService restarted successfully"
Write-Info "  [x] Service has processed files (check logs)"
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
Write-Info "Next: Run Step 5 to analyze results"
Write-Host "   .\5-Analyze-Results.ps1 -GenerateHTML" -ForegroundColor White
Write-Info ""
Write-Warning "IMPORTANT: After testing, restore your production config:"
Write-Host "   Stop-Service -Name 'RJAutoMoverService'" -ForegroundColor White
Write-Host "   Copy-Item '$BackupConfigPath' '$ProductionConfigPath' -Force" -ForegroundColor White
Write-Host "   Start-Service -Name 'RJAutoMoverService'" -ForegroundColor White
Write-Info ""
