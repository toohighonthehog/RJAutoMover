# ================================================================================
# RJAutoMover Test Results Analyzer
# ================================================================================
# Analyzes source and destination folders after running test rules to verify
# that files were moved correctly according to DateFilter criteria.
#
# Usage:
#   .\Analyze-TestResults.ps1 [-RuleName <name>] [-Detailed]
#
# Examples:
#   .\Analyze-TestResults.ps1 -RuleName "Test-OldDocs7Days"
#   .\Analyze-TestResults.ps1 -Detailed
#   .\Analyze-TestResults.ps1  (analyzes all rules)
# ================================================================================

param(
    [string]$BaseFolder = "C:\RJAutoMover_TestData",
    [string]$RuleName = "",
    [switch]$Detailed = $false
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

# Helper function to parse DateFilter format
function Parse-DateFilter {
    param([string]$DateFilter)

    if ([string]::IsNullOrWhiteSpace($DateFilter)) {
        return $null
    }

    if ($DateFilter -match '^(LA|LM|FC):([+-])(\d+)$') {
        $type = $matches[1]
        $sign = $matches[2]
        $minutes = [int]$matches[3]

        return @{
            Type = $type
            Sign = $sign
            Minutes = $minutes
            IsPositive = ($sign -eq '+')
            TypeName = switch ($type) {
                'LA' { 'LastAccessed' }
                'LM' { 'LastModified' }
                'FC' { 'FileCreated' }
            }
        }
    }

    return $null
}

# Helper function to check if file matches date filter
function Test-FileMatchesDateFilter {
    param(
        [System.IO.FileInfo]$File,
        [hashtable]$FilterInfo
    )

    if ($null -eq $FilterInfo) {
        return $true  # No filter = all files match
    }

    $now = Get-Date
    $fileTimestamp = switch ($FilterInfo.Type) {
        'LA' { $File.LastAccessTime }
        'LM' { $File.LastWriteTime }
        'FC' { $File.CreationTime }
    }

    $ageMinutes = [Math]::Round((New-TimeSpan -Start $fileTimestamp -End $now).TotalMinutes, 1)

    if ($FilterInfo.IsPositive) {
        # Positive (+ sign): file must be OLDER than threshold (age >= minutes)
        return $ageMinutes -ge $FilterInfo.Minutes
    } else {
        # Negative (- sign): file must be WITHIN last threshold (age <= minutes)
        return $ageMinutes -le $FilterInfo.Minutes
    }
}

# ================================================================================
# Main Script
# ================================================================================

Write-Section "RJAutoMover Test Results Analyzer"

$sourcePath = Join-Path $BaseFolder "Source"
$configPath = Join-Path $BaseFolder "test-config.yaml"

# Verify paths exist
if (-not (Test-Path $sourcePath)) {
    Write-Error "Source folder not found: $sourcePath"
    Write-Info "Run Create-TestFiles.ps1 first to generate test data"
    exit 1
}

if (-not (Test-Path $configPath)) {
    Write-Error "Config file not found: $configPath"
    Write-Info "Run Create-TestFiles.ps1 first to generate test configuration"
    exit 1
}

# Load config file
Write-Info "Loading test configuration..."
try {
    $configContent = Get-Content -Path $configPath -Raw

    # Parse YAML manually (simple parsing for our structured config)
    $rules = @()
    $currentRule = $null

    foreach ($line in $configContent -split "`n") {
        $line = $line.Trim()

        if ($line -match '^\s*- Name:\s*(.+)$') {
            if ($null -ne $currentRule) {
                $rules += $currentRule
            }
            $currentRule = @{
                Name = $matches[1].Trim()
                Extension = ""
                DestinationFolder = ""
                DateFilter = ""
                IsActive = $false
            }
        }
        elseif ($null -ne $currentRule) {
            if ($line -match '^\s*Extension:\s*(.+)$') {
                $currentRule.Extension = $matches[1].Trim()
            }
            elseif ($line -match '^\s*DestinationFolder:\s*(.+)$') {
                $currentRule.DestinationFolder = $matches[1].Trim()
            }
            elseif ($line -match '^\s*DateFilter:\s*"(.+)"$') {
                $currentRule.DateFilter = $matches[1].Trim()
            }
            elseif ($line -match '^\s*IsActive:\s*(.+)$') {
                $currentRule.IsActive = $matches[1].Trim() -eq 'true'
            }
        }
    }

    if ($null -ne $currentRule) {
        $rules += $currentRule
    }

    Write-Success "Loaded $($rules.Count) rules from configuration"
}
catch {
    Write-Error "Failed to load config: $_"
    exit 1
}

# Filter rules if specific rule requested
if ($RuleName) {
    $rules = $rules | Where-Object { $_.Name -eq $RuleName }
    if ($rules.Count -eq 0) {
        Write-Error "Rule not found: $RuleName"
        Write-Info "Available rules:"
        $allRules = @()
        $configContent -split "`n" | ForEach-Object {
            if ($_ -match '^\s*- Name:\s*(.+)$') {
                Write-Info "  - $($matches[1].Trim())"
            }
        }
        exit 1
    }
}

# Get current source files
$sourceFiles = Get-ChildItem -Path $sourcePath -File
Write-Info "Current source files: $($sourceFiles.Count)"
Write-Info ""

# Analyze each rule
$totalIssues = 0

foreach ($rule in $rules) {
    Write-Section "Analyzing Rule: $($rule.Name)"

    Write-Info "Extension:     $($rule.Extension)"
    Write-Info "DateFilter:    $($rule.DateFilter)"
    Write-Info "Destination:   $($rule.DestinationFolder)"
    Write-Info "IsActive:      $($rule.IsActive)"
    Write-Info ""

    if (-not $rule.IsActive) {
        Write-Warning "Rule is not active - skipping analysis"
        Write-Info "Enable this rule in config and restart service to test"
        continue
    }

    # Parse date filter
    $filterInfo = Parse-DateFilter -DateFilter $rule.DateFilter

    if ($null -eq $filterInfo) {
        Write-Warning "No date filter or invalid format - skipping analysis"
        continue
    }

    $filterDesc = if ($filterInfo.IsPositive) {
        "Files where $($filterInfo.TypeName) is OLDER than $($filterInfo.Minutes) minutes"
    } else {
        "Files where $($filterInfo.TypeName) is WITHIN last $($filterInfo.Minutes) minutes"
    }

    Write-Info "Filter Logic: $filterDesc"
    Write-Info ""

    # Parse extensions
    $extensions = if ($rule.Extension -eq "OTHERS") {
        @("*")  # Match all
    } else {
        $rule.Extension -split '\|' | ForEach-Object { $_.Trim() }
    }

    # Check destination folder
    if (-not (Test-Path $rule.DestinationFolder)) {
        Write-Warning "Destination folder not found (no files moved yet)"
        continue
    }

    # Get destination files
    $destFiles = Get-ChildItem -Path $rule.DestinationFolder -File
    Write-Info "Files in destination: $($destFiles.Count)"

    # Analyze each destination file
    $correctlyMoved = 0
    $incorrectlyMoved = 0
    $incorrectFiles = @()

    foreach ($file in $destFiles) {
        # Check extension match
        $extensionMatches = $false
        if ($rule.Extension -eq "OTHERS") {
            $extensionMatches = $true
        } else {
            foreach ($ext in $extensions) {
                if ($file.Extension -eq $ext) {
                    $extensionMatches = $true
                    break
                }
            }
        }

        if (-not $extensionMatches) {
            $incorrectlyMoved++
            $incorrectFiles += [PSCustomObject]@{
                FileName = $file.Name
                Reason = "Extension mismatch (expected: $($rule.Extension), found: $($file.Extension))"
            }
            continue
        }

        # Check date filter match
        $matchesFilter = Test-FileMatchesDateFilter -File $file -FilterInfo $filterInfo

        if ($matchesFilter) {
            $correctlyMoved++
        } else {
            $incorrectlyMoved++

            $now = Get-Date
            $timestamp = switch ($filterInfo.Type) {
                'LA' { $file.LastAccessTime }
                'LM' { $file.LastWriteTime }
                'FC' { $file.CreationTime }
            }
            $ageMinutes = [Math]::Round((New-TimeSpan -Start $timestamp -End $now).TotalMinutes, 1)

            $reason = if ($filterInfo.IsPositive) {
                "$($filterInfo.TypeName) is $ageMinutes min ago (should be >= $($filterInfo.Minutes) min)"
            } else {
                "$($filterInfo.TypeName) is $ageMinutes min ago (should be <= $($filterInfo.Minutes) min)"
            }

            $incorrectFiles += [PSCustomObject]@{
                FileName = $file.Name
                Reason = $reason
                Created = $file.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")
                Modified = $file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                Accessed = $file.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss")
            }
        }
    }

    # Check for files that SHOULD have been moved but are still in source
    $shouldHaveMovedCount = 0
    $shouldHaveMoved = @()

    foreach ($file in $sourceFiles) {
        # Check extension match
        $extensionMatches = $false
        if ($rule.Extension -eq "OTHERS") {
            $extensionMatches = $true
        } else {
            foreach ($ext in $extensions) {
                if ($file.Extension -eq $ext) {
                    $extensionMatches = $true
                    break
                }
            }
        }

        if (-not $extensionMatches) {
            continue
        }

        # Check date filter match
        $matchesFilter = Test-FileMatchesDateFilter -File $file -FilterInfo $filterInfo

        if ($matchesFilter) {
            $shouldHaveMovedCount++

            $now = Get-Date
            $timestamp = switch ($filterInfo.Type) {
                'LA' { $file.LastAccessTime }
                'LM' { $file.LastWriteTime }
                'FC' { $file.CreationTime }
            }
            $ageMinutes = [Math]::Round((New-TimeSpan -Start $timestamp -End $now).TotalMinutes, 1)

            $shouldHaveMoved += [PSCustomObject]@{
                FileName = $file.Name
                Reason = "$($filterInfo.TypeName) is $ageMinutes min ago (matches filter criteria)"
                Created = $file.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")
                Modified = $file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                Accessed = $file.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss")
            }
        }
    }

    # Display results
    Write-Info "Results:"
    Write-Host "  Correctly moved:       " -NoNewline -ForegroundColor White
    Write-Host $correctlyMoved -ForegroundColor Green

    Write-Host "  Incorrectly moved:     " -NoNewline -ForegroundColor White
    if ($incorrectlyMoved -eq 0) {
        Write-Host $incorrectlyMoved -ForegroundColor Green
    } else {
        Write-Host $incorrectlyMoved -ForegroundColor Red
    }

    Write-Host "  Should have moved:     " -NoNewline -ForegroundColor White
    if ($shouldHaveMovedCount -eq 0) {
        Write-Host $shouldHaveMovedCount -ForegroundColor Green
    } else {
        Write-Host $shouldHaveMovedCount -ForegroundColor Yellow
    }

    $issueCount = $incorrectlyMoved + $shouldHaveMovedCount
    $totalIssues += $issueCount

    if ($issueCount -eq 0) {
        Write-Success "All files processed correctly!"
    } else {
        Write-Warning "Found $issueCount issue(s)"

        if ($incorrectFiles.Count -gt 0) {
            Write-Info ""
            Write-Info "Incorrectly moved files:"
            if ($Detailed) {
                $incorrectFiles | Format-Table -AutoSize | Out-String | ForEach-Object { Write-Host $_ -ForegroundColor Red }
            } else {
                $incorrectFiles | Select-Object -First 5 | Format-Table -AutoSize | Out-String | ForEach-Object { Write-Host $_ -ForegroundColor Red }
                if ($incorrectFiles.Count -gt 5) {
                    Write-Info "... and $($incorrectFiles.Count - 5) more (use -Detailed to see all)"
                }
            }
        }

        if ($shouldHaveMoved.Count -gt 0) {
            Write-Info ""
            Write-Info "Files that should have been moved but are still in source:"
            if ($Detailed) {
                $shouldHaveMoved | Format-Table -AutoSize | Out-String | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
            } else {
                $shouldHaveMoved | Select-Object -First 5 | Format-Table -AutoSize | Out-String | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
                if ($shouldHaveMoved.Count -gt 5) {
                    Write-Info "... and $($shouldHaveMoved.Count - 5) more (use -Detailed to see all)"
                }
            }

            Write-Info ""
            Write-Warning "Possible reasons:"
            Write-Info "  1. Service hasn't run yet (wait for scan interval)"
            Write-Info "  2. Files exist in destination with FileExists: skip"
            Write-Info "  3. Service encountered an error (check logs)"
            Write-Info "  4. Rule priority conflict (another rule processed them first)"
        }
    }
}

# Final summary
Write-Section "Analysis Complete"

if ($totalIssues -eq 0) {
    Write-Success "All rules processed correctly! No issues found."
} else {
    Write-Warning "Found $totalIssues total issue(s) across all rules"
    Write-Info ""
    Write-Info "Recommended actions:"
    Write-Info "  1. Review service logs: C:\ProgramData\RJAutoMover\Logs\service-*.log"
    Write-Info "  2. Check for config validation errors in logs"
    Write-Info "  3. Verify scan intervals have elapsed"
    Write-Info "  4. Check activity history in tray application"
    Write-Info "  5. Re-run with -Detailed flag for full file list"
}

Write-Info ""
