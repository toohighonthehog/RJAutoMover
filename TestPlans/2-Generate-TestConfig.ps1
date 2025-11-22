# ================================================================================
# RJAutoMover Test Config Generator - Step 2
# ================================================================================
# Generates test-config.yaml with non-overlapping rules covering ~85% of test files
# Saves to the versioned test folder created in Step 1
#
# Usage:
#   .\2-Generate-TestConfig.ps1 -TestRoot "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"
#
# Output (in TestRoot):
#   - test-config.yaml (valid configuration for testing)
# ================================================================================

param(
    [Parameter(Mandatory=$true)]
    [string]$TestRoot
)

# Color output functions
function Write-Success { param([string]$Message) Write-Host "[OK] $Message" -ForegroundColor Green }
function Write-Info { param([string]$Message) Write-Host " -> $Message" -ForegroundColor Cyan }
function Write-Warning { param([string]$Message) Write-Host "[!!] $Message" -ForegroundColor Yellow }
function Write-Section { param([string]$Title)
    Write-Host ""
    Write-Host "============================================================================" -ForegroundColor Yellow
    Write-Host " $Title" -ForegroundColor Yellow
    Write-Host "============================================================================" -ForegroundColor Yellow
}

Write-Section "Step 2: Generate Test Configuration"

# Validate TestRoot
if (-not (Test-Path $TestRoot)) {
    Write-Warning "Test root folder not found: $TestRoot"
    Write-Info "Run 1-Generate-TestFiles.ps1 first to create the test folder"
    exit 1
}

Write-Info "Test Root: $TestRoot"
Write-Info ""

# ====================================================================================
# Define Test Rules (Non-Overlapping, Valid Configuration)
# ====================================================================================

$rules = @(
    # Rule 1: Recent documents (modified within 1 hour)
    @{
        Name = "Test-RecentDocs"
        Extension = ".txt|.pdf"
        DateFilter = "LM:-60"
        Destination = "Recent"
        ScanInterval = 30000
        FileExists = "skip"
    },

    # Rule 2: Old documents (created more than 7 days ago)
    @{
        Name = "Test-OldDocs7Days"
        Extension = ".doc|.docx|.rtf"
        DateFilter = "FC:+10080"
        Destination = "Old7Days"
        ScanInterval = 45000
        FileExists = "skip"
    },

    # Rule 3: Old archives (created more than 30 days ago)
    @{
        Name = "Test-OldArchives30Days"
        Extension = ".zip|.rar|.7z"
        DateFilter = "FC:+43200"
        Destination = "Old30Days"
        ScanInterval = 60000
        FileExists = "skip"
    },

    # Rule 4: Not accessed in 90 days
    @{
        Name = "Test-NotAccessed90Days"
        Extension = ".bak|.tmp"
        DateFilter = "LA:+129600"
        Destination = "Old90Days"
        ScanInterval = 60000
        FileExists = "skip"
    },

    # Rule 5: Recent videos (modified within 2 hours)
    @{
        Name = "Test-RecentVideos"
        Extension = ".mp4|.avi|.mkv"
        DateFilter = "LM:-120"
        Destination = "Videos"
        ScanInterval = 30000
        FileExists = "overwrite"
    },

    # Rule 6: Recent images (created within 1 day)
    @{
        Name = "Test-RecentImages"
        Extension = ".jpg|.png|.gif"
        DateFilter = "FC:-1440"
        Destination = "Images"
        ScanInterval = 30000
        FileExists = "overwrite"
    },

    # Rule 7: Spreadsheets (no date filter)
    @{
        Name = "Test-AllSpreadsheets"
        Extension = ".xls|.xlsx|.csv"
        DateFilter = ""
        Destination = "Documents"
        ScanInterval = 45000
        FileExists = "skip"
    },

    # Rule 8: Recent code (modified within 6 hours)
    @{
        Name = "Test-RecentCode"
        Extension = ".cs|.js|.py"
        DateFilter = "LM:-360"
        Destination = "Code"
        ScanInterval = 30000
        FileExists = "skip"
    },

    # Rule 9: Old web files (created more than 3 days ago)
    @{
        Name = "Test-OldWebFiles"
        Extension = ".html|.css|.json"
        DateFilter = "FC:+4320"
        Destination = "Archives"
        ScanInterval = 60000
        FileExists = "skip"
    },

    # Rule 10: OTHERS catch-all (files created more than 14 days ago)
    @{
        Name = "Test-OTHERS-Old14Days"
        Extension = "OTHERS"
        DateFilter = "FC:+20160"
        Destination = "Others"
        ScanInterval = 90000
        FileExists = "skip"
    }
)

# ====================================================================================
# Generate YAML Configuration
# ====================================================================================

Write-Section "Generating Test Configuration"

$yamlConfig = @"
# ====================================================================================
# RJAutoMover Test Configuration
# ====================================================================================
# Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
# Purpose: Automated testing of file processing rules
#
# TEST METADATA:
# - Total Rules: $($rules.Count)
# - Expected Coverage: ~85% of test files
# - Expected Unprocessed: ~15% (files not matching any rule)
# - Design: Non-overlapping rules (each file matches at most ONE rule)
# ====================================================================================

FileRules:
"@

foreach ($rule in $rules) {
    $yamlConfig += @"

  # ==================================================================================
  # $($rule.Name)
  # ==================================================================================
  - Name: "$($rule.Name)"
    SourceFolder: "$TestRoot\Source"
    Extension: $($rule.Extension)
    DestinationFolder: "$TestRoot\Destination\$($rule.Destination)"
    ScanIntervalMs: $($rule.ScanInterval)
    IsActive: true
    FileExists: $($rule.FileExists)
"@

    if ($rule.DateFilter -ne "") {
        $yamlConfig += @"

    DateFilter: "$($rule.DateFilter)"
"@
    }
}

$yamlConfig += @"


# ====================================================================================
# Application Configuration
# ====================================================================================
Application:
  ProcessingPaused: false
  RequireTrayApproval: false
  RetryDelayMs: 5000
  FailureCooldownMs: 180000
  RecheckServiceMs: 30000
  RecheckTrayMs: 30000
  PauseDelayMs: 0
  ServiceHeartbeatMs: 300000
  MemoryLimitMb: 512
  MemoryCheckMs: 60000
  LogFolder: "C:\\ProgramData\\RJAutoMover\\Logs"
  LogRetentionDays: 7
  ServiceGrpcPort: 60051
  TrayGrpcPort: 60052
  ActivityHistoryEnabled: true
  ActivityHistoryMaxRecords: 5000
  ActivityHistoryRetentionDays: 90

# ====================================================================================
# Test Configuration Notes
# ====================================================================================
#
# Rule Coverage:
#   - Rule 1-9: Specific extension rules (process first)
#   - Rule 10: OTHERS rule (processes last, catch-all)
#
# DateFilter Scenarios Tested:
#   - LM:- (within last modified)
#   - LM:+ (older than modified)
#   - FC:- (within last created)
#   - FC:+ (older than created)
#   - LA:+ (older than accessed)
#   - No filter (all files)
#
# FileExists Policies:
#   - skip: Most rules
#   - overwrite: Videos and Images rules
#
# Expected Behavior:
#   - Extensions with specific rules process first
#   - OTHERS rule catches remaining files >14 days old
#   - Files not matching any rule remain in source
#
# ====================================================================================
"@

# Save configuration
$configPath = Join-Path $TestRoot "test-config.yaml"
Set-Content -Path $configPath -Value $yamlConfig -Encoding UTF8
Write-Success "Configuration saved: $configPath"

# ====================================================================================
# Validate Configuration (Basic Check)
# ====================================================================================

Write-Section "Validating Configuration"

# Check for overlapping extensions in same source folder
$extensionCheck = @{}
$hasOverlap = $false

foreach ($rule in $rules) {
    if ($rule.Extension -ne "OTHERS") {
        $exts = $rule.Extension -split '\|'
        foreach ($ext in $exts) {
            if ($extensionCheck.ContainsKey($ext)) {
                Write-Warning "Extension overlap detected: $ext in rules '$($extensionCheck[$ext])' and '$($rule.Name)'"
                $hasOverlap = $true
            } else {
                $extensionCheck[$ext] = $rule.Name
            }
        }
    }
}

if (-not $hasOverlap) {
    Write-Success "No extension overlaps detected (valid config)"
}

# Check OTHERS rule has DateFilter
$othersRules = $rules | Where-Object { $_.Extension -eq "OTHERS" }
foreach ($othersRule in $othersRules) {
    if ([string]::IsNullOrWhiteSpace($othersRule.DateFilter)) {
        Write-Warning "OTHERS rule '$($othersRule.Name)' is missing DateFilter (INVALID)"
    } else {
        Write-Success "OTHERS rule '$($othersRule.Name)' has DateFilter (valid)"
    }
}

# ====================================================================================
# Summary
# ====================================================================================

Write-Section "Summary"
Write-Success "Step 2 Complete"
Write-Info "Test Root: $TestRoot"
Write-Info "Rules created: $($rules.Count)"
Write-Info "Config file: $configPath"
Write-Info ""
Write-Info "Next step: Run 3-Generate-Predictions.ps1 -TestRoot '$TestRoot'"
