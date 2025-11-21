# TestPlans Directory Index

Comprehensive testing framework for RJAutoMover date filtering functionality.

## 📂 Files in This Directory

| File | Purpose | Usage |
|------|---------|-------|
| **Create-TestFiles.ps1** | Generate 1000 test files with varied ages/types | `.\Create-TestFiles.ps1` |
| **Analyze-TestResults.ps1** | Analyze test results after running rules | `.\Analyze-TestResults.ps1 -RuleName "Test-OldDocs7Days"` |
| **Run-ComprehensiveTest.ps1** | Automated test suite runner (all rules) | `.\Run-ComprehensiveTest.ps1` |
| **README.md** | Complete testing documentation | Read for detailed instructions |
| **QUICK_REFERENCE.md** | Quick command reference | Read for common commands |
| **INDEX.md** | This file - directory overview | You are here |
| **Create-TestFiles-Legacy.ps1** | Old test script (deprecated) | Reference only |

## 🚀 Quick Start (3 Steps)

### For Manual Testing

```powershell
# 1. Generate test data
.\Create-TestFiles.ps1

# 2. Test a specific rule
# - Copy C:\RJAutoMover_TestData\test-config.yaml to C:\Program Files\RJAutoMover\config.yaml
# - Enable ONE rule (IsActive: true)
# - Restart service
Restart-Service RJAutoMoverService

# 3. Analyze results
.\Analyze-TestResults.ps1 -RuleName "Test-OldDocs7Days"
```

### For Automated Testing

```powershell
# Run entire test suite (requires admin)
.\Run-ComprehensiveTest.ps1
```

## 📊 What Gets Tested

The test suite validates:

✅ **DateFilter Format**: All three types (LA, LM, FC)
✅ **Positive Filters**: Files OLDER than threshold (+ sign)
✅ **Negative Filters**: Files WITHIN last threshold (- sign)
✅ **Extension Matching**: Specific extensions and OTHERS
✅ **Timestamp Variations**: 5 different timestamp patterns
✅ **Age Ranges**: 24 different time periods (0 min to 365 days)
✅ **File Types**: 45+ different file extensions
✅ **Edge Cases**: Recent files, ancient files, mixed timestamps

## 📁 Test Data Generated

**Location**: `C:\RJAutoMover_TestData\`

**Structure**:
```
C:\RJAutoMover_TestData\
├── Source\                    (1000 test files)
├── Destination\
│   ├── Recent\
│   ├── Old7Days\
│   ├── Old30Days\
│   ├── Old90Days\
│   ├── Videos\
│   ├── Images\
│   ├── Documents\
│   ├── Archives\
│   ├── Code\
│   └── Others\
├── test-config.yaml           (Test configuration)
├── FILE_SUMMARY.csv           (File metadata)
└── TEST_REPORT_*.txt          (Generated after Run-ComprehensiveTest.ps1)
```

## 🧪 Test Rules

| # | Rule Name | Filter | Tests |
|---|-----------|--------|-------|
| 1 | Test-RecentAccess | LA:-60 | Recent access (within 1 hour) |
| 2 | Test-OldDocs7Days | FC:+10080 | Old files (>7 days) |
| 3 | Test-VeryOld30Days | FC:+43200 | Very old files (>30 days) |
| 4 | Test-Ancient90Days | FC:+129600 | Ancient files (>90 days) |
| 5 | Test-RecentModVideos | LM:-120 | Recent modifications (within 2 hours) |
| 6 | Test-OldImages1Day | FC:+1440 | Old images (>1 day) |
| 7 | Test-StaleDocsNoMod14Days | LM:+20160 | Stale docs (not modified in 14 days) |
| 8 | Test-OldArchives60Days | FC:+86400 | Old archives (>60 days) |
| 9 | Test-StaleCodeNoAccess30Days | LA:+43200 | Stale code (not accessed in 30 days) |
| 10 | Test-OthersVeryOld180Days | FC:+259200 | OTHERS extension (>180 days) |

## 📖 Documentation Files

| Document | Content |
|----------|---------|
| **README.md** | Complete testing guide with examples, troubleshooting, best practices |
| **QUICK_REFERENCE.md** | One-liner commands, cheat sheets, common tasks |
| **INDEX.md** | This overview file |

## 🎯 Common Workflows

### Test Single Rule
1. Generate test data: `.\Create-TestFiles.ps1`
2. Deploy test config
3. Enable one rule
4. Restart service
5. Analyze: `.\Analyze-TestResults.ps1 -RuleName "Test-OldDocs7Days"`

### Test All Rules (Automated)
1. Run: `.\Run-ComprehensiveTest.ps1` (requires admin)
2. Wait for completion (~20 minutes for 10 rules with 120s intervals)
3. Review report in `C:\RJAutoMover_TestData\TEST_REPORT_*.txt`

### Re-Test After Code Changes
1. Rebuild RJAutoMover
2. Re-deploy service
3. Run: `.\Run-ComprehensiveTest.ps1 -SkipGeneration` (uses existing data)

### Cleanup
```powershell
Remove-Item "C:\RJAutoMover_TestData" -Recurse -Force
```

## ⚙️ Script Parameters

### Create-TestFiles.ps1
```powershell
.\Create-TestFiles.ps1 -BaseFolder "D:\TestData" -TotalFiles 2000
```
- `BaseFolder`: Location for test data (default: C:\RJAutoMover_TestData)
- `TotalFiles`: Number of files to generate (default: 1000)

### Analyze-TestResults.ps1
```powershell
.\Analyze-TestResults.ps1 -RuleName "Test-OldDocs7Days" -Detailed
```
- `BaseFolder`: Test data location (default: C:\RJAutoMover_TestData)
- `RuleName`: Specific rule to analyze (default: all active rules)
- `Detailed`: Show full file lists (default: first 5 files only)

### Run-ComprehensiveTest.ps1
```powershell
.\Run-ComprehensiveTest.ps1 -SkipGeneration -RulesTimeoutSec 60
```
- `SkipGeneration`: Skip file generation, use existing data
- `RulesTimeoutSec`: Wait time after activating each rule (default: 120)
- `BaseFolder`: Test data location (default: C:\RJAutoMover_TestData)

## 🐛 Troubleshooting

### "Service failed to start"
- Check Event Viewer for errors
- Review config validation in logs
- Verify DateFilter format is correct

### "No files moved"
- Ensure rule is active (`IsActive: true`)
- Wait for scan interval to elapse
- Check service logs for errors

### "Wrong files moved"
- Verify DateFilter logic (+ vs -)
- Check actual file timestamps
- Review analyzer output with `-Detailed`

### "Analyzer shows errors"
- Check if multiple rules are enabled (should be ONE at a time)
- Verify destination folder path is correct
- Review service logs for processing errors

## 📞 Support

For issues with the test scripts:
1. Check README.md for detailed documentation
2. Review service logs: `C:\ProgramData\RJAutoMover\Logs`
3. Check file timestamps: `Get-Item <file> | Select CreationTime, LastWriteTime, LastAccessTime`
4. Run analyzer with `-Detailed` flag for full output

## 📄 Version Info

- **Test Framework Version**: 1.0
- **RJAutoMover Version**: 0.9.6
- **DateFilter Format**: TYPE:SIGN:MINUTES
- **Generated**: 2025-01-21

---

**Quick Links**:
- [Main README](../README.md)
- [Testing Documentation](README.md)
- [Quick Reference](QUICK_REFERENCE.md)
