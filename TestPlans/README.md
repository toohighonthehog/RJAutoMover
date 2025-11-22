# RJAutoMover Test Plans

**Purpose:** Manual testing framework for validating file processing rules

## Overview

This folder contains a 5-step manual testing framework that validates RJAutoMover's file processing logic by generating test files, creating test configurations, predicting expected outcomes, and analyzing actual results against predictions.

## Quick Start

```powershell
cd TestPlans

# Step 1: Generate 1000 test files
.\1-Generate-TestFiles.ps1

# Step 2: Generate test configuration
.\2-Generate-TestConfig.ps1

# Step 3: Generate predictions
.\3-Generate-Predictions.ps1

# Step 4: Deploy config manually (follow instructions)
.\4-Deploy-And-Wait.ps1

# Step 5: Analyze results
.\5-Analyze-Results.ps1 -GenerateHTML
```

## Framework Components

### Test Scripts (Run in Order)

| Script | Purpose | Manual Intervention |
|--------|---------|---------------------|
| **1-Generate-TestFiles.ps1** | Creates 1000 test files with varied ages and extensions | None |
| **2-Generate-TestConfig.ps1** | Generates valid test configuration with 10 rules | None |
| **3-Generate-Predictions.ps1** | Predicts expected outcomes using service logic simulation | None |
| **4-Deploy-And-Wait.ps1** | Provides deployment instructions and validation | **Required - deploy config and restart service** |
| **5-Analyze-Results.ps1** | Compares actual vs predicted results, generates reports | None |

### Documentation

- **[TESTING-GUIDE.md](TESTING-GUIDE.md)** - Complete testing guide with detailed instructions, troubleshooting, and advanced usage

### Generated Artifacts

| File | Description |
|------|-------------|
| `test-files-manifest.yaml` | Metadata for all generated test files |
| `test-config.yaml` | Test configuration with 10 non-overlapping rules |
| `test-predictions.yaml` | Predicted outcomes for each test file |
| `config-backup.yaml` | Backup of production configuration |
| `Results\v{timestamp}\*` | Versioned test results (YAML + HTML) |

## Test Coverage

The framework tests:
- **10 rules:** 9 specific extension rules + 1 OTHERS catch-all rule
- **DateFilter types:** LA (Last Accessed), LM (Last Modified), FC (File Created)
- **DateFilter directions:** `+` (older than), `-` (within last)
- **18 file extensions:** .txt, .pdf, .doc, .jpg, .mp4, .zip, .cs, .html, etc.
- **24 age ranges:** 0 minutes to 365 days old
- **~85% coverage:** Test rules designed to process ~850 of 1000 files

## Step 4: Manual Deployment

Step 4 requires human interaction to deploy the test configuration and run the service. You must:

1. **Backup production config:**
   ```powershell
   Copy-Item 'C:\Program Files\RJAutoMover\config.yaml' 'config-backup.yaml' -Force
   ```

2. **Deploy test config:**
   ```powershell
   Copy-Item 'test-config.yaml' 'C:\Program Files\RJAutoMover\config.yaml' -Force
   ```

3. **Restart service:**
   ```powershell
   Restart-Service -Name 'RJAutoMoverService' -Force
   ```

4. **Monitor and wait:**
   - Watch service logs for file operations
   - Wait 5-10 minutes for thorough processing
   - Verify service stops actively moving files

5. **Proceed to Step 5** when processing is complete

## Test Results

Step 5 generates versioned results in `Results\v{timestamp}\`:

- **analysis-results.yaml** - Complete results for all test files
- **exceptions.yaml** - Failures only for quick diagnosis
- **analysis-results.html** - Visual report with filtering (optional)

### Success Metrics

- **95-100% success:** Excellent - minor boundary issues only
- **80-95% success:** Good - may have DateFilter timing issues
- **<80% success:** Investigation required - likely logic errors

## Post-Test Cleanup

**Restore production config:**
```powershell
Stop-Service -Name 'RJAutoMoverService'
Copy-Item 'config-backup.yaml' 'C:\Program Files\RJAutoMover\config.yaml' -Force
Start-Service -Name 'RJAutoMoverService'
```

**Clean up test data:**
```powershell
Remove-Item 'C:\RJAutoMover_TestData' -Recurse -Force
```

## Common Issues

### Service Won't Start After Deployment

**Check logs:**
```powershell
Get-Content 'C:\ProgramData\RJAutoMover\Logs\*RJAutoMoverService*.log' -Tail 50
```

**Common causes:**
- Service account lacks permissions on `C:\RJAutoMover_TestData`
- Invalid test-config.yaml syntax

### Low Success Rate (<80%)

**Review exceptions:**
```powershell
Get-Content 'Results\v*\exceptions.yaml'
```

**Common causes:**
- Service didn't run long enough (not enough scan cycles)
- DateFilter boundary issues (files aged during test)
- Prediction engine logic differs from service

## Advanced Usage

### Custom File Count

```powershell
.\1-Generate-TestFiles.ps1 -TotalFiles 2000
```

### Custom Test Data Location

```powershell
.\1-Generate-TestFiles.ps1 -BaseFolder "D:\Tests\RJAutoMover"
.\2-Generate-TestConfig.ps1 -BaseFolder "D:\Tests\RJAutoMover"
```

### Re-run Individual Steps

```powershell
# Re-generate predictions after logic changes
.\3-Generate-Predictions.ps1

# Re-analyze results without re-running service
.\5-Analyze-Results.ps1 -GenerateHTML
```

## Version History

### v1.0 (November 22, 2025)
- Initial release with 5-step manual testing framework
- Prediction engine simulating service logic
- YAML + HTML reporting with service log integration
- Versioned results folders

## Documentation

For complete details, troubleshooting, and advanced usage, see **[TESTING-GUIDE.md](TESTING-GUIDE.md)**

## Future Enhancements

This framework may be extended with:
- Additional test scenarios (overlapping rules, invalid configs)
- Performance benchmarking
- Stress testing with larger file counts
- Automated regression testing
- CI/CD integration

## Support

**Issues:** Report at [GitHub Issues](https://github.com/toohighonthehog/RJAutoMover/issues)

**Logs:** Include service logs when reporting failures:
- `C:\ProgramData\RJAutoMover\Logs\*RJAutoMoverService*.log`

**Results:** Include `exceptions.yaml` for diagnosis
