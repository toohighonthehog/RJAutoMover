# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

RJAutoMover is a Windows service-based file automation tool that monitors folders and automatically moves files based on configurable rules. It consists of three .NET 10.0 applications that communicate via gRPC.

## Solution Structure

The solution contains 4 projects:

- **RJAutoMoverService** - Windows service that performs file monitoring and movement
- **RJAutoMoverTray** - WPF system tray application for monitoring and control
- **RJAutoMoverConfig** - WPF configuration editor for managing rules and settings
- **RJAutoMoverShared** - Shared library containing models, helpers, services, and gRPC definitions

## Build Commands

Build the entire solution:
```powershell
dotnet build RJAutoMover.sln -c Release
```

Publish individual components (for installer):
```powershell
dotnet publish RJAutoMoverService\RJAutoMoverService.csproj -c Release -o installer\publish\service
dotnet publish RJAutoMoverTray\RJAutoMoverTray.csproj -c Release -o installer\publish\tray
dotnet publish RJAutoMoverConfig\RJAutoMoverConfig.csproj -c Release -o installer\publish\config
```

Create installer (requires Inno Setup 6):
```powershell
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\RJAutoMover.iss
```

Run service in console mode for testing (requires admin):
```powershell
.\RJAutoMoverService.exe --service
```

## Architecture

### Communication Pattern

The three applications communicate using bidirectional gRPC:

1. **Service ↔ Tray**: Service hosts `TrayService` gRPC server (port 60051), Tray connects as client
2. **Tray ↔ Service**: Tray hosts `ServiceClient` gRPC server (port 60052), Service connects as client
3. **Config Editor**: Standalone - edits `config.yaml` directly, no runtime communication

gRPC definitions are in [RJAutoMoverShared/Protos/communication.proto](RJAutoMoverShared/Protos/communication.proto).

### Configuration System

Configuration is loaded from `config.yaml` using YamlDotNet:

- **Primary location**: `C:\Program Files\RJAutoMover\config.yaml` (installation directory)
- **Fallback location**: `C:\ProgramData\RJAutoMover\config.yaml`
- **Logic**: [RJAutoMoverShared/Constants.cs:222-245](RJAutoMoverShared/Constants.cs#L222-L245) in `Paths.GetConfigPath()`

Configuration includes:
- **FileRules**: List of file processing rules with source/destination folders, extensions, scan intervals
- **Application**: Service behavior settings (timing, memory limits, ports, logging)

Configuration validation is performed by [ConfigValidator.cs](RJAutoMoverService/Config/ConfigValidator.cs) which enforces:
- Required fields (SourceFolder, DestinationFolder, Name, Extension)
- Path accessibility and permissions
- Extension format and clash detection (no duplicate extensions in same source folder)
- Loop prevention (destination cannot be source)
- Circular chain detection (multi-hop loops)
- Reserved Windows folder name blocking

### File Processing Logic

Each active FileRule operates independently with its own timer:

1. **Timer fires** every `ScanIntervalMs` milliseconds
2. **Scan** `SourceFolder` for files matching `Extension` (case-insensitive)
3. **Filter** by optional `DateFilter` (e.g., "LA:+43200" = files not accessed in last 43200 minutes)
4. **Check** if file exists in `DestinationFolder` based on `FileExists` setting (skip/overwrite)
5. **Move** file (copy then delete) from source to destination
6. **Record** in activity history database

File processing is handled by [FileProcessorService.cs](RJAutoMoverService/Services/FileProcessorService.cs).

### Activity History Database

Persistent SQLite database tracks all file transfers across sessions:

- **Location**: `C:\Program Files\RJAutoMover\Data\ActivityHistory.db` (or `C:\ProgramData\RJAutoMover\Data\`)
- **WAL mode**: Write-ahead logging for crash safety
- **Session tracking**: Each service restart creates unique session ID (12-character GUID prefix)
- **Accountability**: Service blocks transfers if database writes fail (no invisible operations)
- **Auto-cleanup**: Purges records older than `ActivityHistoryRetentionDays`
- **Implementation**: [ActivityHistoryService.cs](RJAutoMoverShared/Services/ActivityHistoryService.cs)

### Logging System

Logs use Serilog with automatic rotation and retention:

- **Location**: `C:\ProgramData\RJAutoMover\Logs` (configurable via `LogFolder`)
- **Service logs**: `YYYY-MM-DD-HH-mm-ss RJAutoMoverService.log`
- **Tray logs**: `YYYY-MM-DD-HH-mm-ss RJAutoMoverTray.log`
- **Rotation**: Automatically creates new file every 10MB (`_001.log`, `_002.log`, etc.)
- **Cleanup**: Old logs deleted on startup based on `LogRetentionDays` (default: 7 days)
- **Shared access**: Tray can read Service logs without locking conflicts
- **Implementation**: [LoggingService.cs](RJAutoMoverShared/Services/LoggingService.cs)

gRPC messages are prefixed with `[gRPC>]` (outgoing) and `[gRPC<]` (incoming) for easy filtering.

## Important Configuration Constants

Constants are centralized in [RJAutoMoverShared/Constants.cs](RJAutoMoverShared/Constants.cs):

- **Default ports**: Service=60051, Tray=60052
- **Max gRPC message size**: 16 MB
- **Timeouts**: Connection=10s, Long operations=30s, Retry delay=5s
- **Memory defaults**: 512 MB limit, 60s check interval
- **Scan interval limits**: 5-900 seconds (5s-15min)

## Key Service Account Considerations

The Windows service runs under a configured service account (default: Local System), NOT the logged-in user:

- Service account must have read/write permissions to all configured source/destination folders
- For network paths (UNC paths), use a domain account with appropriate access
- Configuration validator checks folder permissions at startup using service account context
- At least one "workable" rule (with valid paths) is required for service to start

## Date Filtering Logic

FileRules support an optional `DateFilter` property with format: `"TYPE:SIGN:MINUTES"`

**Format Components:**
- **TYPE**: `LA` (Last Accessed), `LM` (Last Modified), `FC` (File Created)
- **SIGN**: `+` (older than), `-` (within last)
- **MINUTES**: Integer value (1-5256000, representing up to 10 years)

**Examples:**
- `"LA:+43200"` = Files NOT accessed in last 43200 minutes (30 days) - older files
- `"LA:-1440"` = Files accessed within last 1440 minutes (1 day) - recent files
- `"LM:+10080"` = Files NOT modified in last 10080 minutes (1 week)
- `"FC:+43200"` = Files created more than 43200 minutes ago
- `""` or empty = No date filter (process all files)

**Important Rules:**
- **Single filter**: Only one DateFilter per rule (format prevents conflicts)
- **Extension "OTHERS"**: Catch-all rules that match any file type MUST have a DateFilter
- **Validation**: Format is validated by [DateFilterHelper.cs](RJAutoMoverShared/Helpers/DateFilterHelper.cs)

**Legacy Format (Deprecated):**
- Old properties `LastAccessedMins`, `LastModifiedMins`, `AgeCreatedMins` are deprecated
- Use `DateFilterHelper.MigrateFromLegacyFormat()` to convert old configs

See [TEST_PLAN_DATE_FILTERING.md](TEST_PLAN_DATE_FILTERING.md) for detailed testing scenarios.

## Critical Package Version Notes

**Microsoft.Data.Sqlite** is pinned to version 7.0.20:
- Version 8.x triggers Windows Defender false positive (Trojan:Script/Wacatac.B!ml)
- See comment in [RJAutoMoverShared.csproj:17](RJAutoMoverShared/RJAutoMoverShared.csproj#L17)
- DO NOT upgrade to 8.x until this issue is resolved

## Version Checking

The tray application checks GitHub for updates:

- **Version file URL**: `https://raw.githubusercontent.com/toohighonthehog/RJAutoMover/main/installer/version.txt`
- **Logic**: version.txt contains NEXT version to be compiled, so latest released = version.txt - 1 (final octet)
- **Comparison**: If installed < latest → show update notification
- **Implementation**: [VersionCheckerService.cs](RJAutoMoverShared/Services/VersionCheckerService.cs)

## Testing the Service

When making changes to the service:

1. Build in Release mode
2. Stop the Windows service if running: `net stop RJAutoMoverService`
3. Test in console mode with `--service` flag (requires admin)
4. Check logs in `C:\ProgramData\RJAutoMover\Logs`
5. Verify gRPC communication on ports 60051/60052 with `netstat -ano | findstr :60051`

## Configuration Validation Rules

When modifying ConfigValidator or related code:

- Extension clashes only checked for active rules in same source folder
- Loop prevention applies to ALL rules (both active and inactive)
- Path normalization uses `Path.GetFullPath()` for accurate comparison
- Case-insensitive path comparison (Windows standard)
- Mapped drive resolution to UNC paths for alias detection
- Reserved Windows folder names: CON, PRN, AUX, NUL, COM1-9, LPT1-9

## WPF Applications Architecture

Both RJAutoMoverTray and RJAutoMoverConfig are WPF applications:

- **Framework**: .NET 10.0 Windows
- **MVVM**: RJAutoMoverTray uses CommunityToolkit.Mvvm
- **System tray**: Uses Hardcodet.NotifyIcon.Wpf for tray icon with context menu
- **About window**: Multi-tab interface (Transfers, Config, Logs, System, .NET, Version)
- **Config editor**: XAML-based forms with validation for rule management

## Common Development Patterns

When adding new features:

1. **Shared models**: Add to RJAutoMoverShared/Models if used by multiple projects
2. **gRPC messages**: Update `communication.proto` and rebuild to regenerate C# code
3. **Configuration**: Add to SharedModels.cs, update ConfigValidator, update default-config.yaml
4. **Logging**: Use LoggingService with appropriate LogLevel (DEBUG, INFO, WARN, ERROR, FATAL)
5. **Constants**: Add new constants to RJAutoMoverShared/Constants.cs, not hardcoded values

## File Locations

- **Installation**: `C:\Program Files\RJAutoMover`
- **Configuration**: `C:\Program Files\RJAutoMover\config.yaml`
- **Logs**: `C:\ProgramData\RJAutoMover\Logs`
- **Database**: `C:\Program Files\RJAutoMover\Data\ActivityHistory.db`
- **Installer output**: `installer\RJAutoMoverSetup.exe`

## Documentation References

- [README.md](README.md) - Comprehensive user documentation
- [Notes/FileProcessingLogic.md](Notes/FileProcessingLogic.md) - Detailed processing flow diagrams
- [Notes/FileFilteringLogic.md](Notes/FileFilteringLogic.md) - File filtering implementation details
- [TEST_PLAN_DATE_FILTERING.md](TEST_PLAN_DATE_FILTERING.md) - Date filtering test cases
