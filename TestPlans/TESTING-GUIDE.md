# RJAutoMover Testing Guide

**Last Updated:** November 22, 2025
**Purpose:** Manual testing framework for validating RJAutoMover file processing rules

---

## Overview

This testing framework provides a structured 5-step manual workflow to validate RJAutoMover's file processing logic. Each step is run manually by the tester, with Step 4 (deployment) requiring human interaction to deploy the test configuration and monitor the service.

### Testing Philosophy

- **Manual Execution:** Each step is initiated by the tester for full control
- **Comprehensive:** Tests all DateFilter types (LA, LM, FC) and directions (+/-)
- **Predictive:** Simulates service decision logic to predict outcomes before execution
- **Diagnostic:** Integrates service log analysis for failure investigation
- **Fully Isolated:** Each test run is completely isolated in a versioned folder
- **Archival:** Test runs can be preserved indefinitely for historical analysis

---

## Quick Start

### Prerequisites

1. **RJAutoMover installed** at `C:\Program Files\RJAutoMover`
2. **Administrator privileges** (required for service control)
3. **PowerShell 5.1 or later**
4. **~100 MB free disk space** for test files

### Run Tests Manually

Navigate to the TestPlans folder and execute each step in order:

```powershell
cd TestPlans

# Step 1: Generate test files (creates versioned folder)
.\1-Generate-TestFiles.ps1
# OUTPUT: C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045

# Copy the test root path from Step 1 output
$testRoot = "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"

# Step 2: Generate test configuration
.\2-Generate-TestConfig.ps1 -TestRoot $testRoot

# Step 3: Generate predictions
.\3-Generate-Predictions.ps1 -TestRoot $testRoot

# Step 4: Deploy config and run service (MANUAL)
.\4-Deploy-And-Wait.ps1 -TestRoot $testRoot
# Follow on-screen instructions to deploy config, start service, and wait

# Step 5: Analyze results
.\5-Analyze-Results.ps1 -TestRoot $testRoot -GenerateHTML
```

---

## The 5-Step Testing Process

### Step 1: Generate Test Files

**Script:** `1-Generate-TestFiles.ps1`

**Purpose:** Creates a versioned test folder with test files, folder structure, and manifest for prediction analysis.

**What It Does:**
- Reads application version from `installer\version.txt`
- Creates versioned test folder: `C:\RJAutoMoverTest\testdata-<version>-<timestamp>`
- Creates folder structure: `Source\`, `Destination\`, `Logs\`, `Results\`
- Generates files across 24 age ranges (0 minutes to 365 days old)
- Distributes files across 18 extension types (.txt, .pdf, .doc, .jpg, .mp4, etc.)
- Sets realistic file timestamps (CreationTime, LastWriteTime, LastAccessTime)
- Saves `test-files-manifest.yaml` in test root folder

**Usage:**
```powershell
.\1-Generate-TestFiles.ps1
.\1-Generate-TestFiles.ps1 -TotalFiles 2000  # Generate more files
```

**Output:**
- `C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045\` (versioned test folder)
- `Source\*.ext` (test files in test folder)
- `test-files-manifest.yaml` (in test root)
- **Displays test root path for use in subsequent steps**

---

### Step 2: Generate Test Configuration

**Script:** `2-Generate-TestConfig.ps1`

**Purpose:** Creates a valid test configuration with non-overlapping rules covering ~85% of test files.

**What It Does:**
- Generates 10 test rules (9 specific extension rules + 1 OTHERS rule)
- Each rule has unique extensions (no overlaps = valid config)
- Rules cover all DateFilter scenarios (LM:-, LM:+, FC:-, FC:+, LA:+, no filter)
- OTHERS rule includes mandatory DateFilter
- Validates no extension clashes
- Uses paths from the versioned test folder

**Usage:**
```powershell
.\2-Generate-TestConfig.ps1 -TestRoot "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"
```

**Output:**
- `test-config.yaml` (in test root folder)

**Test Rules Created:**

| Rule | Extensions | DateFilter | Destination | Coverage |
|------|-----------|------------|-------------|----------|
| Test-RecentDocs | .txt\|.pdf | LM:-60 | Recent | Recent documents |
| Test-OldDocs7Days | .doc\|.docx\|.rtf | FC:+10080 | Old7Days | Old documents |
| Test-OldArchives30Days | .zip\|.rar\|.7z | FC:+43200 | Old30Days | Old archives |
| Test-NotAccessed90Days | .bak\|.tmp | LA:+129600 | Old90Days | Unused files |
| Test-RecentVideos | .mp4\|.avi\|.mkv | LM:-120 | Videos | Recent videos |
| Test-RecentImages | .jpg\|.png\|.gif | FC:-1440 | Images | Recent images |
| Test-AllSpreadsheets | .xls\|.xlsx\|.csv | (none) | Documents | All spreadsheets |
| Test-RecentCode | .cs\|.js\|.py | LM:-360 | Code | Recent code |
| Test-OldWebFiles | .html\|.css\|.json | FC:+4320 | Archives | Old web files |
| Test-OTHERS-Old14Days | OTHERS | FC:+20160 | Others | Catch-all (14+ days old) |

---

### Step 3: Generate Predictions

**Script:** `3-Generate-Predictions.ps1`

**Purpose:** Simulates the service's exact decision logic to predict which rule will process each file.

**What It Does:**
- Loads test files from manifest (in test root)
- Loads rules from test config (in test root)
- **Simulates service logic:**
  - Process rules in correct order (specific extensions first, OTHERS last)
  - Match file extensions (case-insensitive)
  - Parse and evaluate DateFilter criteria
  - Determine first matching rule for each file
- Generates predictions with expected rule, action, destination, and reason

**Usage:**
```powershell
.\3-Generate-Predictions.ps1 -TestRoot "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"
```

**Output:**
- `test-predictions.yaml` (in test root folder)

**Prediction Engine Logic:**

```
FOR EACH FILE:
  1. Try specific extension rules (in config order):
     - Does file extension match rule extension? (case-insensitive)
     - If YES:
       - Does file match DateFilter? (or no filter?)
       - If YES: MATCH FOUND → predict move to rule's destination
       - If NO: continue to next rule

  2. If no specific rule matched, try OTHERS rules:
     - Does file match DateFilter?
     - If YES: MATCH FOUND → predict move to OTHERS destination

  3. If no rule matched:
     - Predict file stays in source (unprocessed)
```

---

### Step 4: Manual Deployment (HUMAN INTERACTION)

**Script:** `4-Deploy-And-Wait.ps1`

**Purpose:** Provides instructions and validation for manually deploying test configuration and running the service.

**What It Does:**
- Validates test config and production paths exist
- Provides step-by-step deployment instructions with correct paths
- Waits for user confirmation that deployment is complete
- Validates deployment was successful
- Reminds user to restore production config after testing

**Usage:**
```powershell
.\4-Deploy-And-Wait.ps1 -TestRoot "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"
```

**Manual Steps You Must Perform:**

1. **Backup production config (if it exists):**
   ```powershell
   Copy-Item 'C:\Program Files\RJAutoMover\config.yaml' 'C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045\config-backup.yaml' -Force
   ```

2. **Copy test config to production location:**
   ```powershell
   Copy-Item 'C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045\test-config.yaml' 'C:\Program Files\RJAutoMover\config.yaml' -Force
   ```

3. **Start the RJAutoMoverService executable:**
   - Navigate to: `C:\Program Files\RJAutoMover`
   - Run as Administrator: `RJAutoMoverService.exe`
   - OR start via `services.msc` (Win+R → services.msc)

4. **Start the RJAutoMoverTray executable (optional but recommended):**
   - Navigate to: `C:\Program Files\RJAutoMover`
   - Run: `RJAutoMoverTray.exe`
   - Tray icon provides real-time monitoring

5. **Verify service is running:**
   ```powershell
   Get-Service -Name 'RJAutoMoverService'
   ```
   OR check tray icon status

6. **Monitor service logs:**
   ```powershell
   Get-Content 'C:\ProgramData\RJAutoMover\Logs\*RJAutoMoverService*.log' -Wait
   ```
   OR view logs in tray application (Logs tab)

7. **Wait for processing to complete:**
   - Watch logs for file move operations
   - Allow at least 2-3 minutes for scan cycles
   - Recommended: Wait 5-10 minutes for thorough processing
   - Service should stop actively moving files

**When to Proceed to Step 5:**
- All test files have been processed (check logs)
- Service is no longer actively moving files
- No errors in service logs

---

### Step 5: Analyze Results

**Script:** `5-Analyze-Results.ps1`

**Purpose:** Compare actual file locations against predictions, integrate service log analysis, and generate comprehensive exception reports.

**What It Does:**
- Scans source and destination folders for actual file locations (in test root)
- Parses service logs for file operations, errors, and skipped files
- Compares actual vs predicted outcomes for each file
- Generates detailed YAML and optional HTML reports
- Saves results to `Results\` folder in test root

**Usage:**
```powershell
.\5-Analyze-Results.ps1 -TestRoot "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"
.\5-Analyze-Results.ps1 -TestRoot "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045" -GenerateHTML  # Create visual HTML report
.\5-Analyze-Results.ps1 -TestRoot "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045" -Verbose       # Show all mismatches during analysis
```

**Output:**
- `Results\analysis-results.yaml` (full results in test root)
- `Results\exceptions.yaml` (failures only in test root)
- `Results\analysis-results.html` (optional visual report in test root)

**Comparison Logic:**

```
FOR EACH PREDICTED FILE:
  1. Determine actual location:
     - Scan source folder
     - Scan destination folder (recursively)
     - Mark as "Missing" if not found

  2. Compare with prediction:
     - Expected: Move
       → Check if file in destination
       → Check if correct destination subfolder
     - Expected: None (stay in source)
       → Check if file still in source

  3. Classify mismatch type:
     - Missing Move: Expected move but file still in source
     - Unexpected Move: Expected to stay but file moved
     - Wrong Destination: Moved but to wrong subfolder
     - Error/Missing: File not found anywhere

  4. Correlate with service logs:
     - Find log entries for this file
     - Extract rule that processed it
     - Include error messages if any
```

**Success Metrics:**

- **95-100% success:** Excellent - minor boundary issues only
- **80-95% success:** Good - may have DateFilter timing issues
- **<80% success:** Investigation required - logic errors likely

---

## Output Reports

### YAML Report (analysis-results.yaml)

Complete results for all test files:

```yaml
AnalysisMetadata:
  GeneratedDate: "2025-11-22T14:30:00"
  TestRoot: "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"
  TotalFiles: 1000
  Matches: 952
  Mismatches: 48
  SuccessRatePercent: 95.2
  MismatchBreakdown:
    MissingMoves: 12
    UnexpectedMoves: 5
    WrongDestination: 3
    Errors: 28

Results:
  - FileName: "document_2min.txt"
    Extension: ".txt"
    ExpectedAction: "Move"
    ExpectedRule: "Test-RecentDocs"
    ExpectedDestination: "Recent"
    ActualAction: "Move"
    ActualLocation: "Destination"
    ActualDestination: "Recent"
    ActualRule: "Test-RecentDocs"
    Match: true
    MismatchReason: ""
    MatchReason: "Extension .txt matches, DateFilter LM:-60 matches"
    LogEntries: 2
```

### Exceptions Report (exceptions.yaml)

Failures only for quick diagnosis:

```yaml
ExceptionMetadata:
  GeneratedDate: "2025-11-22T14:30:00"
  TestRoot: "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"
  TotalExceptions: 48
  TotalFiles: 1000
  FailureRatePercent: 4.8

Exceptions:
  - FileName: "archive_59min.zip"
    Extension: ".zip"
    Expected: "Move via 'Test-OldArchives30Days' to 'Old30Days'"
    Actual: "None to 'N/A' (Location: Source)"
    MismatchReason: "Expected move but file is in Source"
    MatchReason: "Extension .zip matches, DateFilter FC:+43200 matches"
    LogEntries: 0
```

### HTML Report (analysis-results.html)

Visual report with filtering and color-coding:

**Features:**
- Summary dashboard with success rate and mismatch breakdown
- Interactive table with all test results
- Filtering: View all files, matches only, or mismatches only
- Color coding: Green for matches, red for failures
- Sortable columns
- Detailed reasons for each result

---

## Troubleshooting

### Issue: Service Fails to Start After Deployment

**Symptoms:**
- Service won't start after deploying test config
- Error in service logs about invalid config

**Diagnosis:**
```powershell
Get-Service -Name "RJAutoMoverService"
Get-Content "C:\ProgramData\RJAutoMover\Logs\*RJAutoMoverService*.log" -Tail 50
```

**Common Causes:**
- Invalid test-config.yaml (validation error)
- Insufficient permissions on test data folder
- Service account cannot access `C:\RJAutoMoverTest\testdata-*`

**Solution:**
1. Review service log for validation errors
2. Grant service account read/write on `C:\RJAutoMoverTest\testdata-*` folders
3. Verify test-config.yaml syntax

---

### Issue: Low Success Rate (<80%)

**Symptoms:**
- analysis-results.yaml shows many mismatches
- exceptions.yaml has many "Missing Move" entries

**Diagnosis:**
```powershell
Get-Content "C:\RJAutoMoverTest\testdata-*\Results\exceptions.yaml" | Select-String "MismatchReason"
```

**Common Causes:**
- DateFilter boundary issues (files aged during test)
- Prediction engine logic doesn't match service logic
- Service didn't run long enough (not enough scan cycles)

**Solution:**
1. Wait longer in Step 4 (allow more scan cycles)
2. Review DateFilter boundaries in predictions vs actual
3. Check service logs for files that were skipped or errored

---

### Issue: Files Missing from Both Source and Destination

**Symptoms:**
- exceptions.yaml shows "File is missing from both source and destination"
- ActualLocation shows "Missing"

**Diagnosis:**
```powershell
Test-Path "C:\RJAutoMoverTest\testdata-*\Source\*"
Get-Content "C:\ProgramData\RJAutoMover\Logs\*RJAutoMoverService*.log" | Select-String "ERROR"
```

**Common Causes:**
- Files were deleted by service (error during move)
- Test data folder was cleared
- Service moved files outside test folder structure

**Solution:**
1. Review service logs for file operation errors
2. Verify test data folder permissions
3. Re-run test from Step 1

---

## Advanced Usage

### Re-running Individual Steps

You can re-run any step using the same test root:

```powershell
$testRoot = "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"

# Re-generate predictions after tweaking logic
.\3-Generate-Predictions.ps1 -TestRoot $testRoot

# Re-analyze results without re-running service
.\5-Analyze-Results.ps1 -TestRoot $testRoot -GenerateHTML
```

### Generating Only HTML Report

If you already have YAML results and want to add HTML:

```powershell
.\5-Analyze-Results.ps1 -TestRoot $testRoot -GenerateHTML
```

### Running Multiple Test Variations

Run multiple tests in parallel by creating separate test folders:

```powershell
# Create first test run
.\1-Generate-TestFiles.ps1
# OUTPUT: testdata-0.9.6.108-20251122153045

# Create second test run (different parameters)
.\1-Generate-TestFiles.ps1 -TotalFiles 2000
# OUTPUT: testdata-0.9.6.108-20251122153101

# Each test run is fully isolated
```

---

## Post-Test Cleanup

### Restore Production Config

**IMPORTANT:** After testing, restore your production configuration:

1. Stop RJAutoMoverService.exe (close or stop via services.msc)
2. Stop RJAutoMoverTray.exe (close from system tray)
3. Restore config:
   ```powershell
   $testRoot = "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"
   Copy-Item "$testRoot\config-backup.yaml" 'C:\Program Files\RJAutoMover\config.yaml' -Force
   ```
4. Restart RJAutoMoverService.exe
5. Restart RJAutoMoverTray.exe

### Clean Up Test Data

Remove a specific test run:

```powershell
Remove-Item 'C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045' -Recurse -Force
```

Remove ALL test runs:

```powershell
Remove-Item 'C:\RJAutoMoverTest' -Recurse -Force
```

### Archive Test Results

Versioned test folders are already self-contained and archived. You can:

**Keep for reference:**
```powershell
# Test folders remain at C:\RJAutoMoverTest\testdata-*
# Each contains complete test artifacts, logs, and results
```

**Move to permanent storage:**
```powershell
Move-Item 'C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045' 'D:\TestArchive\' -Force
```

---

## Files Reference

### Versioned Test Folder Structure

```
C:\RJAutoMoverTest\
└── testdata-0.9.6.108-20251122153045\
    ├── Source\                          (test files)
    ├── Destination\                     (target folders)
    │   ├── Recent\
    │   ├── Old7Days\
    │   ├── Old30Days\
    │   └── ... (10 total destination folders)
    ├── Logs\                            (empty until Step 4)
    ├── Results\                         (analysis results from Step 5)
    │   ├── analysis-results.yaml
    │   ├── exceptions.yaml
    │   └── analysis-results.html (if -GenerateHTML)
    ├── test-files-manifest.yaml         (file metadata)
    ├── test-config.yaml                 (test configuration)
    ├── test-predictions.yaml            (predicted outcomes)
    └── config-backup.yaml               (production config backup from Step 4)
```

### Generated Test Artifacts

| File | Purpose | Location |
|------|---------|----------|
| `test-files-manifest.yaml` | Test file metadata | Test root |
| `test-config.yaml` | Test configuration | Test root |
| `test-predictions.yaml` | Predicted outcomes | Test root |
| `config-backup.yaml` | Production config backup | Test root |
| `Results\analysis-results.yaml` | Full results | Test root\Results |
| `Results\exceptions.yaml` | Failures only | Test root\Results |
| `Results\analysis-results.html` | Visual report | Test root\Results (optional) |

### Core Scripts

| Script | Lines | Purpose |
|--------|-------|---------|
| `1-Generate-TestFiles.ps1` | 322 | Test file generation + manifest |
| `2-Generate-TestConfig.ps1` | 293 | Test config generation + validation |
| `3-Generate-Predictions.ps1` | 322 | Prediction engine (service logic simulation) |
| `4-Deploy-And-Wait.ps1` | 171 | Manual deployment instructions + validation |
| `5-Analyze-Results.ps1` | 621 | Results analysis + reporting |

---

## Best Practices

### Before Testing

1. **Backup production config** manually (in case of script failure)
2. **Close tray application** to avoid gRPC conflicts
3. **Verify service is stopped** before starting test
4. **Check disk space** (~100 MB for 1000 files)

### During Testing (Step 4)

1. **Monitor service logs** in real-time for errors
2. **Don't modify test files** while service is running
3. **Wait for full scan cycles** (2-3 complete cycles minimum)
4. **Check service status** periodically

### After Testing

1. **Review exceptions.yaml** before declaring success
2. **Verify production config restored**
3. **Clean up test data** if no longer needed
4. **Archive results** for future reference

---

## Version History

### v2.0 (November 22, 2025)

**Major update - Versioned Test Folders:**
- Complete test isolation in versioned folders (`testdata-<version>-<timestamp>`)
- All artifacts (files, config, predictions, results, logs) in single folder
- Test runs can be archived and preserved indefinitely
- Multiple test runs can coexist without conflicts
- Steps 2-5 accept `-TestRoot` parameter for folder specification

### v1.0 (November 22, 2025)

**Initial release:**
- 5-step manual testing framework
- Prediction engine with service logic simulation
- YAML + HTML reporting
- Service log integration
- Manual deployment workflow (Step 4)

**Test Coverage:**
- 10 test rules (9 specific + 1 OTHERS)
- All DateFilter types (LA, LM, FC)
- All DateFilter directions (+/-)
- 18 file extension types
- 24 age ranges (0 min - 365 days)

---

## Support

**Issues:** Report bugs or request features at [GitHub Issues](https://github.com/toohighonthehog/RJAutoMover/issues)

**Logs:** Always include service logs when reporting test failures:
- `C:\ProgramData\RJAutoMover\Logs\*RJAutoMoverService*.log`

**Results:** Include analysis-results.yaml and exceptions.yaml for diagnosis.
