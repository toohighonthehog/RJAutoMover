# ================================================================================
# RJAutoMover Results Analyzer - Step 5
# ================================================================================
# Compares actual file locations against predicted results
# Integrates service log analysis for comprehensive diagnostics
# Generates both YAML and HTML exception reports
# Saves to the versioned test folder created in Step 1
#
# Usage:
#   .\5-Analyze-Results.ps1 -TestRoot "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045"
#   .\5-Analyze-Results.ps1 -TestRoot "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045" -GenerateHTML
#   .\5-Analyze-Results.ps1 -TestRoot "C:\RJAutoMoverTest\testdata-0.9.6.108-20251122153045" -Verbose
#
# Output (in TestRoot\Results):
#   - analysis-results.yaml (detailed comparison)
#   - analysis-results.html (visual report, if -GenerateHTML)
#   - exceptions.yaml (failures only)
# ================================================================================

param(
    [Parameter(Mandatory=$true)]
    [string]$TestRoot,
    [switch]$GenerateHTML,
    [switch]$Verbose
)

# Color output functions
function Write-Success { param([string]$Message) Write-Host "[OK] $Message" -ForegroundColor Green }
function Write-Info { param([string]$Message) Write-Host " -> $Message" -ForegroundColor Cyan }
function Write-Warning { param([string]$Message) Write-Host "[!!] $Message" -ForegroundColor Yellow }
function Write-Error { param([string]$Message) Write-Host "[ERROR] $Message" -ForegroundColor Red }
function Write-Section { param([string]$Title)
    Write-Host ""
    Write-Host "============================================================================" -ForegroundColor Yellow
    Write-Host " $Title" -ForegroundColor Yellow
    Write-Host "============================================================================" -ForegroundColor Yellow
}

# Validate TestRoot
if (-not (Test-Path $TestRoot)) {
    Write-Warning "Test root folder not found: $TestRoot"
    Write-Info "Run 1-Generate-TestFiles.ps1 first to create the test folder"
    exit 1
}

# Define file paths
$ManifestFile = Join-Path $TestRoot "test-files-manifest.yaml"
$PredictionsFile = Join-Path $TestRoot "test-predictions.yaml"
$SourceFolder = Join-Path $TestRoot "Source"
$DestinationFolder = Join-Path $TestRoot "Destination"
$LogFolder = Join-Path $TestRoot "Logs"
$ResultsFolder = Join-Path $TestRoot "Results"

Write-Section "Step 5: Analyze Results"
Write-Info "Test Root: $TestRoot"
Write-Info "Manifest: $ManifestFile"
Write-Info "Predictions: $PredictionsFile"
Write-Info "Logs: $LogFolder"
Write-Info ""

# ====================================================================================
# Pre-Flight Checks
# ====================================================================================

Write-Section "Pre-Flight Checks"

if (-not (Test-Path $ManifestFile)) {
    Write-Error "Manifest file not found: $ManifestFile"
    Write-Info "Run 2-Generate-TestConfig.ps1 and 3-Generate-Predictions.ps1 first"
    exit 1
}
Write-Success "Manifest found"

if (-not (Test-Path $PredictionsFile)) {
    Write-Error "Predictions file not found: $PredictionsFile"
    Write-Info "Run 3-Generate-Predictions.ps1 first"
    exit 1
}
Write-Success "Predictions found"

if (-not (Test-Path $SourceFolder)) {
    Write-Error "Source folder not found: $SourceFolder"
    exit 1
}
Write-Success "Source folder found"

# Ensure results folder exists
if (-not (Test-Path $ResultsFolder)) {
    New-Item -Path $ResultsFolder -ItemType Directory -Force | Out-Null
}
Write-Success "Results folder ready: $ResultsFolder"

# ====================================================================================
# Load Predictions
# ====================================================================================

Write-Section "Loading Predictions"

$predictionsContent = Get-Content $PredictionsFile -Raw
$predictions = @()
$currentPrediction = $null

$lines = $predictionsContent -split "`n"
foreach ($line in $lines) {
    if ($line -match '^\s+- FileName: "(.+)"') {
        if ($currentPrediction) { $predictions += $currentPrediction }
        $currentPrediction = @{ FileName = $matches[1] }
    }
    elseif ($currentPrediction) {
        if ($line -match '^\s+Extension: "(.+)"') { $currentPrediction.Extension = $matches[1] }
        elseif ($line -match '^\s+ExpectedRule: (.+)') {
            $ruleValue = $matches[1]
            $currentPrediction.ExpectedRule = if ($ruleValue -eq "null") { $null } else { $ruleValue -replace '"', '' }
        }
        elseif ($line -match '^\s+ExpectedAction: "(.+)"') { $currentPrediction.ExpectedAction = $matches[1] }
        elseif ($line -match '^\s+ExpectedDestination: (.+)') {
            $destValue = $matches[1]
            $currentPrediction.ExpectedDestination = if ($destValue -eq "null") { $null } else { $destValue -replace '"', '' }
        }
        elseif ($line -match '^\s+MatchReason: "(.+)"') { $currentPrediction.MatchReason = $matches[1] }
    }
}
if ($currentPrediction) { $predictions += $currentPrediction }

Write-Success "Loaded $($predictions.Count) predictions"

# ====================================================================================
# Scan Actual File Locations
# ====================================================================================

Write-Section "Scanning Actual File Locations"

# Get all files in source
$sourceFiles = @()
if (Test-Path $SourceFolder) {
    $sourceFiles = Get-ChildItem -Path $SourceFolder -File -Recurse | Select-Object -ExpandProperty Name
}
Write-Info "Files in source: $($sourceFiles.Count)"

# Get all files in destination (recursively, tracking which subfolder)
$destinationFiles = @{}
if (Test-Path $DestinationFolder) {
    Get-ChildItem -Path $DestinationFolder -File -Recurse | ForEach-Object {
        $relativePath = $_.DirectoryName.Replace($DestinationFolder, "").TrimStart("\")
        $destinationFiles[$_.Name] = $relativePath
    }
}
Write-Info "Files in destination: $($destinationFiles.Count)"

# ====================================================================================
# Parse Service Logs
# ====================================================================================

Write-Section "Parsing Service Logs"

$logEntries = @{}

if (Test-Path $LogFolder) {
    # Find most recent service log
    $serviceLogs = Get-ChildItem -Path $LogFolder -Filter "*RJAutoMoverService*.log" | Sort-Object LastWriteTime -Descending

    if ($serviceLogs.Count -gt 0) {
        $latestLog = $serviceLogs[0]
        Write-Info "Reading log: $($latestLog.Name)"

        $logContent = Get-Content $latestLog.FullName

        foreach ($line in $logContent) {
            # Parse log entries for file moves
            # Expected format: [timestamp] [level] Rule 'RuleName' moving 'filename.ext' -> 'destination'
            if ($line -match "Rule '([^']+)' moving '([^']+)' ->") {
                $ruleName = $matches[1]
                $fileName = $matches[2]

                if (-not $logEntries.ContainsKey($fileName)) {
                    $logEntries[$fileName] = @{
                        Rule = $ruleName
                        LogLines = @()
                    }
                }
                $logEntries[$fileName].LogLines += $line
            }
            # Parse skipped entries
            elseif ($line -match "Skipping file '([^']+)'") {
                $fileName = $matches[1]

                if (-not $logEntries.ContainsKey($fileName)) {
                    $logEntries[$fileName] = @{
                        Rule = "Skipped"
                        LogLines = @()
                    }
                }
                $logEntries[$fileName].LogLines += $line
            }
            # Parse errors
            elseif ($line -match "ERROR.*'([^']+)'") {
                $fileName = $matches[1]

                if (-not $logEntries.ContainsKey($fileName)) {
                    $logEntries[$fileName] = @{
                        Rule = "Error"
                        LogLines = @()
                    }
                }
                $logEntries[$fileName].LogLines += $line
            }
        }

        Write-Success "Parsed $($logEntries.Count) file operations from logs"
    } else {
        Write-Warning "No service logs found in $LogFolder"
    }
} else {
    Write-Warning "Log folder not found: $LogFolder"
}

# ====================================================================================
# Compare Actual vs Predicted
# ====================================================================================

Write-Section "Comparing Actual vs Predicted"

$results = @()
$stats = @{
    TotalFiles = 0
    Matches = 0
    Mismatches = 0
    UnexpectedMoves = 0
    MissingMoves = 0
    WrongDestination = 0
    Errors = 0
}

foreach ($pred in $predictions) {
    $fileName = $pred.FileName
    $stats.TotalFiles++

    # Determine actual location
    $actualLocation = "Unknown"
    $actualDestination = $null

    if ($sourceFiles -contains $fileName) {
        $actualLocation = "Source"
    } elseif ($destinationFiles.ContainsKey($fileName)) {
        $actualLocation = "Destination"
        $actualDestination = $destinationFiles[$fileName]
    } else {
        $actualLocation = "Missing"
    }

    # Determine actual action
    $actualAction = if ($actualLocation -eq "Destination") { "Move" } else { "None" }

    # Get log info
    $logInfo = if ($logEntries.ContainsKey($fileName)) { $logEntries[$fileName] } else { $null }
    $actualRule = if ($logInfo) { $logInfo.Rule } else { "Unknown" }

    # Compare with prediction
    $match = $true
    $mismatchReason = ""

    # Expected: Move
    if ($pred.ExpectedAction -eq "Move") {
        if ($actualAction -ne "Move") {
            $match = $false
            $mismatchReason = "Expected move but file is in $actualLocation"
            $stats.MissingMoves++
        } elseif ($actualDestination -and $pred.ExpectedDestination) {
            # Normalize paths for comparison
            $expectedSubfolder = Split-Path $pred.ExpectedDestination -Leaf
            if ($actualDestination -ne $expectedSubfolder) {
                $match = $false
                $mismatchReason = "Moved to wrong destination: expected '$expectedSubfolder', got '$actualDestination'"
                $stats.WrongDestination++
            }
        }
    }
    # Expected: None (stay in source)
    elseif ($pred.ExpectedAction -eq "None") {
        if ($actualAction -eq "Move") {
            $match = $false
            $mismatchReason = "Unexpectedly moved to destination"
            $stats.UnexpectedMoves++
        }
    }

    # Check if file is missing entirely
    if ($actualLocation -eq "Missing") {
        $match = $false
        $mismatchReason = "File is missing from both source and destination"
        $stats.Errors++
    }

    # Track result
    if ($match) {
        $stats.Matches++
    } else {
        $stats.Mismatches++
    }

    # Create result entry
    $result = [PSCustomObject]@{
        FileName = $fileName
        Extension = $pred.Extension
        ExpectedAction = $pred.ExpectedAction
        ExpectedRule = $pred.ExpectedRule
        ExpectedDestination = if ($pred.ExpectedDestination) { Split-Path $pred.ExpectedDestination -Leaf } else { "N/A" }
        ActualAction = $actualAction
        ActualLocation = $actualLocation
        ActualDestination = if ($actualDestination) { $actualDestination } else { "N/A" }
        ActualRule = $actualRule
        Match = $match
        MismatchReason = $mismatchReason
        MatchReason = $pred.MatchReason
        LogEntries = if ($logInfo) { $logInfo.LogLines.Count } else { 0 }
    }

    $results += $result

    # Verbose output for mismatches
    if (-not $match -and $Verbose) {
        Write-Warning "MISMATCH: $fileName"
        Write-Info "  Expected: $($pred.ExpectedAction) via '$($pred.ExpectedRule)' to '$($pred.ExpectedDestination)'"
        Write-Info "  Actual: $actualAction to '$actualDestination' (Location: $actualLocation)"
        Write-Info "  Reason: $mismatchReason"
    }
}

Write-Success "Comparison complete"
Write-Info "Matches: $($stats.Matches) / $($stats.TotalFiles) ($([Math]::Round(($stats.Matches / $stats.TotalFiles) * 100, 1))%)"
Write-Info "Mismatches: $($stats.Mismatches)"
Write-Info "  - Missing moves: $($stats.MissingMoves)"
Write-Info "  - Unexpected moves: $($stats.UnexpectedMoves)"
Write-Info "  - Wrong destination: $($stats.WrongDestination)"
Write-Info "  - Errors: $($stats.Errors)"

# ====================================================================================
# Generate YAML Report (Full Results)
# ====================================================================================

Write-Section "Generating YAML Report"

$yamlReport = @"
# ====================================================================================
# RJAutoMover Test Analysis Results
# ====================================================================================
# Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
# Test Root: $TestRoot
# Total Files: $($stats.TotalFiles)
# Matches: $($stats.Matches)
# Mismatches: $($stats.Mismatches)
# Success Rate: $([Math]::Round(($stats.Matches / $stats.TotalFiles) * 100, 1))%
# ====================================================================================

AnalysisMetadata:
  GeneratedDate: "$(Get-Date -Format "yyyy-MM-ddTHH:mm:ss")"
  TestRoot: "$TestRoot"
  TotalFiles: $($stats.TotalFiles)
  Matches: $($stats.Matches)
  Mismatches: $($stats.Mismatches)
  SuccessRatePercent: $([Math]::Round(($stats.Matches / $stats.TotalFiles) * 100, 1))
  MismatchBreakdown:
    MissingMoves: $($stats.MissingMoves)
    UnexpectedMoves: $($stats.UnexpectedMoves)
    WrongDestination: $($stats.WrongDestination)
    Errors: $($stats.Errors)

Results:
"@

foreach ($result in $results) {
    $yamlReport += @"

  - FileName: "$($result.FileName)"
    Extension: "$($result.Extension)"
    ExpectedAction: "$($result.ExpectedAction)"
    ExpectedRule: $(if ($result.ExpectedRule) { "`"$($result.ExpectedRule)`"" } else { "null" })
    ExpectedDestination: "$($result.ExpectedDestination)"
    ActualAction: "$($result.ActualAction)"
    ActualLocation: "$($result.ActualLocation)"
    ActualDestination: "$($result.ActualDestination)"
    ActualRule: "$($result.ActualRule)"
    Match: $($result.Match.ToString().ToLower())
    MismatchReason: "$($result.MismatchReason)"
    MatchReason: "$($result.MatchReason)"
    LogEntries: $($result.LogEntries)
"@
}

$yamlReportPath = Join-Path $ResultsFolder "analysis-results.yaml"
Set-Content -Path $yamlReportPath -Value $yamlReport -Encoding UTF8
Write-Success "Full results saved: $yamlReportPath"

# ====================================================================================
# Generate Exceptions YAML (Mismatches Only)
# ====================================================================================

Write-Section "Generating Exceptions Report"

$exceptions = $results | Where-Object { -not $_.Match }

$exceptionsYaml = @"
# ====================================================================================
# RJAutoMover Test Exceptions (Failures Only)
# ====================================================================================
# Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
# Test Root: $TestRoot
# Total Exceptions: $($exceptions.Count)
# ====================================================================================

ExceptionMetadata:
  GeneratedDate: "$(Get-Date -Format "yyyy-MM-ddTHH:mm:ss")"
  TestRoot: "$TestRoot"
  TotalExceptions: $($exceptions.Count)
  TotalFiles: $($stats.TotalFiles)
  FailureRatePercent: $([Math]::Round(($exceptions.Count / $stats.TotalFiles) * 100, 1))

Exceptions:
"@

foreach ($exc in $exceptions) {
    $exceptionsYaml += @"

  - FileName: "$($exc.FileName)"
    Extension: "$($exc.Extension)"
    Expected: "$($exc.ExpectedAction) via '$($exc.ExpectedRule)' to '$($exc.ExpectedDestination)'"
    Actual: "$($exc.ActualAction) to '$($exc.ActualDestination)' (Location: $($exc.ActualLocation))"
    MismatchReason: "$($exc.MismatchReason)"
    MatchReason: "$($exc.MatchReason)"
    LogEntries: $($exc.LogEntries)
"@
}

$exceptionsYamlPath = Join-Path $ResultsFolder "exceptions.yaml"
Set-Content -Path $exceptionsYamlPath -Value $exceptionsYaml -Encoding UTF8
Write-Success "Exceptions saved: $exceptionsYamlPath"

# ====================================================================================
# Generate HTML Report (Optional)
# ====================================================================================

if ($GenerateHTML) {
    Write-Section "Generating HTML Report"

    $htmlReport = @"
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>RJAutoMover Test Analysis - $(Split-Path $TestRoot -Leaf)</title>
    <style>
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            margin: 20px;
            background-color: #f5f5f5;
        }
        h1 {
            color: #2c3e50;
            border-bottom: 3px solid #3498db;
            padding-bottom: 10px;
        }
        h2 {
            color: #34495e;
            margin-top: 30px;
        }
        .summary {
            background-color: #ecf0f1;
            padding: 20px;
            border-radius: 8px;
            margin-bottom: 20px;
        }
        .summary-stat {
            display: inline-block;
            margin: 10px 20px 10px 0;
            font-size: 16px;
        }
        .summary-stat strong {
            color: #2c3e50;
        }
        .success-rate {
            font-size: 24px;
            font-weight: bold;
            color: #27ae60;
        }
        .success-rate.warning {
            color: #f39c12;
        }
        .success-rate.danger {
            color: #e74c3c;
        }
        table {
            width: 100%;
            border-collapse: collapse;
            background-color: white;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
            margin-bottom: 20px;
        }
        th {
            background-color: #34495e;
            color: white;
            padding: 12px;
            text-align: left;
            font-weight: 600;
        }
        td {
            padding: 10px 12px;
            border-bottom: 1px solid #ddd;
        }
        tr:hover {
            background-color: #f9f9f9;
        }
        .match-true {
            color: #27ae60;
            font-weight: bold;
        }
        .match-false {
            color: #e74c3c;
            font-weight: bold;
        }
        .filter-buttons {
            margin: 20px 0;
        }
        .filter-btn {
            padding: 10px 20px;
            margin-right: 10px;
            border: none;
            border-radius: 4px;
            cursor: pointer;
            font-size: 14px;
            font-weight: 600;
        }
        .filter-btn.active {
            background-color: #3498db;
            color: white;
        }
        .filter-btn:not(.active) {
            background-color: #ecf0f1;
            color: #34495e;
        }
        .hidden {
            display: none;
        }
    </style>
</head>
<body>
    <h1>RJAutoMover Test Analysis Report</h1>
    <div class="summary">
        <div class="summary-stat"><strong>Test Run:</strong> $(Split-Path $TestRoot -Leaf)</div>
        <div class="summary-stat"><strong>Generated:</strong> $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")</div>
        <div class="summary-stat"><strong>Total Files:</strong> $($stats.TotalFiles)</div>
        <div class="summary-stat"><strong>Matches:</strong> $($stats.Matches)</div>
        <div class="summary-stat"><strong>Mismatches:</strong> $($stats.Mismatches)</div>
        <br>
        <div class="summary-stat">
            <strong>Success Rate:</strong>
            <span class="success-rate $(if ($stats.Matches / $stats.TotalFiles -ge 0.95) { "" } elseif ($stats.Matches / $stats.TotalFiles -ge 0.80) { "warning" } else { "danger" })">
                $([Math]::Round(($stats.Matches / $stats.TotalFiles) * 100, 1))%
            </span>
        </div>
    </div>

    <h2>Mismatch Breakdown</h2>
    <div class="summary">
        <div class="summary-stat"><strong>Missing Moves:</strong> $($stats.MissingMoves)</div>
        <div class="summary-stat"><strong>Unexpected Moves:</strong> $($stats.UnexpectedMoves)</div>
        <div class="summary-stat"><strong>Wrong Destination:</strong> $($stats.WrongDestination)</div>
        <div class="summary-stat"><strong>Errors:</strong> $($stats.Errors)</div>
    </div>

    <h2>Detailed Results</h2>
    <div class="filter-buttons">
        <button class="filter-btn active" onclick="filterResults('all')">All Files</button>
        <button class="filter-btn" onclick="filterResults('matches')">Matches Only</button>
        <button class="filter-btn" onclick="filterResults('mismatches')">Mismatches Only</button>
    </div>

    <table id="resultsTable">
        <thead>
            <tr>
                <th>File Name</th>
                <th>Ext</th>
                <th>Expected Action</th>
                <th>Expected Rule</th>
                <th>Expected Dest</th>
                <th>Actual Action</th>
                <th>Actual Dest</th>
                <th>Match</th>
                <th>Reason</th>
            </tr>
        </thead>
        <tbody>
"@

    foreach ($result in $results) {
        $matchClass = if ($result.Match) { "match-true" } else { "match-false" }
        $rowClass = if ($result.Match) { "row-match" } else { "row-mismatch" }
        $reason = if ($result.Match) { $result.MatchReason } else { $result.MismatchReason }

        $htmlReport += @"
            <tr class="$rowClass">
                <td>$($result.FileName)</td>
                <td>$($result.Extension)</td>
                <td>$($result.ExpectedAction)</td>
                <td>$($result.ExpectedRule)</td>
                <td>$($result.ExpectedDestination)</td>
                <td>$($result.ActualAction)</td>
                <td>$($result.ActualDestination)</td>
                <td class="$matchClass">$($result.Match)</td>
                <td>$reason</td>
            </tr>
"@
    }

    $htmlReport += @"
        </tbody>
    </table>

    <script>
        function filterResults(filter) {
            const rows = document.querySelectorAll('#resultsTable tbody tr');
            const buttons = document.querySelectorAll('.filter-btn');

            // Update button states
            buttons.forEach(btn => btn.classList.remove('active'));
            event.target.classList.add('active');

            // Filter rows
            rows.forEach(row => {
                if (filter === 'all') {
                    row.classList.remove('hidden');
                } else if (filter === 'matches') {
                    row.classList.toggle('hidden', !row.classList.contains('row-match'));
                } else if (filter === 'mismatches') {
                    row.classList.toggle('hidden', !row.classList.contains('row-mismatch'));
                }
            });
        }
    </script>
</body>
</html>
"@

    $htmlReportPath = Join-Path $ResultsFolder "analysis-results.html"
    Set-Content -Path $htmlReportPath -Value $htmlReport -Encoding UTF8
    Write-Success "HTML report saved: $htmlReportPath"
}

# ====================================================================================
# Summary
# ====================================================================================

Write-Section "Summary"
Write-Success "Step 5 Complete"
Write-Info "Test Root: $TestRoot"
Write-Info "Results folder: $ResultsFolder"
Write-Info "Success rate: $([Math]::Round(($stats.Matches / $stats.TotalFiles) * 100, 1))%"
Write-Info ""

if ($stats.Mismatches -eq 0) {
    Write-Success "ALL TESTS PASSED! No exceptions found."
} else {
    Write-Warning "$($stats.Mismatches) exceptions found - see exceptions.yaml for details"
    Write-Info "Exception breakdown:"
    Write-Info "  - Missing moves: $($stats.MissingMoves)"
    Write-Info "  - Unexpected moves: $($stats.UnexpectedMoves)"
    Write-Info "  - Wrong destination: $($stats.WrongDestination)"
    Write-Info "  - Errors/Missing files: $($stats.Errors)"
}

Write-Info ""
Write-Info "Generated files:"
Write-Info "  - analysis-results.yaml (full results)"
Write-Info "  - exceptions.yaml (failures only)"
if ($GenerateHTML) {
    Write-Info "  - analysis-results.html (visual report)"
}
