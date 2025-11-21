# RJAutoMover - Date Filtering Test Plan

## Test Plan Version
- **Version**: 2.0 (Updated for DateFilter format)
- **Feature**: Date-Based File Filtering (DateFilter: TYPE:SIGN:MINUTES)
- **Application Version**: 0.9.6
- **Date**: 2025-01-21

---

## Table of Contents
1. [Overview](#overview)
2. [Test Environment Setup](#test-environment-setup)
3. [Automated Testing](#automated-testing)
4. [Test Cases](#test-cases)
5. [Configuration Validation Tests](#configuration-validation-tests)
6. [Processing Logic Tests](#processing-logic-tests)
7. [UI Display Tests](#ui-display-tests)
8. [Edge Cases and Error Handling](#edge-cases-and-error-handling)
9. [Integration Tests](#integration-tests)

---

## Overview

### Feature Description
RJAutoMover supports filtering files based on date criteria using the **new DateFilter format**:

```
DateFilter: "TYPE:SIGN:MINUTES"
```

### Format Components
- **TYPE**: `LA` (Last Accessed), `LM` (Last Modified), `FC` (File Created)
- **SIGN**: `+` (older than), `-` (within last)
- **MINUTES**: Integer value (1-5256000, representing up to 10 years)

### Examples
| DateFilter | Meaning |
|------------|---------|
| `LA:+43200` | Files NOT accessed in last 43200 minutes (30 days) - older files |
| `LA:-1440` | Files accessed within last 1440 minutes (1 day) - recent files |
| `LM:+10080` | Files NOT modified in last 10080 minutes (7 days) |
| `FC:+4320` | Files created more than 4320 minutes (3 days) ago |
| `FC:-60` | Files created within last 60 minutes (1 hour) |

### Key Rules
- Only ONE DateFilter per rule (format prevents conflicts)
- Range: 1 to 5256000 minutes (up to 10 years)
- **OTHERS** extension rules MUST have a DateFilter
- Empty/null DateFilter = no date filtering (process all files)

---

## Test Environment Setup

### Prerequisites
1. **Clean Windows System** (Windows 10/11 or Server 2016+)
2. **RJAutoMover v0.9.6+** installed
3. **Administrator privileges** for service control
4. **PowerShell 5.1+** for test scripts

### Quick Setup (Automated)

```powershell
# Navigate to TestPlans folder
cd c:\Users\rjohnson\source\repos\RJAutoMover\TestPlans

# Generate 1000 test files with comprehensive coverage
.\Create-TestFiles.ps1

# This creates:
# - C:\RJAutoMover_TestData\Source\ (1000 test files)
# - C:\RJAutoMover_TestData\Destination\ (multiple folders)
# - C:\RJAutoMover_TestData\test-config.yaml
# - C:\RJAutoMover_TestData\FILE_SUMMARY.csv
```

---

## Automated Testing

### Quick Test (Single Rule)

```powershell
# 1. Generate test data
.\Create-TestFiles.ps1

# 2. Deploy test config
Copy-Item "C:\RJAutoMover_TestData\test-config.yaml" "C:\Program Files\RJAutoMover\config.yaml"

# 3. Edit config - enable ONE rule (IsActive: true)
notepad "C:\Program Files\RJAutoMover\config.yaml"

# 4. Restart service
Restart-Service RJAutoMoverService

# 5. Wait for scan interval, then analyze
Start-Sleep -Seconds 30
.\Analyze-TestResults.ps1 -RuleName "Test-OldDocs7Days"
```

### Comprehensive Test Suite (All Rules)

```powershell
# Run automated test suite (requires admin)
.\Run-ComprehensiveTest.ps1

# This will:
# - Generate test data
# - Test all 10 rules sequentially
# - Analyze results for each
# - Generate comprehensive report
# - Restore production config
```

---

## Test Cases

## Configuration Validation Tests

### CV-001: Validate DateFilter Format (LA:+43200)
**Objective**: Verify correct DateFilter format is accepted

**Test Steps**:
1. Stop RJAutoMoverService
2. Edit `C:\Program Files\RJAutoMover\config.yaml`
3. Add test rule:
```yaml
FileRules:
  - Name: Test DateFilter Format
    SourceFolder: C:\RJAutoMover_TestData\Source
    Extension: .txt
    DestinationFolder: C:\RJAutoMover_TestData\Destination\Recent
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    DateFilter: "LA:+43200"  # Files NOT accessed in last 30 days
```
4. Start RJAutoMoverService
5. Check logs and Event Viewer

**Expected Results**:
- ✅ Service starts successfully
- ✅ No validation errors in Event Viewer
- ✅ Log shows: "Configuration loaded successfully"
- ✅ DateFilter parsed correctly

**Pass Criteria**: Service starts with no config errors

---

### CV-002: Validate Negative DateFilter (LM:-60)
**Objective**: Verify negative values (within last) are accepted

**Test Steps**:
1. Configure rule with negative DateFilter:
```yaml
FileRules:
  - Name: Test Negative DateFilter
    SourceFolder: C:\RJAutoMover_TestData\Source
    Extension: .txt
    DestinationFolder: C:\RJAutoMover_TestData\Destination\Recent
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    DateFilter: "LM:-60"  # Within last 60 minutes
```
2. Start service and check logs

**Expected Results**:
- ✅ Service accepts negative DateFilter
- ✅ No validation errors

**Pass Criteria**: Negative DateFilter accepted

---

### CV-003: Reject Invalid DateFilter Format
**Objective**: Verify malformed DateFilter strings are rejected

**Test Steps**:
1. Configure rule with invalid format:
```yaml
FileRules:
  - Name: Test Invalid Format
    SourceFolder: C:\RJAutoMover_TestData\Source
    Extension: .txt
    DestinationFolder: C:\RJAutoMover_TestData\Destination\Recent
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    DateFilter: "INVALID"  # Bad format
```
2. Attempt to start service

**Expected Results**:
- ❌ Service enters ERROR mode
- ❌ Error message: "Invalid date filter format. Expected 'TYPE:SIGN:MINUTES' (e.g., 'LA:+43200'), got 'INVALID'"
- ✅ Error tab shows detailed error

**Pass Criteria**: Invalid format rejected with clear error

---

### CV-004: Reject Out-of-Range Minutes
**Objective**: Verify minutes exceeding 5256000 are rejected

**Test Steps**:
1. Configure with excessive minutes:
```yaml
FileRules:
  - Name: Test Out of Range
    SourceFolder: C:\RJAutoMover_TestData\Source
    Extension: .txt
    DestinationFolder: C:\RJAutoMover_TestData\Destination\Old
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    DateFilter: "FC:+6000000"  # Exceeds max (5256000)
```
2. Attempt to start service

**Expected Results**:
- ❌ Service rejects configuration
- ❌ Error message: "Minutes must be between 1 and 5256000 (got 6000000)"

**Pass Criteria**: Out-of-range value rejected

---

### CV-005: Reject Zero Minutes
**Objective**: Verify minutes cannot be zero

**Test Steps**:
1. Configure with zero:
```yaml
FileRules:
  - Name: Test Zero Minutes
    SourceFolder: C:\RJAutoMover_TestData\Source
    Extension: .txt
    DestinationFolder: C:\RJAutoMover_TestData\Destination\Old
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    DateFilter: "FC:+0"  # Zero not allowed
```
2. Attempt to start service

**Expected Results**:
- ❌ Service rejects configuration
- ❌ Error message mentions minimum is 1 minute

**Pass Criteria**: Zero value rejected

---

### CV-006: Require DateFilter for OTHERS Extension
**Objective**: Verify OTHERS rules must have a DateFilter

**Test Steps**:
1. Configure OTHERS rule without DateFilter:
```yaml
FileRules:
  - Name: Test OTHERS Without DateFilter
    SourceFolder: C:\RJAutoMover_TestData\Source
    Extension: OTHERS
    DestinationFolder: C:\RJAutoMover_TestData\Destination\Others
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    # NO DateFilter - INVALID
```
2. Attempt to start service

**Expected Results**:
- ❌ Service rejects configuration
- ❌ Error message: "Extension 'OTHERS' rules MUST have a DateFilter"

**Pass Criteria**: OTHERS without DateFilter rejected

---

## Processing Logic Tests

### PL-001: Process Files OLDER Than Threshold (FC:+10080)
**Objective**: Verify positive DateFilter matches files older than threshold

**Use Automated Test**:
```powershell
.\Analyze-TestResults.ps1 -RuleName "Test-OldDocs7Days"
```

**Or Manual Test**:
1. Use test data from Create-TestFiles.ps1
2. Enable rule: "Test-OldDocs7Days" (DateFilter: "FC:+10080")
3. Restart service, wait 30 seconds
4. Check destination: C:\RJAutoMover_TestData\Destination\Old7Days

**Expected Results**:
- ✅ Files created more than 7 days ago are moved
- ❌ Files created less than 7 days ago remain in source
- ✅ Log shows: "File matches DateFilter criteria"

**Pass Criteria**: Only files older than threshold processed

---

### PL-002: Process Files WITHIN Last X Minutes (LM:-120)
**Objective**: Verify negative DateFilter matches recent files

**Use Automated Test**:
```powershell
.\Analyze-TestResults.ps1 -RuleName "Test-RecentModVideos"
```

**Expected Results**:
- ✅ Files modified within last 2 hours are moved
- ❌ Files modified more than 2 hours ago remain in source
- ✅ Analyzer shows "Correctly moved" count

**Pass Criteria**: Only recently modified files processed

---

### PL-003: Process by Last Access Time (LA:+43200)
**Objective**: Verify LastAccessed type works correctly

**Use Automated Test**:
```powershell
.\Analyze-TestResults.ps1 -RuleName "Test-StaleCodeNoAccess30Days"
```

**Expected Results**:
- ✅ Files NOT accessed in last 30 days are moved
- ❌ Recently accessed files remain in source

**Pass Criteria**: LastAccessed filtering works correctly

---

### PL-004: Boundary Test - Exact Match at Threshold
**Objective**: Verify file at exact threshold is processed

**Test Steps**:
1. Create file exactly 60 minutes old
2. Configure DateFilter: "FC:+60"
3. Verify file is moved (>= logic includes exact match)

**Expected Results**:
- ✅ File at exact threshold IS moved
- ✅ Log shows age = 60.0 min, threshold = 60 min

**Pass Criteria**: Exact threshold match processed (>= for positive, <= for negative)

---

### PL-005: Multiple DateFilter Types
**Objective**: Verify all three types (LA, LM, FC) work independently

**Use Automated Test**:
```powershell
# Test all rules
.\Analyze-TestResults.ps1
```

**Expected Results**:
- ✅ LA (Last Accessed) rules process correctly
- ✅ LM (Last Modified) rules process correctly
- ✅ FC (File Created) rules process correctly
- ✅ Each type operates independently

**Pass Criteria**: All three types work correctly

---

### PL-006: OTHERS Extension with DateFilter
**Objective**: Verify OTHERS catches remaining files with date filter

**Use Automated Test**:
```powershell
.\Analyze-TestResults.ps1 -RuleName "Test-OthersVeryOld180Days"
```

**Expected Results**:
- ✅ Files not matched by specific rules are caught by OTHERS
- ✅ Only files matching DateFilter are moved
- ✅ OTHERS processes last (after specific extensions)

**Pass Criteria**: OTHERS rule with DateFilter works correctly

---

## UI Display Tests

### UI-001: Display DateFilter in Config Tab
**Objective**: Verify DateFilter displays in human-readable format

**Test Steps**:
1. Configure rule with DateFilter: "FC:+10080"
2. Open RJAutoMover tray application
3. View "Configuration" tab
4. Find rule and check "Date Criteria" column

**Expected Results**:
- ✅ Shows: "Files created more than 7 days ago (older files)"
- ✅ Human-readable format (not raw "FC:+10080")
- ✅ Clearly indicates "older files" for positive
- ✅ Would show "within last" for negative

**Pass Criteria**: DateFilter displays in user-friendly format

---

### UI-002: Display All DateFilter Types
**Objective**: Verify all three types display correctly

**Test Steps**:
1. Configure rules with LA, LM, and FC types
2. View Configuration tab

**Expected Results**:
- ✅ LA displays as "Files accessed..."
- ✅ LM displays as "Files modified..."
- ✅ FC displays as "Files created..."
- ✅ Positive shows "more than X ago (older files)"
- ✅ Negative shows "within the last X"

**Pass Criteria**: All types display with correct labels

---

### UI-003: Error Tab Shows DateFilter Validation Errors
**Objective**: Verify DateFilter errors appear in Error tab

**Test Steps**:
1. Configure invalid DateFilter (e.g., "INVALID")
2. Save config while service running
3. Service detects error and enters ERROR mode
4. Open About window and check Error tab

**Expected Results**:
- ✅ Error tab shows detailed validation error
- ✅ Error message includes expected format
- ✅ Tray icon shows error state
- ✅ Status menu shows short error message

**Pass Criteria**: DateFilter errors clearly displayed

---

## Edge Cases and Error Handling

### EC-001: Empty DateFilter String
**Objective**: Verify empty/null DateFilter allows all files

**Test Steps**:
1. Configure rule with DateFilter: ""
2. Verify all files (regardless of date) are processed

**Expected Results**:
- ✅ Empty DateFilter = no date filtering
- ✅ All matching extensions processed

**Pass Criteria**: Empty DateFilter bypasses date check

---

### EC-002: File Timestamp in Future
**Objective**: Verify handling of future timestamps

**Test Steps**:
1. Create file with creation time set to tomorrow
2. Configure DateFilter: "FC:+60"
3. Verify file behavior

**Expected Results**:
- ❌ File with future timestamp NOT matched by positive filter
- ✅ Service handles gracefully (no crash)
- ✅ May show negative age in logs

**Pass Criteria**: Future timestamps handled correctly

---

### EC-003: System Clock Change
**Objective**: Verify service adapts to clock changes

**Test Steps**:
1. Start service with DateFilter rule
2. Change system clock forward/backward
3. Verify service continues operating

**Expected Results**:
- ✅ Service adapts to new system time
- ✅ Date calculations use current time
- ⚠️ Files may suddenly match/unmatch criteria

**Pass Criteria**: Service continues operating after clock change

---

### EC-004: Maximum DateFilter Value
**Objective**: Test maximum allowed value (5256000 minutes = 10 years)

**Test Steps**:
1. Configure DateFilter: "FC:+5256000"
2. Verify service accepts configuration
3. Test with recent file (< 10 years old)

**Expected Results**:
- ✅ Configuration accepted
- ✅ Service starts successfully
- ❌ Recent files not moved (correct)

**Pass Criteria**: Maximum value works correctly

---

### EC-005: Config Change Detection with DateFilter
**Objective**: Verify config change protection works with DateFilter

**Test Steps**:
1. Start service with DateFilter rule
2. Edit config to change DateFilter
3. Save config
4. Observe service behavior

**Expected Results**:
- ✅ Service detects config change
- ✅ Service enters ERROR mode
- ✅ Error tab shows: "Configuration file changed externally. Service requires restart..."
- ✅ All processing stops

**Pass Criteria**: Config change protection works with DateFilter

---

## Integration Tests

### IT-001: End-to-End Archival Workflow
**Objective**: Complete workflow for archiving old files

**Use Automated Test**:
```powershell
.\Run-ComprehensiveTest.ps1 -RulesTimeoutSec 60
```

**Or Manual Scenario**:
1. Use Create-TestFiles.ps1 to generate test data
2. Enable rule "Test-OldDocs7Days" (DateFilter: "FC:+10080")
3. Restart service
4. Wait for scan interval
5. Run analyzer: `.\Analyze-TestResults.ps1 -RuleName "Test-OldDocs7Days"`

**Expected Results**:
- ✅ Only files older than 7 days are moved
- ✅ Activity history shows transfers
- ✅ Logs show successful date filtering
- ✅ Analyzer shows 0 incorrectly moved files

**Pass Criteria**: Complete workflow executes correctly

---

### IT-002: Multi-Rule DateFilter Processing
**Objective**: Verify multiple rules with different DateFilters work together

**Use Automated Test**:
```powershell
# Enable multiple rules and run comprehensive test
.\Run-ComprehensiveTest.ps1
```

**Expected Results**:
- ✅ Each rule processes independently
- ✅ Different DateFilter types don't conflict
- ✅ File priority rules respected (specific before OTHERS)
- ✅ All rules complete successfully

**Pass Criteria**: Multiple DateFilter rules coexist correctly

---

### IT-003: Performance with 1000 Files
**Objective**: Verify DateFilter performance with large file set

**Use Test Data**:
```powershell
.\Create-TestFiles.ps1  # Creates 1000 files
```

**Test Steps**:
1. Enable any DateFilter rule
2. Restart service
3. Monitor scan time in logs
4. Check memory usage

**Expected Results**:
- ✅ Scan completes within reasonable time (< 10 seconds)
- ✅ Memory usage stays under limit
- ✅ Correct subset processed based on DateFilter
- ✅ No performance degradation

**Pass Criteria**: DateFilter performs well with 1000 files

---

## Test Execution Summary Template

### Test Run Information
- **Tester Name**: _______________
- **Test Date**: _______________
- **Application Version**: 0.9.6
- **Test Environment**: Windows _____ (version)
- **.NET Version**: 10.0
- **Test Method**: ☐ Automated ☐ Manual ☐ Both

### Automated Test Results

```powershell
# Run comprehensive test suite
.\Run-ComprehensiveTest.ps1
```

**Report Location**: `C:\RJAutoMover_TestData\TEST_REPORT_*.txt`

### Test Results Summary

| Test ID | Test Name | Status | Notes |
|---------|-----------|--------|-------|
| CV-001 | Validate DateFilter Format | ☐ Pass ☐ Fail | |
| CV-002 | Validate Negative DateFilter | ☐ Pass ☐ Fail | |
| CV-003 | Reject Invalid Format | ☐ Pass ☐ Fail | |
| CV-004 | Reject Out-of-Range Minutes | ☐ Pass ☐ Fail | |
| CV-005 | Reject Zero Minutes | ☐ Pass ☐ Fail | |
| CV-006 | Require DateFilter for OTHERS | ☐ Pass ☐ Fail | |
| PL-001 | Process OLDER Than | ☐ Pass ☐ Fail | |
| PL-002 | Process WITHIN Last | ☐ Pass ☐ Fail | |
| PL-003 | Last Access Time | ☐ Pass ☐ Fail | |
| PL-004 | Boundary - Exact Match | ☐ Pass ☐ Fail | |
| PL-005 | Multiple DateFilter Types | ☐ Pass ☐ Fail | |
| PL-006 | OTHERS with DateFilter | ☐ Pass ☐ Fail | |
| UI-001 | Display DateFilter | ☐ Pass ☐ Fail | |
| UI-002 | Display All Types | ☐ Pass ☐ Fail | |
| UI-003 | Error Tab Validation | ☐ Pass ☐ Fail | |
| EC-001 | Empty DateFilter | ☐ Pass ☐ Fail | |
| EC-002 | Future Timestamp | ☐ Pass ☐ Fail | |
| EC-003 | System Clock Change | ☐ Pass ☐ Fail | |
| EC-004 | Maximum Value | ☐ Pass ☐ Fail | |
| EC-005 | Config Change Detection | ☐ Pass ☐ Fail | |
| IT-001 | E2E Archival Workflow | ☐ Pass ☐ Fail | |
| IT-002 | Multi-Rule Processing | ☐ Pass ☐ Fail | |
| IT-003 | Performance 1000 Files | ☐ Pass ☐ Fail | |

### Overall Summary
- **Total Tests**: 23
- **Tests Passed**: _____
- **Tests Failed**: _____
- **Tests Skipped**: _____
- **Pass Rate**: _____%

### Critical Issues Found
1.
2.
3.

### Recommendations
1.
2.
3.

---

## Appendix A: Quick Reference

### DateFilter Format
```
"TYPE:SIGN:MINUTES"
```

### Common DateFilter Examples
| Want to Move | DateFilter | Minutes |
|-------------|------------|---------|
| Files accessed today | `LA:-1440` | 1440 |
| Files NOT accessed this month | `LA:+43200` | 43200 |
| Files modified this week | `LM:-10080` | 10080 |
| Files NOT modified this year | `LM:+525600` | 525600 |
| Files created this hour | `FC:-60` | 60 |
| Files created over 90 days ago | `FC:+129600` | 129600 |

### Time Conversions
| Period | Minutes |
|--------|---------|
| 1 hour | 60 |
| 1 day | 1440 |
| 1 week | 10080 |
| 1 month (30 days) | 43200 |
| 3 months | 129600 |
| 6 months | 259200 |
| 1 year | 525600 |
| 10 years (max) | 5256000 |

## Appendix B: Automated Testing Scripts

### Generate Test Data
```powershell
.\Create-TestFiles.ps1
```
Creates 1000 files with various ages and types.

### Analyze Single Rule
```powershell
.\Analyze-TestResults.ps1 -RuleName "Test-OldDocs7Days"
```

### Analyze All Active Rules
```powershell
.\Analyze-TestResults.ps1
```

### Run Complete Test Suite
```powershell
.\Run-ComprehensiveTest.ps1
```

### Detailed Analysis
```powershell
.\Analyze-TestResults.ps1 -Detailed
```

## Appendix C: Log File Locations

- **Service Logs**: `C:\ProgramData\RJAutoMover\Logs\*service*.log`
- **Tray Logs**: `C:\ProgramData\RJAutoMover\Logs\*tray*.log`
- **Event Viewer**: Application log → Source: RJAutoMoverService
- **Test Reports**: `C:\RJAutoMover_TestData\TEST_REPORT_*.txt`

---

**END OF TEST PLAN**
