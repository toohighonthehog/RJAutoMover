# ================================================================================
# RJAutoMover Comprehensive Test Data Generator
# ================================================================================
# Creates 1000 test files with various ages, extensions, and timestamp variations
# to test all date filtering scenarios.
#
# Usage:
#   .\Create-TestFiles.ps1
#
# Output:
#   Creates C:\RJAutoMover_TestData\ with:
#   - Source\ folder containing 1000 test files
#   - Destination\ folders for various test scenarios
#   - test-config.yaml for testing (using new DateFilter format)
# ================================================================================

param(
    [string]$BaseFolder = "C:\RJAutoMover_TestData",
    [int]$TotalFiles = 1000
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

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "============================================================================" -ForegroundColor Yellow
    Write-Host " $Title" -ForegroundColor Yellow
    Write-Host "============================================================================" -ForegroundColor Yellow
}

# ================================================================================
# Main Script
# ================================================================================

Write-Section "RJAutoMover Comprehensive Test Data Generator"
Write-Info "Target: $TotalFiles test files"
Write-Info "Location: $BaseFolder"
Write-Info ""

# Define folder structure
$folders = @{
    "Source"                    = "$BaseFolder\Source"
    "DestRecent"                = "$BaseFolder\Destination\Recent"
    "DestOld7Days"              = "$BaseFolder\Destination\Old7Days"
    "DestOld30Days"             = "$BaseFolder\Destination\Old30Days"
    "DestOld90Days"             = "$BaseFolder\Destination\Old90Days"
    "DestVideos"                = "$BaseFolder\Destination\Videos"
    "DestImages"                = "$BaseFolder\Destination\Images"
    "DestDocuments"             = "$BaseFolder\Destination\Documents"
    "DestArchives"              = "$BaseFolder\Destination\Archives"
    "DestCode"                  = "$BaseFolder\Destination\Code"
    "DestOthers"                = "$BaseFolder\Destination\Others"
}

# Define file extensions to test (comprehensive list)
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

# Flatten extensions
$allExtensions = $extensionGroups.Values | ForEach-Object { $_ }

# Define age ranges (in minutes) - comprehensive coverage
$ageRanges = @(
    @{Name = "Now";             Minutes = 0;        Count = 50},
    @{Name = "2min";            Minutes = 2;        Count = 40},
    @{Name = "5min";            Minutes = 5;        Count = 40},
    @{Name = "10min";           Minutes = 10;       Count = 40},
    @{Name = "15min";           Minutes = 15;       Count = 40},
    @{Name = "30min";           Minutes = 30;       Count = 50},
    @{Name = "45min";           Minutes = 45;       Count = 40},
    @{Name = "1hour";           Minutes = 60;       Count = 50},
    @{Name = "2hours";          Minutes = 120;      Count = 50},
    @{Name = "6hours";          Minutes = 360;      Count = 50},
    @{Name = "12hours";         Minutes = 720;      Count = 50},
    @{Name = "1day";            Minutes = 1440;     Count = 60},
    @{Name = "2days";           Minutes = 2880;     Count = 50},
    @{Name = "3days";           Minutes = 4320;     Count = 50},
    @{Name = "5days";           Minutes = 7200;     Count = 40},
    @{Name = "7days";           Minutes = 10080;    Count = 60},
    @{Name = "14days";          Minutes = 20160;    Count = 50},
    @{Name = "21days";          Minutes = 30240;    Count = 40},
    @{Name = "30days";          Minutes = 43200;    Count = 60},
    @{Name = "45days";          Minutes = 64800;    Count = 40},
    @{Name = "60days";          Minutes = 86400;    Count = 40},
    @{Name = "90days";          Minutes = 129600;   Count = 50},
    @{Name = "180days";         Minutes = 259200;   Count = 30},
    @{Name = "365days";         Minutes = 525600;   Count = 20}
)

# ================================================================================
# Step 1: Create Folder Structure
# ================================================================================

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

# ================================================================================
# Step 2: Generate Test Files
# ================================================================================

Write-Section "Generating $TotalFiles Test Files"

$fileCounter = 0
$filesPerAgeRange = @{}
$filesPerExtensionGroup = @{}

# Initialize counters
foreach ($range in $ageRanges) {
    $filesPerAgeRange[$range.Name] = 0
}
foreach ($group in $extensionGroups.Keys) {
    $filesPerExtensionGroup[$group] = 0
}

Write-Info "Distributing files across $($ageRanges.Count) age ranges..."
Write-Info "Using $($allExtensions.Count) different file extensions..."
Write-Info ""

# Calculate files to create per age range
$totalWeight = ($ageRanges | Measure-Object -Property Count -Sum).Sum
$filesCreated = 0

foreach ($range in $ageRanges) {
    $targetForRange = [Math]::Round(($range.Count / $totalWeight) * $TotalFiles)

    Write-Info "Age: $($range.Name.PadRight(10)) ($($range.Minutes.ToString().PadLeft(6)) min) - $targetForRange files"

    for ($i = 0; $i -lt $targetForRange; $i++) {
        # Select random extension and group
        $extGroup = $extensionGroups.Keys | Get-Random
        $ext = $extensionGroups[$extGroup] | Get-Random

        # Generate file name with age indicator
        $fileName = "test_{0:D4}_{1}_{2}{3}" -f $fileCounter, $range.Name, $extGroup, $ext
        $filePath = Join-Path $folders["Source"] $fileName

        # Generate random test content (random size between 100-5000 bytes)
        $contentSize = Get-Random -Minimum 100 -Maximum 5000
        $content = -join ((1..$contentSize) | ForEach-Object {
            [char](Get-Random -Minimum 32 -Maximum 127)
        })

        # Create file
        Set-Content -Path $filePath -Value $content -NoNewline

        # Calculate timestamp
        $baseTimestamp = (Get-Date).AddMinutes(-$range.Minutes)

        # Get file object
        $file = Get-Item $filePath

        # For variety, randomize timestamp patterns (5 different scenarios)
        $timestampPattern = Get-Random -Minimum 1 -Maximum 6

        switch ($timestampPattern) {
            1 {
                # Pattern 1: All timestamps the same (40% of files)
                $file.CreationTime = $baseTimestamp
                $file.LastWriteTime = $baseTimestamp
                $file.LastAccessTime = $baseTimestamp
            }
            2 {
                # Pattern 2: Created old, modified recently (20% of files)
                $file.CreationTime = $baseTimestamp
                $recentMod = (Get-Date).AddMinutes(-(Get-Random -Minimum 1 -Maximum 30))
                $file.LastWriteTime = $recentMod
                $file.LastAccessTime = (Get-Date).AddMinutes(-(Get-Random -Minimum 1 -Maximum 10))
            }
            3 {
                # Pattern 3: Created old, accessed recently (20% of files)
                $file.CreationTime = $baseTimestamp
                $file.LastWriteTime = $baseTimestamp
                $file.LastAccessTime = (Get-Date).AddMinutes(-(Get-Random -Minimum 1 -Maximum 60))
            }
            4 {
                # Pattern 4: All different timestamps (10% of files)
                $file.CreationTime = $baseTimestamp
                if ($range.Minutes -gt 0) {
                    $modOffset = Get-Random -Minimum 1 -Maximum ([Math]::Max(2, [Math]::Floor($range.Minutes / 2)))
                    $accOffset = Get-Random -Minimum 1 -Maximum ([Math]::Max(2, [Math]::Floor($range.Minutes / 3)))
                    $file.LastWriteTime = $baseTimestamp.AddMinutes($modOffset)
                    $file.LastAccessTime = $baseTimestamp.AddMinutes($accOffset)
                } else {
                    $file.LastWriteTime = $baseTimestamp
                    $file.LastAccessTime = $baseTimestamp
                }
            }
            5 {
                # Pattern 5: Created recently, modified old (10% of files - unusual but valid)
                if ($range.Minutes -gt 60) {
                    $file.CreationTime = (Get-Date).AddMinutes(-(Get-Random -Minimum 1 -Maximum 60))
                    $file.LastWriteTime = $baseTimestamp
                    $file.LastAccessTime = $baseTimestamp
                } else {
                    # Fall back to pattern 1 for recent files
                    $file.CreationTime = $baseTimestamp
                    $file.LastWriteTime = $baseTimestamp
                    $file.LastAccessTime = $baseTimestamp
                }
            }
        }

        $fileCounter++
        $filesCreated++
        $filesPerAgeRange[$range.Name]++
        $filesPerExtensionGroup[$extGroup]++

        # Progress indicator
        if ($fileCounter % 100 -eq 0) {
            Write-Host "." -NoNewline -ForegroundColor Gray
        }
    }
}

Write-Host ""
Write-Success "Created $filesCreated test files"
Write-Info ""

# Display summary by age
Write-Info "Files by age range:"
foreach ($range in $ageRanges | Sort-Object Minutes) {
    $count = $filesPerAgeRange[$range.Name]
    if ($count -gt 0) {
        $ageDesc = if ($range.Minutes -eq 0) { "now" } else { "$($range.Minutes) min ago" }
        Write-Host ("  {0,-12} ({1,-15}) : {2,4} files" -f $range.Name, $ageDesc, $count) -ForegroundColor White
    }
}

Write-Info ""
Write-Info "Files by type:"
foreach ($group in $filesPerExtensionGroup.Keys | Sort-Object) {
    $count = $filesPerExtensionGroup[$group]
    Write-Host ("  {0,-15} : {1,4} files" -f $group, $count) -ForegroundColor White
}

# ================================================================================
# Step 3: Create Test Config File (NEW DateFilter format)
# ================================================================================

Write-Section "Creating Test Configuration File"

$configPath = Join-Path $BaseFolder "test-config.yaml"

$configContent = @"
# ==============================================================================
# RJAutoMover COMPREHENSIVE TEST Configuration
# ==============================================================================
# This configuration tests ALL date filtering scenarios using the new DateFilter
# format: "TYPE:SIGN:MINUTES"
#
# DateFilter Format:
# - TYPE: LA (Last Accessed), LM (Last Modified), FC (File Created)
# - SIGN: + (older than), - (within last)
# - MINUTES: 1-5256000 (up to 10 years)
#
# Examples:
# - "LA:+43200" = Files NOT accessed in last 30 days (older files)
# - "LA:-1440" = Files accessed within last 1 day (recent files)
# - "LM:+10080" = Files NOT modified in last 7 days
# - "FC:+4320" = Files created more than 3 days ago
# ==============================================================================

FileRules:
  # ============================================================================
  # TEST 1: Recent Files (Within Last 1 Hour) - NEGATIVE LastAccessed
  # ============================================================================
  # Tests: LA:-60 (files accessed within last 60 minutes)
  # Expected: Files with recent access times
  - Name: Test-RecentAccess
    SourceFolder: $($folders["Source"])
    Extension: .txt|.doc|.docx|.pdf
    DestinationFolder: $($folders["DestRecent"])
    ScanIntervalMs: 15000
    IsActive: false
    FileExists: skip
    DateFilter: "LA:-60"  # Within last hour

  # ============================================================================
  # TEST 2: Old Documents (Older Than 7 Days) - POSITIVE FileCreated
  # ============================================================================
  # Tests: FC:+10080 (files created more than 7 days ago)
  # Expected: Files from "7 days ago" range and older
  - Name: Test-OldDocs7Days
    SourceFolder: $($folders["Source"])
    Extension: .txt|.doc|.docx|.rtf|.odt|.pdf
    DestinationFolder: $($folders["DestOld7Days"])
    ScanIntervalMs: 20000
    IsActive: false
    FileExists: skip
    DateFilter: "FC:+10080"  # Older than 7 days

  # ============================================================================
  # TEST 3: Very Old Files (Older Than 30 Days) - POSITIVE FileCreated
  # ============================================================================
  # Tests: FC:+43200 (files created more than 30 days ago)
  # Expected: Files from "30 days ago" range and older
  - Name: Test-VeryOld30Days
    SourceFolder: $($folders["Source"])
    Extension: .xls|.xlsx|.csv|.ppt|.pptx
    DestinationFolder: $($folders["DestOld30Days"])
    ScanIntervalMs: 30000
    IsActive: false
    FileExists: skip
    DateFilter: "FC:+43200"  # Older than 30 days

  # ============================================================================
  # TEST 4: Ancient Files (Older Than 90 Days) - POSITIVE FileCreated
  # ============================================================================
  # Tests: FC:+129600 (files created more than 90 days ago)
  # Expected: Files from "90 days ago" range and older
  - Name: Test-Ancient90Days
    SourceFolder: $($folders["Source"])
    Extension: .log|.tmp|.bak
    DestinationFolder: $($folders["DestOld90Days"])
    ScanIntervalMs: 40000
    IsActive: false
    FileExists: skip
    DateFilter: "FC:+129600"  # Older than 90 days

  # ============================================================================
  # TEST 5: Recently Modified Videos (Within Last 2 Hours) - NEGATIVE LastModified
  # ============================================================================
  # Tests: LM:-120 (files modified within last 2 hours)
  # Expected: Video files with recent modifications
  - Name: Test-RecentModVideos
    SourceFolder: $($folders["Source"])
    Extension: .mp4|.avi|.mkv|.mov|.wmv|.flv
    DestinationFolder: $($folders["DestVideos"])
    ScanIntervalMs: 25000
    IsActive: false
    FileExists: skip
    DateFilter: "LM:-120"  # Within last 2 hours

  # ============================================================================
  # TEST 6: Old Images (Created Older Than 1 Day) - POSITIVE FileCreated
  # ============================================================================
  # Tests: FC:+1440 (files created more than 1 day ago)
  # Expected: Images from "1 day ago" range and older
  - Name: Test-OldImages1Day
    SourceFolder: $($folders["Source"])
    Extension: .jpg|.jpeg|.png|.gif|.bmp|.svg|.webp
    DestinationFolder: $($folders["DestImages"])
    ScanIntervalMs: 20000
    IsActive: false
    FileExists: skip
    DateFilter: "FC:+1440"  # Older than 1 day

  # ============================================================================
  # TEST 7: Stale Documents (Not Modified in Last 14 Days) - POSITIVE LastModified
  # ============================================================================
  # Tests: LM:+20160 (files NOT modified in last 14 days)
  # Expected: Documents with no recent edits
  - Name: Test-StaleDocsNoMod14Days
    SourceFolder: $($folders["Source"])
    Extension: .doc|.docx|.odt|.rtf
    DestinationFolder: $($folders["DestDocuments"])
    ScanIntervalMs: 35000
    IsActive: false
    FileExists: skip
    DateFilter: "LM:+20160"  # Older than 14 days (not modified)

  # ============================================================================
  # TEST 8: Old Archives (Created Older Than 60 Days) - POSITIVE FileCreated
  # ============================================================================
  # Tests: FC:+86400 (files created more than 60 days ago)
  # Expected: Archive files from "60 days ago" range and older
  - Name: Test-OldArchives60Days
    SourceFolder: $($folders["Source"])
    Extension: .zip|.rar|.7z|.tar|.gz|.bz2
    DestinationFolder: $($folders["DestArchives"])
    ScanIntervalMs: 45000
    IsActive: false
    FileExists: skip
    DateFilter: "FC:+86400"  # Older than 60 days

  # ============================================================================
  # TEST 9: Stale Code (Not Accessed in Last 30 Days) - POSITIVE LastAccessed
  # ============================================================================
  # Tests: LA:+43200 (files NOT accessed in last 30 days)
  # Expected: Code files that haven't been opened recently
  - Name: Test-StaleCodeNoAccess30Days
    SourceFolder: $($folders["Source"])
    Extension: .cs|.js|.py|.java|.cpp|.h|.ts|.go
    DestinationFolder: $($folders["DestCode"])
    ScanIntervalMs: 50000
    IsActive: false
    FileExists: skip
    DateFilter: "LA:+43200"  # Older than 30 days (not accessed)

  # ============================================================================
  # TEST 10: Catch-All for Very Old Files (OTHERS with Date Filter)
  # ============================================================================
  # Tests: Extension "OTHERS" with FC:+259200 (created more than 180 days ago)
  # Expected: Any file type not caught by other rules, older than 180 days
  # NOTE: OTHERS rules MUST have a DateFilter
  - Name: Test-OthersVeryOld180Days
    SourceFolder: $($folders["Source"])
    Extension: OTHERS
    DestinationFolder: $($folders["DestOthers"])
    ScanIntervalMs: 60000
    IsActive: false
    FileExists: skip
    DateFilter: "FC:+259200"  # Older than 180 days

# ==============================================================================
# APPLICATION SETTINGS
# ==============================================================================

Application:
  ProcessingPaused: false
  RequireTrayApproval: false
  RetryDelayMs: 5000
  FailureCooldownMs: 180000
  RecheckServiceMs: 30000
  RecheckTrayMs: 30000
  PauseDelayMs: 1000
  ServiceHeartbeatMs: 300000
  MemoryLimitMb: 512
  MemoryCheckMs: 60000
  LogFolder: C:\ProgramData\RJAutoMover\Logs
  LogRetentionDays: 7
  ServiceGrpcPort: 60051
  TrayGrpcPort: 60052
  ActivityHistoryEnabled: true
  ActivityHistoryMaxRecords: 5000
  ActivityHistoryRetentionDays: 90

# ==============================================================================
# TESTING INSTRUCTIONS
# ==============================================================================
# 1. Enable ONE rule at a time (change IsActive: false to IsActive: true)
# 2. Restart RJAutoMoverService
# 3. Wait for scan interval
# 4. Check destination folder for moved files
# 5. Run Analyze-TestResults.ps1 to verify correctness
# 6. Review logs for detailed date filtering output
# ==============================================================================
"@

Set-Content -Path $configPath -Value $configContent
Write-Success "Created test configuration: $configPath"

# ================================================================================
# Step 4: Generate File Summary CSV
# ================================================================================

Write-Section "Generating File Summary CSV"

$summaryPath = Join-Path $BaseFolder "FILE_SUMMARY.csv"

Write-Info "Collecting metadata for $filesCreated files..."

$files = Get-ChildItem -Path $folders["Source"] -File | Select-Object Name,
    @{Name='Extension';Expression={$_.Extension}},
    @{Name='SizeBytes';Expression={$_.Length}},
    @{Name='Created';Expression={$_.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")}},
    @{Name='Modified';Expression={$_.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")}},
    @{Name='Accessed';Expression={$_.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss")}},
    @{Name='CreatedMinsAgo';Expression={[Math]::Round((New-TimeSpan -Start $_.CreationTime -End (Get-Date)).TotalMinutes, 1)}},
    @{Name='ModifiedMinsAgo';Expression={[Math]::Round((New-TimeSpan -Start $_.LastWriteTime -End (Get-Date)).TotalMinutes, 1)}},
    @{Name='AccessedMinsAgo';Expression={[Math]::Round((New-TimeSpan -Start $_.LastAccessTime -End (Get-Date)).TotalMinutes, 1)}}

$files | Export-Csv -Path $summaryPath -NoTypeInformation
Write-Success "Created file summary: $summaryPath ($($files.Count) files)"

# ================================================================================
# Final Summary
# ================================================================================

Write-Section "Test Data Generation Complete"

Write-Success "Test environment ready!"
Write-Info ""
Write-Info "Location:          $BaseFolder"
Write-Info "Total Files:       $filesCreated"
Write-Info "Total Size:        $([Math]::Round($filesCreated * 2.5KB / 1MB, 2)) MB (approx)"
Write-Info "Age Ranges:        $($ageRanges.Count) different time periods"
Write-Info "File Types:        $($allExtensions.Count) different extensions"
Write-Info ""
Write-Success "Files Created:"
Write-Host "  Source Folder:     $($folders['Source'])" -ForegroundColor White
Write-Host "  Test Config:       $configPath" -ForegroundColor White
Write-Host "  File Summary:      $summaryPath" -ForegroundColor White
Write-Info ""

Write-Section "Next Steps"

Write-Host @"
1. Review test files:     explorer "$($folders['Source'])"
2. Review test config:    notepad "$configPath"
3. Review file summary:   "$summaryPath"

To run tests:
-------------
1. Copy test-config.yaml to C:\Program Files\RJAutoMover\config.yaml
2. Enable ONE rule at a time (IsActive: true)
3. Restart RJAutoMoverService
4. Wait for scan interval
5. Run .\Analyze-TestResults.ps1 to verify results

For detailed testing instructions, see the README files in TestPlans folder.
"@ -ForegroundColor Cyan

Write-Info ""
