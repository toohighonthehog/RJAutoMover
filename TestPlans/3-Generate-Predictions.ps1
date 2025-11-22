# ================================================================================
# RJAutoMover Predictions Generator - Step 3
# ================================================================================
# Generates predictions for test files based on test config
# Simulates service decision logic to predict which rule processes each file
#
# Usage:
#   .\3-Generate-Predictions.ps1
#
# Output:
#   - test-predictions.yaml (expected outcomes for all test files)
# ================================================================================

param(
    [string]$ManifestFile = "test-files-manifest.yaml",
    [string]$ConfigFile = "test-config.yaml",
    [string]$OutputPredictions = "test-predictions.yaml"
)

# Color output functions
function Write-Success { param([string]$Message) Write-Host "[OK] $Message" -ForegroundColor Green }
function Write-Info { param([string]$Message) Write-Host " -> $Message" -ForegroundColor Cyan }
function Write-Warning { param([string]$Message) Write-Host "[!!] $Message" -ForegroundColor Yellow }
function Write-Section { param([string]$Title)
    Write-Host ""
    Write-Host "============================================================================" -ForegroundColor Yellow
    Write-Host " $Title" -ForegroundColor Yellow
    Write-Host "============================================================================" -ForegroundColor Yellow
}

Write-Section "Step 3: Generate Predictions"
Write-Info "Manifest: $ManifestFile"
Write-Info "Config: $ConfigFile"
Write-Info "Output: $OutputPredictions"
Write-Info ""

# ====================================================================================
# Load Manifest
# ====================================================================================

Write-Section "Loading Test Files Manifest"

if (-not (Test-Path $ManifestFile)) {
    Write-Warning "Manifest file not found: $ManifestFile"
    Write-Info "Run 1-Generate-TestFiles.ps1 first"
    exit 1
}

$manifestContent = Get-Content $ManifestFile -Raw
$testFiles = @()

# Parse YAML manually (simplified parser for our known structure)
$lines = $manifestContent -split "`n"
$currentFile = $null

foreach ($line in $lines) {
    if ($line -match '^\s+- FileName: "(.+)"') {
        if ($currentFile) { $testFiles += $currentFile }
        $currentFile = @{ FileName = $matches[1] }
    }
    elseif ($currentFile) {
        if ($line -match '^\s+Extension: "(.+)"') { $currentFile.Extension = $matches[1] }
        elseif ($line -match '^\s+ExtensionGroup: "(.+)"') { $currentFile.ExtensionGroup = $matches[1] }
        elseif ($line -match '^\s+Size: (\d+)') { $currentFile.Size = [int]$matches[1] }
        elseif ($line -match '^\s+CreationTime: "(.+)"') { $currentFile.CreationTime = [DateTime]::Parse($matches[1]) }
        elseif ($line -match '^\s+LastWriteTime: "(.+)"') { $currentFile.LastWriteTime = [DateTime]::Parse($matches[1]) }
        elseif ($line -match '^\s+LastAccessTime: "(.+)"') { $currentFile.LastAccessTime = [DateTime]::Parse($matches[1]) }
        elseif ($line -match '^\s+AgeMinutes: (\d+)') { $currentFile.AgeMinutes = [int]$matches[1] }
        elseif ($line -match '^\s+TimestampPattern: (\d+)') { $currentFile.TimestampPattern = [int]$matches[1] }
    }
}
if ($currentFile) { $testFiles += $currentFile }

Write-Success "Loaded $($testFiles.Count) test files from manifest"

# ====================================================================================
# Load Configuration
# ====================================================================================

Write-Section "Loading Test Configuration"

if (-not (Test-Path $ConfigFile)) {
    Write-Warning "Config file not found: $ConfigFile"
    Write-Info "Run 2-Generate-TestConfig.ps1 first"
    exit 1
}

$configContent = Get-Content $ConfigFile -Raw
$rules = @()
$currentRule = $null

$lines = $configContent -split "`n"
foreach ($line in $lines) {
    if ($line -match '^\s+- Name: "(.+)"') {
        if ($currentRule) { $rules += $currentRule }
        $currentRule = @{ Name = $matches[1]; IsActive = $true }
    }
    elseif ($currentRule) {
        if ($line -match '^\s+Extension: (.+)') { $currentRule.Extension = $matches[1] }
        elseif ($line -match '^\s+DestinationFolder: "(.+)"') { $currentRule.DestinationFolder = $matches[1] }
        elseif ($line -match '^\s+FileExists: (.+)') { $currentRule.FileExists = $matches[1] }
        elseif ($line -match '^\s+DateFilter: "(.+)"') { $currentRule.DateFilter = $matches[1] }
        elseif ($line -match '^\s+IsActive: (true|false)') { $currentRule.IsActive = ($matches[1] -eq 'true') }
    }
}
if ($currentRule) { $rules += $currentRule }

Write-Success "Loaded $($rules.Count) rules from config"

# ====================================================================================
# Prediction Engine
# ====================================================================================

Write-Section "Generating Predictions"

function Parse-DateFilter {
    param([string]$DateFilter)

    if ([string]::IsNullOrWhiteSpace($DateFilter)) { return $null }

    if ($DateFilter -match '^(LA|LM|FC):([+-])(\d+)$') {
        return @{
            Type = $matches[1]
            Sign = $matches[2]
            Minutes = [int]$matches[3]
            IsOlderThan = ($matches[2] -eq '+')
        }
    }
    return $null
}

function Test-DateFilterMatch {
    param(
        [hashtable]$File,
        [hashtable]$FilterInfo
    )

    if ($null -eq $FilterInfo) { return $true }

    $now = Get-Date
    $fileTimestamp = switch ($FilterInfo.Type) {
        'LA' { $File.LastAccessTime }
        'LM' { $File.LastWriteTime }
        'FC' { $File.CreationTime }
    }

    $ageMinutes = [Math]::Round((New-TimeSpan -Start $fileTimestamp -End $now).TotalMinutes, 1)

    if ($FilterInfo.IsOlderThan) {
        return $ageMinutes >= $FilterInfo.Minutes
    } else {
        return $ageMinutes <= $FilterInfo.Minutes
    }
}

function Test-ExtensionMatch {
    param(
        [string]$FileExtension,
        [string]$RuleExtension
    )

    if ($RuleExtension -eq "OTHERS") { return $true }

    $extensions = $RuleExtension -split '\|'
    foreach ($ext in $extensions) {
        if ($FileExtension -ieq $ext) { return $true }
    }
    return $false
}

# Separate rules: specific extensions first, OTHERS last
$specificRules = $rules | Where-Object { $_.Extension -ne "OTHERS" -and $_.IsActive }
$othersRules = $rules | Where-Object { $_.Extension -eq "OTHERS" -and $_.IsActive }
$allRules = $specificRules + $othersRules

$predictions = @()
$stats = @{
    ExpectedMoves = 0
    ExpectedSkips = 0
    ExpectedUnprocessed = 0
}

foreach ($file in $testFiles) {
    $matchedRule = $null
    $matchReason = ""

    # Try specific rules first
    foreach ($rule in $specificRules) {
        $extMatch = Test-ExtensionMatch -FileExtension $file.Extension -RuleExtension $rule.Extension
        if (-not $extMatch) { continue }

        $filterInfo = Parse-DateFilter -DateFilter $rule.DateFilter
        $dateMatch = Test-DateFilterMatch -File $file -FilterInfo $filterInfo

        if ($dateMatch) {
            $matchedRule = $rule

            if ($filterInfo) {
                $typeStr = switch ($filterInfo.Type) {
                    'LA' { 'Last Accessed' }
                    'LM' { 'Last Modified' }
                    'FC' { 'File Created' }
                }
                $dirStr = if ($filterInfo.IsOlderThan) { "older than $($filterInfo.Minutes) min" } else { "within last $($filterInfo.Minutes) min" }
                $matchReason = "Extension $($file.Extension) matches, DateFilter $($rule.DateFilter) matches ($typeStr $dirStr)"
            } else {
                $matchReason = "Extension $($file.Extension) matches, no DateFilter"
            }
            break
        }
    }

    # If no specific rule matched, try OTHERS rules
    if (-not $matchedRule) {
        foreach ($rule in $othersRules) {
            $filterInfo = Parse-DateFilter -DateFilter $rule.DateFilter
            $dateMatch = Test-DateFilterMatch -File $file -FilterInfo $filterInfo

            if ($dateMatch) {
                $matchedRule = $rule
                $typeStr = switch ($filterInfo.Type) {
                    'LA' { 'Last Accessed' }
                    'LM' { 'Last Modified' }
                    'FC' { 'File Created' }
                }
                $dirStr = if ($filterInfo.IsOlderThan) { "older than $($filterInfo.Minutes) min" } else { "within last $($filterInfo.Minutes) min" }
                $matchReason = "OTHERS rule (catch-all), DateFilter $($rule.DateFilter) matches ($typeStr $dirStr)"
                break
            }
        }
    }

    # Determine action and destination
    $expectedAction = "None"
    $expectedDestination = $null

    if ($matchedRule) {
        $expectedAction = "Move"
        $expectedDestination = $matchedRule.DestinationFolder
        $stats.ExpectedMoves++
    } else {
        $matchReason = "No matching rule"
        $stats.ExpectedUnprocessed++
    }

    # Create prediction entry
    $predictions += [PSCustomObject]@{
        FileName = $file.FileName
        Extension = $file.Extension
        ExpectedRule = if ($matchedRule) { $matchedRule.Name } else { $null }
        ExpectedAction = $expectedAction
        ExpectedDestination = $expectedDestination
        MatchReason = $matchReason
        FileAge_CreationMinutes = [Math]::Round((New-TimeSpan -Start $file.CreationTime -End (Get-Date)).TotalMinutes, 1)
        FileAge_ModifiedMinutes = [Math]::Round((New-TimeSpan -Start $file.LastWriteTime -End (Get-Date)).TotalMinutes, 1)
        FileAge_AccessedMinutes = [Math]::Round((New-TimeSpan -Start $file.LastAccessTime -End (Get-Date)).TotalMinutes, 1)
    }
}

Write-Success "Generated predictions for $($predictions.Count) files"
Write-Info "Expected moves: $($stats.ExpectedMoves)"
Write-Info "Expected unprocessed: $($stats.ExpectedUnprocessed)"
Write-Info ""

# ====================================================================================
# Generate Predictions YAML
# ====================================================================================

Write-Section "Saving Predictions"

$yamlPredictions = @"
# ====================================================================================
# RJAutoMover Test Predictions
# ====================================================================================
# Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
# Total Files: $($predictions.Count)
# Expected Moves: $($stats.ExpectedMoves)
# Expected Unprocessed: $($stats.ExpectedUnprocessed)
# ====================================================================================

PredictionMetadata:
  GeneratedDate: "$(Get-Date -Format "yyyy-MM-ddTHH:mm:ss")"
  TotalFiles: $($predictions.Count)
  ExpectedMoves: $($stats.ExpectedMoves)
  ExpectedUnprocessed: $($stats.ExpectedUnprocessed)
  CoveragePercent: $([Math]::Round(($stats.ExpectedMoves / $predictions.Count) * 100, 1))

Predictions:
"@

foreach ($pred in $predictions) {
    $yamlPredictions += @"

  - FileName: "$($pred.FileName)"
    Extension: "$($pred.Extension)"
    ExpectedRule: $(if ($pred.ExpectedRule) { "`"$($pred.ExpectedRule)`"" } else { "null" })
    ExpectedAction: "$($pred.ExpectedAction)"
    ExpectedDestination: $(if ($pred.ExpectedDestination) { "`"$($pred.ExpectedDestination)`"" } else { "null" })
    MatchReason: "$($pred.MatchReason)"
    FileAge:
      CreationMinutes: $($pred.FileAge_CreationMinutes)
      ModifiedMinutes: $($pred.FileAge_ModifiedMinutes)
      AccessedMinutes: $($pred.FileAge_AccessedMinutes)
"@
}

# Save predictions
$predictionsPath = Join-Path (Get-Location) $OutputPredictions
Set-Content -Path $predictionsPath -Value $yamlPredictions -Encoding UTF8
Write-Success "Predictions saved: $predictionsPath"

# ====================================================================================
# Summary
# ====================================================================================

Write-Section "Summary"
Write-Success "Step 3 Complete"
Write-Info "Predictions file: $predictionsPath"
Write-Info "Expected coverage: $([Math]::Round(($stats.ExpectedMoves / $predictions.Count) * 100, 1))%"
Write-Info ""
Write-Info "Next step: Run 4-Deploy-And-Wait.ps1 (or manually deploy and run service)"
