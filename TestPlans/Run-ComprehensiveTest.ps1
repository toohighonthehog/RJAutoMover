# ================================================================================
# RJAutoMover Comprehensive Test Suite Runner
# ================================================================================
# Automates the entire testing process: generates test data, deploys config,
# tests each rule sequentially, analyzes results, and generates report.
#
# Usage:
#   .\Run-ComprehensiveTest.ps1 [-SkipGeneration] [-RulesTimeoutSec 120]
#
# Options:
#   -SkipGeneration: Skip test file generation (use existing data)
#   -RulesTimeoutSec: Seconds to wait after activating each rule (default: 120)
#
# ================================================================================

param(
    [switch]$SkipGeneration = $false,
    [int]$RulesTimeoutSec = 120,
    [string]$BaseFolder = "C:\RJAutoMover_TestData"
)

# Color output functions
function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host " -> $Message" -ForegroundColor Cyan
}

function Write-Warning {
    param([string]$Message)
    Write-Host "[!!] $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "============================================================================" -ForegroundColor Yellow
    Write-Host " $Title" -ForegroundColor Yellow
    Write-Host "============================================================================" -ForegroundColor Yellow
}

# ================================================================================
# Pre-Flight Checks
# ================================================================================

Write-Section "RJAutoMover Comprehensive Test Suite"

# Check if running as admin
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "This script requires administrator privileges to manage the service"
    Write-Info "Please run PowerShell as Administrator and try again"
    exit 1
}

# Check if service exists
$service = Get-Service -Name "RJAutoMoverService" -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Error "RJAutoMoverService not found"
    Write-Info "Please install RJAutoMover before running tests"
    exit 1
}

Write-Info "Pre-flight checks passed"
Write-Info ""

# ================================================================================
# Step 1: Generate Test Data
# ================================================================================

if (-not $SkipGeneration) {
    Write-Section "Generating Test Data"

    $scriptPath = Join-Path $PSScriptRoot "Create-TestFiles.ps1"

    if (-not (Test-Path $scriptPath)) {
        Write-Error "Create-TestFiles.ps1 not found in $PSScriptRoot"
        exit 1
    }

    Write-Info "Running Create-TestFiles.ps1..."
    & $scriptPath

    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
        Write-Error "Test file generation failed"
        exit 1
    }

    Write-Success "Test data generated successfully"
} else {
    Write-Section "Skipping Test Data Generation"
    Write-Info "Using existing test data in $BaseFolder"

    if (-not (Test-Path "$BaseFolder\Source")) {
        Write-Error "Source folder not found: $BaseFolder\Source"
        Write-Info "Run without -SkipGeneration to generate test data"
        exit 1
    }
}

# ================================================================================
# Step 2: Backup Production Config
# ================================================================================

Write-Section "Backing Up Production Config"

$prodConfigPath = "C:\Program Files\RJAutoMover\config.yaml"
$backupConfigPath = "C:\Program Files\RJAutoMover\config.yaml.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

if (Test-Path $prodConfigPath) {
    Copy-Item -Path $prodConfigPath -Destination $backupConfigPath -Force
    Write-Success "Backed up to: $backupConfigPath"
} else {
    Write-Warning "Production config not found (new installation?)"
}

# ================================================================================
# Step 3: Deploy Test Config
# ================================================================================

Write-Section "Deploying Test Config"

$testConfigPath = Join-Path $BaseFolder "test-config.yaml"

if (-not (Test-Path $testConfigPath)) {
    Write-Error "Test config not found: $testConfigPath"
    Write-Info "Run Create-TestFiles.ps1 to generate test configuration"
    exit 1
}

# Read test config to extract rules
$testConfigContent = Get-Content -Path $testConfigPath -Raw
Copy-Item -Path $testConfigPath -Destination $prodConfigPath -Force
Write-Success "Deployed test config to: $prodConfigPath"

# Parse rules from config
$rules = @()
$currentRule = $null

foreach ($line in $testConfigContent -split "`n") {
    $line = $line.Trim()

    if ($line -match '^\s*- Name:\s*(.+)$') {
        if ($null -ne $currentRule -and $currentRule.Name -match '^Test-') {
            $rules += $currentRule
        }
        $currentRule = @{
            Name = $matches[1].Trim()
            Extension = ""
            DateFilter = ""
            DestinationFolder = ""
        }
    }
    elseif ($null -ne $currentRule) {
        if ($line -match '^\s*Extension:\s*(.+)$') {
            $currentRule.Extension = $matches[1].Trim()
        }
        elseif ($line -match '^\s*DateFilter:\s*"(.+)"$') {
            $currentRule.DateFilter = $matches[1].Trim()
        }
        elseif ($line -match '^\s*DestinationFolder:\s*(.+)$') {
            $currentRule.DestinationFolder = $matches[1].Trim()
        }
    }
}

if ($null -ne $currentRule -and $currentRule.Name -match '^Test-') {
    $rules += $currentRule
}

Write-Info "Found $($rules.Count) test rules in configuration"
Write-Info ""

# ================================================================================
# Step 4: Test Each Rule Sequentially
# ================================================================================

Write-Section "Testing Rules Sequentially"

$testResults = @()

foreach ($rule in $rules) {
    Write-Info ""
    Write-Info "======================================================================"
    Write-Info "Testing Rule: $($rule.Name)"
    Write-Info "======================================================================"
    Write-Info "Extension:     $($rule.Extension)"
    Write-Info "DateFilter:    $($rule.DateFilter)"
    Write-Info "Destination:   $($rule.DestinationFolder)"
    Write-Info ""

    # Modify config to enable only this rule
    $modifiedConfig = $testConfigContent -replace '(?m)^\s*- Name:\s*Test-.*?(?=IsActive:)IsActive:\s*\w+', {
        param($match)
        if ($match.Value -match "Name:\s*$($rule.Name)") {
            $match.Value -replace 'IsActive:\s*\w+', 'IsActive: true'
        } else {
            $match.Value -replace 'IsActive:\s*\w+', 'IsActive: false'
        }
    }

    Set-Content -Path $prodConfigPath -Value $modifiedConfig

    # Restart service
    Write-Info "Restarting service..."
    Restart-Service -Name RJAutoMoverService -Force
    Start-Sleep -Seconds 5

    # Wait for service to be running
    $timeout = 30
    $elapsed = 0
    while ((Get-Service -Name RJAutoMoverService).Status -ne 'Running' -and $elapsed -lt $timeout) {
        Start-Sleep -Seconds 1
        $elapsed++
    }

    if ((Get-Service -Name RJAutoMoverService).Status -ne 'Running') {
        Write-Error "Service failed to start"
        $testResults += [PSCustomObject]@{
            Rule = $rule.Name
            Status = "FAILED - Service Error"
            CorrectlyMoved = 0
            IncorrectlyMoved = 0
            ShouldHaveMoved = 0
        }
        continue
    }

    Write-Success "Service restarted successfully"

    # Wait for rule to process files
    Write-Info "Waiting $RulesTimeoutSec seconds for rule to process files..."
    for ($i = 1; $i -le $RulesTimeoutSec; $i++) {
        Write-Progress -Activity "Processing $($rule.Name)" -Status "$i of $RulesTimeoutSec seconds" -PercentComplete (($i / $RulesTimeoutSec) * 100)
        Start-Sleep -Seconds 1
    }
    Write-Progress -Activity "Processing $($rule.Name)" -Completed

    # Analyze results
    Write-Info "Analyzing results..."

    $analyzerScript = Join-Path $PSScriptRoot "Analyze-TestResults.ps1"
    if (-not (Test-Path $analyzerScript)) {
        Write-Error "Analyze-TestResults.ps1 not found"
        exit 1
    }

    # Capture analyzer output
    $analysisOutput = & $analyzerScript -RuleName $rule.Name 2>&1 | Out-String

    # Parse results
    $correctlyMoved = 0
    $incorrectlyMoved = 0
    $shouldHaveMoved = 0

    if ($analysisOutput -match 'Correctly moved:\s+(\d+)') {
        $correctlyMoved = [int]$matches[1]
    }
    if ($analysisOutput -match 'Incorrectly moved:\s+(\d+)') {
        $incorrectlyMoved = [int]$matches[1]
    }
    if ($analysisOutput -match 'Should have moved:\s+(\d+)') {
        $shouldHaveMoved = [int]$matches[1]
    }

    $status = if ($incorrectlyMoved -eq 0 -and $shouldHaveMoved -eq 0) {
        "PASSED"
    } elseif ($correctlyMoved -gt 0) {
        "PARTIAL"
    } else {
        "FAILED"
    }

    $testResults += [PSCustomObject]@{
        Rule = $rule.Name
        Status = $status
        CorrectlyMoved = $correctlyMoved
        IncorrectlyMoved = $incorrectlyMoved
        ShouldHaveMoved = $shouldHaveMoved
    }

    Write-Info ""
    if ($status -eq "PASSED") {
        Write-Success "Rule test PASSED"
    } elseif ($status -eq "PARTIAL") {
        Write-Warning "Rule test PARTIAL - some issues found"
    } else {
        Write-Error "Rule test FAILED"
    }
}

# ================================================================================
# Step 5: Generate Test Report
# ================================================================================

Write-Section "Generating Test Report"

$reportPath = Join-Path $BaseFolder "TEST_REPORT_$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"

$reportContent = @"
================================================================================
                  RJAutoMover Comprehensive Test Report
================================================================================

Test Date: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Test Location: $BaseFolder
Rules Tested: $($rules.Count)
Total Wait Time: $($rules.Count * $RulesTimeoutSec) seconds

================================================================================
TEST RESULTS SUMMARY
================================================================================

"@

# Add results table
$reportContent += $testResults | Format-Table -AutoSize | Out-String

$reportContent += @"

================================================================================
DETAILED RESULTS
================================================================================

"@

foreach ($result in $testResults) {
    $reportContent += @"
Rule: $($result.Rule)
  Status:            $($result.Status)
  Correctly Moved:   $($result.CorrectlyMoved)
  Incorrectly Moved: $($result.IncorrectlyMoved)
  Should Have Moved: $($result.ShouldHaveMoved)

"@
}

# Add statistics
$passed = ($testResults | Where-Object { $_.Status -eq "PASSED" }).Count
$partial = ($testResults | Where-Object { $_.Status -eq "PARTIAL" }).Count
$failed = ($testResults | Where-Object { $_.Status -eq "FAILED" }).Count

$reportContent += @"

================================================================================
STATISTICS
================================================================================

Total Rules:         $($rules.Count)
Passed:              $passed
Partial:             $partial
Failed:              $failed

Success Rate:        $([Math]::Round(($passed / $rules.Count) * 100, 1))%

================================================================================
END OF REPORT
================================================================================
"@

Set-Content -Path $reportPath -Value $reportContent
Write-Success "Test report saved to: $reportPath"

# ================================================================================
# Step 6: Restore Production Config
# ================================================================================

Write-Section "Restoring Production Config"

Stop-Service -Name RJAutoMoverService -Force

if (Test-Path $backupConfigPath) {
    Copy-Item -Path $backupConfigPath -Destination $prodConfigPath -Force
    Write-Success "Restored production config from: $backupConfigPath"
} else {
    Write-Warning "No backup found - test config still in place"
    Write-Info "Manual restore may be required"
}

Start-Service -Name RJAutoMoverService
Write-Success "Service restarted with production config"

# ================================================================================
# Final Summary
# ================================================================================

Write-Section "Test Suite Complete"

Write-Info ""
Write-Info "Results Summary:"
Write-Host "  Passed:   " -NoNewline -ForegroundColor White
Write-Host $passed -ForegroundColor Green
Write-Host "  Partial:  " -NoNewline -ForegroundColor White
Write-Host $partial -ForegroundColor Yellow
Write-Host "  Failed:   " -NoNewline -ForegroundColor White
Write-Host $failed -ForegroundColor Red

Write-Info ""
Write-Info "Success Rate: $([Math]::Round(($passed / $rules.Count) * 100, 1))%"
Write-Info ""
Write-Success "Full report: $reportPath"
Write-Info ""

# Display results table
$testResults | Format-Table -AutoSize

if ($failed -eq 0 -and $partial -eq 0) {
    Write-Success "All tests passed! DateFilter implementation is working correctly."
} elseif ($failed -eq 0) {
    Write-Warning "Some tests had minor issues. Review report for details."
} else {
    Write-Error "Some tests failed. Review report and logs for details."
    Write-Info "Service logs: C:\ProgramData\RJAutoMover\Logs"
}

Write-Info ""
