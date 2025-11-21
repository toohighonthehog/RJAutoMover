# RJAutoMover Test Plans

This folder contains comprehensive testing scripts for validating RJAutoMover's date filtering functionality with the new `DateFilter` format.

## 📁 Contents

| File | Description |
|------|-------------|
| `Create-TestFiles.ps1` | Generates 1000 test files with various ages, extensions, and timestamp patterns |
| `Analyze-TestResults.ps1` | Analyzes source and destination folders to verify correct file processing |
| `README.md` | This file - testing documentation |

## 🚀 Quick Start

### Step 1: Generate Test Data

```powershell
cd c:\Users\rjohnson\source\repos\RJAutoMover\TestPlans
.\Create-TestFiles.ps1
```

This creates:
- **C:\RJAutoMover_TestData\Source** - 1000 test files
- **C:\RJAutoMover_TestData\Destination** - Multiple destination folders
- **C:\RJAutoMover_TestData\test-config.yaml** - Test configuration
- **C:\RJAutoMover_TestData\FILE_SUMMARY.csv** - File metadata

### Step 2: Deploy Test Configuration

```powershell
# Backup current config
Copy-Item "C:\Program Files\RJAutoMover\config.yaml" "C:\Program Files\RJAutoMover\config.yaml.backup"

# Deploy test config
Copy-Item "C:\RJAutoMover_TestData\test-config.yaml" "C:\Program Files\RJAutoMover\config.yaml"
```

### Step 3: Run Tests

1. Edit `C:\Program Files\RJAutoMover\config.yaml`
2. Enable **ONE rule at a time** (`IsActive: true`)
3. Restart RJAutoMoverService: `Restart-Service RJAutoMoverService`
4. Wait for scan interval (15-60 seconds)
5. Analyze results:

```powershell
.\Analyze-TestResults.ps1 -RuleName "Test-OldDocs7Days"
```

### Step 4: Restore Production Config

```powershell
Stop-Service RJAutoMoverService
Copy-Item "C:\Program Files\RJAutoMover\config.yaml.backup" "C:\Program Files\RJAutoMover\config.yaml"
Start-Service RJAutoMoverService
```

## 📊 Test Data Details

### File Distribution

The test data includes **1000 files** with:

| Age Range | Approximate Count | Minutes Ago |
|-----------|------------------|-------------|
| Now | 50 | 0 |
| 2 minutes | 40 | 2 |
| 5 minutes | 40 | 5 |
| 10 minutes | 40 | 10 |
| 15 minutes | 40 | 15 |
| 30 minutes | 50 | 30 |
| 45 minutes | 40 | 45 |
| 1 hour | 50 | 60 |
| 2 hours | 50 | 120 |
| 6 hours | 50 | 360 |
| 12 hours | 50 | 720 |
| 1 day | 60 | 1440 |
| 2 days | 50 | 2880 |
| 3 days | 50 | 4320 |
| 5 days | 40 | 7200 |
| 7 days | 60 | 10080 |
| 14 days | 50 | 20160 |
| 21 days | 40 | 30240 |
| 30 days | 60 | 43200 |
| 45 days | 40 | 64800 |
| 60 days | 40 | 86400 |
| 90 days | 50 | 129600 |
| 180 days | 30 | 259200 |
| 365 days | 20 | 525600 |

### File Types

Files are distributed across multiple extension groups:

- **Documents**: .txt, .doc, .docx, .pdf, .rtf, .odt
- **Spreadsheets**: .xls, .xlsx, .csv, .ods
- **Presentations**: .ppt, .pptx, .odp
- **Images**: .jpg, .jpeg, .png, .gif, .bmp, .svg, .tif, .webp
- **Videos**: .mp4, .avi, .mkv, .mov, .wmv, .flv, .webm
- **Audio**: .mp3, .wav, .flac, .aac, .ogg, .wma
- **Archives**: .zip, .rar, .7z, .tar, .gz, .bz2
- **Code**: .cs, .js, .py, .java, .cpp, .h, .ts, .go, .rs
- **Web**: .html, .htm, .css, .scss, .json, .xml
- **Data**: .yaml, .yml, .toml, .ini, .cfg
- **Other**: .log, .tmp, .bak, .dat, .bin

### Timestamp Patterns

Files have 5 different timestamp variation patterns:

1. **All timestamps identical** (40%): Created = Modified = Accessed
2. **Created old, modified recently** (20%): Old creation, recent modification, very recent access
3. **Created old, accessed recently** (20%): Old creation/modification, recent access
4. **All timestamps different** (10%): Created, modified, accessed at different times
5. **Created recently, modified old** (10%): Recent creation, old modification (unusual but valid)

This variety ensures comprehensive testing of all three date criteria (LA, LM, FC).

## 🧪 Test Rules

The test configuration includes 10 rules to test all date filtering scenarios:

### TEST 1: Recent Files (Within Last 1 Hour)
```yaml
DateFilter: "LA:-60"  # Within last hour (NEGATIVE LastAccessed)
```
**Tests**: Files accessed within last 60 minutes
**Expected**: Files with recent access times move to `Destination\Recent`

### TEST 2: Old Documents (Older Than 7 Days)
```yaml
DateFilter: "FC:+10080"  # Older than 7 days (POSITIVE FileCreated)
```
**Tests**: Files created more than 7 days ago
**Expected**: Old documents move to `Destination\Old7Days`

### TEST 3: Very Old Files (Older Than 30 Days)
```yaml
DateFilter: "FC:+43200"  # Older than 30 days (POSITIVE FileCreated)
```
**Tests**: Files created more than 30 days ago
**Expected**: Very old spreadsheets/presentations move to `Destination\Old30Days`

### TEST 4: Ancient Files (Older Than 90 Days)
```yaml
DateFilter: "FC:+129600"  # Older than 90 days (POSITIVE FileCreated)
```
**Tests**: Files created more than 90 days ago
**Expected**: Ancient logs/temp files move to `Destination\Old90Days`

### TEST 5: Recently Modified Videos (Within Last 2 Hours)
```yaml
DateFilter: "LM:-120"  # Within last 2 hours (NEGATIVE LastModified)
```
**Tests**: Files modified within last 2 hours
**Expected**: Recently modified videos move to `Destination\Videos`

### TEST 6: Old Images (Created Older Than 1 Day)
```yaml
DateFilter: "FC:+1440"  # Older than 1 day (POSITIVE FileCreated)
```
**Tests**: Files created more than 1 day ago
**Expected**: Old images move to `Destination\Images`

### TEST 7: Stale Documents (Not Modified in Last 14 Days)
```yaml
DateFilter: "LM:+20160"  # Older than 14 days (POSITIVE LastModified)
```
**Tests**: Files NOT modified in last 14 days
**Expected**: Stale documents move to `Destination\Documents`

### TEST 8: Old Archives (Created Older Than 60 Days)
```yaml
DateFilter: "FC:+86400"  # Older than 60 days (POSITIVE FileCreated)
```
**Tests**: Files created more than 60 days ago
**Expected**: Old archives move to `Destination\Archives`

### TEST 9: Stale Code (Not Accessed in Last 30 Days)
```yaml
DateFilter: "LA:+43200"  # Older than 30 days (POSITIVE LastAccessed)
```
**Tests**: Files NOT accessed in last 30 days
**Expected**: Stale code files move to `Destination\Code`

### TEST 10: Catch-All for Very Old Files (OTHERS)
```yaml
Extension: OTHERS
DateFilter: "FC:+259200"  # Older than 180 days (POSITIVE FileCreated)
```
**Tests**: Extension "OTHERS" with date filter requirement
**Expected**: Any file type not caught by other rules, older than 180 days, moves to `Destination\Others`

## 📈 Analyzing Results

### Basic Analysis

```powershell
# Analyze a specific rule
.\Analyze-TestResults.ps1 -RuleName "Test-OldDocs7Days"

# Analyze all active rules
.\Analyze-TestResults.ps1
```

### Detailed Analysis

```powershell
# Show full file lists for all issues
.\Analyze-TestResults.ps1 -Detailed
```

### Analysis Output

The analyzer will report:

- ✅ **Correctly moved**: Files that match the rule criteria and were moved
- ❌ **Incorrectly moved**: Files in destination that don't match criteria
- ⚠️ **Should have moved**: Files in source that match criteria but weren't moved

Example output:
```
============================================================================
 Analyzing Rule: Test-OldDocs7Days
============================================================================
 -> Extension:     .txt|.doc|.docx|.rtf|.odt|.pdf
 -> DateFilter:    FC:+10080
 -> Destination:   C:\RJAutoMover_TestData\Destination\Old7Days
 -> IsActive:      True

 -> Filter Logic: Files where FileCreated is OLDER than 10080 minutes

 -> Files in destination: 89

 -> Results:
  Correctly moved:       89
  Incorrectly moved:     0
  Should have moved:     0

[OK] All files processed correctly!
```

## 🔍 Troubleshooting

### No files are being moved

**Possible causes:**
1. Service not running: Check `services.msc`
2. Rule not active: Verify `IsActive: true` in config
3. Scan interval not elapsed: Wait for the configured interval
4. Service errors: Check logs

**Solution:**
```powershell
Get-Service RJAutoMoverService
Get-Content "C:\ProgramData\RJAutoMover\Logs\*.log" | Select-Object -Last 50
```

### Wrong files are being moved

**Possible causes:**
1. DateFilter logic confusion (positive vs negative)
2. Multiple timestamp variations
3. File timestamps modified after creation

**Solution:**
```powershell
# Check actual file timestamps
Get-Item "C:\RJAutoMover_TestData\Source\test_0123_*" |
    Select-Object Name, CreationTime, LastWriteTime, LastAccessTime
```

### Service won't start

**Possible causes:**
1. Config validation errors
2. Invalid DateFilter format
3. YAML syntax errors

**Solution:**
```powershell
# Check Event Viewer
Get-EventLog -LogName Application -Source RJAutoMoverService -Newest 10

# Check service logs
Get-Content "C:\ProgramData\RJAutoMover\Logs\*service*.log" | Select-Object -Last 100
```

### Tray shows Error icon

**Solution:**
1. Open RJAutoMover tray application
2. Click "View About / Status"
3. Check **Error tab** for detailed error message
4. Fix config.yaml based on error details
5. Restart service

## 📝 Best Practices

### Testing Individual Rules

Always test ONE rule at a time:

1. ✅ **DO**: Enable one rule, restart service, analyze results, disable rule
2. ❌ **DON'T**: Enable multiple rules simultaneously

### Timestamp Awareness

Be aware that file timestamps change:
- **LastAccessTime** updates when you view/open a file
- **LastWriteTime** updates when you modify a file
- **CreationTime** is set when file is created

After generating test data, timestamps age naturally. A file generated "1 day ago" will become "2 days ago" after 24 hours.

### Re-generating Test Data

To start fresh:

```powershell
Remove-Item "C:\RJAutoMover_TestData" -Recurse -Force
.\Create-TestFiles.ps1
```

## 🎯 DateFilter Format Reference

### Format

```
TYPE:SIGN:MINUTES
```

### Components

- **TYPE**:
  - `LA` = Last Accessed
  - `LM` = Last Modified
  - `FC` = File Created

- **SIGN**:
  - `+` = Older than (positive - files NOT accessed/modified/created in last X minutes)
  - `-` = Within last (negative - files accessed/modified/created within last X minutes)

- **MINUTES**: Integer value (1-5256000, representing up to 10 years)

### Examples

| DateFilter | Meaning |
|------------|---------|
| `LA:+43200` | Files NOT accessed in last 43200 minutes (30 days) - older files |
| `LA:-1440` | Files accessed within last 1440 minutes (1 day) - recent files |
| `LM:+10080` | Files NOT modified in last 10080 minutes (7 days) |
| `FC:+4320` | Files created more than 4320 minutes (3 days) ago |
| `FC:-60` | Files created within last 60 minutes (1 hour) |

### Time Conversions

| Period | Minutes |
|--------|---------|
| 1 hour | 60 |
| 6 hours | 360 |
| 12 hours | 720 |
| 1 day | 1440 |
| 3 days | 4320 |
| 7 days | 10080 |
| 14 days | 20160 |
| 30 days | 43200 |
| 60 days | 86400 |
| 90 days | 129600 |
| 180 days | 259200 |
| 365 days | 525600 |

## 📚 Additional Resources

- **Main Documentation**: [../README.md](../README.md)
- **Date Filtering Logic**: [../Notes/FileFilteringLogic.md](../Notes/FileFilteringLogic.md)
- **Service Logs**: `C:\ProgramData\RJAutoMover\Logs`
- **Activity History**: `C:\Program Files\RJAutoMover\Data\ActivityHistory.db`

## 🧹 Cleanup

After testing is complete:

```powershell
# Stop service
Stop-Service RJAutoMoverService

# Restore production config
Copy-Item "C:\Program Files\RJAutoMover\config.yaml.backup" "C:\Program Files\RJAutoMover\config.yaml"

# Remove test data
Remove-Item "C:\RJAutoMover_TestData" -Recurse -Force

# Start service
Start-Service RJAutoMoverService
```

## 🐛 Reporting Issues

If tests reveal bugs or unexpected behavior:

1. Capture service logs
2. Capture tray logs
3. Note exact rule configuration used
4. Note expected vs actual behavior
5. Provide sample file details (timestamps, name, extension)
6. Run analysis with `-Detailed` flag and capture output

## 📄 License

These test scripts are part of RJAutoMover and are subject to the same license terms.

---

**Generated**: 2025-01-21
**RJAutoMover Version**: 0.9.6
**Test Framework Version**: 1.0
