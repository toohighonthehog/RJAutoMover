# RJAutoMover - Date-Based File Filtering Test Plan

## Test Plan Version
- **Version**: 1.0
- **Feature**: Date-Based File Filtering (LastAccessedMins, LastModifiedMins, AgeCreatedMins)
- **Application Version**: 0.9.6.1
- **Date**: 2025-11-07

---

## Table of Contents
1. [Overview](#overview)
2. [Test Environment Setup](#test-environment-setup)
3. [Test Data Preparation](#test-data-preparation)
4. [Test Cases](#test-cases)
5. [Configuration Validation Tests](#configuration-validation-tests)
6. [Processing Logic Tests](#processing-logic-tests)
7. [UI Display Tests](#ui-display-tests)
8. [Edge Cases and Error Handling](#edge-cases-and-error-handling)
9. [Integration Tests](#integration-tests)

---

## Overview

### Feature Description
RJAutoMover now supports filtering files based on three date criteria:
- **LastAccessedMins**: Filter by last access time
- **LastModifiedMins**: Filter by last modification time
- **AgeCreatedMins**: Filter by creation time

### Positive vs Negative Values
- **POSITIVE values** (e.g., `1440`): Files **OLDER than** X minutes (accessed/modified/created MORE than X minutes ago)
- **NEGATIVE values** (e.g., `-60`): Files **WITHIN last** X minutes (accessed/modified/created LESS than |X| minutes ago)
- **ZERO**: NOT allowed - validation error

### Key Rules
- Only ONE date criteria per rule (mutually exclusive)
- Range: -5256000 to +5256000 minutes (+/- 10 years)
- ALL extension rules MUST have a date criteria

---

## Test Environment Setup

### Prerequisites
1. **Clean Windows System** (Windows 10/11 or Server 2016+)
2. **RJAutoMover v0.9.6.1** installed
3. **Administrator privileges** for service control
4. **Test folders** with appropriate permissions
5. **Text editor** for config.yaml modification
6. **File date manipulation tool** (PowerShell recommended)

### Test Folder Structure
```
C:\TestData\
├── Source\
│   ├── TestFiles\
│   └── Archive\
└── Destination\
    ├── Recent\
    ├── Old\
    └── Archive\
```

### PowerShell Helper Scripts

#### Create Test File with Specific Creation Date
```powershell
# Create file with specific creation time (5 days ago)
$file = New-Item -Path "C:\TestData\Source\TestFiles\old_file.txt" -ItemType File -Force
$file.CreationTime = (Get-Date).AddDays(-5)
$file.LastWriteTime = (Get-Date).AddDays(-5)
$file.LastAccessTime = (Get-Date).AddDays(-5)
```

#### Create Test File with Specific Modified Date
```powershell
# Create file modified 2 hours ago
$file = New-Item -Path "C:\TestData\Source\TestFiles\recent_file.txt" -ItemType File -Force
$file.LastWriteTime = (Get-Date).AddHours(-2)
```

#### Create Test File with Specific Access Date
```powershell
# Create file accessed 30 minutes ago
$file = New-Item -Path "C:\TestData\Source\TestFiles\accessed_file.txt" -ItemType File -Force
$file.LastAccessTime = (Get-Date).AddMinutes(-30)
```

#### Verify File Dates
```powershell
Get-Item "C:\TestData\Source\TestFiles\*.txt" | Select-Object Name, CreationTime, LastWriteTime, LastAccessTime | Format-Table -AutoSize
```

---

## Test Data Preparation

### Test File Sets

#### Set 1: Creation Time Test Files
| File Name | Created | Purpose |
|-----------|---------|---------|
| `created_1_hour_ago.txt` | 1 hour ago | Test AgeCreatedMins positive (should NOT match < 120 min) |
| `created_3_hours_ago.txt` | 3 hours ago | Test AgeCreatedMins positive (should match >= 120 min) |
| `created_1_day_ago.txt` | 1 day ago | Test AgeCreatedMins positive (should match >= 1440 min) |
| `created_7_days_ago.txt` | 7 days ago | Test AgeCreatedMins positive (should match >= 10080 min) |
| `created_30_days_ago.txt` | 30 days ago | Test AgeCreatedMins positive (should match >= 43200 min) |
| `created_just_now.txt` | < 1 min ago | Test AgeCreatedMins negative (should match <= 5 min) |
| `created_5_min_ago.txt` | 5 min ago | Test AgeCreatedMins negative (should match <= 60 min) |

#### Set 2: Modified Time Test Files
| File Name | Modified | Purpose |
|-----------|----------|---------|
| `modified_15_min_ago.txt` | 15 min ago | Test LastModifiedMins negative (should match <= 30 min) |
| `modified_45_min_ago.txt` | 45 min ago | Test LastModifiedMins negative (should NOT match <= 30 min) |
| `modified_90_min_ago.txt` | 90 min ago | Test LastModifiedMins positive (should match >= 60 min) |
| `modified_30_min_ago.txt` | 30 min ago | Test LastModifiedMins boundary (edge case) |

#### Set 3: Access Time Test Files
| File Name | Accessed | Purpose |
|-----------|----------|---------|
| `accessed_10_min_ago.txt` | 10 min ago | Test LastAccessedMins negative (should match <= 60 min) |
| `accessed_2_hours_ago.txt` | 2 hours ago | Test LastAccessedMins positive (should match >= 60 min) |
| `accessed_just_now.txt` | < 1 min ago | Test LastAccessedMins negative (should match <= 5 min) |

#### PowerShell Script to Create All Test Files
```powershell
# Set 1: Creation Time Test Files
$testFiles = @(
    @{Name="created_1_hour_ago.txt"; Created=(Get-Date).AddHours(-1)},
    @{Name="created_3_hours_ago.txt"; Created=(Get-Date).AddHours(-3)},
    @{Name="created_1_day_ago.txt"; Created=(Get-Date).AddDays(-1)},
    @{Name="created_7_days_ago.txt"; Created=(Get-Date).AddDays(-7)},
    @{Name="created_30_days_ago.txt"; Created=(Get-Date).AddDays(-30)},
    @{Name="created_just_now.txt"; Created=(Get-Date)},
    @{Name="created_5_min_ago.txt"; Created=(Get-Date).AddMinutes(-5)}
)

foreach ($testFile in $testFiles) {
    $file = New-Item -Path "C:\TestData\Source\TestFiles\$($testFile.Name)" -ItemType File -Force
    $file.CreationTime = $testFile.Created
    $file.LastWriteTime = $testFile.Created
    $file.LastAccessTime = $testFile.Created
    Add-Content -Path $file.FullName -Value "Test content for $($testFile.Name)"
}

# Set 2: Modified Time Test Files
$modifiedFiles = @(
    @{Name="modified_15_min_ago.txt"; Modified=(Get-Date).AddMinutes(-15)},
    @{Name="modified_45_min_ago.txt"; Modified=(Get-Date).AddMinutes(-45)},
    @{Name="modified_90_min_ago.txt"; Modified=(Get-Date).AddMinutes(-90)},
    @{Name="modified_30_min_ago.txt"; Modified=(Get-Date).AddMinutes(-30)}
)

foreach ($testFile in $modifiedFiles) {
    $file = New-Item -Path "C:\TestData\Source\TestFiles\$($testFile.Name)" -ItemType File -Force
    $file.LastWriteTime = $testFile.Modified
    Add-Content -Path $file.FullName -Value "Test content for $($testFile.Name)"
}

# Set 3: Access Time Test Files
$accessedFiles = @(
    @{Name="accessed_10_min_ago.txt"; Accessed=(Get-Date).AddMinutes(-10)},
    @{Name="accessed_2_hours_ago.txt"; Accessed=(Get-Date).AddHours(-2)},
    @{Name="accessed_just_now.txt"; Accessed=(Get-Date)}
)

foreach ($testFile in $accessedFiles) {
    $file = New-Item -Path "C:\TestData\Source\TestFiles\$($testFile.Name)" -ItemType File -Force
    $file.LastAccessTime = $testFile.Accessed
    Add-Content -Path $file.FullName -Value "Test content for $($testFile.Name)"
}

Write-Host "Test files created successfully!" -ForegroundColor Green
```

---

## Test Cases

## Configuration Validation Tests

### CV-001: Validate Positive AgeCreatedMins
**Objective**: Verify that positive AgeCreatedMins values are accepted

**Test Steps**:
1. Stop RJAutoMoverService
2. Edit `C:\Program Files\RJAutoMover\config.yaml`
3. Add test rule:
```yaml
FileRules:
  - Name: Test Positive Age
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: .txt
    DestinationFolder: C:\TestData\Destination\Old
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    AgeCreatedMins: 10080  # 7 days
```
4. Start RJAutoMoverService
5. Check Windows Event Viewer (Application log)
6. Check `C:\ProgramData\RJAutoMover\Logs\service-*.log`

**Expected Results**:
- ✅ Service starts successfully
- ✅ No validation errors in Event Viewer
- ✅ Log shows: "Configuration loaded successfully"
- ✅ Rule is active and processing

**Pass Criteria**: Service starts with no config errors

---

### CV-002: Validate Negative LastModifiedMins
**Objective**: Verify that negative LastModifiedMins values are accepted

**Test Steps**:
1. Stop RJAutoMoverService
2. Edit `C:\Program Files\RJAutoMover\config.yaml`
3. Add test rule:
```yaml
FileRules:
  - Name: Test Negative Modified
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: .txt
    DestinationFolder: C:\TestData\Destination\Recent
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    LastModifiedMins: -30  # Within last 30 minutes
```
4. Start RJAutoMoverService
5. Check logs and Event Viewer

**Expected Results**:
- ✅ Service starts successfully
- ✅ No validation errors
- ✅ Rule accepts negative value

**Pass Criteria**: Service starts with no config errors

---

### CV-003: Reject Zero Value for Date Criteria
**Objective**: Verify that zero values are rejected

**Test Steps**:
1. Stop RJAutoMoverService
2. Edit config.yaml with zero value:
```yaml
FileRules:
  - Name: Test Zero Age
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: .txt
    DestinationFolder: C:\TestData\Destination\Old
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    AgeCreatedMins: 0  # INVALID
```
3. Attempt to start RJAutoMoverService
4. Check Event Viewer and logs

**Expected Results**:
- ❌ Service fails to start OR enters ERROR mode
- ❌ Event Viewer shows validation error
- ❌ Error message: "AgeCreatedMins cannot be zero for rule 'Test Zero Age'. Use positive (older than) or negative (within last) values."
- ✅ Tray icon shows ERROR state (if tray is running)
- ✅ Error tab shows detailed validation error

**Pass Criteria**: Service rejects zero value with clear error message

---

### CV-004: Reject Out-of-Range Positive Value
**Objective**: Verify that values exceeding +5256000 are rejected

**Test Steps**:
1. Stop RJAutoMoverService
2. Edit config.yaml with excessive value:
```yaml
FileRules:
  - Name: Test Excessive Age
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: .txt
    DestinationFolder: C:\TestData\Destination\Old
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    AgeCreatedMins: 6000000  # Exceeds max (5256000)
```
3. Attempt to start service
4. Check logs and Event Viewer

**Expected Results**:
- ❌ Service fails to start OR enters ERROR mode
- ❌ Error message: "AgeCreatedMins must be between -5256000 and +5256000 (+/- 10 years) for rule 'Test Excessive Age' (found: 6000000)"

**Pass Criteria**: Service rejects out-of-range value

---

### CV-005: Reject Out-of-Range Negative Value
**Objective**: Verify that values below -5256000 are rejected

**Test Steps**:
1. Stop RJAutoMoverService
2. Edit config.yaml:
```yaml
FileRules:
  - Name: Test Excessive Negative
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: .txt
    DestinationFolder: C:\TestData\Destination\Recent
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    LastModifiedMins: -6000000  # Exceeds min (-5256000)
```
3. Attempt to start service

**Expected Results**:
- ❌ Service rejects configuration
- ❌ Error message mentions valid range (-5256000 to +5256000)

**Pass Criteria**: Service rejects out-of-range negative value

---

### CV-006: Reject Multiple Date Criteria in Same Rule
**Objective**: Verify mutual exclusivity of date criteria

**Test Steps**:
1. Stop RJAutoMoverService
2. Edit config.yaml with multiple criteria:
```yaml
FileRules:
  - Name: Test Multiple Criteria
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: .txt
    DestinationFolder: C:\TestData\Destination\Old
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    LastAccessedMins: 1440
    LastModifiedMins: 60  # CONFLICT
```
3. Attempt to start service

**Expected Results**:
- ❌ Service rejects configuration
- ❌ Error message: "Rule 'Test Multiple Criteria': Only one date criteria can be specified. Found: LastAccessedMins, LastModifiedMins. Remove all but one date criteria."

**Pass Criteria**: Service rejects multiple date criteria with detailed error

---

### CV-007: Require Date Criteria for ALL Extension Rules
**Objective**: Verify that ALL rules must have a date criteria

**Test Steps**:
1. Stop RJAutoMoverService
2. Edit config.yaml with ALL rule without date criteria:
```yaml
FileRules:
  - Name: Test ALL Without Date
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: ALL
    DestinationFolder: C:\TestData\Destination\Archive
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    # NO DATE CRITERIA - INVALID
```
3. Attempt to start service

**Expected Results**:
- ❌ Service rejects configuration
- ❌ Error message: "Rule 'Test ALL Without Date': Extension 'ALL' rules MUST have a date criteria (LastAccessedMins, LastModifiedMins, or AgeCreatedMins)"

**Pass Criteria**: Service requires date criteria for ALL rules

---

## Processing Logic Tests

### PL-001: Process Files OLDER Than Specified Time (Positive AgeCreatedMins)
**Objective**: Verify that positive values match files older than X minutes

**Pre-requisites**:
- Create test files with known creation dates (use PowerShell script from Test Data Preparation)

**Test Steps**:
1. Create test files:
   - `old_file.txt` created 3 hours ago (180 minutes)
   - `recent_file.txt` created 30 minutes ago
2. Configure rule with positive AgeCreatedMins:
```yaml
FileRules:
  - Name: Archive Old Files
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: .txt
    DestinationFolder: C:\TestData\Destination\Old
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    AgeCreatedMins: 120  # Files created MORE than 2 hours ago
```
3. Start service (or restart if running)
4. Wait for scan interval (30 seconds)
5. Check destination folder
6. Check service logs for TRACE-level messages

**Expected Results**:
- ✅ `old_file.txt` (180 min old) is moved to destination (matches >= 120 min)
- ❌ `recent_file.txt` (30 min old) remains in source (does NOT match >= 120 min)
- ✅ Log shows: "File matches AgeCreatedMins criteria: old_file.txt (created 180.0 min ago, requires >= 120 min [older than])"
- ✅ Log shows: "File does NOT match AgeCreatedMins criteria: recent_file.txt (created 30.0 min ago, requires >= 120 min [older than])"

**Pass Criteria**: Only files older than 120 minutes are processed

---

### PL-002: Process Files WITHIN Last X Minutes (Negative LastModifiedMins)
**Objective**: Verify that negative values match files within last X minutes

**Pre-requisites**:
- Create test files with known modification dates

**Test Steps**:
1. Create test files:
   - `recent_mod.txt` modified 15 minutes ago
   - `old_mod.txt` modified 45 minutes ago
2. Configure rule with negative LastModifiedMins:
```yaml
FileRules:
  - Name: Process Recent Modifications
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: .txt
    DestinationFolder: C:\TestData\Destination\Recent
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    LastModifiedMins: -30  # Files modified WITHIN last 30 minutes
```
3. Start service
4. Wait for scan
5. Verify results

**Expected Results**:
- ✅ `recent_mod.txt` (15 min ago) is moved (matches <= 30 min)
- ❌ `old_mod.txt` (45 min ago) remains in source (does NOT match <= 30 min)
- ✅ Log shows: "File matches LastModifiedMins criteria: recent_mod.txt (modified 15.0 min ago, requires <= 30 min [within last])"
- ✅ Log shows: "File does NOT match LastModifiedMins criteria: old_mod.txt (modified 45.0 min ago, requires <= 30 min [within last])"

**Pass Criteria**: Only files modified within last 30 minutes are processed

---

### PL-003: Process Files Based on Last Access Time (Positive LastAccessedMins)
**Objective**: Verify LastAccessedMins with positive value

**Test Steps**:
1. Create test files:
   - `accessed_recently.txt` accessed 30 minutes ago
   - `accessed_long_ago.txt` accessed 2 hours ago (120 minutes)
2. Configure rule:
```yaml
FileRules:
  - Name: Archive Unaccessed Files
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: .txt
    DestinationFolder: C:\TestData\Destination\Archive
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    LastAccessedMins: 60  # Files accessed MORE than 1 hour ago
```
3. Start service and verify

**Expected Results**:
- ❌ `accessed_recently.txt` remains (30 min < 60 min threshold)
- ✅ `accessed_long_ago.txt` is moved (120 min >= 60 min threshold)

**Pass Criteria**: Only files accessed more than 60 minutes ago are processed

---

### PL-004: Boundary Test - Exact Match at Threshold (Positive)
**Objective**: Verify behavior when file age exactly matches threshold

**Test Steps**:
1. Create file `exact_match.txt` created exactly 60 minutes ago
2. Configure rule with `AgeCreatedMins: 60`
3. Start service and verify

**Expected Results**:
- ✅ File should be moved (>= threshold includes exact match)
- ✅ Log shows: "File matches AgeCreatedMins criteria: exact_match.txt (created 60.0 min ago, requires >= 60 min [older than])"

**Pass Criteria**: File at exact threshold is processed (>= logic)

---

### PL-005: Boundary Test - Exact Match at Threshold (Negative)
**Objective**: Verify behavior when file age exactly matches negative threshold

**Test Steps**:
1. Create file `exact_within.txt` modified exactly 30 minutes ago
2. Configure rule with `LastModifiedMins: -30`
3. Start service and verify

**Expected Results**:
- ✅ File should be moved (<= threshold includes exact match)
- ✅ Log shows: "File matches LastModifiedMins criteria: exact_within.txt (modified 30.0 min ago, requires <= 30 min [within last])"

**Pass Criteria**: File at exact threshold is processed (<= logic)

---

### PL-006: Boundary Test - Just Below Threshold (Positive)
**Objective**: Verify file just below positive threshold is NOT processed

**Test Steps**:
1. Create file `just_below.txt` created 59 minutes ago
2. Configure rule with `AgeCreatedMins: 60`
3. Start service and verify

**Expected Results**:
- ❌ File remains in source (59 min < 60 min threshold)
- ✅ Log shows: "File does NOT match AgeCreatedMins criteria: just_below.txt (created 59.0 min ago, requires >= 60 min [older than])"

**Pass Criteria**: File below threshold is not processed

---

### PL-007: Boundary Test - Just Above Threshold (Negative)
**Objective**: Verify file just above negative threshold is NOT processed

**Test Steps**:
1. Create file `just_above.txt` modified 31 minutes ago
2. Configure rule with `LastModifiedMins: -30`
3. Start service and verify

**Expected Results**:
- ❌ File remains in source (31 min > 30 min threshold)
- ✅ Log shows: "File does NOT match LastModifiedMins criteria: just_above.txt (modified 31.0 min ago, requires <= 30 min [within last])"

**Pass Criteria**: File above negative threshold is not processed

---

### PL-008: Multiple Rules with Different Date Criteria
**Objective**: Verify multiple rules with different date criteria work independently

**Test Steps**:
1. Create test files with various ages
2. Configure multiple rules:
```yaml
FileRules:
  - Name: Archive Very Old
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: .txt
    DestinationFolder: C:\TestData\Destination\Archive
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    AgeCreatedMins: 10080  # 7 days

  - Name: Process Recent
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: .pdf
    DestinationFolder: C:\TestData\Destination\Recent
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    LastModifiedMins: -60  # Within last hour
```
3. Start service and verify each rule processes correctly

**Expected Results**:
- ✅ .txt files older than 7 days go to Archive
- ✅ .pdf files modified within last hour go to Recent
- ✅ Each rule operates independently

**Pass Criteria**: Multiple rules with different criteria work correctly

---

### PL-009: ALL Extension Rule with Date Criteria
**Objective**: Verify ALL rules process only files matching date criteria

**Test Steps**:
1. Create mixed-age files with various extensions (.txt, .pdf, .jpg, .doc)
2. Configure ALL rule:
```yaml
FileRules:
  - Name: Archive All Old Files
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: ALL
    DestinationFolder: C:\TestData\Destination\Archive
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    AgeCreatedMins: 1440  # Files older than 24 hours
```
3. Start service and verify

**Expected Results**:
- ✅ ALL file types older than 24 hours are moved
- ❌ Recent files (< 24 hours) of any type remain in source
- ✅ Log shows processing for all extensions (.txt, .pdf, .jpg, .doc)

**Pass Criteria**: ALL rule processes all extensions with date filter

---

### PL-010: Date Criteria Priority with Specific + ALL Rules
**Objective**: Verify specific extension rules process before ALL rules

**Test Steps**:
1. Create old .txt files and old .pdf files (both > 7 days old)
2. Configure rules:
```yaml
FileRules:
  # Specific rule for .txt (processes FIRST)
  - Name: Keep Old TXT
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: .txt
    DestinationFolder: C:\TestData\Destination\TXTArchive
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    AgeCreatedMins: 10080  # 7 days

  # ALL rule (processes LAST - catches remaining files)
  - Name: Archive Everything Else Old
    SourceFolder: C:\TestData\Source\TestFiles
    Extension: ALL
    DestinationFolder: C:\TestData\Destination\AllArchive
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    AgeCreatedMins: 10080  # 7 days
```
3. Start service and verify

**Expected Results**:
- ✅ Old .txt files go to TXTArchive (specific rule wins)
- ✅ Old .pdf files go to AllArchive (ALL rule catches them)
- ✅ Specific extension rule processes before ALL rule

**Pass Criteria**: Specific rules take priority over ALL rules

---

## UI Display Tests

### UI-001: Display Positive Date Criteria in Config Tab
**Objective**: Verify positive values display as "older than" in AboutWindow

**Test Steps**:
1. Configure rule with positive AgeCreatedMins:
```yaml
FileRules:
  - Name: Test Display Positive
    SourceFolder: C:\TestData\Source
    Extension: .txt
    DestinationFolder: C:\TestData\Destination
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    AgeCreatedMins: 1440  # 24 hours
```
2. Start service
3. Open RJAutoMoverTray (system tray)
4. Double-click tray icon to open AboutWindow
5. Navigate to "Config" tab
6. Find "Test Display Positive" rule
7. Check date criteria display

**Expected Results**:
- ✅ Date criteria shows: "Age (Created): older than 1 day"
- ✅ Uses "older than" wording for positive value
- ✅ Converts 1440 minutes to "1 day" for readability

**Pass Criteria**: UI shows "older than" for positive values

---

### UI-002: Display Negative Date Criteria in Config Tab
**Objective**: Verify negative values display as "within" in AboutWindow

**Test Steps**:
1. Configure rule with negative LastModifiedMins:
```yaml
FileRules:
  - Name: Test Display Negative
    SourceFolder: C:\TestData\Source
    Extension: .txt
    DestinationFolder: C:\TestData\Destination
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    LastModifiedMins: -60  # Within last 60 minutes
```
2. Open AboutWindow and check Config tab

**Expected Results**:
- ✅ Date criteria shows: "Last Modified: within 1 hr"
- ✅ Uses "within" wording for negative value
- ✅ Converts 60 minutes to "1 hr" for readability

**Pass Criteria**: UI shows "within" for negative values

---

### UI-003: Display Different Date Criteria Types
**Objective**: Verify all three criteria types display correctly

**Test Steps**:
1. Configure three rules with different criteria:
```yaml
FileRules:
  - Name: Test Access
    SourceFolder: C:\TestData\Source
    Extension: .txt
    DestinationFolder: C:\TestData\Destination\Access
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    LastAccessedMins: 1440

  - Name: Test Modified
    SourceFolder: C:\TestData\Source
    Extension: .pdf
    DestinationFolder: C:\TestData\Destination\Modified
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    LastModifiedMins: -30

  - Name: Test Created
    SourceFolder: C:\TestData\Source
    Extension: .doc
    DestinationFolder: C:\TestData\Destination\Created
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    AgeCreatedMins: 10080
```
2. Check Config tab displays

**Expected Results**:
- ✅ "Test Access" shows: "Last Accessed: older than 1 day"
- ✅ "Test Modified" shows: "Last Modified: within 30 min"
- ✅ "Test Created" shows: "Age (Created): older than 7 days"

**Pass Criteria**: All three criteria types display with correct labels

---

### UI-004: Time Formatting in UI
**Objective**: Verify time values are formatted correctly for readability

**Test Steps**:
1. Configure rules with various time values:
   - 5 minutes
   - 30 minutes
   - 60 minutes
   - 120 minutes
   - 1440 minutes
   - 10080 minutes
2. Check Config tab display

**Expected Results**:
- ✅ 5 min → "5 min"
- ✅ 30 min → "30 min"
- ✅ 60 min → "1 hr"
- ✅ 120 min → "2 hr"
- ✅ 1440 min → "1 day"
- ✅ 10080 min → "7 days"

**Pass Criteria**: Time values are human-readable

---

### UI-005: Display Validation Errors in Error Tab
**Objective**: Verify detailed validation errors appear in Error tab

**Test Steps**:
1. Configure invalid rule (zero value):
```yaml
FileRules:
  - Name: Invalid Zero
    SourceFolder: C:\TestData\Source
    Extension: .txt
    DestinationFolder: C:\TestData\Destination
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    AgeCreatedMins: 0  # INVALID
```
2. Save config while service is running
3. Service detects config change and enters ERROR mode
4. Open AboutWindow
5. Navigate to "Error" tab

**Expected Results**:
- ✅ Error tab shows detailed message: "AgeCreatedMins cannot be zero for rule 'Invalid Zero'. Use positive (older than) or negative (within last) values."
- ✅ Tray icon shows error state (red icon)
- ✅ Error is clear and actionable

**Pass Criteria**: Validation errors show in Error tab with details

---

### UI-006: Service Restart Refreshes Config Display
**Objective**: Verify AboutWindow auto-refreshes when service restarts

**Test Steps**:
1. Configure initial rule with AgeCreatedMins: 1440
2. Open AboutWindow and note config display
3. Stop service
4. Edit config to change AgeCreatedMins to 10080
5. Start service
6. Check if AboutWindow updates automatically

**Expected Results**:
- ✅ AboutWindow detects service reconnection
- ✅ Config display automatically refreshes
- ✅ Shows updated value: "older than 7 days" (not "older than 1 day")
- ✅ User does not need to close/reopen window

**Pass Criteria**: Config auto-refreshes on service restart

---

## Edge Cases and Error Handling

### EC-001: File Date Cannot Be Determined
**Objective**: Verify handling when file date properties are inaccessible

**Test Steps**:
1. Create test file
2. Configure rule with date criteria
3. While service is running, lock the file (open in exclusive mode)
4. Observe service behavior

**Expected Results**:
- ✅ Service logs error: "Error checking date criteria for [filename]: [error message]"
- ✅ File is NOT processed (safe default)
- ✅ Service continues processing other files
- ✅ No service crash

**Pass Criteria**: Service handles inaccessible files gracefully

---

### EC-002: System Clock Change Impact
**Objective**: Verify behavior when system time changes during processing

**Test Steps**:
1. Create test file with known creation time
2. Configure rule with date criteria
3. Start service
4. Change system clock forward/backward
5. Observe file processing

**Expected Results**:
- ✅ Service adapts to new system time
- ✅ Date calculations use current system time
- ⚠️ Note: Files may suddenly match/unmatch criteria after clock change

**Pass Criteria**: Service continues operating with new system time

---

### EC-003: Very Large Positive Value (Near Maximum)
**Objective**: Test behavior with maximum allowed positive value

**Test Steps**:
1. Configure rule with `AgeCreatedMins: 5256000` (max = 10 years)
2. Create recent file (< 10 years old)
3. Start service and verify

**Expected Results**:
- ✅ Configuration is valid
- ✅ Service starts successfully
- ❌ Recent files are not moved (correct - not old enough)

**Pass Criteria**: Maximum value works correctly

---

### EC-004: Very Large Negative Value (Near Minimum)
**Objective**: Test behavior with minimum allowed negative value

**Test Steps**:
1. Configure rule with `LastModifiedMins: -5256000` (min = -10 years)
2. Create recent file
3. Start service and verify

**Expected Results**:
- ✅ Configuration is valid
- ✅ Service starts successfully
- ✅ Recent files are moved (within last 10 years)

**Pass Criteria**: Minimum value works correctly

---

### EC-005: File Timestamp in Future
**Objective**: Verify handling when file has future timestamp

**Test Steps**:
1. Create file and set creation time to future:
```powershell
$file = New-Item -Path "C:\TestData\Source\TestFiles\future_file.txt" -ItemType File -Force
$file.CreationTime = (Get-Date).AddDays(1)  # Tomorrow
```
2. Configure rule with positive AgeCreatedMins
3. Start service and observe

**Expected Results**:
- ❌ File with future timestamp should NOT match positive criteria (age is negative)
- ✅ Service handles gracefully without error
- ✅ Log may show negative age value in TRACE output

**Pass Criteria**: Future timestamps handled correctly

---

### EC-006: Rapid Config Changes with Date Criteria
**Objective**: Verify config change detection with date criteria modifications

**Test Steps**:
1. Start service with rule containing `AgeCreatedMins: 1440`
2. While running, edit config to change to `AgeCreatedMins: 60`
3. Save config
4. Observe service behavior

**Expected Results**:
- ✅ Service detects config change
- ✅ Service enters ERROR mode
- ✅ Status shows: "Configuration file changed externally. Service requires restart for changes to take effect."
- ✅ All processing stops
- ✅ Error tab shows detailed message

**Pass Criteria**: Config change protection works with date criteria

---

## Integration Tests

### IT-001: End-to-End File Archival Workflow (Positive Criteria)
**Objective**: Complete workflow for archiving old files

**Scenario**: Archive log files older than 7 days

**Test Steps**:
1. Create test scenario:
   - 5 log files created 8 days ago
   - 5 log files created 3 days ago
   - 5 log files created today
2. Configure archival rule:
```yaml
FileRules:
  - Name: Archive Old Logs
    SourceFolder: C:\TestData\Source\Logs
    Extension: .log
    DestinationFolder: C:\TestData\Destination\LogArchive
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip
    AgeCreatedMins: 10080  # 7 days
```
3. Start service
4. Monitor for 2 scan intervals
5. Verify results

**Expected Results**:
- ✅ 5 files (8 days old) moved to LogArchive
- ❌ 10 files (< 7 days) remain in source
- ✅ Activity tab shows 5 transfers
- ✅ Logs show successful moves

**Pass Criteria**: Only files older than 7 days are archived

---

### IT-002: End-to-End Recent File Processing (Negative Criteria)
**Objective**: Complete workflow for processing recent modifications

**Scenario**: Move recently modified reports to processing folder

**Test Steps**:
1. Create test scenario:
   - 5 reports modified 10 minutes ago
   - 5 reports modified 50 minutes ago
2. Configure rule:
```yaml
FileRules:
  - Name: Process Recent Reports
    SourceFolder: C:\TestData\Source\Reports
    Extension: .pdf
    DestinationFolder: C:\TestData\Destination\Processing
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: overwrite
    LastModifiedMins: -30  # Within last 30 minutes
```
3. Start service and verify

**Expected Results**:
- ✅ 5 reports (10 min ago) moved to Processing
- ❌ 5 reports (50 min ago) remain in source
- ✅ Activity history shows 5 successful transfers

**Pass Criteria**: Only recently modified files are processed

---

### IT-003: Multi-User Scenario with Date Filtering
**Objective**: Verify date filtering works across multiple user sessions

**Test Steps**:
1. Configure rule with date criteria
2. Log in as User A, start tray
3. Log in as User B (separate session), start tray
4. Create test files in shared source folder
5. Verify both users see correct activity

**Expected Results**:
- ✅ Service processes files based on date criteria (not user)
- ✅ Both trays show same file transfer activity
- ✅ Activity history visible to all users

**Pass Criteria**: Date filtering independent of user sessions

---

### IT-004: Performance Test with Large File Set
**Objective**: Verify performance with many files and date filtering

**Test Steps**:
1. Create 1000 files with random creation dates (spread over 30 days)
2. Configure rule with `AgeCreatedMins: 14400` (10 days)
3. Start service
4. Monitor memory usage and scan performance
5. Check logs for completion time

**Expected Results**:
- ✅ Service completes scan within reasonable time (< 5 seconds)
- ✅ Memory usage stays under limit
- ✅ Correct subset of files processed
- ✅ No performance degradation

**Pass Criteria**: Date filtering performs well with large file sets

---

## Test Execution Summary Template

### Test Run Information
- **Tester Name**: _______________
- **Test Date**: _______________
- **Application Version**: 0.9.6.1
- **Test Environment**: Windows _____ (version)
- **.NET Version**: 10.0

### Test Results Summary

| Test ID | Test Name | Status | Notes |
|---------|-----------|--------|-------|
| CV-001 | Validate Positive AgeCreatedMins | ☐ Pass ☐ Fail | |
| CV-002 | Validate Negative LastModifiedMins | ☐ Pass ☐ Fail | |
| CV-003 | Reject Zero Value | ☐ Pass ☐ Fail | |
| CV-004 | Reject Out-of-Range Positive | ☐ Pass ☐ Fail | |
| CV-005 | Reject Out-of-Range Negative | ☐ Pass ☐ Fail | |
| CV-006 | Reject Multiple Criteria | ☐ Pass ☐ Fail | |
| CV-007 | Require Date for ALL Rules | ☐ Pass ☐ Fail | |
| PL-001 | Process OLDER Than (Positive) | ☐ Pass ☐ Fail | |
| PL-002 | Process WITHIN Last (Negative) | ☐ Pass ☐ Fail | |
| PL-003 | Last Access Time Processing | ☐ Pass ☐ Fail | |
| PL-004 | Boundary - Exact Match (Positive) | ☐ Pass ☐ Fail | |
| PL-005 | Boundary - Exact Match (Negative) | ☐ Pass ☐ Fail | |
| PL-006 | Boundary - Just Below | ☐ Pass ☐ Fail | |
| PL-007 | Boundary - Just Above | ☐ Pass ☐ Fail | |
| PL-008 | Multiple Rules Different Criteria | ☐ Pass ☐ Fail | |
| PL-009 | ALL Rule with Date Criteria | ☐ Pass ☐ Fail | |
| PL-010 | Date Criteria Priority | ☐ Pass ☐ Fail | |
| UI-001 | Display Positive Criteria | ☐ Pass ☐ Fail | |
| UI-002 | Display Negative Criteria | ☐ Pass ☐ Fail | |
| UI-003 | Display All Criteria Types | ☐ Pass ☐ Fail | |
| UI-004 | Time Formatting | ☐ Pass ☐ Fail | |
| UI-005 | Validation Errors in Error Tab | ☐ Pass ☐ Fail | |
| UI-006 | Auto-Refresh on Service Restart | ☐ Pass ☐ Fail | |
| EC-001 | Inaccessible File Dates | ☐ Pass ☐ Fail | |
| EC-002 | System Clock Change | ☐ Pass ☐ Fail | |
| EC-003 | Maximum Positive Value | ☐ Pass ☐ Fail | |
| EC-004 | Minimum Negative Value | ☐ Pass ☐ Fail | |
| EC-005 | Future Timestamp | ☐ Pass ☐ Fail | |
| EC-006 | Rapid Config Changes | ☐ Pass ☐ Fail | |
| IT-001 | E2E File Archival | ☐ Pass ☐ Fail | |
| IT-002 | E2E Recent File Processing | ☐ Pass ☐ Fail | |
| IT-003 | Multi-User Scenario | ☐ Pass ☐ Fail | |
| IT-004 | Performance Large File Set | ☐ Pass ☐ Fail | |

### Overall Summary
- **Total Tests**: 34
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

## Appendix A: Log File Locations

- **Service Logs**: `C:\ProgramData\RJAutoMover\Logs\service-*.log`
- **Tray Logs**: `C:\ProgramData\RJAutoMover\Logs\tray-*.log`
- **Event Viewer**: Application log → Source: RJAutoMoverService

## Appendix B: Common Log Messages

### Successful Date Match (Positive)
```
[TRACE] File matches AgeCreatedMins criteria: testfile.txt (created 180.0 min ago, requires >= 120 min [older than])
```

### Failed Date Match (Positive)
```
[TRACE] File does NOT match AgeCreatedMins criteria: testfile.txt (created 30.0 min ago, requires >= 120 min [older than])
```

### Successful Date Match (Negative)
```
[TRACE] File matches LastModifiedMins criteria: testfile.txt (modified 15.0 min ago, requires <= 30 min [within last])
```

### Failed Date Match (Negative)
```
[TRACE] File does NOT match LastModifiedMins criteria: testfile.txt (modified 45.0 min ago, requires <= 30 min [within last])
```

---

**END OF TEST PLAN**
