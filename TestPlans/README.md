# RJAutoMover Test Plans

**Purpose:** Manual testing framework for validating file processing rules

## Overview

This folder contains a 5-step manual testing framework that validates RJAutoMover's file processing logic by generating test files, creating test configurations, predicting expected outcomes, and analyzing actual results against predictions.

## Quick Start

```powershell
cd TestPlans

# Step 1: Generate 1000 test files (creates versioned folder)
.\1-Generate-TestFiles.ps1
# OUTPUT: C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045

# Copy the test root path and use in subsequent steps
$testRoot = "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"

# Step 2: Generate test configuration
.\2-Generate-TestConfig.ps1 -TestRoot $testRoot

# Step 3: Generate predictions
.\3-Generate-Predictions.ps1 -TestRoot $testRoot

# Step 4: Deploy config manually (follow instructions)
.\4-Deploy-And-Wait.ps1 -TestRoot $testRoot

# Step 5: Analyze results
.\5-Analyze-Results.ps1 -TestRoot $testRoot -GenerateHTML
```

## Framework Components

### Test Scripts (Run in Order)

| Script | Purpose | Manual Intervention | Parameters |
|--------|---------|---------------------|------------|
| **1-Generate-TestFiles.ps1** | Creates versioned test folder with 1000 test files | None | `-TotalFiles` (optional) |
| **2-Generate-TestConfig.ps1** | Generates valid test configuration with 10 rules | None | `-TestRoot` (required) |
| **3-Generate-Predictions.ps1** | Predicts expected outcomes using service logic simulation | None | `-TestRoot` (required) |
| **4-Deploy-And-Wait.ps1** | Provides deployment instructions and validation | **Required - deploy config and start service** | `-TestRoot` (required) |
| **5-Analyze-Results.ps1** | Compares actual vs predicted results, generates reports | None | `-TestRoot` (required), `-GenerateHTML`, `-Verbose` |

### Documentation

- **[TESTING-GUIDE.md](TESTING-GUIDE.md)** - Complete testing guide with detailed instructions, troubleshooting, and advanced usage

### Versioned Test Folder

Each test run creates a fully isolated folder at `C:\RJAutoMoverTest\testdata-<version>-<timestamp>`:

```
testdata-0.9.6.108-20251122153045\
├── Source\                          (1000 test files)
├── Destination\                     (10 target folders)
├── Logs\                            (service logs)
├── Results\                         (analysis reports)
├── test-files-manifest.yaml
├── test-config.yaml
├── test-predictions.yaml
└── config-backup.yaml
```

### Generated Artifacts

| File | Description | Location |
|------|-------------|----------|
| `test-files-manifest.yaml` | Metadata for all generated test files | Test root |
| `test-config.yaml` | Test configuration with 10 non-overlapping rules | Test root |
| `test-predictions.yaml` | Predicted outcomes for each test file | Test root |
| `config-backup.yaml` | Backup of production configuration | Test root |
| `Results\analysis-results.yaml` | Complete test results | Test root\Results |
| `Results\exceptions.yaml` | Failures only | Test root\Results |
| `Results\analysis-results.html` | Visual report (optional) | Test root\Results |

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

1. **Backup production config (if it exists):**
   ```powershell
   $testRoot = "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"
   Copy-Item 'C:\Program Files\RJAutoMover\config.yaml' "$testRoot\config-backup.yaml" -Force
   ```

2. **Copy test config to production location:**
   ```powershell
   Copy-Item "$testRoot\test-config.yaml" 'C:\Program Files\RJAutoMover\config.yaml' -Force
   ```

3. **Start the service executable:**
   - Navigate to: `C:\Program Files\RJAutoMover`
   - Run as Administrator: `RJAutoMoverService.exe`
   - OR start via `services.msc`

4. **Start the tray executable (optional):**
   - Navigate to: `C:\Program Files\RJAutoMover`
   - Run: `RJAutoMoverTray.exe`

5. **Monitor and wait:**
   - Watch service logs or tray application
   - Wait 5-10 minutes for thorough processing
   - Verify service stops actively moving files

6. **Proceed to Step 5** when processing is complete

## Test Results

Step 5 generates analysis reports in the test root's `Results\` folder:

- **analysis-results.yaml** - Complete results for all test files
- **exceptions.yaml** - Failures only for quick diagnosis
- **analysis-results.html** - Visual report with filtering (optional, use `-GenerateHTML`)

### Success Metrics

- **95-100% success:** Excellent - minor boundary issues only
- **80-95% success:** Good - may have DateFilter timing issues
- **<80% success:** Investigation required - likely logic errors

## Post-Test Cleanup

**Restore production config:**

1. Stop RJAutoMoverService.exe (close or stop via services.msc)
2. Stop RJAutoMoverTray.exe (close from system tray)
3. Restore config:
   ```powershell
   $testRoot = "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"
   Copy-Item "$testRoot\config-backup.yaml" 'C:\Program Files\RJAutoMover\config.yaml' -Force
   ```
4. Restart RJAutoMoverService.exe
5. Restart RJAutoMoverTray.exe

**Clean up test data:**

Remove specific test run:
```powershell
Remove-Item 'C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045' -Recurse -Force
```

Remove ALL test runs:
```powershell
Remove-Item 'C:\RJAutoMoverTest' -Recurse -Force
```

## Common Issues

### Service Won't Start After Deployment

**Check logs:**
```powershell
Get-Content 'C:\ProgramData\RJAutoMover\Logs\*RJAutoMoverService*.log' -Tail 50
```

**Common causes:**
- Service account lacks permissions on `C:\RJAutoMoverTest\testdata-*`
- Invalid test-config.yaml syntax

### Low Success Rate (<80%)

**Review exceptions:**
```powershell
Get-Content 'C:\RJAutoMoverTest\testdata-*\Results\exceptions.yaml'
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

### Re-run Individual Steps

```powershell
$testRoot = "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"

# Re-generate predictions after logic changes
.\3-Generate-Predictions.ps1 -TestRoot $testRoot

# Re-analyze results without re-running service
.\5-Analyze-Results.ps1 -TestRoot $testRoot -GenerateHTML
```

### Multiple Test Runs

Each test run is fully isolated - run multiple tests in parallel:

```powershell
# Create first test run
.\1-Generate-TestFiles.ps1
# OUTPUT: testdata-0.9.6.108-20251122153045

# Create second test run
.\1-Generate-TestFiles.ps1 -TotalFiles 2000
# OUTPUT: testdata-0.9.6.108-20251122153101

# Each test run is independent and can be archived
```

## Version History

### v2.0 (November 22, 2025)
- **Versioned test folders:** Complete test isolation in `testdata-<version>-<timestamp>`
- All test artifacts (files, config, predictions, results, logs) in single folder
- Steps 2-5 accept `-TestRoot` parameter
- Multiple test runs can coexist without conflicts
- Test runs are archival and can be preserved indefinitely

### v1.0 (November 22, 2025)
- Initial release with 5-step manual testing framework
- Prediction engine simulating service logic
- YAML + HTML reporting with service log integration

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
