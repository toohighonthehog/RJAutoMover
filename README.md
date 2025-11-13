<div align="center">
  <img src="icons/base.ico" alt="RJAutoMover Logo" width="128" height="128">
  <h1>RJAutoMover</h1>
</div>

<div align="center">

[![Version](https://img.shields.io/badge/version-0.9.6.47-blue.svg)](https://github.com/toohighonthehog/RJAutoMover)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE.txt)

</div>

> **Automated file management made simple** - A Windows service that intelligently monitors and moves files based on configurable rules, keeping your folders organized without manual intervention.

## 📥 Download & Install

**[Download RJAutoMoverSetup.exe](https://github.com/toohighonthehog/RJAutoMover/blob/main/installer/RJAutoMoverSetup.exe)** (Version 0.9.6.47)

Simply run the installer with administrator privileges - it handles everything automatically, including service installation and configuration.

---

## 🎯 What Does It Do?

RJAutoMover is a powerful file automation tool that watches specified folders and automatically moves files to designated locations based on customizable rules. Perfect for:

- 📂 Organizing downloads automatically by file type.
- 🎬 Managing media files (videos, images, music).
- 📄 Sorting documents as they arrive
- 🔄 Automating repetitive file management tasks
- 🗂️ Keeping workspaces clean and organized

The application runs silently in the background as a Windows service and provides a convenient system tray interface for monitoring and control.

---

## 🏗️ Architecture

RJAutoMover consists of three integrated components:

### 1. **RJAutoMoverService** (Background Service)
- Runs as a Windows service with configurable service account
- Monitors folders based on configured rules
- Processes and moves files automatically
- **Autonomous operation** - can run without tray icon (optional)
- **Persistent activity history** - SQLite database tracks all transfers across sessions
- Handles retries and error recovery
- Memory-optimized with configurable limits
- Accountability-first design - blocks transfers if database writes fail

### 2. **RJAutoMoverTray** (System Tray Application)
- Provides real-time status monitoring via system tray icon
- Shows balloon notifications for file transfer events
- Allows pausing/resuming processing via context menu
- Displays comprehensive information in the About window with multiple tabs:
  - **Transfers Tab** - View recent file transfers with live status updates
  - **Config Tab** - Review current configuration and application settings
  - **Logs Tab** - Browse service and tray logs with filtering and search
  - **System Tab** - View installation location, memory usage, and uptime
  - **.NET Tab** - See build SDK version, runtime info, and NuGet packages
  - **Version Tab** - Check tray and service version information

### 3. **RJAutoMoverConfig** (Configuration Editor)
- Standalone WPF application for editing configuration files
- Visual interface for creating, editing, and managing file rules
- Real-time validation of configuration settings
- Features:
  - **File Rules Management** - Add, edit, duplicate, and delete rules
  - **Application Settings** - Configure timing, memory limits, ports, and logging options
  - **Validation** - Comprehensive validation before saving (folder permissions, path conflicts, extension overlaps)
  - **Service Integration** - Option to automatically restart the service after saving the active config
- Available in Start Menu: "RJAutoMover > Configuration Editor"

**Communication:** Service and Tray components communicate via gRPC over localhost (ports configurable in `config.yaml`).

---

## ⚙️ Installation

### Prerequisites

- **Operating System:** Windows 10/11 or Windows Server 2016+
- **.NET Runtime:** .NET 10.0 Runtime (included in installer for self-contained builds)
- **Permissions:** Administrator rights for installation

### Installation Steps

1. **Download** [RJAutoMoverSetup.exe](installer/RJAutoMoverSetup.exe)

2. **Run the installer** with administrator privileges (right-click → "Run as administrator")

3. The installer will:
   - Install files to `C:\Program Files\RJAutoMover` (default)
   - Create a default configuration file (`config.yaml`)
   - Install and start the Windows service
   - Set up the system tray application to run at startup

4. **Verify installation:** The system tray icon should appear, indicating the service is running

### Service Account Configuration

By default, the service runs under the **Local System** account. For enhanced security or specific folder access requirements, you can configure a custom service account:

1. Open **Services** (services.msc)
2. Find **RJAutoMover Service**
3. Right-click → **Properties** → **Log On** tab
4. Select **"This account"** and enter credentials
5. Ensure the account has:
   - Read/Write permissions to all configured source and destination folders
   - Local login rights
   - Service logon rights

**Important:** If using a domain or local user account, ensure the account has appropriate permissions to access all folders specified in your rules.

### Service Permissions & File Access

**Critical:** RJAutoMover operates under the **service account context**, not the tray application user or the logged-in user. This means:

#### Permission Requirements

The service account (e.g., Local System, NT AUTHORITY\SYSTEM, or a custom user account) must have:

- ✅ **Read permissions** on all source folders specified in your file rules
- ✅ **Write permissions** on all destination folders specified in your file rules
- ✅ **Delete permissions** on source folders (files are moved, not copied)
- ✅ **Network access** if using UNC paths (e.g., `\\server\share\folder`)

#### Automatic Validation

The configuration validator checks folder permissions at service startup:

- **Source folders** - Verified for read access
- **Destination folders** - Verified for write access
- **Invalid paths** - Rules with inaccessible folders are flagged as "unworkable"
- **Startup behavior** - Service requires at least one workable rule to start successfully

If the service account lacks permissions to a configured folder, you'll see errors in:
- Service logs (`C:\ProgramData\RJAutoMover\Logs`)
- About window → Config tab (shows rule validation errors)
- About window → Error tab (if service fails to start)

#### Network Folder Access

For network paths (UNC paths like `\\server\share\folder`):

- **Local System account** - Has limited network access; may not work for domain resources
- **Domain account** - Recommended for accessing network shares
- **Local user account** - Cannot access remote network resources

**Best Practice for Network Paths:**
1. Configure service to run as a domain user account
2. Grant the domain user read/write permissions on the network shares
3. Test folder access from the service account context before deployment

#### Permission Troubleshooting

If files aren't moving and logs show access denied errors:

1. **Check service account** - Verify which account the service runs under (Services → RJAutoMover Service → Properties → Log On)
2. **Test folder access** - Use `runas` to simulate service account access
3. **Review folder security** - Right-click folder → Properties → Security → verify service account has appropriate permissions
4. **Check inheritance** - Ensure permissions are inherited to all subfolders if needed
5. **Validate configuration** - Check About window → Config tab for validation warnings

---

## 🔧 Configuration

Configuration is managed through `config.yaml` located in the installation directory (`C:\Program Files\RJAutoMover` by default).

### Basic Configuration Structure

```yaml
FileRules:
  - SourceFolder: C:\Users\YourName\Downloads
    Extension: .mp4|.mov|.avi|.mkv
    DestinationFolder: D:\Videos
    ScanIntervalMs: 15000
    IsActive: true
    Name: Video Files
    FileExists: skip

  - SourceFolder: C:\Users\YourName\Downloads
    Extension: .doc|.docx|.xls|.xlsx|.pdf
    DestinationFolder: D:\Documents
    ScanIntervalMs: 5000
    IsActive: true
    Name: Document Files
    FileExists: overwrite

Application:
  ProcessingPaused: false
  RetryDelayMs: 5000
  FailureCooldownMs: 180000
  MemoryLimitMb: 512
  ServiceGrpcPort: 60051
  TrayGrpcPort: 60052
```

### Configuration Options

#### FileRules Section

| Parameter | Description | Valid Values / Range | Example |
|-----------|-------------|---------------------|---------|
| `SourceFolder` | Folder to monitor for files (required) | Any valid folder path | `C:\Users\John\Downloads` |
| `Extension` | File extensions to match (pipe-separated, required) | Extensions with leading dot | `.pdf\|.docx\|.txt` |
| `DestinationFolder` | Where to move matched files (required) | Any valid folder path | `D:\Documents` |
| `ScanIntervalMs` | How often to scan (milliseconds) | `5000` - `900000` (5 sec - 15 min) | `5000` (5 seconds) |
| `IsActive` | Enable/disable this rule | `true` or `false` | `true` |
| `Name` | Friendly name for the rule (required) | Any non-empty string | `"My Documents"` |
| `FileExists` | Action if file exists in destination | `skip` or `overwrite` | `skip` |
| `LastAccessedMins` | Only move files accessed within X minutes (optional) | `1` - `5256000` (1 min - 10 years) | `1440` (24 hours) |
| `LastModifiedMins` | Only move files modified within X minutes (optional) | `1` - `5256000` (1 min - 10 years) | `60` (1 hour) |
| `AgeCreatedMins` | Only move files older than X minutes (optional) | `1` - `5256000` (1 min - 10 years) | `10080` (7 days) |

**FileRule Validation Rules:**

✅ **Required Fields:**
- `SourceFolder` - Must exist and be accessible by service account
- `DestinationFolder` - Must exist and be writable by service account
- `Extension` - Must not be empty
- `Name` - Must not be empty (alphanumeric and spaces, max 32 characters)

✅ **Name Format:**
- Alphanumeric characters and spaces only
- Maximum 32 characters
- Example: `"Video Files"`, `"My Documents 2024"`

✅ **Numeric Ranges:**
- `ScanIntervalMs`: 5,000 - 900,000 ms (5 seconds to 15 minutes)

✅ **FileExists Options:**
- `skip` - Skip file if it exists in destination (default, recommended)
- `overwrite` - Replace existing file in destination

✅ **Date Criteria (Optional - Mutually Exclusive):**
- **Only ONE date criteria can be specified per rule**
- `LastAccessedMins` - Only move files that were last accessed within X minutes
  - Example: `1440` = only move files accessed in the last 24 hours
  - Use case: Archive recently accessed files while ignoring old ones
- `LastModifiedMins` - Only move files that were last modified within X minutes
  - Example: `60` = only move files modified in the last hour
  - Use case: Process recently changed files immediately
- `AgeCreatedMins` - Only move files that are OLDER than X minutes (created at least X minutes ago)
  - Example: `10080` = only move files created at least 7 days ago
  - Use case: Archive old files while leaving recent ones alone
- **Range:** 1 - 5,256,000 minutes (1 minute to 10 years)
- **Validation:** If multiple date criteria are specified, configuration will fail with an error
- **Omitting date criteria:** If not specified, all files matching extensions will be moved regardless of age

✅ **Extension Format:**
- Must include leading dot (`.pdf` not `pdf`)
- Must be alphanumeric, 1-10 characters after the dot
- Multiple extensions separated by pipe: `.mp4|.mov|.avi`
- Case-insensitive matching
- **No wildcards allowed** (`*` or `?`)

✅ **Extension Clash Detection:**
- **Same source folder cannot have duplicate extensions across active rules**
- Example: Two active rules with `SourceFolder: C:\Downloads` cannot both use `.pdf`
- This prevents ambiguous file routing
- Inactive rules are excluded from clash detection

✅ **Folder Path Restrictions:**
- **No wildcards allowed** in SourceFolder or DestinationFolder (`*` or `?`)
- Paths must be specific directories, not patterns
- Special variables are allowed: `<InstallingUserDownloads>`, etc.

✅ **Advanced Rule Validations:**

**1. Infinite Loop Prevention (Direct):**
- **A DestinationFolder cannot match any SourceFolder across all rules**
- This prevents infinite processing loops where moved files get picked up again
- Example violation:
  - Rule 1: `SourceFolder: C:\Downloads`, `DestinationFolder: D:\Sorted`
  - Rule 2: `SourceFolder: D:\Sorted`, `DestinationFolder: E:\Archive` ❌ (D:\Sorted is used as both destination and source)
- This validation applies to **all rules** (both active and inactive)

**2. Circular Move Chain Detection (Multi-hop):**
- Detects indirect circular loops across multiple rules
- Example violation:
  - Rule A: `C:\Temp → C:\Archive`
  - Rule B: `C:\Archive → C:\Backup`
  - Rule C: `C:\Backup → C:\Temp` ❌ (Creates circular chain: Temp → Archive → Backup → Temp)
- Uses graph analysis to find chains of any length

**3. Same Source and Destination:**
- SourceFolder and DestinationFolder cannot be identical
- Example violation: `SourceFolder: C:\Temp`, `DestinationFolder: C:\Temp` ❌
- Would cause pointless file operations and potential lock issues

**4. Destination Inside Source (Recursive Growth):**
- DestinationFolder cannot be a subdirectory of SourceFolder
- Example violation: `SourceFolder: C:\Downloads`, `DestinationFolder: C:\Downloads\Archive` ❌
- Would cause infinite growth as moved files remain in monitored directory
- Also checks reverse: Source cannot be inside Destination

**5. Duplicate Rule Names:**
- All rule names must be unique (case-insensitive)
- Example violation: Two rules both named "Documents" ❌
- Ensures clear identification in logs and UI

**6. Reserved Windows Folder Names:**
- Paths cannot contain reserved Windows names
- Reserved names: `CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`
- Example violations:
  - `C:\Temp\CON\Files` ❌
  - `D:\Data\PRN` ❌
  - `C:\LPT1\Archive` ❌
- Prevents Windows file system compatibility issues

**7. Case-Insensitive Path Comparison:**
- All path comparisons are case-insensitive (Windows standard)
- `C:\temp` and `C:\TEMP` are treated as the same folder
- Prevents duplicate source folders with different casing

**8. Path Normalization:**
- All paths are normalized using `Path.GetFullPath()` before comparison
- Handles variations like:
  - `C:\Downloads` vs `C:\Downloads\`
  - `C:\Downloads\.` vs `C:\Downloads`
  - Relative paths are converted to absolute paths
- Ensures accurate duplicate and conflict detection

⚠️ **Important Notes:**
- At least one rule must be workable (have valid paths) for configuration to be valid
- Inactive rules (`IsActive: false`) are validated but don't cause configuration failure
- Source and destination folders must be accessible by the service account
- **Extension clashes are only checked for active rules in the same source folder**
- **Loop prevention and circular chain detection applies to ALL active rules**
- All validations occur at service startup - invalid configurations prevent service from starting

⚠️ **Path Aliasing Warning (Advanced):**

If you use **mixed path types** that reference the same physical location, the validator will detect and log warnings:

**Example of path aliasing:**
```yaml
FileRules:
  - Name: Rule1
    SourceFolder: P:\Data          # Mapped drive
    DestinationFolder: D:\Archive

  - Name: Rule2
    SourceFolder: \\server\share   # UNC path to same location as P:\
    DestinationFolder: D:\Backup
```

If `P:\` is mapped to `\\server\share`, the validator will:
1. ✅ **Detect** the aliasing by resolving mapped drives to UNC paths
2. ⚠️ **Log warnings** showing which paths are aliases:
   ```
   Path aliasing detected - multiple paths may reference the same physical location:
     'P:\Data' → '\\server\share\Data' (canonical)
   ```
3. ✅ **Continue validation** using canonical paths to prevent false positives in loop detection

**Best Practice:** Use **consistent path types** across all rules:
- ✅ All UNC paths: `\\server\share\folder`
- ✅ All mapped drives: `P:\folder`, `Q:\folder`
- ❌ Avoid mixing: `P:\folder` AND `\\server\share\folder` in different rules

**Why this matters:**
- Circular chain detection uses canonical paths, so aliases won't cause false loop warnings
- However, at runtime, file monitoring might behave unexpectedly with mixed paths
- Using consistent paths makes configuration clearer and more maintainable

#### Application Section

| Parameter | Description | Valid Range | Default |
|-----------|-------------|-------------|---------|
| `ProcessingPaused` | Start with processing paused | `true` or `false` | `false` |
| `RetryDelayMs` | Delay before retrying failed operations | `1000` - `30000` ms (1-30 sec) | `5000` |
| `FailureCooldownMs` | Cooldown period after repeated failures | `0` - `180000` ms (0-3 min) | `180000` |
| `RecheckServiceMs` | Service reconnection check interval | `5000` - `60000` ms (5-60 sec) | `30000` |
| `RecheckTrayMs` | Tray reconnection check interval | `5000` - `60000` ms (5-60 sec) | `30000` |
| `PauseDelayMs` | Delay when paused | `0` - `60000` ms (0-60 sec) | `0` |
| `ServiceHeartbeatMs` | Service heartbeat interval | `60000` - `3600000` ms (1-60 min) | `300000` |
| `MemoryLimitMb` | Memory limit before restart | `128` - `4096` MB | `512` |
| `MemoryCheckMs` | Memory check interval | `30000` - `300000` ms (30 sec-5 min) | `60000` |
| `ServiceGrpcPort` | Service gRPC port | `1024` - `65535` | `60051` |
| `TrayGrpcPort` | Tray app gRPC port | `1024` - `65535` | `60052` |
| `LogFolder` | Custom log folder path (optional) | Any valid folder path | `.\Logs` |
| `LogRetentionDays` | Days to retain log files before cleanup | `1` - `365` | `7` |
| `RequireTrayApproval` | Require tray approval before moves | `true` or `false` | `false` |
| `ActivityHistoryEnabled` | Enable persistent activity database | `true` or `false` | `true` |
| `ActivityHistoryMaxRecords` | Maximum records in database | `100` - `50000` | `5000` |
| `ActivityHistoryRetentionDays` | Auto-purge records older than days | `1` - `365` | `90` |

**Application Configuration Validation Rules:**

✅ **Timing Intervals (all in milliseconds):**
- `RetryDelayMs`: 1,000 - 30,000 (how long to wait before retrying failed file operations)
- `FailureCooldownMs`: 0 - 180,000 (pause after repeated failures to prevent resource exhaustion)
- `RecheckServiceMs`: 5,000 - 60,000 (how often tray checks service connection)
- `RecheckTrayMs`: 5,000 - 60,000 (how often service checks tray connection)
- `PauseDelayMs`: 0 - 60,000 (artificial delay when processing is paused)
- `ServiceHeartbeatMs`: 60,000 - 3,600,000 (service health check interval)
- `MemoryCheckMs`: 30,000 - 300,000 (memory monitoring interval)

✅ **Resource Limits:**
- `MemoryLimitMb`: 128 - 4,096 MB (service auto-restarts if exceeded)

✅ **Network Ports:**
- Must be valid port numbers (1024-65535)
- Ensure ports are not used by other applications
- Defaults: Service=60051, Tray=60052

⚠️ **Performance Tips:**
- Lower `ScanIntervalMs` = more responsive but higher CPU usage
- Higher `RetryDelayMs` = more resilient but slower recovery
- Higher `MemoryLimitMb` = handle more files but use more resources

### Special Folder Variables

Use these variables in folder paths for user-specific locations:

- `<InstallingUserDownloads>` - Downloads folder of the user who installed the application
- `<InstallingUserDocuments>` - Documents folder of the installing user
- `<InstallingUserDesktop>` - Desktop folder of the installing user

**Example:**
```yaml
SourceFolder: <InstallingUserDownloads>
DestinationFolder: <InstallingUserDocuments>\Sorted
```

### Activity History & Autonomous Operation

- **Location:** `C:\ProgramData\RJAutoMover\Data\ActivityHistory.db`
- **Session tracking** - Each service restart creates a unique session ID
- **Cross-user visibility** - All users can see complete transfer history
- **Automatic cleanup** - Old records purged based on `ActivityHistoryRetentionDays`
- **Accountability** - If database writes fail, transfers are blocked to ensure no "invisible" operations

**Autonomous Operation** allows the service to run without tray approval:

```yaml
Application:
  RequireTrayApproval: false  # Service operates independently (default)
  ActivityHistoryEnabled: true  # All transfers logged to database
```

When `RequireTrayApproval: false` (default):
- ✅ Service moves files without waiting for tray icon confirmation
- ✅ Works even when no user is logged in
- ✅ All transfers recorded in persistent database
- ✅ Any user who logs in can view full history in tray

When `RequireTrayApproval: true` (legacy mode):
- Service waits for tray icon approval before each file move
- Requires tray icon to be running
- Original behavior from earlier versions

**Database Resilience:**
- Automatic corruption detection and backup
- Retry logic for write failures
- Service stops all transfers if database becomes unavailable
- Prevents "invasive actions without record or visibility"

### Editing Configuration

You can edit the configuration in two ways:

#### Option 1: Configuration Editor (Recommended)

1. **Launch** the Configuration Editor from Start Menu: "RJAutoMover > Configuration Editor"
2. **Load** the configuration file (default: `C:\Program Files\RJAutoMover\config.yaml`)
3. **Edit** file rules and application settings using the visual interface:
   - Add, edit, duplicate, or delete rules
   - Configure application settings with descriptions and validation
4. **Validate** the configuration before saving (checks for errors, conflicts, and permissions)
5. **Save** the configuration - the editor will offer to restart the service automatically if editing the active config

The Configuration Editor provides:
- Real-time folder path validation
- Extension format checking
- Extension overlap detection (prevents duplicate extensions in the same source folder)
- Date filter configuration with explanations
- Permission verification
- Service restart integration

#### Option 2: Manual Editing

1. **Stop the service** (via tray icon or Services)
2. **Edit** `C:\Program Files\RJAutoMover\config.yaml` in a text editor
3. **Save changes**
4. **Restart the service**

Changes take effect immediately upon service restart.

---

## 🚀 Usage

### System Tray Interface

After installation, the RJAutoMover icon appears in your system tray with color-coded status:

- **Active (Blue):** Service running and processing files
- **Waiting (Green):** Service running but no files to process
- **Paused (Yellow):** Service paused (not processing files)
- **Error (Red):** Configuration error or service issue
- **Stopped (Gray):** Service is not running

**Right-click context menu options:**
- **Status** - Displays current service state (read-only)
- **Pause Processing / Resume Processing** - Toggle file processing on/off
- **View Recent Transfers...** - Opens About window on the Transfers tab showing recent file activity
- **About...** - Opens About window with comprehensive application information

### About Window

The About window provides detailed information across multiple tabs:

#### Transfers Tab
- View complete file transfer history from persistent database (not just current session)
- Live status updates with animated spinner for in-progress transfers
- Shows timestamp, file size, filename, rule name, destination folder, and transfer status
- **Session IDs** displayed for cross-session tracking (12-character unique IDs)
- **Pale blue highlighting** for transfers from current service session (since service started)
- Status indicators: ⠋ (in progress), ✓ (success), ✗ (failed), ⚠ (blacklisted)
- Pause/Resume processing button
- Transfer count and status summary

#### Config Tab
- **Parsed View** - Easy-to-read display of all file rules and application settings
  - File rules with source/destination folders, extensions, scan intervals
  - Rule status badges (Active/Inactive)
  - Application settings with descriptions and default indicators
- **Raw YAML View** - View the actual config.yaml content for validation

#### Logs Tab
- Browse both Service and Tray logs
- Filter by log level (All, ERROR, WARN, INFO, DEBUG, gRPC)
- Search logs with real-time text filtering
- Monospace font display for easy reading
- Shows log folder location path

#### System Tab
- Installation location and operating system
- Memory usage (Tray and Service)
- Uptime tracking (Tray and Service)
- System resource information

#### .NET Tab
- Build SDK version used for compilation
- Current runtime version
- NuGet package versions and dependencies

#### Version Tab
- Tray application version and build date
- Service version and build date
- Service account information (Run As user)
- **Automatic version checking** - Checks GitHub for new releases
  - Shows update notification if newer version is available
  - Displays "(Latest Version)" if running current release
  - Shows "(Pre-release)" if running unreleased development version
  - Displays "(unable to confirm)" if version check fails
  - **Refresh on tab selection** - Version information automatically refreshes each time the Version tab is opened

**Tab Navigation:**
- Clicking "About..." opens to the Version tab by default
- Clicking "View Recent Transfers..." opens directly to the Transfers tab
- If service is in error state, automatically opens to Error tab with details

### Command Line Operations

**Check service status:**
```batch
sc query RJAutoMoverService
```

**Start/Stop service:**
```batch
net start RJAutoMoverService
net stop RJAutoMoverService
```

**View service configuration:**
```batch
sc qc RJAutoMoverService
```

---

## 🔄 Version Checking

RJAutoMover includes automatic version checking to notify you when updates are available.

### How It Works

The version checker compares your installed version against the latest release on GitHub:

1. **Version File URL:** `https://raw.githubusercontent.com/toohighonthehog/RJAutoMover/main/installer/version.txt`
   - Contains the NEXT version number to be compiled (development version)
   - The actual latest released version is calculated by subtracting 1 from the final octet
   - Example: If `version.txt` contains `0.9.5.10`, the latest released version is `0.9.5.9`

2. **Installer URL:** `https://github.com/toohighonthehog/RJAutoMover/blob/main/installer/RJAutoMoverSetup.exe`
   - Direct link to download the latest installer
   - Displayed in the update notification when a new version is available

### Version Status Messages

When you open the About window's Version tab, the following messages may appear:

- **"(new version available: X.X.X.X)"** - An update is available (blue hyperlink to installer)
- **"(Latest Version)"** - You're running the current released version (green text)
- **"(Pre-release)"** - You're running a development version newer than the latest release (orange text)
- **"(unable to confirm)"** - Version check failed (network error or timeout) (gray italic text)

### Version Calculation Logic

The version checker uses the following logic:

```
Remote Version in version.txt: 0.9.5.10 (next to be compiled)
Actual Latest Released Version: 0.9.5.9 (remote - 1)
Your Installed Version: 0.9.5.8

Comparison:
- If Latest (0.9.5.9) > Installed (0.9.5.8) → Update Available
- If Latest (0.9.5.9) = Installed (0.9.5.9) → Latest Version
- If Latest (0.9.5.9) < Installed (0.9.5.10) → Pre-release
```

### Automatic Refresh

- Version information is checked when the About window is first opened
- **Automatically refreshes** each time you click on the Version tab
- Checks are performed asynchronously without blocking the UI
- 10-second timeout for network requests

### Privacy & Network Usage

- Version checks only occur when you open the About window or switch to the Version tab
- No automatic background version checking
- No telemetry or usage data is sent
- Simple HTTP GET request to GitHub's raw content

---

## 📋 Dependencies

### Runtime Dependencies
- **.NET 10.0 Runtime** (included in self-contained builds)
- **Windows OS** (Windows 10/11, Server 2016+)

### NuGet Packages
- **Grpc.AspNetCore** (2.71.0) - gRPC communication
- **Grpc.Net.Client** (2.71.0) - gRPC client library
- **Microsoft.Extensions.Hosting.WindowsServices** (8.0.0) - Windows service support
- **YamlDotNet** (16.3.0) - YAML configuration parsing
- **Microsoft.Data.Sqlite** (7.0.20) - Activity history database
  - ⚠️ **Important:** Version pinned to 7.x due to Windows Defender false positive with 8.x (Trojan:Script/Wacatac.B!ml)
- **Google.Protobuf** (3.32.1) - Protocol buffer serialization
- **Hardcodet.NotifyIcon.Wpf** (2.0.1) - WPF system tray icon support
- **CommunityToolkit.Mvvm** (8.4.0) - MVVM helpers for WPF
- **Serilog** (4.3.0) - Structured logging framework
- **Serilog.Sinks.File** (7.0.0) - File logging with rotation and retention

---

## 🔨 Building from Source

### Prerequisites
- **.NET 10.0 SDK** (or .NET 8.0 SDK for building the shared library)
- **Visual Studio 2022** (optional, or any C# IDE)
- **Inno Setup 6** (for creating installer)

### Build Commands

**Build solution:**
```powershell
dotnet build RJAutoMover.sln -c Release
```

**Publish service, tray, and config editor:**
```powershell
dotnet publish RJAutoMoverService\RJAutoMoverService.csproj -c Release -o installer\publish\service
dotnet publish RJAutoMoverTray\RJAutoMoverTray.csproj -c Release -o installer\publish\tray
dotnet publish RJAutoMoverConfig\RJAutoMoverConfig.csproj -c Release -o installer\publish\config
```

**Create installer:**
```powershell
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\RJAutoMover.iss
```

The installer will be created as `installer\RJAutoMoverSetup.exe`.

---

## 📝 Logs & Data

### Logs

Logs are powered by **Serilog** for reliable, high-performance logging with automatic rotation and retention.

**Log Location:** `C:\ProgramData\RJAutoMover\Logs` (or custom path from `config.yaml`)

**Log Files:**
- **Service logs:** `YYYY-MM-DD-HH-mm-ss RJAutoMoverService.log`
- **Tray logs:** `YYYY-MM-DD-HH-mm-ss RJAutoMoverTray.log`
- **Rotated files:** `YYYY-MM-DD-HH-mm-ss RJAutoMoverService_001.log`, `_002.log`, etc.

**Automatic Log Management:**
- ✅ **Automatic rotation** - New log file created every 10MB to prevent oversized files
- ✅ **Automatic cleanup** - Old logs deleted on startup based on `LogRetentionDays` (default: 7 days)
- ✅ **Shared file access** - Tray can read Service logs in real-time without locking conflicts
- ✅ **Buffered writes** - Optimized performance with 1-second flush interval

**Log Contents:**
- File operations (moves, errors, retries)
- Service status changes
- Configuration changes
- Performance metrics
- Error details with stack traces
- gRPC communication (labeled `[gRPC>]` and `[gRPC<]`)

### Activity Database

Persistent activity history is stored in the `Data\` folder:

- **Location:** `C:\Program Files\RJAutoMover\Data\ActivityHistory.db`
- **Format:** SQLite database with WAL mode for crash safety
- **Backed up during upgrades** - Installer automatically preserves database
- **Schema:** Activities table with session IDs, timestamps, file details, and status
- **Corruption handling:** Automatic detection and backup to `.corrupted.{timestamp}.bak`

---

## 🔍 Troubleshooting

### Service Won't Start
- Check Windows Event Viewer for error details
- Verify service account permissions
- Ensure ports 60051 and 60052 are not in use
- Check `config.yaml` for syntax errors

### Files Not Moving
- Verify source folder exists and is accessible
- Check file extensions match exactly (case-insensitive)
- Ensure destination folder exists and is writable
- Review logs for error messages
- Confirm rule is set to `IsActive: true`

### Permission Errors
- **Remember:** The service runs under the service account context (usually Local System), NOT the logged-in user or tray application user
- Ensure service account has read access to source folders
- Verify write permissions to destination folders
- For network paths, use a domain account with appropriate access
- Check folder ownership and inheritance
- See [Service Permissions & File Access](#service-permissions--file-access) for detailed permission requirements

### High Memory Usage
- Reduce `MemoryLimitMb` in configuration
- Increase `ScanIntervalMs` to reduce polling frequency
- Check for large files causing processing delays

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE.txt](LICENSE.txt) file for details.

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit issues, feature requests, or pull requests.

---

## 📧 Support

For issues, questions, or feature requests, please visit the [GitHub Issues](https://github.com/toohighonthehog/RJAutoMover/issues) page.

---

**Made with ❤️ by RJ Software**
