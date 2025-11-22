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
- **Versioned:** All test runs are isolated in versioned result folders

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

# Step 1: Generate test files
.\1-Generate-TestFiles.ps1

# Step 2: Generate test configuration
.\2-Generate-TestConfig.ps1

# Step 3: Generate predictions
.\3-Generate-Predictions.ps1

# Step 4: Deploy config and run service (MANUAL)
.\4-Deploy-And-Wait.ps1
# Follow on-screen instructions to deploy config, restart service, and wait

# Step 5: Analyze results
.\5-Analyze-Results.ps1 -GenerateHTML
```

---

## The 5-Step Testing Process

### Step 1: Generate Test Files

**Script:** `1-Generate-TestFiles.ps1`

**Purpose:** Creates test files with controlled properties (extensions, timestamps, sizes) and generates a manifest for prediction analysis.

**What It Does:**
- Creates test data folder structure: `C:\RJAutoMover_TestData\Source` and `\Destination`
- Generates files across 24 age ranges (0 minutes to 365 days old)
- Distributes files across 18 extension types (.txt, .pdf, .doc, .jpg, .mp4, etc.)
- Sets realistic file timestamps (CreationTime, LastWriteTime, LastAccessTime)
- Generates `test-files-manifest.yaml` with metadata for each file

**Usage:**
```powershell
.\1-Generate-TestFiles.ps1
.\1-Generate-TestFiles.ps1 -TotalFiles 2000  # Generate more files
```

**Output:**
- `C:\RJAutoMover_TestData\Source\*.ext` (test files)
- `test-files-manifest.yaml` (file metadata)

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

**Usage:**
```powershell
.\2-Generate-TestConfig.ps1
```

**Output:**
- `test-config.yaml` (valid test configuration)

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
- Loads test files from manifest
- Loads rules from test config
- **Simulates service logic:**
  - Process rules in correct order (specific extensions first, OTHERS last)
  - Match file extensions (case-insensitive)
  - Parse and evaluate DateFilter criteria
  - Determine first matching rule for each file
- Generates predictions with expected rule, action, destination, and reason

**Usage:**
```powershell
.\3-Generate-Predictions.ps1
```

**Output:**
- `test-predictions.yaml` (expected outcomes for all test files)

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
- Provides step-by-step deployment instructions
- Waits for user confirmation that deployment is complete
- Validates deployment was successful
- Reminds user to restore production config after testing

**Usage:**
```powershell
.\4-Deploy-And-Wait.ps1
```

**Manual Steps You Must Perform:**

1. **Backup production config:**
   ```powershell
   Copy-Item 'C:\Program Files\RJAutoMover\config.yaml' 'config-backup.yaml' -Force
   ```

2. **Deploy test config:**
   ```powershell
   Copy-Item 'test-config.yaml' 'C:\Program Files\RJAutoMover\config.yaml' -Force
   ```

3. **Restart the service:**
   ```powershell
   Restart-Service -Name 'RJAutoMoverService' -Force
   ```
   OR use `services.msc` (Win+R → services.msc)

4. **Verify service is running:**
   ```powershell
   Get-Service -Name 'RJAutoMoverService'
   ```

5. **Monitor service logs:**
   ```powershell
   Get-Content 'C:\ProgramData\RJAutoMover\Logs\*RJAutoMoverService*.log' -Wait
   ```

6. **Wait for processing to complete:**
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
- Scans source and destination folders for actual file locations
- Parses service logs for file operations, errors, and skipped files
- Compares actual vs predicted outcomes for each file
- Generates detailed YAML and optional HTML reports
- Creates versioned results folder

**Usage:**
```powershell
.\5-Analyze-Results.ps1
.\5-Analyze-Results.ps1 -GenerateHTML  # Create visual HTML report
.\5-Analyze-Results.ps1 -Verbose       # Show all mismatches during analysis
```

**Output:**
- `Results\v{timestamp}\analysis-results.yaml` (full results)
- `Results\v{timestamp}\exceptions.yaml` (failures only)
- `Results\v{timestamp}\analysis-results.html` (optional visual report)

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
  Version: "v202511221430"
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
  Version: "v202511221430"
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
- Service account cannot access `C:\RJAutoMover_TestData`

**Solution:**
1. Review service log for validation errors
2. Grant service account read/write on `C:\RJAutoMover_TestData`
3. Verify test-config.yaml syntax

---

### Issue: Low Success Rate (<80%)

**Symptoms:**
- analysis-results.yaml shows many mismatches
- exceptions.yaml has many "Missing Move" entries

**Diagnosis:**
```powershell
Get-Content "Results\v*\exceptions.yaml" | Select-String "MismatchReason"
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
Test-Path "C:\RJAutoMover_TestData\Source\*"
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

### Custom Test Data Locations

Override default test data folder:

```powershell
.\1-Generate-TestFiles.ps1 -BaseFolder "D:\Tests\RJAutoMover"
.\2-Generate-TestConfig.ps1 -BaseFolder "D:\Tests\RJAutoMover"
# Steps 3-5 will automatically use manifest and config
```

### Re-running Individual Steps

You can re-run any step without affecting others:

```powershell
# Re-generate predictions after tweaking logic
.\3-Generate-Predictions.ps1

# Re-analyze results without re-running service
.\5-Analyze-Results.ps1 -GenerateHTML
```

### Generating Only HTML Report

If you already have YAML results and want to add HTML:

```powershell
.\5-Analyze-Results.ps1 -GenerateHTML
```

---

## Post-Test Cleanup

### Restore Production Config

**IMPORTANT:** After testing, restore your production configuration:

```powershell
Stop-Service -Name 'RJAutoMoverService'
Copy-Item 'config-backup.yaml' 'C:\Program Files\RJAutoMover\config.yaml' -Force
Start-Service -Name 'RJAutoMoverService'
```

### Clean Up Test Data

Remove test files and folders:

```powershell
Remove-Item 'C:\RJAutoMover_TestData' -Recurse -Force
```

### Archive Test Results

Move results to permanent storage:

```powershell
$latestResults = Get-ChildItem "Results" -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Copy-Item $latestResults.FullName "D:\TestArchive\$(Get-Date -Format 'yyyy-MM-dd')" -Recurse
```

---

## Files Reference

### Generated Test Artifacts

| File | Purpose | Lifespan |
|------|---------|----------|
| `test-files-manifest.yaml` | Test file metadata | Overwritten each run |
| `test-config.yaml` | Test configuration | Overwritten each run |
| `test-predictions.yaml` | Predicted outcomes | Overwritten each run |
| `config-backup.yaml` | Production config backup | Overwritten each run |
| `Results\v{timestamp}\*` | Test results | Persisted (versioned) |

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

### v1.0 (November 22, 2025)

**Initial release:**
- 5-step manual testing framework
- Prediction engine with service logic simulation
- YAML + HTML reporting
- Service log integration
- Versioned results folders
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
