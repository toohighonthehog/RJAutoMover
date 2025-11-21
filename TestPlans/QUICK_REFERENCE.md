# Quick Reference Guide

## One-Command Test Execution

### Generate Test Data
```powershell
cd c:\Users\rjohnson\source\repos\RJAutoMover\TestPlans
.\Create-TestFiles.ps1
```

### Deploy & Test Single Rule
```powershell
# 1. Backup config
Copy-Item "C:\Program Files\RJAutoMover\config.yaml" "C:\Program Files\RJAutoMover\config.yaml.backup"

# 2. Deploy test config
Copy-Item "C:\RJAutoMover_TestData\test-config.yaml" "C:\Program Files\RJAutoMover\config.yaml"

# 3. Edit config and enable ONE rule (IsActive: true)
notepad "C:\Program Files\RJAutoMover\config.yaml"

# 4. Restart service
Restart-Service RJAutoMoverService

# 5. Wait 30 seconds, then analyze
Start-Sleep -Seconds 30
.\Analyze-TestResults.ps1 -RuleName "Test-OldDocs7Days"
```

### Restore Production Config
```powershell
Stop-Service RJAutoMoverService
Copy-Item "C:\Program Files\RJAutoMover\config.yaml.backup" "C:\Program Files\RJAutoMover\config.yaml"
Start-Service RJAutoMoverService
```

## Quick DateFilter Examples

| Want to Move | DateFilter | Explanation |
|-------------|------------|-------------|
| Files accessed TODAY | `LA:-1440` | Within last 1 day (1440 min) |
| Files NOT accessed this month | `LA:+43200` | Older than 30 days (43200 min) |
| Files modified this week | `LM:-10080` | Within last 7 days (10080 min) |
| Files NOT modified this year | `LM:+525600` | Older than 365 days (525600 min) |
| Files created this hour | `FC:-60` | Within last 60 minutes |
| Files created over 90 days ago | `FC:+129600` | Older than 90 days (129600 min) |

## Common Time Values

| Period | Minutes |
|--------|---------|
| 1 hour | 60 |
| 1 day | 1440 |
| 1 week | 10080 |
| 1 month | 43200 |
| 3 months | 129600 |
| 1 year | 525600 |

## Test Rule Cheat Sheet

| Test# | Rule Name | Filter | What It Tests |
|-------|-----------|--------|---------------|
| 1 | Test-RecentAccess | LA:-60 | NEGATIVE LastAccessed (within 1 hour) |
| 2 | Test-OldDocs7Days | FC:+10080 | POSITIVE FileCreated (>7 days) |
| 3 | Test-VeryOld30Days | FC:+43200 | POSITIVE FileCreated (>30 days) |
| 4 | Test-Ancient90Days | FC:+129600 | POSITIVE FileCreated (>90 days) |
| 5 | Test-RecentModVideos | LM:-120 | NEGATIVE LastModified (within 2 hours) |
| 6 | Test-OldImages1Day | FC:+1440 | POSITIVE FileCreated (>1 day) |
| 7 | Test-StaleDocsNoMod14Days | LM:+20160 | POSITIVE LastModified (>14 days) |
| 8 | Test-OldArchives60Days | FC:+86400 | POSITIVE FileCreated (>60 days) |
| 9 | Test-StaleCodeNoAccess30Days | LA:+43200 | POSITIVE LastAccessed (>30 days) |
| 10 | Test-OthersVeryOld180Days | FC:+259200 | OTHERS extension (>180 days) |

## Analyzer Commands

```powershell
# Test specific rule
.\Analyze-TestResults.ps1 -RuleName "Test-OldDocs7Days"

# Test all active rules
.\Analyze-TestResults.ps1

# Show detailed file lists
.\Analyze-TestResults.ps1 -Detailed
```

## Troubleshooting One-Liners

```powershell
# Check service status
Get-Service RJAutoMoverService

# View recent service logs
Get-Content "C:\ProgramData\RJAutoMover\Logs\*service*.log" | Select-Object -Last 50

# Check file timestamps
Get-Item "C:\RJAutoMover_TestData\Source\*.txt" | Select-Object Name, CreationTime, LastWriteTime, LastAccessTime

# Count files in source
(Get-ChildItem "C:\RJAutoMover_TestData\Source").Count

# Count files in destination
(Get-ChildItem "C:\RJAutoMover_TestData\Destination\Old7Days").Count

# View config
Get-Content "C:\Program Files\RJAutoMover\config.yaml"
```

## Quick Cleanup

```powershell
Stop-Service RJAutoMoverService
Copy-Item "C:\Program Files\RJAutoMover\config.yaml.backup" "C:\Program Files\RJAutoMover\config.yaml"
Remove-Item "C:\RJAutoMover_TestData" -Recurse -Force
Start-Service RJAutoMoverService
```
