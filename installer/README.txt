================================================================================
                          RJAutoMover v0.9.6.107
        Automated File Processing and Movement Service with Tray Monitor
================================================================================

TABLE OF CONTENTS
-----------------
1. What is RJAutoMover?
2. Quick Start Guide
3. How It Works
4. Configuration Guide
   - File Rules
   - Application Settings
5. Using the Tray Icon
6. Common Use Cases
7. Troubleshooting
8. Advanced Topics
9. Technical Information


================================================================================
1. WHAT IS RJAUTOMOVER?
================================================================================

RJAutoMover automatically monitors folders and moves files based on rules you
define. It's perfect for organizing downloads, processing incoming files, or
automating file workflows.

KEY FEATURES:
  * Automatic file monitoring and movement based on file extensions
  * Support for local folders and network (UNC) paths
  * Real-time status monitoring via system tray icon
  * Configurable scan intervals and retry logic
  * Safe by default - starts in paused mode
  * Comprehensive logging for troubleshooting
  * Graceful handling of locked files and network issues

TWO COMPONENTS:
  * RJAutoMoverService - Windows background service that does the actual file processing
  * RJAutoMoverTray    - System tray application for monitoring and control


================================================================================
2. QUICK START GUIDE
================================================================================

STEP 1: INSTALL
---------------
Run the installer. It will:
  - Install files to C:\Program Files\RJAutoMover\
  - Create the RJAutoMoverService Windows service
  - Set up RJAutoMoverTray to start automatically at login
  - Create a default config.yaml file

STEP 2: CONFIGURE
-----------------
1. Open C:\Program Files\RJAutoMover\config.yaml in a text editor
2. Add your file processing rules (see examples below)
3. Save the file

STEP 3: START
-------------
1. Press Win+R, type "services.msc", press Enter
2. Find "RJAutoMoverService" in the list
3. Right-click -> Start
4. The tray icon will appear automatically

STEP 4: TEST
------------
1. The service starts PAUSED by default (for safety)
2. Place a test file in your source folder
3. Right-click the tray icon and select "Resume Processing"
4. Verify the file moves to the destination folder
5. Check the logs if anything goes wrong


================================================================================
3. HOW IT WORKS
================================================================================

OVERVIEW:
  1. You define "rules" in config.yaml (what files, where from, where to)
  2. RJAutoMoverService scans source folders at regular intervals
  3. When matching files are found, they are moved to destination folders
  4. RJAutoMoverTray displays real-time status and recent activity
  5. Everything is logged for troubleshooting

IMPORTANT CONCEPTS:

File Rules
  A "rule" defines one file processing workflow:
  - What file types to process (by extension)
  - Which folder to monitor (source)
  - Where to move files (destination)
  - How often to scan for new files

Processing States
  * Active   - Service is running and moving files
  * Paused   - Service is running but not processing files
  * Stopped  - Service is not running
  * Error    - Configuration error or system problem

File Handling
  * Files are MOVED (not copied) - originals are deleted from source
  * Locked files are automatically skipped and retried later
  * Zero-byte files are skipped until they have content
  * Existing destination files can be skipped or overwritten


================================================================================
4. CONFIGURATION GUIDE
================================================================================

The config.yaml file has two main sections:
  1. FileRules       - Define what files to process
  2. Application     - Control service behavior


--------------------------------------------------------------------------------
4.1 FILE RULES
--------------------------------------------------------------------------------

File rules tell RJAutoMover what to do. Each rule monitors one source folder
for specific file types and moves them to a destination folder.

SIMPLE EXAMPLE:
---------------
FileRules:
  - Name: PDFs
    SourceFolder: C:\Users\John\Downloads
    Extension: .pdf
    DestinationFolder: C:\Documents\PDFs
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip

This rule moves all PDF files from Downloads to Documents\PDFs every 30 seconds.


MULTIPLE FILE TYPES:
--------------------
FileRules:
  - Name: Office Documents
    SourceFolder: C:\Users\John\Downloads
    Extension: .doc|.docx|.xls|.xlsx|.ppt|.pptx
    DestinationFolder: \\NAS\Documents\Office
    ScanIntervalMs: 60000
    IsActive: true
    FileExists: skip

Use the pipe symbol (|) to separate multiple extensions.


MULTIPLE RULES:
---------------
FileRules:
  - Name: Photos
    SourceFolder: C:\Users\John\Downloads
    Extension: .jpg|.jpeg|.png|.gif
    DestinationFolder: C:\Pictures\Incoming
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip

  - Name: Videos
    SourceFolder: C:\Users\John\Downloads
    Extension: .mp4|.avi|.mkv
    DestinationFolder: D:\Videos\Incoming
    ScanIntervalMs: 60000
    IsActive: true
    FileExists: skip

  - Name: Archives
    SourceFolder: \\NetworkShare\Incoming
    Extension: .zip|.rar|.7z
    DestinationFolder: D:\Archives
    ScanIntervalMs: 120000
    IsActive: true
    FileExists: overwrite


RULE SETTINGS EXPLAINED:
-------------------------

Name (Required)
  What:  A friendly name for this rule (shown in logs and tray)
  Rules: - Must be 1-32 characters
         - Use alphanumeric characters only (letters, numbers, spaces)
         - No special characters
  Example: "Photos", "Office Docs", "Customer Files"

SourceFolder (Required)
  What:  The folder to monitor for files
  Rules: - Must exist before starting the service
         - Service must have read/write access
         - Can be local path: C:\Users\John\Downloads
         - Can be UNC path: \\Server\Share\Incoming
         - NO wildcards allowed
  Important: Folder must already exist - service will not create it

DestinationFolder (Required)
  What:  Where files will be moved to
  Rules: - Must exist before starting the service
         - Service must have read/write access
         - Can be local path: C:\Archive\Documents
         - Can be UNC path: \\NAS\Storage\Files
         - NO wildcards allowed
  Important: Folder must already exist - service will not create it

Extension (Required)
  What:  File extensions to match
  Rules: - Must start with a period (.pdf not pdf)
         - Use pipe symbol (|) for multiple extensions
         - Each extension max 10 characters
         - Case insensitive (.PDF and .pdf both work)
         - NO wildcards allowed
  Special: ALL - Matches ANY file type (catch-all rule)
         - ALL rules MUST have a date criteria
         - Only ONE OTHERS rule allowed per source folder
         - Processes LAST (lowest priority - after specific extensions)
  Example: .pdf
           .doc|.docx
           .jpg|.jpeg|.png|.gif|.bmp
           ALL

ScanIntervalMs (Required)
  What:  How often to scan for files (milliseconds)
  Range: 5000 - 900000 (5 seconds to 15 minutes)
  Common Values:
    5000   = 5 seconds  (fast, higher CPU usage)
    30000  = 30 seconds (recommended)
    60000  = 1 minute   (slower, lower CPU usage)
    300000 = 5 minutes  (very slow, minimal CPU usage)
  Tip: Start with 30 seconds and adjust based on your needs

IsActive (Required)
  What:  Whether this rule is enabled
  Values: true  = Process files with this rule
          false = Skip this rule (useful for testing)
  Tip: Set to false while testing, then activate when ready

FileExists (Required)
  What:  What to do if destination file already exists
  Values: skip      = Don't move file (leave it in source)
          overwrite = Delete destination file and move new one
  WARNING: "overwrite" permanently deletes the existing file!
  Tip: Use "skip" unless you're sure you want to overwrite

DateFilter (Optional)
  What:  Filter files by date/time criteria (Last Accessed, Last Modified, or File Created)
  Format: "TYPE:SIGN:MINUTES"
    TYPE  = LA (Last Accessed), LM (Last Modified), FC (File Created)
    SIGN  = + (older than), - (within last)
    MINUTES = 1-5256000 (up to 10 years)

  Examples:
    "LA:+1440"  = Files NOT accessed in last 24 hours (older files)
    "LA:-60"    = Files accessed within last 60 minutes (recent files)
    "LM:+60"    = Files NOT modified in last 60 minutes (older files)
    "LM:-30"    = Files modified within last 30 minutes (recent files)
    "FC:+10080" = Files created more than 7 days ago (older files)
    "FC:-1440"  = Files created within last 24 hours (recent files)
    ""          = No date filter (process all matching files)

  Use Cases:
    LA:+ (older than)  = Archive files not accessed in a long time
    LA:- (within last) = Process only recently accessed files
    LM:+ (older than)  = Archive files not changed in a long time
    LM:- (within last) = Process only recently modified files
    FC:+ (older than)  = Archive old files based on creation date
    FC:- (within last) = Process only newly created files

  Important Notes:
    - Only ONE date filter per rule (cannot combine LA, LM, and FC)
    - OTHERS extension rules MUST have a DateFilter (required)
    - Date filters are optional for specific extension rules
    - "+" sign means "older than" (files NOT matching recent criteria)
    - "-" sign means "within last" (recent files only)
    - Windows may disable Last Access tracking - check with: fsutil behavior query disablelastaccess


IMPORTANT RULE NOTES:
---------------------

Extension Conflicts
  ✗ WRONG: Two rules both processing .pdf from C:\Downloads
  ✓ RIGHT: Each extension in a folder is handled by only one rule

  Why: If multiple rules match the same file, only the first rule processes it

Network Paths
  - Both source and destination can be UNC paths (\\server\share\folder)
  - Service account must have network access permissions
  - Test paths manually before adding to config
  - Network issues cause files to be skipped and retried

File Locking
  - Files locked by other programs are automatically skipped
  - Service will retry on the next scan interval
  - Check logs if files never get processed

Zero-Byte Files
  - Files with 0 bytes are automatically skipped
  - Service tracks them and processes when they have content
  - Prevents moving incomplete downloads

Scan Timing
  - A random 0-10 second offset is added to each scan
  - Prevents all rules from running at the exact same time
  - Reduces CPU spikes and improves performance


--------------------------------------------------------------------------------
4.2 APPLICATION SETTINGS
--------------------------------------------------------------------------------

Application settings control how the service behaves. Most users can use the
defaults, but you can customize for specific needs.

EXAMPLE:
--------
Application:
  ProcessingPaused: true
  RetryDelayMs: 5000
  FailureCooldownMs: 180000
  RecheckServiceMs: 30000
  RecheckTrayMs: 30000
  PauseDelayMs: 0
  ServiceHeartbeatMs: 900000
  MemoryLimitMb: 512
  MemoryCheckMs: 60000
  LogFolder: C:\Program Files\RJAutoMover\Logs
  ServiceGrpcPort: 60051
  TrayGrpcPort: 60052


SETTINGS EXPLAINED:
-------------------

ProcessingPaused
  What:  Whether service starts in paused mode
  Default: true (RECOMMENDED)
  Values:  true  = Service starts paused (must manually resume)
           false = Service starts processing immediately
  Why: Starting paused prevents accidental file moves during setup
  Tip: Keep this true and use the tray icon to resume when ready

RetryDelayMs
  What:  Delay between retry attempts for failed operations
  Default: 5000 (5 seconds)
  Range: 1000 - 30000 (1 to 30 seconds)
  Example: File is locked -> Wait 5 seconds -> Try again
  Note: Files are retried up to 5 times before giving up

FailureCooldownMs
  What:  How long to ignore a file after repeated failures
  Default: 180000 (3 minutes)
  Range: 0 - 180000 (0 to 3 minutes)
  Example: File fails 5 times -> Ignore for 3 minutes -> Try again
  Tip: Set to 0 to disable cooldown (not recommended)

RecheckServiceMs
  What:  Internal health check interval
  Default: 30000 (30 seconds)
  Range: 5000 - 60000
  Note: Rarely needs changing - this is for internal monitoring

RecheckTrayMs
  What:  How often tray refreshes status
  Default: 30000 (30 seconds)
  Range: 5000 - 60000
  Note: Lower = more responsive tray, higher CPU usage

PauseDelayMs
  What:  Delay between processing individual files
  Default: 0 (no delay)
  Range: 0 - 60000 (0 to 1 minute)
  Use Case: Slow down processing to avoid network saturation
  Example: 1000 = Wait 1 second between each file

ServiceHeartbeatMs
  What:  Interval for service/tray communication health checks
  Default: 300000 (5 minutes)
  Range: 60000 - 3600000 (1 minute to 1 hour)
  Note: This is for connection monitoring - rarely needs changing

MemoryLimitMb
  What:  Memory usage threshold before entering error mode
  Default: 512 MB (recommended)
  Range: 256 - 1024 MB
  Guidance:
    256  = Constrained systems (old hardware)
    512  = Normal systems (recommended)
    1024 = Busy systems (many rules, large files)
  Note: Service stops processing if limit exceeded

MemoryCheckMs
  What:  How often to check and log memory usage
  Default: 60000 (1 minute)
  Range: 30000 - 300000 (30 seconds to 5 minutes)
  Tip: Check logs to see memory trends over time

LogFolder
  What:  Where to store log files
  Default: C:\Program Files\RJAutoMover\Logs
  Example: C:\Logs\RJAutoMover
           D:\ApplicationLogs\RJAutoMover
  Notes: - Folder is created automatically if needed
         - Service must have write access
         - Old logs (>7 days) are auto-deleted on startup

ServiceGrpcPort
  What:  TCP port for RJAMService gRPC server
  Default: 60051
  Range: 1024 - 65535 (user ports only, no system ports)
  When to change:
    - Port conflict with another application
    - Windows reserves this port range
    - Security requirements (custom port)
  Important: Must be different from TrayGrpcPort
  Note: Service and Tray must both be restarted after changing

TrayGrpcPort
  What:  TCP port for RJAMTray gRPC server
  Default: 60052
  Range: 1024 - 65535 (user ports only, no system ports)
  When to change:
    - Port conflict with another application
    - Windows reserves this port range
    - Security requirements (custom port)
  Important: Must be different from ServiceGrpcPort
  Note: Service and Tray must both be restarted after changing


================================================================================
5. USING THE TRAY ICON
================================================================================

The system tray icon shows service status and provides quick controls.

ICON COLORS:
------------
  (Active)   - Service is running and processing files
  (Waiting)  - Service is running but no files to process
  (Paused)   - Service is paused (not processing files)
  (Error)    - Configuration error or system problem
  (Stopped)  - Service is not running

RIGHT-CLICK MENU:
-----------------
  * Status                    - Shows current service state (read-only)
  * Pause/Resume Processing   - Toggle file processing on/off
  * View Recent Transfers...  - Opens About window to Transfers tab
  * About...                  - Opens About window to Version tab

ABOUT WINDOW:
-------------
The About window has multiple tabs providing comprehensive information:

  TRANSFERS TAB:
    * Shows recent file transfers in real-time from persistent database
    * Displays: time, filename, size, rule name, destination folder, status
    * Pale blue highlighting for transfers from current service session
    * Animated braille spinner for files currently being processed
    * Status icons: ⠋ (processing), ✓ (success), ✗ (failed), ⚠ (blocked)
    * Session ID tracking for cross-session history
    * Pause/Resume processing button
    * Transfer count and status summary

  CONFIG TAB:
    * Parsed View: Easy-to-read display of file rules and app settings
    * Raw YAML View: View actual config.yaml content
    * Shows rule status badges (Active/Inactive)
    * Displays application settings with defaults

  LOGS TAB:
    * View both Service and Tray logs
    * Filter by log level (All, ERROR, WARN, INFO, DEBUG, gRPC)
    * Search logs with text filtering
    * Shows log folder location

  SYSTEM TAB:
    * Installation location and OS version
    * Memory usage (Tray and Service)
    * Uptime tracking

  .NET TAB:
    * Build SDK version
    * Current runtime version
    * NuGet package information

  VERSION TAB:
    * Tray and Service version numbers
    * Build dates
    * Service account (Run As)


================================================================================
6. COMMON USE CASES
================================================================================

ORGANIZING DOWNLOADS:
---------------------
Problem: Downloads folder gets cluttered with different file types
Solution: Automatically sort by file type

FileRules:
  - Name: Documents
    SourceFolder: C:\Users\Me\Downloads
    Extension: .pdf|.doc|.docx|.xls|.xlsx
    DestinationFolder: C:\Documents\Incoming
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip

  - Name: Images
    SourceFolder: C:\Users\Me\Downloads
    Extension: .jpg|.jpeg|.png|.gif
    DestinationFolder: C:\Pictures\Incoming
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip

  - Name: Archives
    SourceFolder: C:\Users\Me\Downloads
    Extension: .zip|.rar|.7z
    DestinationFolder: C:\Archives
    ScanIntervalMs: 30000
    IsActive: true
    FileExists: skip


PROCESSING NETWORK SHARE:
--------------------------
Problem: Need to move files from network share to local storage
Solution: Monitor UNC path and move to local folder

FileRules:
  - Name: Incoming Files
    SourceFolder: \\FileServer\Incoming\ToProcess
    Extension: .xml|.csv|.txt
    DestinationFolder: C:\ProcessedData\Incoming
    ScanIntervalMs: 60000
    IsActive: true
    FileExists: skip


PHOTO WORKFLOW:
---------------
Problem: Import camera photos from card to organized folders
Solution: Monitor card location, move to dated folder

FileRules:
  - Name: Camera Import
    SourceFolder: E:\DCIM\Camera
    Extension: .jpg|.jpeg|.raw|.cr2
    DestinationFolder: D:\Photos\Import
    ScanIntervalMs: 10000
    IsActive: true
    FileExists: skip


BACKUP AUTOMATION:
------------------
Problem: Need to backup specific file types to backup location
Solution: Monitor work folder, copy to backup (using two rules)

FileRules:
  - Name: Project Files Backup
    SourceFolder: C:\Projects\Active
    Extension: .dwg|.dxf
    DestinationFolder: \\BackupNAS\ProjectBackups
    ScanIntervalMs: 300000
    IsActive: true
    FileExists: overwrite


ARCHIVING OLD FILES:
--------------------
Problem: Need to automatically archive files older than a certain age
Solution: Use DateFilter with FC:+ (files created MORE than X minutes ago)

FileRules:
  - Name: Archive Old Logs
    SourceFolder: C:\Logs
    Extension: .log|.txt
    DestinationFolder: C:\Logs\Archive
    ScanIntervalMs: 86400000
    IsActive: true
    FileExists: skip
    DateFilter: "FC:+10080"  # Older than 7 days (7 * 24 * 60)

  - Name: Archive Old Screenshots
    SourceFolder: C:\Users\Me\Pictures\Screenshots
    Extension: .png|.jpg
    DestinationFolder: D:\Archive\Screenshots
    ScanIntervalMs: 3600000
    IsActive: true
    FileExists: skip
    DateFilter: "FC:+43200"  # Older than 30 days (30 * 24 * 60)


PROCESSING RECENT FILES ONLY:
------------------------------
Problem: Only want to process files modified in the last hour
Solution: Use DateFilter with LM:- (files modified WITHIN last X minutes)

FileRules:
  - Name: Recent Reports
    SourceFolder: \\ReportServer\Output
    Extension: .pdf|.xlsx
    DestinationFolder: C:\Reports\Today
    ScanIntervalMs: 60000
    IsActive: true
    FileExists: overwrite
    DateFilter: "LM:-60"  # Within last 60 minutes (recent only)


CATCH-ALL RULES WITH EXTENSION: OTHERS
------------------------------------
Problem: Want to move ANY old file from Downloads, not just specific types
Solution: Use Extension: OTHERS with a DateFilter (lowest priority)

FileRules:
  # Specific rules process FIRST
  - Name: Keep Recent Videos
    SourceFolder: C:\Users\Me\Downloads
    Extension: .mp4|.avi|.mkv
    DestinationFolder: C:\Users\Me\Videos
    ScanIntervalMs: 60000
    IsActive: true
    FileExists: skip

  - Name: Keep Recent Documents
    SourceFolder: C:\Users\Me\Downloads
    Extension: .pdf|.doc|.docx
    DestinationFolder: C:\Users\Me\Documents
    ScanIntervalMs: 60000
    IsActive: true
    FileExists: skip

  # OTHERS rule processes LAST (catch-all for everything else)
  - Name: Archive Old Downloads
    SourceFolder: C:\Users\Me\Downloads
    Extension: OTHERS
    DestinationFolder: C:\Users\Me\Downloads\Archive
    ScanIntervalMs: 3600000
    IsActive: true
    FileExists: skip
    AgeCreatedMins: 43200  # POSITIVE = older than 30 days (move old files)

Important Notes:
  - OTHERS rules MUST have a DateFilter
  - Only ONE OTHERS rule allowed per source folder
  - OTHERS rules process LAST (lowest priority - after specific extension rules)
  - Date criteria can be positive (older than) or negative (within last)
  - In the example above:
    * Videos and documents are moved to specific folders immediately
    * Everything else (images, executables, etc.) older than 30 days is archived


================================================================================
7. TROUBLESHOOTING
================================================================================

PROBLEM: Service won't start
SOLUTIONS:
  ✓ Open Windows Event Viewer -> Application log
  ✓ Check C:\Program Files\RJAutoMover\Logs for error messages
  ✓ Verify config.yaml is valid YAML syntax (proper indentation)
  ✓ Ensure all folders in config exist
  ✓ Verify LogFolder is writable

PROBLEM: Files aren't being moved
SOLUTIONS:
  ✓ Verify service is running (services.msc)
  ✓ Check if processing is paused (tray icon blue = paused)
  ✓ Verify rule IsActive is true
  ✓ Check file extension matches exactly
  ✓ Ensure files aren't locked by another program
  ✓ Review logs for errors
  ✓ Test with a simple file like a .txt file first

PROBLEM: Tray icon shows red (error)
SOLUTIONS:
  ✓ Check logs for detailed error messages
  ✓ Verify all folders exist and are accessible
  ✓ Fix config.yaml validation errors
  ✓ Restart RJService after fixing issues

PROBLEM: Configuration changes not taking effect
SOLUTIONS:
  ✓ Restart RJAutoMoverService (Win+R -> services.msc -> RJAutoMoverService -> Restart)
  ✓ WARNING: Service detects external config changes and enters ERROR MODE
  ✓ Service restart is REQUIRED after any config.yaml changes
  ✓ Check logs to verify new config was loaded
  ✓ Verify YAML indentation is correct (spaces, not tabs)

PROBLEM: Network folders not working
SOLUTIONS:
  ✓ Test UNC path manually (\\server\share\folder)
  ✓ Verify service account has network permissions
  ✓ Check Windows firewall rules
  ✓ Ensure SMB/CIFS ports are open
  ✓ Try accessing the share from File Explorer first

PROBLEM: Memory error mode
SOLUTIONS:
  ✓ Restart RJAutoMoverService Windows service
  ✓ Increase MemoryLimitMb in config.yaml
  ✓ Check logs for unusual patterns
  ✓ Reduce number of active rules
  ✓ Increase scan intervals


================================================================================
8. ADVANCED TOPICS
================================================================================

VALIDATION ON STARTUP:
----------------------
RJAutoMoverService validates config.yaml when it starts:
  1. Checks all required fields are present
  2. Validates folder paths exist and are accessible
  3. Checks for extension conflicts
  4. Validates numeric ranges
  5. If validation fails -> service enters error mode

CONFIG CHANGE DETECTION:
------------------------
  * Service monitors config.yaml for external changes
  * If config is modified while service is running, it enters ERROR MODE
  * All file processing stops immediately
  * Service must be restarted to apply changes and resume processing
  * This prevents unexpected behavior from mid-run config changes

LOGGING:
--------
RJAutoMover uses Serilog for reliable, high-performance logging with automatic
rotation and retention.

Log files are in C:\ProgramData\RJAutoMover\Logs\ (default):
  * YYYY-MM-DD-HH-mm-ss RJAutoMoverService.log  (service logs)
  * YYYY-MM-DD-HH-mm-ss RJAutoMoverTray.log     (tray logs)
  * When logs reach 10MB, they rotate: RJAutoMoverService_001.log, _002.log, etc.

Log levels:
  [FATAL] - Critical errors causing service termination
  [ERROR] - Errors preventing operations
  [WARN ] - Warnings that don't stop operations
  [INFO ] - General operational information
  [DEBUG] - Detailed debugging information
  [gRPC>] - Outgoing gRPC communication
  [gRPC<] - Incoming gRPC communication

Automatic Log Management:
  * Logs automatically rotate when they reach 10MB
  * Old logs are automatically deleted on service/tray startup (default: >7 days)
  * Log retention period configurable via LogRetentionDays setting
  * Shared file access allows tray to read service logs in real-time

CONFIGURATION TIPS:
-------------------
  * Always backup config.yaml before editing
  * Start with ProcessingPaused: true for safety
  * Test new rules with IsActive: false first
  * Use descriptive rule names for easier troubleshooting
  * Start with longer scan intervals (30-60 seconds)
  * Monitor logs after making changes
  * Test network paths manually before using in rules

FILE OPERATION DETAILS:
-----------------------
  * Files are MOVED (not copied) - originals are deleted
  * Move is atomic on the same volume
  * Cross-volume moves are copy-then-delete
  * FileExists: overwrite DELETES the existing file permanently
  * Locked files are skipped and retried on next scan
  * Zero-byte files are tracked until they have content

PERMISSIONS:
------------
  * RJAutoMoverService runs as Local System by default
  * For network shares, may need to configure service account
  * RJAutoMoverTray runs with standard user privileges (no admin required)
  * All folders must have read/write permissions for service account

PERFORMANCE:
------------
  * CPU usage depends on scan intervals and number of rules
  * Faster scanning = more CPU usage
  * Random offset (0-10 seconds) spreads out rule execution
  * Memory usage typically 50-150 MB under normal load
  * Network operations are slower than local disk operations


================================================================================
9. TECHNICAL INFORMATION
================================================================================

SYSTEM REQUIREMENTS:
--------------------
  Operating System: Windows 10, Windows 11, Windows Server 2016 or later
  Framework: .NET 10.0 Runtime (included in installer)
  Disk Space: ~50 MB for application files
  Memory: 256-512 MB RAM minimum
  Permissions: Administrator rights required for installation

DEPENDENCIES:
-------------
  Core Libraries:
    * .NET 10.0 Runtime (included in installer)
    * Grpc.AspNetCore 2.71.0 - gRPC communication
    * YamlDotNet 16.3.0 - Configuration file parsing
    * Serilog 4.3.0 - Structured logging framework
    * Serilog.Sinks.File 7.0.0 - File logging with rotation

  Database:
    * Microsoft.Data.Sqlite 7.0.20 - Activity history database

    IMPORTANT: SQLite version is pinned to 7.x due to Windows Defender false
    positive detection with version 8.x (Trojan:Script/Wacatac.B!ml). Do not
    upgrade to SQLite 8.x as it will trigger antivirus warnings.

  UI Components (Tray):
    * Hardcodet.NotifyIcon.Wpf 2.0.1 - System tray icon
    * CommunityToolkit.Mvvm 8.4.0 - MVVM framework

INSTALLATION DETAILS:
---------------------
  Install Location: C:\Program Files\RJAutoMover\
  Service Name: RJAutoMoverService
  Service Display Name: RJAutoMover Service
  Service Description: Monitors folders and automatically moves files based on configured rules
  Service Startup: Automatic
  Tray Startup: Scheduled task (user-specific, at logon)

FILES INSTALLED:
----------------
  RJAutoMoverService.exe   - Windows service executable
  RJAutoMoverTray.exe      - System tray application
  config.yaml              - Configuration file
  README.txt               - This documentation
  *.dll                    - .NET libraries and dependencies
  Logs\                    - Log file directory (created on first run)

ARCHITECTURE:
-------------
  * RJAutoMoverService and RJAutoMoverTray communicate via gRPC (localhost ports 60051/60052)
  * Bidirectional gRPC communication for real-time status updates
  * Connection automatically reconnects if interrupted
  * Service can run independently of tray application
  * Tray application provides monitoring only (not required for processing)

SINGLE INSTANCE ENFORCEMENT:
----------------------------
  * Only one RJAutoMoverTray instance can run at a time system-wide
  * Starting a new instance automatically closes the previous one
  * Orphaned mutex detection - recovers from crashed/killed instances
  * Cross-user enforcement - works across different Windows user sessions
  * RJAutoMoverService is a Windows service (only one instance by design)

UNINSTALLATION:
---------------
  1. Uninstall via Windows Settings -> Apps -> RJAutoMover
  2. Service is stopped and removed automatically
  3. Scheduled task is removed automatically
  4. Configuration and logs are deleted
  5. Manual cleanup: Delete C:\Program Files\RJAutoMover\ if needed


================================================================================
IMPORTANT WARNINGS
================================================================================

⚠ Configuration Changes Require Service Restart
  You MUST restart RJAutoMoverService after editing config.yaml
  Win+R -> services.msc -> RJAutoMoverService -> Right-click -> Restart
  Service automatically enters ERROR MODE if config is changed while running

⚠ File Operations Are Permanent
  Files are MOVED (not copied) - originals are deleted from source
  Use FileExists: overwrite carefully - it permanently deletes destination files
  Always test with non-critical files first

⚠ Safe Mode by Default
  ProcessingPaused starts as true to prevent accidental file moves
  This is intentional for safety - use tray icon to resume when ready


================================================================================
SUPPORT & RESOURCES
================================================================================

Documentation:
  * This README file
  * Configuration examples in the installation directory
  * Log files in C:\Program Files\RJAutoMover\Logs\

Source Code:
  * GitHub: https://github.com/toohighonthehog/RJAutoMover

Getting Help:
  * Check log files first for error messages
  * Review troubleshooting section in this README
  * Check GitHub issues for known problems
  * Create a GitHub issue with logs and config for support

Disclaimer:
  RJAutoMover is provided as-is with no warranty. Use at your own risk.
  Always test thoroughly with non-critical files before production use.


================================================================================
VERSION INFORMATION
================================================================================

Version: 0.9.6.107
Release Date: October 2025
Built with: .NET 10.0
Platform: Windows 10/11, Windows Server 2016+
License: MIT License

================================================================================
