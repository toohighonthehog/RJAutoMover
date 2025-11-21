# ================================================================================
# RJAutoMover Date Filtering Test Data Generator
# ================================================================================
# This script creates hundreds of test files with various ages and file types
# for testing the date-based filtering functionality.
#
# Usage:
#   .\Create-TestFiles.ps1
#
# Output:
#   Creates C:\RJAutoMover_TestData\ with:
#   - Source\ folder containing test files
#   - Destination\ folders for various test scenarios
#   - test-config.yaml for testing
# ================================================================================

# Requires -RunAsAdministrator
param(
    [string]$BaseFolder = "C:\RJAutoMover_TestData",
    [int]$TotalFiles = 300
)

# Color output functions
function Write-Success {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host "→ $Message" -ForegroundColor Cyan
}

function Write-Error {
    param([string]$Message)
    Write-Host "✗ $Message" -ForegroundColor Red
}

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host " $Title" -ForegroundColor Yellow
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Yellow
}

# ================================================================================
# Main Script
# ================================================================================

Write-Section "RJAutoMover Test Data Generator"

# Define folder structure
$folders = @{
    "Source"                    = "$BaseFolder\Source"
    "DestOldFiles"              = "$BaseFolder\Destination\OldFiles"
    "DestRecentFiles"           = "$BaseFolder\Destination\RecentFiles"
    "DestArchive"               = "$BaseFolder\Destination\Archive"
    "DestDocuments"             = "$BaseFolder\Destination\Documents"
    "DestVideos"                = "$BaseFolder\Destination\Videos"
    "DestImages"                = "$BaseFolder\Destination\Images"
    "DestAllOld"                = "$BaseFolder\Destination\AllOld"
}

# Define file extensions to test
$extensions = @(
    # Documents
    ".txt", ".doc", ".docx", ".pdf", ".rtf", ".odt",
    # Spreadsheets
    ".xls", ".xlsx", ".csv", ".ods",
    # Presentations
    ".ppt", ".pptx",
    # Images
    ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg",
    # Videos
    ".mp4", ".avi", ".mkv", ".mov", ".wmv",
    # Archives
    ".zip", ".rar", ".7z", ".tar", ".gz",
    # Code
    ".cs", ".js", ".py", ".java", ".cpp", ".h",
    # Data
    ".json", ".xml", ".yaml", ".yml",
    # Other
    ".log", ".tmp", ".bak"
)

# Define age ranges (in minutes)
$ageRanges = @(
    @{Name = "Just Now";        Minutes = 0;          Count = 20},
    @{Name = "5 min ago";       Minutes = 5;          Count = 20},
    @{Name = "15 min ago";      Minutes = 15;         Count = 20},
    @{Name = "30 min ago";      Minutes = 30;         Count = 20},
    @{Name = "1 hour ago";      Minutes = 60;         Count = 20},
    @{Name = "2 hours ago";     Minutes = 120;        Count = 20},
    @{Name = "6 hours ago";     Minutes = 360;        Count = 20},
    @{Name = "12 hours ago";    Minutes = 720;        Count = 20},
    @{Name = "1 day ago";       Minutes = 1440;       Count = 30},
    @{Name = "3 days ago";      Minutes = 4320;       Count = 30},
    @{Name = "7 days ago";      Minutes = 10080;      Count = 30},
    @{Name = "14 days ago";     Minutes = 20160;      Count = 20},
    @{Name = "30 days ago";     Minutes = 43200;      Count = 20},
    @{Name = "60 days ago";     Minutes = 86400;      Count = 15},
    @{Name = "90 days ago";     Minutes = 129600;     Count = 15}
)

# ================================================================================
# Step 1: Create Folder Structure
# ================================================================================

Write-Section "Creating Folder Structure"

foreach ($folderName in $folders.Keys) {
    $path = $folders[$folderName]
    if (Test-Path $path) {
        Write-Info "Folder already exists: $path"
    } else {
        New-Item -Path $path -ItemType Directory -Force | Out-Null
        Write-Success "Created folder: $path"
    }
}

# ================================================================================
# Step 2: Generate Test Files
# ================================================================================

Write-Section "Generating Test Files"

$fileCounter = 0
$filesPerAgeRange = @{}

# Initialize counters
foreach ($range in $ageRanges) {
    $filesPerAgeRange[$range.Name] = 0
}

Write-Info "Target: $TotalFiles files across all age ranges"
Write-Info ""

# Calculate files to create per age range (proportional to Count property)
$totalWeight = ($ageRanges | Measure-Object -Property Count -Sum).Sum
$filesCreated = 0

foreach ($range in $ageRanges) {
    $targetForRange = [Math]::Floor(($range.Count / $totalWeight) * $TotalFiles)

    Write-Info "Creating files for age: $($range.Name) ($targetForRange files)"

    for ($i = 0; $i -lt $targetForRange; $i++) {
        # Select random extension
        $ext = $extensions | Get-Random

        # Generate file name
        $fileName = "test_file_{0:D4}_{1:D3}{2}" -f $fileCounter, $range.Minutes, $ext
        $filePath = Join-Path $folders["Source"] $fileName

        # Generate random test content (1000 bytes)
        $content = -join ((1..1000) | ForEach-Object {
            [char](Get-Random -Minimum 32 -Maximum 127)
        })

        # Create file
        Set-Content -Path $filePath -Value $content -NoNewline

        # Calculate timestamp
        $timestamp = (Get-Date).AddMinutes(-$range.Minutes)

        # Set file timestamps
        $file = Get-Item $filePath

        # For variety, randomize which timestamp to use
        $timestampVariation = Get-Random -Minimum 1 -Maximum 4

        switch ($timestampVariation) {
            1 {
                # All timestamps the same (common case)
                $file.CreationTime = $timestamp
                $file.LastWriteTime = $timestamp
                $file.LastAccessTime = $timestamp
            }
            2 {
                # Created old, modified recently
                $file.CreationTime = $timestamp
                $file.LastWriteTime = $timestamp.AddMinutes([Math]::Abs($range.Minutes) / 2)
                $file.LastAccessTime = (Get-Date).AddMinutes(-5)
            }
            3 {
                # Created old, accessed recently
                $file.CreationTime = $timestamp
                $file.LastWriteTime = $timestamp
                $file.LastAccessTime = (Get-Date).AddMinutes(-10)
            }
            4 {
                # All different timestamps
                $file.CreationTime = $timestamp
                $file.LastWriteTime = $timestamp.AddMinutes([Math]::Abs($range.Minutes) / 3)
                $file.LastAccessTime = $timestamp.AddMinutes([Math]::Abs($range.Minutes) / 2)
            }
        }

        $fileCounter++
        $filesCreated++
        $filesPerAgeRange[$range.Name]++

        # Progress indicator
        if ($fileCounter % 50 -eq 0) {
            Write-Host "." -NoNewline -ForegroundColor Gray
        }
    }
}

Write-Host ""
Write-Success "Created $filesCreated test files"
Write-Info ""

# Display summary
Write-Info "Files by age range:"
foreach ($range in $ageRanges) {
    $count = $filesPerAgeRange[$range.Name]
    if ($count -gt 0) {
        Write-Host "  $($range.Name.PadRight(20)) : $count files" -ForegroundColor White
    }
}

# ================================================================================
# Step 3: Create Test Config File
# ================================================================================

Write-Section "Creating Test Configuration File"

$configPath = Join-Path $BaseFolder "test-config.yaml"

$configContent = @"
# ==============================================================================
# RJAutoMover TEST Configuration File
# ==============================================================================
# This is a TEST configuration for validating date-based filtering functionality.
# DO NOT use this in production!
#
# Test Scenario:
# - Rule 1: Move recent documents (modified within last 30 minutes) -> RecentFiles
# - Rule 2: Move old documents (created more than 7 days ago) -> OldFiles
# - Rule 3: Move video files (created more than 1 day ago) -> Videos
# - Rule 4: Move image files (accessed within last 1 hour) -> Images
# - Rule 5: Move PDF files (created more than 3 days ago) -> Documents
# - Rule 6: Catch-all for very old files (created more than 30 days ago) -> AllOld
# ==============================================================================

FileRules:
  # ==============================================================================
  # RULE 1: Process Recently Modified Documents (NEGATIVE criteria - within last)
  # ==============================================================================
  # This rule demonstrates NEGATIVE LastModifiedMins
  # Files modified WITHIN the last 30 minutes will be moved
  - Name: Recent Documents
    SourceFolder: $($folders["Source"])
    Extension: .txt|.doc|.docx|.pdf|.rtf
    DestinationFolder: $($folders["DestRecentFiles"])
    ScanIntervalMs: 15000
    IsActive: false
    FileExists: skip
    LastModifiedMins: -30  # NEGATIVE = within last 30 minutes

  # ==============================================================================
  # RULE 2: Archive Old Documents (POSITIVE criteria - older than)
  # ==============================================================================
  # This rule demonstrates POSITIVE AgeCreatedMins
  # Files created MORE than 7 days ago will be archived
  - Name: Archive Old Documents
    SourceFolder: $($folders["Source"])
    Extension: .txt|.doc|.docx|.rtf|.odt
    DestinationFolder: $($folders["DestOldFiles"])
    ScanIntervalMs: 30000
    IsActive: false
    FileExists: skip
    AgeCreatedMins: 10080  # POSITIVE = older than 7 days (7 * 24 * 60)

  # ==============================================================================
  # RULE 3: Move Old Video Files (POSITIVE criteria - older than)
  # ==============================================================================
  # Files created MORE than 1 day ago
  - Name: Old Videos
    SourceFolder: $($folders["Source"])
    Extension: .mp4|.avi|.mkv|.mov|.wmv
    DestinationFolder: $($folders["DestVideos"])
    ScanIntervalMs: 30000
    IsActive: false
    FileExists: skip
    AgeCreatedMins: 1440  # POSITIVE = older than 1 day (24 * 60)

  # ==============================================================================
  # RULE 4: Move Recently Accessed Images (NEGATIVE criteria - within last)
  # ==============================================================================
  # Files accessed WITHIN the last 1 hour
  - Name: Recent Images
    SourceFolder: $($folders["Source"])
    Extension: .jpg|.jpeg|.png|.gif|.bmp|.svg
    DestinationFolder: $($folders["DestImages"])
    ScanIntervalMs: 30000
    IsActive: false
    FileExists: skip
    LastAccessedMins: -60  # NEGATIVE = within last 60 minutes

  # ==============================================================================
  # RULE 5: Archive Old PDF Files (POSITIVE criteria - older than)
  # ==============================================================================
  # Files created MORE than 3 days ago
  - Name: Archive Old PDFs
    SourceFolder: $($folders["Source"])
    Extension: .pdf
    DestinationFolder: $($folders["DestDocuments"])
    ScanIntervalMs: 30000
    IsActive: false
    FileExists: skip
    AgeCreatedMins: 4320  # POSITIVE = older than 3 days (3 * 24 * 60)

  # ==============================================================================
  # RULE 6: Catch-All for Very Old Files (ALL extension with date criteria)
  # ==============================================================================
  # This demonstrates an ALL rule (lowest priority - processes last)
  # ALL rules MUST have a date criteria
  # Files created MORE than 30 days ago (any extension not caught by specific rules)
  - Name: Archive Very Old Files
    SourceFolder: $($folders["Source"])
    Extension: ALL
    DestinationFolder: $($folders["DestAllOld"])
    ScanIntervalMs: 60000
    IsActive: false
    FileExists: skip
    AgeCreatedMins: 43200  # POSITIVE = older than 30 days (30 * 24 * 60)

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
#
# 1. Copy this entire folder to your test machine
# 2. Copy test-config.yaml to C:\Program Files\RJAutoMover\config.yaml
# 3. Enable ONE rule at a time by changing IsActive: false to IsActive: true
# 4. Restart RJAutoMoverService
# 5. Wait for scan interval
# 6. Verify files are moved correctly based on date criteria
# 7. Check logs in C:\ProgramData\RJAutoMover\Logs for TRACE-level messages
#
# Example Testing Sequence:
# -------------------------
# Test 1: Enable "Recent Documents" (negative criteria)
#         Expected: Files modified within last 30 min moved to RecentFiles
#
# Test 2: Enable "Archive Old Documents" (positive criteria)
#         Expected: Files created > 7 days ago moved to OldFiles
#
# Test 3: Enable "Old Videos" (positive criteria)
#         Expected: Video files created > 1 day ago moved to Videos
#
# Test 4: Enable "Recent Images" (negative criteria with LastAccessedMins)
#         Expected: Images accessed within last hour moved to Images
#
# Test 5: Enable "Archive Old PDFs" (positive criteria)
#         Expected: PDFs created > 3 days ago moved to Documents
#
# Test 6: Enable "Archive Very Old Files" (ALL rule with date criteria)
#         Expected: All files > 30 days old (not caught by other rules) moved to AllOld
#
# IMPORTANT:
# - Only enable ONE rule at a time for isolated testing
# - Disable all rules before enabling a new one
# - Restart service after each config change
# - Check service logs for detailed TRACE output showing date matching
#
# ==============================================================================
"@

Set-Content -Path $configPath -Value $configContent
Write-Success "Created test configuration: $configPath"

# ================================================================================
# Step 4: Create README
# ================================================================================

Write-Section "Creating Test Instructions"

$readmePath = Join-Path $BaseFolder "TEST_INSTRUCTIONS.txt"

$readmeContent = @"
================================================================================
                    RJAutoMover Date Filtering Test Data
================================================================================

This folder contains test data for validating RJAutoMover's date-based file
filtering functionality (LastAccessedMins, LastModifiedMins, AgeCreatedMins).

================================================================================
FOLDER STRUCTURE
================================================================================

$BaseFolder\
├── Source\                     (Test files with various ages)
├── Destination\
│   ├── OldFiles\              (Files older than 7 days)
│   ├── RecentFiles\           (Files modified within 30 min)
│   ├── Archive\               (General archive)
│   ├── Documents\             (Old PDF files)
│   ├── Videos\                (Old video files)
│   ├── Images\                (Recently accessed images)
│   └── AllOld\                (Catch-all for very old files)
├── test-config.yaml           (Test configuration file)
└── TEST_INSTRUCTIONS.txt      (This file)

================================================================================
TEST FILES SUMMARY
================================================================================

Total Files Created: $filesCreated
File Size: 1000 bytes each
File Types: $($extensions.Count) different extensions
Age Ranges: Just created to 90 days old

Files have varying timestamps:
- Some files: All timestamps identical (Created = Modified = Accessed)
- Some files: Created old, modified recently
- Some files: Created old, accessed recently
- Some files: All timestamps different

This variety ensures comprehensive testing of all three date criteria.

================================================================================
QUICK START TESTING
================================================================================

1. COPY TEST DATA TO TEST MACHINE
   - Copy this entire folder to C:\RJAutoMover_TestData\ on test machine

2. BACKUP CURRENT CONFIG
   - Copy C:\Program Files\RJAutoMover\config.yaml to config.yaml.backup

3. DEPLOY TEST CONFIG
   - Copy test-config.yaml to C:\Program Files\RJAutoMover\config.yaml

4. TEST EACH RULE INDIVIDUALLY
   - Edit config.yaml
   - Enable ONE rule at a time (change IsActive: false to IsActive: true)
   - Save file
   - Restart RJAutoMoverService
   - Wait for scan interval (15-60 seconds depending on rule)
   - Check destination folder for moved files
   - Check logs for TRACE-level date matching output

5. VERIFY RESULTS
   - Open C:\ProgramData\RJAutoMover\Logs\service-*.log
   - Search for "matches" or "does NOT match" to see date filtering in action
   - Verify files in destination folders match expected criteria

================================================================================
TEST SCENARIOS
================================================================================

SCENARIO 1: Negative LastModifiedMins (Within Last 30 Minutes)
---------------------------------------------------------------
Rule: "Recent Documents"
Config: LastModifiedMins: -30
Expected: Files modified WITHIN last 30 minutes moved to RecentFiles
Note: Few files will match (only very recent modifications)

SCENARIO 2: Positive AgeCreatedMins (Older Than 7 Days)
--------------------------------------------------------
Rule: "Archive Old Documents"
Config: AgeCreatedMins: 10080
Expected: .txt/.doc/.docx/.rtf files created MORE than 7 days ago moved to OldFiles
Note: Should move files from "7 days ago", "14 days ago", "30 days ago", etc. ranges

SCENARIO 3: Positive AgeCreatedMins (Older Than 1 Day)
-------------------------------------------------------
Rule: "Old Videos"
Config: AgeCreatedMins: 1440
Expected: Video files created MORE than 1 day ago moved to Videos
Note: Should move videos from all ranges except "Just Now" to "12 hours ago"

SCENARIO 4: Negative LastAccessedMins (Within Last 1 Hour)
-----------------------------------------------------------
Rule: "Recent Images"
Config: LastAccessedMins: -60
Expected: Images accessed WITHIN last 60 minutes moved to Images
Note: Some files have recent access times despite old creation dates

SCENARIO 5: Positive AgeCreatedMins (Older Than 3 Days)
--------------------------------------------------------
Rule: "Archive Old PDFs"
Config: AgeCreatedMins: 4320
Expected: PDF files created MORE than 3 days ago moved to Documents
Note: Should move PDFs from "3 days ago" range and older

SCENARIO 6: ALL Extension Rule (Older Than 30 Days)
----------------------------------------------------
Rule: "Archive Very Old Files"
Config: Extension: ALL, AgeCreatedMins: 43200
Expected: ALL file types created MORE than 30 days ago moved to AllOld
Note: This is a catch-all rule (lowest priority)
      Processes AFTER specific extension rules
      MUST have a date criteria (validates ALL rule requirement)

================================================================================
VALIDATION TESTING
================================================================================

TEST INVALID CONFIGURATIONS:

1. Zero Value (Should FAIL)
   AgeCreatedMins: 0
   Expected: Service enters ERROR mode
   Error: "AgeCreatedMins cannot be zero for rule '...'. Use positive (older than) or negative (within last) values."

2. Out of Range Positive (Should FAIL)
   AgeCreatedMins: 6000000
   Expected: Service enters ERROR mode
   Error: "AgeCreatedMins must be between -5256000 and +5256000 (+/- 10 years) for rule '...' (found: 6000000)"

3. Out of Range Negative (Should FAIL)
   LastModifiedMins: -6000000
   Expected: Service enters ERROR mode
   Error: "LastModifiedMins must be between -5256000 and +5256000 (+/- 10 years) for rule '...' (found: -6000000)"

4. Multiple Date Criteria (Should FAIL)
   LastAccessedMins: 1440
   LastModifiedMins: 60
   Expected: Service enters ERROR mode
   Error: "Rule '...': Only one date criteria can be specified. Found: LastAccessedMins, LastModifiedMins. Remove all but one date criteria."

5. ALL Rule Without Date Criteria (Should FAIL)
   Extension: ALL
   (No date criteria)
   Expected: Service enters ERROR mode
   Error: "Rule '...': Extension 'ALL' rules MUST have a date criteria (LastAccessedMins, LastModifiedMins, or AgeCreatedMins)"

================================================================================
LOG FILE ANALYSIS
================================================================================

Look for these log messages in C:\ProgramData\RJAutoMover\Logs\service-*.log:

SUCCESSFUL MATCH (Positive):
[TRACE] File matches AgeCreatedMins criteria: testfile.txt (created 180.0 min ago, requires >= 120 min [older than])

FAILED MATCH (Positive):
[TRACE] File does NOT match AgeCreatedMins criteria: testfile.txt (created 30.0 min ago, requires >= 120 min [older than])

SUCCESSFUL MATCH (Negative):
[TRACE] File matches LastModifiedMins criteria: testfile.txt (modified 15.0 min ago, requires <= 30 min [within last])

FAILED MATCH (Negative):
[TRACE] File does NOT match LastModifiedMins criteria: testfile.txt (modified 45.0 min ago, requires <= 30 min [within last])

================================================================================
CLEANUP
================================================================================

After testing:

1. Stop RJAutoMoverService
2. Restore original config:
   copy C:\Program Files\RJAutoMover\config.yaml.backup C:\Program Files\RJAutoMover\config.yaml
3. Delete test data:
   rmdir /s /q $BaseFolder
4. Start RJAutoMoverService

================================================================================
TROUBLESHOOTING
================================================================================

Q: No files are being moved
A: - Check service is running (services.msc)
   - Verify rule IsActive: true
   - Check scan interval hasn't been set too high
   - Review service logs for errors

Q: Wrong files are being moved
A: - Verify date criteria logic (positive vs negative)
   - Check file timestamps using PowerShell: Get-Item <file> | Select CreationTime, LastWriteTime, LastAccessTime
   - Review TRACE logs to see which files match criteria

Q: Service won't start after config change
A: - Check Event Viewer -> Application log for errors
   - Verify YAML syntax (indentation, colons, values)
   - Check for validation errors in service logs
   - Common issues: zero values, out of range, multiple criteria

Q: Tray shows "Error" icon
A: - Open AboutWindow
   - Go to Error tab
   - Read detailed validation error message
   - Fix config.yaml accordingly
   - Restart service

================================================================================
EXPECTED FILE DISTRIBUTION
================================================================================

Based on the test data generation:

Just Now (0 min):          ~20 files
5 min ago:                 ~20 files
15 min ago:                ~20 files
30 min ago:                ~20 files
1 hour ago:                ~20 files
2 hours ago:               ~20 files
6 hours ago:               ~20 files
12 hours ago:              ~20 files
1 day ago:                 ~30 files
3 days ago:                ~30 files
7 days ago:                ~30 files
14 days ago:               ~20 files
30 days ago:               ~20 files
60 days ago:               ~15 files
90 days ago:               ~15 files

Total: ~$filesCreated files

Files are distributed across various extensions:
Documents: .txt, .doc, .docx, .pdf, .rtf, .odt
Spreadsheets: .xls, .xlsx, .csv, .ods
Images: .jpg, .png, .gif, .bmp, .svg
Videos: .mp4, .avi, .mkv, .mov
Archives: .zip, .rar, .7z
Code: .cs, .js, .py, .java
Data: .json, .xml, .yaml
Other: .log, .tmp, .bak

================================================================================
REPORTING ISSUES
================================================================================

If you encounter unexpected behavior:

1. Capture service logs (C:\ProgramData\RJAutoMover\Logs\service-*.log)
2. Capture tray logs (C:\ProgramData\RJAutoMover\Logs\tray-*.log)
3. Capture Event Viewer Application log entries
4. Note exact config used (rule settings)
5. Note expected vs actual behavior
6. Provide sample file details (timestamp, name, extension)

================================================================================
END OF TEST INSTRUCTIONS
================================================================================

Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Script Version: 1.0
RJAutoMover Version: 0.9.6.1

================================================================================
"@

Set-Content -Path $readmePath -Value $readmeContent
Write-Success "Created test instructions: $readmePath"

# ================================================================================
# Step 5: Generate File Summary CSV
# ================================================================================

Write-Section "Generating File Summary"

$summaryPath = Join-Path $BaseFolder "FILE_SUMMARY.csv"

Write-Info "Collecting file metadata..."

$files = Get-ChildItem -Path $folders["Source"] -File | Select-Object Name,
    @{Name='Extension';Expression={$_.Extension}},
    @{Name='Size';Expression={$_.Length}},
    @{Name='Created';Expression={$_.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")}},
    @{Name='Modified';Expression={$_.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")}},
    @{Name='Accessed';Expression={$_.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss")}},
    @{Name='AgeMinutes';Expression={[Math]::Round((New-TimeSpan -Start $_.CreationTime -End (Get-Date)).TotalMinutes, 1)}},
    @{Name='ModifiedMinutesAgo';Expression={[Math]::Round((New-TimeSpan -Start $_.LastWriteTime -End (Get-Date)).TotalMinutes, 1)}},
    @{Name='AccessedMinutesAgo';Expression={[Math]::Round((New-TimeSpan -Start $_.LastAccessTime -End (Get-Date)).TotalMinutes, 1)}}

$files | Export-Csv -Path $summaryPath -NoTypeInformation
Write-Success "Created file summary: $summaryPath"

# ================================================================================
# Final Summary
# ================================================================================

Write-Section "Test Data Generation Complete"

Write-Success "Test environment ready!"
Write-Info ""
Write-Info "Location: $BaseFolder"
Write-Info "Total Files: $filesCreated"
Write-Info "Total Size: $([Math]::Round($filesCreated * 1KB / 1MB, 2)) MB"
Write-Info ""
Write-Info "Files Created:"
Write-Host "  Source Folder:      $($folders['Source'])" -ForegroundColor White
Write-Host "  Test Config:        $configPath" -ForegroundColor White
Write-Host "  Instructions:       $readmePath" -ForegroundColor White
Write-Host "  File Summary:       $summaryPath" -ForegroundColor White
Write-Info ""

Write-Section "Next Steps"

Write-Host @"
1. Review test files in: $($folders['Source'])
2. Review test config:   $configPath
3. Read instructions:    $readmePath

To deploy to test machine:
---------------------------
1. Copy entire folder to test machine: $BaseFolder
2. Backup current config:
   copy "C:\Program Files\RJAutoMover\config.yaml" "C:\Program Files\RJAutoMover\config.yaml.backup"
3. Deploy test config:
   copy "$configPath" "C:\Program Files\RJAutoMover\config.yaml"
4. Edit config.yaml and enable ONE rule at a time (IsActive: true)
5. Restart RJAutoMoverService
6. Monitor logs: C:\ProgramData\RJAutoMover\Logs\service-*.log

Happy Testing!
"@ -ForegroundColor Cyan

Write-Info ""
Write-Section "Generation Summary Complete"
