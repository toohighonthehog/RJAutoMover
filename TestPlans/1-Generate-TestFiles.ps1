# ================================================================================
# RJAutoMover Test Data Generator - Step 1
# ================================================================================
# Creates versioned test folder with test files and generates manifest for predictions
# All test artifacts are isolated in C:\RJAutoMoverTest\testdata-<version>\
#
# Usage:
#   .\1-Generate-TestFiles.ps1
#   .\1-Generate-TestFiles.ps1 -TotalFiles 2000
#
# Output (in versioned test folder):
#   - Source\ (test files)
#   - Destination\* (empty folders for each rule)
#   - test-files-manifest.yaml (file metadata)
#   - Logs\ (for test run logs)
# ================================================================================

param(
    [int]$TotalFiles = 1000
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

# ====================================================================================
# Create Versioned Test Folder
# ====================================================================================

Write-Section "Step 1: Generate Test Files + Manifest"

# Read application version from installer\version.txt
$versionFile = Join-Path $PSScriptRoot "..\installer\version.txt"
if (Test-Path $versionFile) {
    $appVersion = (Get-Content $versionFile -Raw).Trim()
} else {
    $appVersion = "0.0.0.0"
    Write-Warning "Version file not found, using default: $appVersion"
}

# Generate versioned folder name: <appVersion>-<timestamp>
$timestamp = Get-Date -Format "yyyyMMddHHmmss"
$version = "$appVersion-$timestamp"
$testRoot = "C:\RJAutoMoverTest\testdata-$version"

Write-Info "Creating versioned test folder:"
Write-Info "  App Version: $appVersion"
Write-Info "  Timestamp: $timestamp"
Write-Info "  Test Folder: testdata-$version"
Write-Info "  Location: $testRoot"
Write-Info "  Total Files: $TotalFiles"
Write-Info ""

# Create root test folder
if (-not (Test-Path "C:\RJAutoMoverTest")) {
    New-Item -Path "C:\RJAutoMoverTest" -ItemType Directory -Force | Out-Null
}

if (Test-Path $testRoot) {
    Write-Warning "Test folder already exists, cleaning..."
    Remove-Item $testRoot -Recurse -Force
}

New-Item -Path $testRoot -ItemType Directory -Force | Out-Null
Write-Success "Test root created: $testRoot"

# Define folder structure
$folders = @{
    "Source"       = "$testRoot\Source"
    "DestRecent"   = "$testRoot\Destination\Recent"
    "DestOld7Days" = "$testRoot\Destination\Old7Days"
    "DestOld30Days"= "$testRoot\Destination\Old30Days"
    "DestOld90Days"= "$testRoot\Destination\Old90Days"
    "DestVideos"   = "$testRoot\Destination\Videos"
    "DestImages"   = "$testRoot\Destination\Images"
    "DestDocuments"= "$testRoot\Destination\Documents"
    "DestArchives" = "$testRoot\Destination\Archives"
    "DestCode"     = "$testRoot\Destination\Code"
    "DestOthers"   = "$testRoot\Destination\Others"
    "Logs"         = "$testRoot\Logs"
    "Results"      = "$testRoot\Results"
}

# Extension groups
$extensionGroups = @{
    "Documents" = @(".txt", ".doc", ".docx", ".pdf", ".rtf", ".odt")
    "Spreadsheets" = @(".xls", ".xlsx", ".csv", ".ods")
    "Presentations" = @(".ppt", ".pptx", ".odp")
    "Images" = @(".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".tif", ".webp")
    "Videos" = @(".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm")
    "Audio" = @(".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma")
    "Archives" = @(".zip", ".rar", ".7z", ".tar", ".gz", ".bz2")
    "Code" = @(".cs", ".js", ".py", ".java", ".cpp", ".h", ".ts", ".go", ".rs")
    "Web" = @(".html", ".htm", ".css", ".scss", ".json", ".xml")
    "Data" = @(".yaml", ".yml", ".toml", ".ini", ".cfg")
    "Other" = @(".log", ".tmp", ".bak", ".dat", ".bin")
}

$allExtensions = $extensionGroups.Values | ForEach-Object { $_ }

# Age ranges (realistic distribution)
$ageRanges = @(
    @{Name = "Now";       Minutes = 0;      Count = 50},
    @{Name = "2min";      Minutes = 2;      Count = 40},
    @{Name = "5min";      Minutes = 5;      Count = 40},
    @{Name = "10min";     Minutes = 10;     Count = 40},
    @{Name = "15min";     Minutes = 15;     Count = 40},
    @{Name = "30min";     Minutes = 30;     Count = 50},
    @{Name = "45min";     Minutes = 45;     Count = 40},
    @{Name = "1hour";     Minutes = 60;     Count = 50},
    @{Name = "2hours";    Minutes = 120;    Count = 50},
    @{Name = "6hours";    Minutes = 360;    Count = 50},
    @{Name = "12hours";   Minutes = 720;    Count = 50},
    @{Name = "1day";      Minutes = 1440;   Count = 60},
    @{Name = "2days";     Minutes = 2880;   Count = 50},
    @{Name = "3days";     Minutes = 4320;   Count = 50},
    @{Name = "5days";     Minutes = 7200;   Count = 40},
    @{Name = "7days";     Minutes = 10080;  Count = 60},
    @{Name = "14days";    Minutes = 20160;  Count = 50},
    @{Name = "21days";    Minutes = 30240;  Count = 40},
    @{Name = "30days";    Minutes = 43200;  Count = 60},
    @{Name = "45days";    Minutes = 64800;  Count = 40},
    @{Name = "60days";    Minutes = 86400;  Count = 40},
    @{Name = "90days";    Minutes = 129600; Count = 50},
    @{Name = "180days";   Minutes = 259200; Count = 30},
    @{Name = "365days";   Minutes = 525600; Count = 20}
)

# ====================================================================================
# Create Folder Structure
# ====================================================================================

Write-Section "Creating Folder Structure"

foreach ($folderName in $folders.Keys | Sort-Object) {
    $path = $folders[$folderName]
    if (Test-Path $path) {
        Remove-Item -Path "$path\*" -Force -ErrorAction SilentlyContinue
        Write-Info "Cleaned existing folder: $folderName"
    } else {
        New-Item -Path $path -ItemType Directory -Force | Out-Null
        Write-Success "Created folder: $folderName"
    }
}

# ====================================================================================
# Generate Test Files + Manifest
# ====================================================================================

Write-Section "Generating $TotalFiles Test Files"

$fileCounter = 0
$manifest = @()
$totalWeight = ($ageRanges | Measure-Object -Property Count -Sum).Sum

foreach ($range in $ageRanges) {
    $targetForRange = [Math]::Round(($range.Count / $totalWeight) * $TotalFiles)

    Write-Info "Age: $($range.Name.PadRight(10)) ($($range.Minutes.ToString().PadLeft(6)) min) - $targetForRange files"

    for ($i = 0; $i -lt $targetForRange; $i++) {
        # Select random extension and group
        $extGroup = $extensionGroups.Keys | Get-Random
        $ext = $extensionGroups[$extGroup] | Get-Random

        # Generate file name
        $fileName = "test_{0:D4}_{1}_{2}{3}" -f $fileCounter, $range.Name, $extGroup, $ext
        $filePath = Join-Path $folders["Source"] $fileName

        # Generate random content (100-5000 bytes)
        $contentSize = Get-Random -Minimum 100 -Maximum 5000
        $content = -join ((1..$contentSize) | ForEach-Object { [char](Get-Random -Minimum 32 -Maximum 127) })

        # Create file
        Set-Content -Path $filePath -Value $content -NoNewline

        # Calculate timestamp (current time minus age)
        $baseTimestamp = (Get-Date).AddMinutes(-$range.Minutes)

        # Get file object
        $file = Get-Item $filePath

        # Randomize timestamp patterns (5 scenarios)
        $timestampPattern = Get-Random -Minimum 1 -Maximum 6

        switch ($timestampPattern) {
            1 {
                # All timestamps the same (40%)
                $file.CreationTime = $baseTimestamp
                $file.LastWriteTime = $baseTimestamp
                $file.LastAccessTime = $baseTimestamp
            }
            2 {
                # Created old, modified recently (20%)
                $file.CreationTime = $baseTimestamp
                $file.LastWriteTime = (Get-Date).AddMinutes(-(Get-Random -Minimum 1 -Maximum 60))
                $file.LastAccessTime = (Get-Date).AddMinutes(-(Get-Random -Minimum 1 -Maximum 30))
            }
            3 {
                # Created recently, modified old (10%)
                if ($range.Minutes -gt 60) {
                    $file.CreationTime = (Get-Date).AddMinutes(-(Get-Random -Minimum 1 -Maximum 60))
                    $file.LastWriteTime = $baseTimestamp
                    $file.LastAccessTime = $baseTimestamp
                } else {
                    $file.CreationTime = $baseTimestamp
                    $file.LastWriteTime = $baseTimestamp
                    $file.LastAccessTime = $baseTimestamp
                }
            }
            4 {
                # Modified and accessed recently, created old (20%)
                $file.CreationTime = $baseTimestamp
                $file.LastWriteTime = (Get-Date).AddMinutes(-(Get-Random -Minimum 1 -Maximum 120))
                $file.LastAccessTime = (Get-Date).AddMinutes(-(Get-Random -Minimum 1 -Maximum 60))
            }
            5 {
                # All different times (10%)
                $file.CreationTime = $baseTimestamp
                $file.LastWriteTime = $baseTimestamp.AddMinutes((Get-Random -Minimum 1 -Maximum [Math]::Min(60, $range.Minutes)))
                $file.LastAccessTime = $baseTimestamp.AddMinutes((Get-Random -Minimum 1 -Maximum [Math]::Min(120, $range.Minutes)))
            }
        }

        # Refresh file info
        $file = Get-Item $filePath

        # Add to manifest
        $manifest += [PSCustomObject]@{
            FileName = $fileName
            Extension = $ext
            ExtensionGroup = $extGroup
            Size = $file.Length
            CreationTime = $file.CreationTime.ToString("yyyy-MM-ddTHH:mm:ss")
            LastWriteTime = $file.LastWriteTime.ToString("yyyy-MM-ddTHH:mm:ss")
            LastAccessTime = $file.LastAccessTime.ToString("yyyy-MM-ddTHH:mm:ss")
            AgeMinutes = $range.Minutes
            TimestampPattern = $timestampPattern
        }

        $fileCounter++

        # Progress indicator
        if ($fileCounter % 100 -eq 0) {
            Write-Host "." -NoNewline -ForegroundColor Gray
        }
    }
}

Write-Host ""
Write-Success "Created $fileCounter test files"

# ====================================================================================
# Generate Manifest YAML
# ====================================================================================

Write-Section "Generating Manifest"

$yamlContent = @"
# ====================================================================================
# RJAutoMover Test Files Manifest
# ====================================================================================
# Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
# Total Files: $fileCounter
# Base Folder: $BaseFolder
# ====================================================================================

TestMetadata:
  GeneratedDate: "$(Get-Date -Format "yyyy-MM-ddTHH:mm:ss")"
  TotalFiles: $fileCounter
  BaseFolder: "$BaseFolder"
  SourceFolder: "$($folders["Source"])"

TestFiles:
"@

foreach ($file in $manifest) {
    $yamlContent += @"

  - FileName: "$($file.FileName)"
    Extension: "$($file.Extension)"
    ExtensionGroup: "$($file.ExtensionGroup)"
    Size: $($file.Size)
    CreationTime: "$($file.CreationTime)"
    LastWriteTime: "$($file.LastWriteTime)"
    LastAccessTime: "$($file.LastAccessTime)"
    AgeMinutes: $($file.AgeMinutes)
    TimestampPattern: $($file.TimestampPattern)
"@
}

# Save manifest
$manifestPath = Join-Path $testRoot "test-files-manifest.yaml"
Set-Content -Path $manifestPath -Value $yamlContent -Encoding UTF8
Write-Success "Manifest saved: $manifestPath"

# ====================================================================================
# Summary
# ====================================================================================

Write-Section "Summary"
Write-Success "Step 1 Complete"
Write-Info "Test Root: $testRoot"
Write-Info "Files created: $fileCounter"
Write-Info "Source folder: $($folders["Source"])"
Write-Info "Manifest: $manifestPath"
Write-Info ""
Write-Info "IMPORTANT: Copy this test root path for use in subsequent steps:"
Write-Host "   $testRoot" -ForegroundColor White
Write-Info ""
Write-Info "Next step: Run 2-Generate-TestConfig.ps1 -TestRoot '$testRoot'"
