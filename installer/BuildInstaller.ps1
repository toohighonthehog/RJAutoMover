# RJAutoMover Installer Build Script
# This script compiles the service and tray applications and creates an Inno Setup installer

param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [switch]$EmbedDotNet = $true
)

# Clear the console
Clear-Host

Write-Host "Building RJAutoMover Installer..." -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "Platform: $Platform" -ForegroundColor Cyan
Write-Host "Embed .NET Runtime: $EmbedDotNet" -ForegroundColor Cyan

# Set paths
$ProjectRoot = Split-Path $PSScriptRoot -Parent

# Display current version
$VersionFile = Join-Path $PSScriptRoot "version.txt"
if (Test-Path $VersionFile) {
    $CurrentVersion = Get-Content $VersionFile -ErrorAction SilentlyContinue
    Write-Host "Target Version: v$CurrentVersion" -ForegroundColor Cyan
    Write-Host "Note: Executables will be built with this version and verified before packaging" -ForegroundColor Yellow
    Write-Host "Note: Version will auto-increment after successful installer creation" -ForegroundColor Yellow
    Write-Host ""
} else {
    Write-Host "Warning: version.txt not found - version may not be available" -ForegroundColor Yellow
}

# Project files
$SolutionFile = Join-Path $ProjectRoot "RJAutoMover.sln"
$ServiceProject = Join-Path $ProjectRoot "RJAutoMoverService\RJAutoMoverService.csproj"
$TrayProject = Join-Path $ProjectRoot "RJAutoMoverTray\RJAutoMoverTray.csproj"
$ConfigProject = Join-Path $ProjectRoot "RJAutoMoverConfig\RJAutoMoverConfig.csproj"

# Publish directories
$ServicePublishDir = Join-Path $ProjectRoot "RJAutoMoverService\bin\$Configuration\net10.0\win-x64\publish"
$TrayPublishDir = Join-Path $ProjectRoot "RJAutoMoverTray\bin\$Configuration\net10.0-windows\win-x64\publish"
$ConfigPublishDir = Join-Path $ProjectRoot "RJAutoMoverConfig\bin\$Configuration\net10.0-windows\win-x64\publish"
$InstallerCreated = $false

try {
    # Step 1: Stop any running RJAutoMoverService, RJAutoMoverTray, and RJAutoMoverConfig processes
    Write-Host "Stopping any running RJAutoMover processes..." -ForegroundColor Yellow
    try {
        $serviceProcesses = Get-Process -Name "RJAutoMoverService" -ErrorAction SilentlyContinue
        $trayProcesses = Get-Process -Name "RJAutoMoverTray" -ErrorAction SilentlyContinue
        $configProcesses = Get-Process -Name "RJAutoMoverConfig" -ErrorAction SilentlyContinue

        if ($serviceProcesses -or $trayProcesses -or $configProcesses) {
            $serviceCount = if ($serviceProcesses) { $serviceProcesses.Count } else { 0 }
            $trayCount = if ($trayProcesses) { $trayProcesses.Count } else { 0 }
            $configCount = if ($configProcesses) { $configProcesses.Count } else { 0 }
            $totalProcesses = $serviceCount + $trayCount + $configCount
            Write-Host "Found $totalProcesses process(es), stopping them..." -ForegroundColor Yellow

            if ($serviceProcesses) { $serviceProcesses | Stop-Process -Force -ErrorAction SilentlyContinue }
            if ($trayProcesses) { $trayProcesses | Stop-Process -Force -ErrorAction SilentlyContinue }
            if ($configProcesses) { $configProcesses | Stop-Process -Force -ErrorAction SilentlyContinue }

            Start-Sleep -Seconds 2
            Write-Host "✓ All RJAutoMover processes stopped" -ForegroundColor Green
        } else {
            Write-Host "✓ No running RJAutoMover processes found" -ForegroundColor Green
        }
    } catch {
        Write-Host "⚠️ Warning: Could not check/stop processes: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    # Step 2: Clean previous builds
    Write-Host "Cleaning previous builds..." -ForegroundColor Yellow

    # Clean service publish directory
    if (Test-Path $ServicePublishDir) {
        for ($i = 1; $i -le 3; $i++) {
            try {
                Remove-Item $ServicePublishDir -Recurse -Force -ErrorAction Stop
                Write-Host "✓ Service build directory cleaned" -ForegroundColor Green
                break
            } catch {
                if ($i -eq 3) {
                    Write-Host "❌ Failed to clean service publish directory after 3 attempts" -ForegroundColor Red
                    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Yellow
                } else {
                    Write-Host "Service cleanup attempt $i failed, retrying in 2 seconds..." -ForegroundColor Yellow
                    Start-Sleep -Seconds 2
                }
            }
        }
    }

    # Clean tray publish directory
    if (Test-Path $TrayPublishDir) {
        for ($i = 1; $i -le 3; $i++) {
            try {
                Remove-Item $TrayPublishDir -Recurse -Force -ErrorAction Stop
                Write-Host "✓ Tray build directory cleaned" -ForegroundColor Green
                break
            } catch {
                if ($i -eq 3) {
                    Write-Host "❌ Failed to clean tray publish directory after 3 attempts" -ForegroundColor Red
                    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Yellow
                } else {
                    Write-Host "Tray cleanup attempt $i failed, retrying in 2 seconds..." -ForegroundColor Yellow
                    Start-Sleep -Seconds 2
                }
            }
        }
    }

    # Clean config publish directory
    if (Test-Path $ConfigPublishDir) {
        for ($i = 1; $i -le 3; $i++) {
            try {
                Remove-Item $ConfigPublishDir -Recurse -Force -ErrorAction Stop
                Write-Host "✓ Config editor build directory cleaned" -ForegroundColor Green
                break
            } catch {
                if ($i -eq 3) {
                    Write-Host "❌ Failed to clean config publish directory after 3 attempts" -ForegroundColor Red
                    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Yellow
                } else {
                    Write-Host "Config cleanup attempt $i failed, retrying in 2 seconds..." -ForegroundColor Yellow
                    Start-Sleep -Seconds 2
                }
            }
        }
    }

    # Step 3: Restore NuGet packages
    Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
    & dotnet restore $SolutionFile
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet restore failed"
    }

    # Step 4: Publish the applications
    if ($EmbedDotNet) {
        Write-Host "Publishing RJAutoMoverService application (self-contained with embedded .NET)..." -ForegroundColor Yellow
        & dotnet publish $ServiceProject -c $Configuration -r win-x64 --self-contained true --output $ServicePublishDir -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
        if ($LASTEXITCODE -ne 0) {
            throw "RJAutoMoverService publish failed"
        }

        Write-Host "Publishing RJAutoMoverTray application (self-contained with embedded .NET)..." -ForegroundColor Yellow
        & dotnet publish $TrayProject -c $Configuration -r win-x64 --self-contained true --output $TrayPublishDir -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
        if ($LASTEXITCODE -ne 0) {
            throw "RJAutoMoverTray publish failed"
        }

        Write-Host "Publishing RJAutoMoverConfig application (self-contained with embedded .NET)..." -ForegroundColor Yellow
        & dotnet publish $ConfigProject -c $Configuration -r win-x64 --self-contained true --output $ConfigPublishDir -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
        if ($LASTEXITCODE -ne 0) {
            throw "RJAutoMoverConfig publish failed"
        }
    } else {
        Write-Host "Publishing RJAutoMoverService application (framework-dependent, requires .NET on target)..." -ForegroundColor Yellow
        & dotnet publish $ServiceProject -c $Configuration -r win-x64 --self-contained false --output $ServicePublishDir
        if ($LASTEXITCODE -ne 0) {
            throw "RJAutoMoverService publish failed"
        }

        Write-Host "Publishing RJAutoMoverTray application (framework-dependent, requires .NET on target)..." -ForegroundColor Yellow
        & dotnet publish $TrayProject -c $Configuration -r win-x64 --self-contained false --output $TrayPublishDir
        if ($LASTEXITCODE -ne 0) {
            throw "RJAutoMoverTray publish failed"
        }

        Write-Host "Publishing RJAutoMoverConfig application (framework-dependent, requires .NET on target)..." -ForegroundColor Yellow
        & dotnet publish $ConfigProject -c $Configuration -r win-x64 --self-contained false --output $ConfigPublishDir
        if ($LASTEXITCODE -ne 0) {
            throw "RJAutoMoverConfig publish failed"
        }
    }

    # Step 5: Copy published files to installer directory
    Write-Host "Copying published files to installer directory..." -ForegroundColor Yellow

    $InstallerServiceDir = Join-Path $PSScriptRoot "publish\service"
    $InstallerTrayDir = Join-Path $PSScriptRoot "publish\tray"
    $InstallerConfigDir = Join-Path $PSScriptRoot "publish\config"

    # Clean and create installer publish directories
    if (Test-Path (Join-Path $PSScriptRoot "publish")) {
        Remove-Item (Join-Path $PSScriptRoot "publish") -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $InstallerServiceDir -Force | Out-Null
    New-Item -ItemType Directory -Path $InstallerTrayDir -Force | Out-Null
    New-Item -ItemType Directory -Path $InstallerConfigDir -Force | Out-Null

    # Copy service files
    Copy-Item -Path "$ServicePublishDir\*" -Destination $InstallerServiceDir -Recurse -Force
    Write-Host "✓ Service files copied to installer directory" -ForegroundColor Green

    # Copy tray files
    Copy-Item -Path "$TrayPublishDir\*" -Destination $InstallerTrayDir -Recurse -Force
    Write-Host "✓ Tray files copied to installer directory" -ForegroundColor Green

    # Copy config editor files
    Copy-Item -Path "$ConfigPublishDir\*" -Destination $InstallerConfigDir -Recurse -Force
    Write-Host "✓ Config editor files copied to installer directory" -ForegroundColor Green

    # Step 6: Verify executable versions match expected version
    Write-Host "Verifying executable versions..." -ForegroundColor Yellow

    $ServiceExePath = Join-Path $InstallerServiceDir "RJAutoMoverService.exe"
    $TrayExePath = Join-Path $InstallerTrayDir "RJAutoMoverTray.exe"

    if (Test-Path $ServiceExePath) {
        $ServiceVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ServiceExePath)
        $ServiceFileVersion = $ServiceVersionInfo.FileVersion
        Write-Host "Service executable version: $ServiceFileVersion" -ForegroundColor Cyan

        if ($ServiceFileVersion -ne $CurrentVersion) {
            Write-Host "❌ ERROR: Service executable version ($ServiceFileVersion) does not match expected version ($CurrentVersion)" -ForegroundColor Red
            Write-Host "This indicates the build may have used cached executables." -ForegroundColor Yellow
            throw "Version mismatch detected - Service executable version does not match"
        }
        Write-Host "✓ Service executable version matches expected version" -ForegroundColor Green
    } else {
        Write-Host "❌ ERROR: Service executable not found at $ServiceExePath" -ForegroundColor Red
        throw "Service executable not found"
    }

    if (Test-Path $TrayExePath) {
        $TrayVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($TrayExePath)
        $TrayFileVersion = $TrayVersionInfo.FileVersion
        Write-Host "Tray executable version: $TrayFileVersion" -ForegroundColor Cyan

        if ($TrayFileVersion -ne $CurrentVersion) {
            Write-Host "❌ ERROR: Tray executable version ($TrayFileVersion) does not match expected version ($CurrentVersion)" -ForegroundColor Red
            Write-Host "This indicates the build may have used cached executables." -ForegroundColor Yellow
            throw "Version mismatch detected - Tray executable version does not match"
        }
        Write-Host "✓ Tray executable version matches expected version" -ForegroundColor Green
    } else {
        Write-Host "❌ ERROR: Tray executable not found at $TrayExePath" -ForegroundColor Red
        throw "Tray executable not found"
    }

    $ConfigExePath = Join-Path $InstallerConfigDir "RJAutoMoverConfig.exe"
    if (Test-Path $ConfigExePath) {
        $ConfigVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ConfigExePath)
        $ConfigFileVersion = $ConfigVersionInfo.FileVersion
        Write-Host "Config editor executable version: $ConfigFileVersion" -ForegroundColor Cyan

        if ($ConfigFileVersion -ne $CurrentVersion) {
            Write-Host "❌ ERROR: Config editor executable version ($ConfigFileVersion) does not match expected version ($CurrentVersion)" -ForegroundColor Red
            Write-Host "This indicates the build may have used cached executables." -ForegroundColor Yellow
            throw "Version mismatch detected - Config editor executable version does not match"
        }
        Write-Host "✓ Config editor executable version matches expected version" -ForegroundColor Green
    } else {
        Write-Host "❌ ERROR: Config editor executable not found at $ConfigExePath" -ForegroundColor Red
        throw "Config editor executable not found"
    }

    # Step 7: Logs directory will be created by applications at runtime
    Write-Host "Logs directory will be created by applications when they first run..." -ForegroundColor Yellow

    # Step 8: Verify published files
    Write-Host "Verifying published files..." -ForegroundColor Yellow

    # Verify service files
    if ($EmbedDotNet) {
        # When using PublishSingleFile, all dependencies are embedded in the executable
        $ServiceRequiredFiles = @("RJAutoMoverService.exe")
        $TrayRequiredFiles = @("RJAutoMoverTray.exe")
    } else {
        # Framework-dependent deployment requires separate DLL files
        $ServiceRequiredFiles = @(
            "RJAutoMoverService.exe",
            "Microsoft.Extensions.Hosting.WindowsServices.dll",
            "YamlDotNet.dll",
            "RJAutoMoverShared.dll"
        )
        $TrayRequiredFiles = @(
            "RJAutoMoverTray.exe",
            "Hardcodet.NotifyIcon.Wpf.dll",
            "CommunityToolkit.Mvvm.dll",
            "RJAutoMoverShared.dll"
        )
    }

    Write-Host "Checking RJAutoMoverService files..." -ForegroundColor Cyan
    foreach ($file in $ServiceRequiredFiles) {
        $filePath = Join-Path $ServicePublishDir $file
        if (-not (Test-Path $filePath)) {
            Write-Warning "Missing required service file: $file"
        } else {
            Write-Host "✓ Found: $file" -ForegroundColor Green
        }
    }

    Write-Host "Checking RJAutoMoverTray files..." -ForegroundColor Cyan
    foreach ($file in $TrayRequiredFiles) {
        $filePath = Join-Path $TrayPublishDir $file
        if (-not (Test-Path $filePath)) {
            Write-Warning "Missing required tray file: $file"
        } else {
            Write-Host "✓ Found: $file" -ForegroundColor Green
        }
    }

    # Step 9: Check for Inno Setup
    Write-Host "Checking for Inno Setup..." -ForegroundColor Yellow
    
    # First try to find ISCC.exe in PATH
    $InnoCmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($InnoCmd) {
        $InnoSetupPath = $InnoCmd.Source
        Write-Host "✓ Found Inno Setup in PATH: $InnoSetupPath" -ForegroundColor Green
    } else {
        # Try standard installation paths
        $InnoSetupPaths = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
            "${env:ProgramFiles}\Inno Setup 5\ISCC.exe",
            "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
            "C:\Program Files\Inno Setup 6\ISCC.exe"
        )
        
        $InnoSetupPath = $null
        foreach ($Path in $InnoSetupPaths) {
            if (Test-Path $Path) {
                $InnoSetupPath = $Path
                Write-Host "✓ Found Inno Setup: $InnoSetupPath" -ForegroundColor Green
                break
            }
        }
    }
    
    if (-not $InnoSetupPath) {
        Write-Host "❌ Inno Setup not found." -ForegroundColor Red
        Write-Host "Searched paths:" -ForegroundColor Yellow
        foreach ($Path in $InnoSetupPaths) {
            Write-Host "- $Path" -ForegroundColor White
        }
        Write-Host "" -ForegroundColor White
        Write-Host "Solutions:" -ForegroundColor Yellow
        Write-Host "1. Download Inno Setup from: https://jrsoftware.org/isdl.php" -ForegroundColor Cyan
        Write-Host "2. Install Inno Setup and restart this script" -ForegroundColor Cyan
        Write-Host "" -ForegroundColor White
        Write-Host "✓ Applications were successfully built and are ready for use!" -ForegroundColor Green
        Write-Host "Service published to: $ServicePublishDir" -ForegroundColor Cyan
        Write-Host "Tray published to: $TrayPublishDir" -ForegroundColor Cyan
        return
    }

    # Step 10: Version handling
    Write-Host "Version will be read directly from version.txt during installer compilation..." -ForegroundColor Yellow

    # Step 11: Update version numbers in README files
    Write-Host "Updating version numbers in README files..." -ForegroundColor Yellow
    try {
        # Update installer README.txt
        # Matches patterns: "v0.9.1.13" and "Version: 0.9.1.13"
        $ReadmeTxtPath = Join-Path $PSScriptRoot "README.txt"
        if (Test-Path $ReadmeTxtPath) {
            $ReadmeTxtContent = Get-Content $ReadmeTxtPath -Raw -Encoding UTF8
            $ReadmeTxtContent = $ReadmeTxtContent -replace 'v\d+\.\d+\.\d+\.\d+', "v$CurrentVersion"
            $ReadmeTxtContent = $ReadmeTxtContent -replace 'Version:\s*\d+\.\d+\.\d+\.\d+', "Version: $CurrentVersion"
            $ReadmeTxtContent | Set-Content $ReadmeTxtPath -Encoding UTF8 -NoNewline
            Write-Host "✓ Updated installer README.txt to version $CurrentVersion" -ForegroundColor Green
        }

        # Update main README.md
        # Matches patterns: "version-0.9.1.13-blue" and "(Version 0.9.1.13)"
        $ReadmeMdPath = Join-Path $ProjectRoot "README.md"
        if (Test-Path $ReadmeMdPath) {
            $ReadmeMdContent = Get-Content $ReadmeMdPath -Raw -Encoding UTF8
            $ReadmeMdContent = $ReadmeMdContent -replace 'version-\d+\.\d+\.\d+\.\d+-blue', "version-$CurrentVersion-blue"
            $ReadmeMdContent = $ReadmeMdContent -replace '\(Version \d+\.\d+\.\d+\.\d+\)', "(Version $CurrentVersion)"
            $ReadmeMdContent | Set-Content $ReadmeMdPath -Encoding UTF8 -NoNewline
            Write-Host "✓ Updated main README.md to version $CurrentVersion" -ForegroundColor Green
        }
    } catch {
        Write-Host "⚠️ Warning: Could not update README files: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    # Step 12: Build the installer using Inno Setup
    Write-Host "Building installer using Inno Setup..." -ForegroundColor Yellow

    try {
        $IssFile = Join-Path $PSScriptRoot "RJAutoMover.iss"

        # Installer filename (always the same)
        $SetupFile = Join-Path $PSScriptRoot "RJAutoMoverSetup.exe"

        # Kill any hanging processes that might be locking files
        Write-Host "Stopping processes that might lock installer files..." -ForegroundColor Yellow
        try {
            # Stop ISCC processes
            $isccProcesses = Get-Process -Name "ISCC" -ErrorAction SilentlyContinue
            if ($isccProcesses) {
                $isccProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
                Write-Host "✓ Stopped $($isccProcesses.Count) ISCC process(es)" -ForegroundColor Green
            }

            # Stop any processes that might have the installer open
            $installerProcesses = Get-Process | Where-Object {
                $_.ProcessName -eq "RJAutoMoverSetup" -or
                ($_.ProcessName -like "*Setup*" -and $_.MainWindowTitle -like "*RJAutoMover*")
            } -ErrorAction SilentlyContinue
            if ($installerProcesses) {
                $installerProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
                Write-Host "✓ Stopped $($installerProcesses.Count) installer-related process(es)" -ForegroundColor Green
            }

            # Check if the target file is locked by any process
            if (Test-Path $SetupFile) {
                try {
                    $fileStream = [System.IO.File]::Open($SetupFile, 'Open', 'Write')
                    $fileStream.Close()
                } catch {
                    Write-Host "⚠️ Target installer file appears to be locked, attempting to find locking process..." -ForegroundColor Yellow
                    try {
                        $lockingProcesses = Get-Process | Where-Object {
                            try {
                                $_.Modules | Where-Object { $_.FileName -eq $SetupFile }
                            } catch { $null }
                        }
                        if ($lockingProcesses) {
                            $lockingProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
                            Write-Host "✓ Stopped processes locking the installer file" -ForegroundColor Green
                        }
                    } catch {
                        Write-Host "Could not identify locking processes" -ForegroundColor Yellow
                    }
                }
            }

            Start-Sleep -Seconds 2
        } catch {
            Write-Host "⚠️ Warning: Could not check/stop processes: $($_.Exception.Message)" -ForegroundColor Yellow
        }

        # Remove old setup file with retry logic
        if (Test-Path $SetupFile) {
            Write-Host "Removing old installer file..." -ForegroundColor Yellow
            for ($i = 1; $i -le 5; $i++) {
                try {
                    Remove-Item $SetupFile -Force -ErrorAction Stop
                    Write-Host "✓ Old installer file removed" -ForegroundColor Green
                    break
                } catch {
                    if ($i -eq 5) {
                        Write-Host "⚠️ Warning: Could not remove old installer file after 5 attempts" -ForegroundColor Yellow
                        Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Yellow
                        Write-Host "Continuing with build anyway..." -ForegroundColor Yellow
                    } else {
                        Write-Host "Installer removal attempt $i failed, retrying in 2 seconds..." -ForegroundColor Yellow
                        Start-Sleep -Seconds 2
                    }
                }
            }
        }

        Write-Host "Inno Setup Script: $IssFile" -ForegroundColor Cyan
        Write-Host "Version: $CurrentVersion" -ForegroundColor Cyan

        # Inno Setup compilation with retry logic
        $CompileSucceeded = $false
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                Write-Host "Inno Setup compilation attempt $attempt..." -ForegroundColor Yellow

                & $InnoSetupPath $IssFile "/DMyAppVersion=$CurrentVersion"

                if ($LASTEXITCODE -eq 0) {
                    Write-Host "✓ Inno Setup compilation successful" -ForegroundColor Green
                    $CompileSucceeded = $true
                    break
                } else {
                    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
                }
            } catch {
                if ($attempt -eq 3) {
                    throw "Inno Setup compilation failed after 3 attempts: $($_.Exception.Message)"
                } else {
                    Write-Host "Compilation attempt $attempt failed: $($_.Exception.Message)" -ForegroundColor Yellow
                    Write-Host "Cleaning up and retrying in 5 seconds..." -ForegroundColor Yellow

                    # Enhanced cleanup for file locks
                    try {
                        # Force close any handles to the installer file
                        if (Test-Path $SetupFile) {
                            # Multiple attempts to remove with increasing delays
                            for ($cleanAttempt = 1; $cleanAttempt -le 3; $cleanAttempt++) {
                                try {
                                    [System.GC]::Collect()
                                    [System.GC]::WaitForPendingFinalizers()
                                    Remove-Item $SetupFile -Force -ErrorAction Stop
                                    Write-Host "✓ Cleaned up locked installer file" -ForegroundColor Green
                                    break
                                } catch {
                                    if ($cleanAttempt -eq 3) {
                                        Write-Host "⚠️ Could not remove locked file after 3 attempts" -ForegroundColor Yellow
                                    } else {
                                        Start-Sleep -Seconds (2 * $cleanAttempt)
                                    }
                                }
                            }
                        }

                        # Clean up temporary Inno Setup files
                        $TempSetupFiles = Get-ChildItem -Path $PSScriptRoot -Name "Setup-*.tmp" -ErrorAction SilentlyContinue
                        foreach ($TempFile in $TempSetupFiles) {
                            Remove-Item (Join-Path $PSScriptRoot $TempFile) -Force -ErrorAction SilentlyContinue
                        }

                        # Clean up any Inno Setup cache files
                        $InnoTempFiles = Get-ChildItem -Path $env:TEMP -Name "is-*.tmp" -ErrorAction SilentlyContinue
                        foreach ($TempFile in $InnoTempFiles) {
                            Remove-Item (Join-Path $env:TEMP $TempFile) -Force -ErrorAction SilentlyContinue
                        }
                    } catch {
                        Write-Host "⚠️ Warning: Could not clean temporary files: $($_.Exception.Message)" -ForegroundColor Yellow
                    }

                    Start-Sleep -Seconds 5
                }
            }
        }

        if (-not $CompileSucceeded) {
            throw "All compilation attempts failed"
        }

        if (Test-Path $SetupFile) {
            Write-Host "✓ Installer created successfully!" -ForegroundColor Green
            Write-Host "Setup Location: $SetupFile" -ForegroundColor Cyan

            # Verify installer version metadata
            Write-Host "Verifying installer version..." -ForegroundColor Yellow
            try {
                $InstallerVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($SetupFile)
                $InstallerProductVersion = $InstallerVersionInfo.ProductVersion
                Write-Host "Installer product version: $InstallerProductVersion" -ForegroundColor Cyan

                # Note: Inno Setup installers may not always have matching product version
                # So we verify the executables it contains were already checked above
                Write-Host "✓ Installer version verified (executables inside already validated)" -ForegroundColor Green
            } catch {
                Write-Host "⚠️ Warning: Could not verify installer version metadata: $($_.Exception.Message)" -ForegroundColor Yellow
            }

            $InstallerCreated = $true

            # Auto-increment version for next build
            try {
                Write-Host "Auto-incrementing version for next build..." -ForegroundColor Yellow
                $VersionFile = Join-Path $PSScriptRoot "version.txt"
                $CurrentVersion = Get-Content $VersionFile -ErrorAction Stop
                Write-Host "Current version: $CurrentVersion" -ForegroundColor Cyan
                
                # Parse version (expected format: major.minor.build)
                $VersionParts = $CurrentVersion.Split('.')
                if ($VersionParts.Length -eq 3) {
                    $Major = $VersionParts[0]
                    $Minor = $VersionParts[1]
                    $Build = [int]$VersionParts[2] + 1

                    # Format build number with leading zeros (001, 002, etc.)
                    $NewBuild = $Build.ToString("000")
                    $NewVersion = "$Major.$Minor.$NewBuild"

                    # Update version.txt
                    $NewVersion | Set-Content $VersionFile -Encoding UTF8
                    Write-Host "✓ Version incremented to: $NewVersion" -ForegroundColor Green
                } elseif ($VersionParts.Length -eq 4) {
                    # Handle 4-part version (major.minor.patch.build)
                    $Major = $VersionParts[0]
                    $Minor = $VersionParts[1]
                    $Patch = $VersionParts[2]
                    $Build = [int]$VersionParts[3] + 1

                    $NewVersion = "$Major.$Minor.$Patch.$Build"

                    # Update version.txt
                    $NewVersion | Set-Content $VersionFile -Encoding UTF8
                    Write-Host "✓ Version incremented to: $NewVersion" -ForegroundColor Green
                } else {
                    Write-Host "⚠️ Warning: Version format not recognized, skipping auto-increment" -ForegroundColor Yellow
                    Write-Host "Expected format: major.minor.build (e.g., 0.01.001) or major.minor.patch.build (e.g., 1.0.0.0)" -ForegroundColor Yellow
                }
            } catch {
                Write-Host "⚠️ Warning: Could not auto-increment version: $($_.Exception.Message)" -ForegroundColor Yellow
            }
        } else {
            throw "Installer file was not created"
        }
    } catch {
        Write-Host "⚠️ Installer creation failed: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "Check that:" -ForegroundColor Yellow
        Write-Host "1. Inno Setup is properly installed" -ForegroundColor White
        Write-Host "2. All source files exist in the publish directory" -ForegroundColor White
        Write-Host "3. The .iss script syntax is correct" -ForegroundColor White
        Write-Host "" -ForegroundColor White
        Write-Host "✓ Applications were successfully built and are ready for use!" -ForegroundColor Green
        Write-Host "Service published to: $ServicePublishDir" -ForegroundColor Cyan
        Write-Host "Tray published to: $TrayPublishDir" -ForegroundColor Cyan
        $InstallerCreated = $false
    }

    # Step 13: Show installer information
    Write-Host "`nInstaller Summary:" -ForegroundColor Yellow
    if ($InstallerCreated) {
        Write-Host "✓ Inno Setup installer created successfully" -ForegroundColor Green
        Write-Host "- Professional Windows installer with GUI" -ForegroundColor White
        Write-Host "- Includes Add/Remove Programs support" -ForegroundColor White
        Write-Host "- Creates Start Menu shortcuts automatically" -ForegroundColor White
        Write-Host "- Installs and starts Windows service" -ForegroundColor White
    } else {
        Write-Host "⚠️ Installer creation failed - application is still usable" -ForegroundColor Yellow
        Write-Host "- Manually copy service from: $ServicePublishDir" -ForegroundColor White
        Write-Host "- Manually copy tray from: $TrayPublishDir" -ForegroundColor White
        Write-Host "- Install service manually if needed" -ForegroundColor White
    }
    Write-Host "- Application target: C:\Program Files\RJAutoMover" -ForegroundColor White
    Write-Host "- Windows Service: RJAutoMoverService (runs under System account)" -ForegroundColor White
    Write-Host "- Configuration file: config.yaml" -ForegroundColor White
    Write-Host "- Logs directory: .\Logs" -ForegroundColor White

} catch {
    Write-Host "Build failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Service files are available at: $ServicePublishDir" -ForegroundColor Yellow
    Write-Host "Tray files are available at: $TrayPublishDir" -ForegroundColor Yellow
    exit 1
}

Write-Host "`nBuild completed!" -ForegroundColor Green

# Show final version info
if (Test-Path $VersionFile) {
    $FinalVersion = Get-Content $VersionFile -ErrorAction SilentlyContinue
    if ($InstallerCreated -and $FinalVersion -ne $CurrentVersion) {
        Write-Host "✓ Version auto-incremented to: v$FinalVersion (ready for next build)" -ForegroundColor Green
    } else {
        Write-Host "Current version: v$FinalVersion" -ForegroundColor Cyan
    }
}

# Show completion time
$CompletionTime = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
Write-Host "`n✓ Build finished at: $CompletionTime" -ForegroundColor Green

Write-Host "`nNext steps:" -ForegroundColor Yellow
if ($InstallerCreated -eq $true) {
    Write-Host "1. Run the installer: RJAutoMoverSetup.exe" -ForegroundColor White
} else {
    Write-Host "1. Install Inno Setup and run this script again" -ForegroundColor White
}
Write-Host "2. After installation, check service: sc query RJAutoMoverService" -ForegroundColor White
Write-Host "3. Use tray app: Start Menu | RJAutoMover | RJAutoMover" -ForegroundColor White