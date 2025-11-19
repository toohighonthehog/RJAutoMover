using System.Collections.Concurrent;
using System.IO;
using System.Timers;
using RJAutoMoverShared.Models;
using RJAutoMoverShared.Services;
using LogLevel = RJAutoMoverShared.Models.LogLevel;

namespace RJAutoMoverS.Services;

public class SkippedFileInfo
{
    public long Size { get; set; }
    public DateTime LastWriteTime { get; set; }
    public DateTime SkippedAt { get; set; }
}

public class FileProcessorService
{
    private readonly RJAutoMoverShared.Services.LoggingService _logger;
    private readonly RecentActivityService _recentActivityService;
    private readonly RuntimeStateService _runtimeState;
    private readonly ConcurrentDictionary<string, System.Timers.Timer> _timers = new();
    private readonly ConcurrentDictionary<string, bool> _processingRules = new();
    private readonly ConcurrentDictionary<string, bool> _processingFiles = new();
    private readonly ConcurrentDictionary<string, SkippedFileInfo> _skippedFiles = new();
    private readonly ConcurrentDictionary<string, SkippedFileInfo> _zeroByteFiles = new();
    private Configuration? _config;
    private bool _processingPaused;
    private bool _configErrorMode = false;
    private string _lastRecentsContent = "";
    private RJAutoMoverS.Config.ConfigValidator? _configValidator;
    private GrpcServerServiceSimplified? _grpcServer;
    private ServiceGrpcClientServiceV2? _serviceClient;
    private ActivityHistoryService? _activityHistoryService;
    public event EventHandler<StatusUpdateEventArgs>? StatusUpdated;
    public event EventHandler<IconUpdateEventArgs>? IconUpdated;
    public event EventHandler<RecentsUpdateEventArgs>? RecentsUpdated;
    public event EventHandler<FileCountUpdateEventArgs>? FileCountUpdated;

    public bool ProcessingPaused => _processingPaused;
    public List<string> TrayRecent => _recentActivityService.GetRecentActivitiesDisplay();
    public ActivityHistoryService? ActivityHistory => _activityHistoryService;

    /// <summary>
    /// Gets recent activities with session-aware historical data if available
    /// Merges current session activities with historical database records
    /// </summary>
    public List<string> GetActivitiesWithHistory(int historyLimit = 1000)
    {
        if (_activityHistoryService == null)
        {
            // No history service - return current session only
            _logger.Log(LogLevel.DEBUG, "ActivityHistoryService is null - returning current session only");
            return _recentActivityService.GetRecentActivitiesDisplay();
        }

        // Get current session activities (in-memory)
        var currentActivities = _recentActivityService.GetRecentActivitiesDisplay();
        _logger.Log(LogLevel.DEBUG, $"GetActivitiesWithHistory: Current session has {currentActivities.Count} activities");

        // Get historical activities from database (from previous sessions)
        var historicalRecords = _activityHistoryService.GetRecentActivities(historyLimit);
        _logger.Log(LogLevel.DEBUG, $"GetActivitiesWithHistory: Database returned {historicalRecords.Count} total records");

        // Filter out records from current session (they're already in currentActivities)
        var currentSessionId = _activityHistoryService.SessionId;
        var historicalDisplay = historicalRecords
            .Where(r => r.SessionId != currentSessionId) // Only include records from OTHER sessions
            .Select(r => r.ToDisplayString(includeSession: true))
            .ToList();
        _logger.Log(LogLevel.DEBUG, $"GetActivitiesWithHistory: After filtering current session, {historicalDisplay.Count} historical records remain");

        // Merge: current session activities first, then historical
        var combined = new List<string>();
        combined.AddRange(currentActivities);
        combined.AddRange(historicalDisplay);
        _logger.Log(LogLevel.DEBUG, $"GetActivitiesWithHistory: Returning {combined.Count} total activities (limit={historyLimit})");

        return combined.Take(historyLimit).ToList();
    }

    public FileProcessorService(LoggingService logger, RuntimeStateService runtimeState)
    {
        _logger = logger;
        _runtimeState = runtimeState;
        _recentActivityService = new RecentActivityService(logger);

        // Subscribe to recent activity updates
        _recentActivityService.RecentsUpdated += (sender, args) => OnRecentsUpdated(args.Recents);
    }

    public void Initialize(Configuration config, RJAutoMoverS.Config.ConfigValidator? configValidator = null, GrpcServerServiceSimplified? grpcServer = null, ServiceGrpcClientServiceV2? serviceClient = null)
    {
        _config = config;
        _configValidator = configValidator;
        _grpcServer = grpcServer;
        _serviceClient = serviceClient;

        // Start new session
        _runtimeState.StartNewSession();

        // Determine initial paused state: runtime state takes precedence over config
        bool startPaused;
        if (_runtimeState.StateFileExists() && _runtimeState.LastModified.HasValue)
        {
            // Runtime state exists and has been modified - use it (persisted from previous session)
            startPaused = _runtimeState.IsProcessingPaused;
            _logger.Log(LogLevel.INFO,
                $"Using persisted paused state: {startPaused} " +
                $"(set by {_runtimeState.LastModifiedBy} at {_runtimeState.LastModified})");
        }
        else
        {
            // First run or state file deleted - use config default
            startPaused = config.Application.ProcessingPaused;
            _runtimeState.IsProcessingPaused = startPaused;
            _logger.Log(LogLevel.INFO,
                $"Using config default paused state: {startPaused} (first run or state file not found)");
        }

        _processingPaused = startPaused;

        // Initialize activity history if enabled
        if (config.Application.ActivityHistoryEnabled)
        {
            _logger.Log(LogLevel.INFO, "Activity history is ENABLED in config - initializing database...");
            try
            {
                // Use ProgramData for database (shared, writable by service)
                // Path: C:\ProgramData\RJAutoMover\Data\ActivityHistory.db
                var dataFolder = RJAutoMoverShared.Constants.Paths.GetSharedDataFolder();
                Directory.CreateDirectory(dataFolder);

                var dbPath = Path.Combine(dataFolder, "ActivityHistory.db");
                _logger.Log(LogLevel.INFO, $"Activity history database path: {dbPath}");

                _activityHistoryService = new ActivityHistoryService(
                    _logger,
                    dbPath,
                    enabled: true,
                    maxRecords: config.Application.ActivityHistoryMaxRecords,
                    retentionDays: config.Application.ActivityHistoryRetentionDays);

                // Connect activity history to recent activity service
                _recentActivityService.SetActivityHistoryService(_activityHistoryService);

                _logger.Log(LogLevel.INFO, $"Activity history SUCCESSFULLY initialized - Session ID: {_activityHistoryService.SessionId}");

                // Clean up orphaned transfers from previous sessions
                var orphanedCount = _activityHistoryService.CleanupOrphanedTransfers();
                if (orphanedCount > 0)
                {
                    _logger.Log(LogLevel.INFO, $"Cleaned up {orphanedCount} orphaned transfer(s) that will no longer show as in-progress");
                }

                // Refresh gRPC server's recent items cache with historical data now that database is ready
                _grpcServer?.RefreshHistoricalCache();
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.ERROR, "============================================");
                _logger.Log(LogLevel.FATAL, "ACTIVITY HISTORY INITIALIZATION FAILED");
                _logger.Log(LogLevel.ERROR, "============================================");
                _logger.Log(LogLevel.ERROR, $"Exception: {ex.Message}");
                _logger.Log(LogLevel.ERROR, $"Stack trace: {ex.StackTrace}");
                _logger.Log(LogLevel.ERROR, "Activity history will be DISABLED for this session");
                _logger.Log(LogLevel.ERROR, "File transfers will proceed without database logging");
                _logger.Log(LogLevel.ERROR, "============================================");
                _activityHistoryService = null; // Ensure it's null if initialization failed
            }
        }
        else
        {
            _logger.Log(LogLevel.WARN, "Activity history is DISABLED in config - transfers will not be logged to database");
        }

        // Clear skipped files on initialization (fresh start each session)
        _skippedFiles.Clear();
        _logger.Log(LogLevel.INFO, "Skipped file tracking initialized - files will be tracked for changes during this session");
    }

    /// <summary>
    /// Initializes only the ActivityHistory service without full initialization.
    /// Used when config validation fails but we still want to show historical transfer data.
    /// </summary>
    public void InitializeActivityHistoryOnly(Configuration config)
    {
        try
        {
            // Only initialize ActivityHistory if enabled in config
            if (config.Application.ActivityHistoryEnabled)
            {
                _logger.Log(LogLevel.INFO, "Activity history is ENABLED - initializing for historical data display...");

                var dataFolder = RJAutoMoverShared.Constants.Paths.GetSharedDataFolder();
                Directory.CreateDirectory(dataFolder);

                var dbPath = Path.Combine(dataFolder, "ActivityHistory.db");
                _logger.Log(LogLevel.INFO, $"Activity history database path: {dbPath}");

                _activityHistoryService = new ActivityHistoryService(
                    _logger,
                    dbPath,
                    enabled: true,
                    maxRecords: config.Application.ActivityHistoryMaxRecords,
                    retentionDays: config.Application.ActivityHistoryRetentionDays);

                _logger.Log(LogLevel.INFO, $"Activity history SUCCESSFULLY initialized (read-only mode) - Session ID: {_activityHistoryService.SessionId}");
                _logger.Log(LogLevel.INFO, "Historical transfer data will be available in the Transfers tab");
            }
            else
            {
                _logger.Log(LogLevel.INFO, "Activity history is DISABLED in config - no historical data available");
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Failed to initialize ActivityHistory for historical data: {ex.Message}");
            _activityHistoryService = null;
        }
    }

    public async Task StartProcessingAsync(CancellationToken cancellationToken)
    {
        if (_config == null) return;

        var errorId = Guid.NewGuid().ToString()[..8];
        _logger.Log(LogLevel.INFO, $"[{errorId}] StartProcessingAsync initiated");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Check for config changes - if detected, enter error mode and stop all processing
                if (_configValidator?.HasConfigChanged() == true && !_configErrorMode)
                {
                    _configErrorMode = true;
                    _logger.Log(LogLevel.INFO, "Service entering configuration error mode - all processing stopped");
                    _logger.Log(LogLevel.INFO, "Service will not process any files until restarted");

                    // Stop all timers immediately
                    StopAllTimers();

                    // Re-validate config to get detailed error message
                    var detailedError = await GetConfigChangeErrorMessageAsync();
                    OnStatusUpdated(detailedError);
                    OnIconUpdated(IconNames.Error);
                }

                // If in config error mode, do nothing except wait
                if (_configErrorMode)
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                }
                else if (_processingPaused)
                {
                    OnStatusUpdated("Processing Paused...");
                    OnIconUpdated(IconNames.Paused);

                    var configCheckIntervalMs = _config?.Application?.RecheckServiceMs ?? 30000;
                    var lastConfigCheck = DateTime.Now;

                    while (_processingPaused && !cancellationToken.IsCancellationRequested && !_configErrorMode)
                    {
                        // Check for config changes at RecheckServiceMs intervals even when paused
                        if ((DateTime.Now - lastConfigCheck).TotalMilliseconds >= configCheckIntervalMs)
                        {
                            if (_configValidator?.HasConfigChanged() == true)
                            {
                                _configErrorMode = true;
                                _logger.Log(LogLevel.INFO, "Configuration file changed while processing was paused - service entering error mode");
                                _logger.Log(LogLevel.INFO, "Service will not process any files until restarted");

                                // Stop all timers immediately
                                StopAllTimers();

                                // Re-validate config to get detailed error message
                                var detailedError = await GetConfigChangeErrorMessageAsync();
                                OnStatusUpdated(detailedError);
                                OnIconUpdated(IconNames.Error);

                                // Notify tray of config error with detailed message
                                NotifyConfigError(detailedError);

                                break;
                            }

                            lastConfigCheck = DateTime.Now;
                        }

                        await Task.Delay(1000, cancellationToken);
                    }
                }
                else
                {
                    OnStatusUpdated("Waiting for files...");
                    OnIconUpdated(IconNames.Waiting);

                    // Set up timers for each active FileRule
                    foreach (var rule in _config.FileRules.Where(r => r.IsActive))
                    {
                        if (!_timers.ContainsKey(rule.Name))
                        {
                            var timer = new System.Timers.Timer(rule.ScanIntervalMs);
                            timer.Elapsed += async (sender, e) =>
                            {
                                try
                                {
                                    // Add random offset between 0 and 10 seconds (0-10000ms) before each execution
                                    var random = new Random();
                                    var randomOffset = random.Next(0, 10001);

                                    _logger.Log(LogLevel.DEBUG, $"Rule '{rule.Name}' waiting {randomOffset}ms random offset before processing");
                                    await Task.Delay(randomOffset);

                                    await ProcessFileRule(rule);
                                }
                                catch (Exception timerEx)
                                {
                                    var timerErrorId = Guid.NewGuid().ToString()[..8];
                                    _logger.Log(LogLevel.ERROR, $"[{timerErrorId}] Timer exception for rule '{rule.Name}': {timerEx.Message}");
                                }
                            };
                            timer.Start();
                            _timers[rule.Name] = timer;

                            _logger.Log(LogLevel.INFO, $"Started timer for rule '{rule.Name}' with interval {rule.ScanIntervalMs}ms (with 0-10s random offset per execution)");
                        }
                    }

                    // Trigger immediate scan of all rules when resuming to catch any files that accumulated during pause
                    // Process specific extension rules first, then OTHERS rules (lowest priority)
                    _logger.Log(LogLevel.INFO, "Processing resumed - triggering immediate scan of all rules to process any pending files");

                    var activeRules = _config.FileRules.Where(r => r.IsActive).ToList();
                    var specificRules = activeRules.Where(r => !r.IsAllExtensionRule()).ToList();
                    var allRules = activeRules.Where(r => r.IsAllExtensionRule()).ToList();

                    // Process specific extension rules first
                    foreach (var rule in specificRules)
                    {
                        try
                        {
                            await ProcessFileRule(rule);
                        }
                        catch (Exception immediateEx)
                        {
                            var immediateErrorId = Guid.NewGuid().ToString()[..8];
                            _logger.Log(LogLevel.ERROR, $"[{immediateErrorId}] Immediate scan exception for rule '{rule.Name}': {immediateEx.Message}");
                        }
                    }

                    // Then process OTHERS extension rules (lowest priority - catch-all)
                    foreach (var rule in allRules)
                    {
                        try
                        {
                            _logger.Log(LogLevel.DEBUG, $"Processing OTHERS rule '{rule.Name}' (lowest priority - catch-all)");
                            await ProcessFileRule(rule);
                        }
                        catch (Exception immediateEx)
                        {
                            var immediateErrorId = Guid.NewGuid().ToString()[..8];
                            _logger.Log(LogLevel.ERROR, $"[{immediateErrorId}] Immediate scan exception for ALL rule '{rule.Name}': {immediateEx.Message}");
                        }
                    }

                    // Wait while processing
                    while (!_processingPaused && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Log(LogLevel.INFO, $"[{errorId}] Processing cancelled gracefully");
                break;
            }
            catch (Exception ex)
            {
                var loopErrorId = Guid.NewGuid().ToString()[..8];
                _logger.Log(LogLevel.ERROR, $"[{loopErrorId}] Critical error in processing loop: {ex.Message}");
                _logger.Log(LogLevel.ERROR, $"[{loopErrorId}] Stack: {ex.StackTrace}");
                _logger.Log(LogLevel.ERROR, $"[{loopErrorId}] Attempting recovery in 30 seconds...");

                try
                {
                    OnStatusUpdated("Recovery Mode - Processing Error");
                    OnIconUpdated(IconNames.Error);
                }
                catch { }

                // Wait before retry to prevent tight error loops
                try
                {
                    await Task.Delay(30000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.Log(LogLevel.INFO, $"[{errorId}] StartProcessingAsync completed");
    }

    private async Task ProcessFileRule(FileRule rule)
    {
        if (_processingPaused || _config == null || _configErrorMode) return;

        // Check for config changes before processing ANY files for this rule
        if (_configValidator?.HasConfigChanged() == true)
        {
            if (!_configErrorMode)
            {
                _configErrorMode = true;
                _logger.Log(LogLevel.INFO, $"Rule '{rule.Name}' stopped - configuration file changed externally");
                _logger.Log(LogLevel.INFO, "Service entering configuration error mode - all processing stopped");

                // Stop all timers immediately
                StopAllTimers();

                // Re-validate config to get detailed error message
                var detailedError = await GetConfigChangeErrorMessageAsync();
                OnStatusUpdated(detailedError);
                OnIconUpdated(IconNames.Error);
            }
            return;
        }


        // Prevent concurrent processing of the same rule
        if (!_processingRules.TryAdd(rule.Name, true))
        {
            _logger.Log(LogLevel.DEBUG, $"Rule '{rule.Name}' is already being processed, skipping this cycle");
            return;
        }

        var ruleErrorId = Guid.NewGuid().ToString()[..8];

        try
        {
            _logger.Log(LogLevel.INFO, $"Started processing FileRule '{rule.Name}'");

            var isAllRule = rule.IsAllExtensionRule();
            var extensions = isAllRule ? new List<string>() : rule.GetExtensions();
            var processedFiles = new List<string>();
            long totalSize = 0;
            int fileCount = 0;

            // Get all matching files that are not zero-byte and not locked
            List<string> files;
            try
            {
                if (!Directory.Exists(rule.SourceFolder))
                {
                    _logger.Log(LogLevel.ERROR, $"[{ruleErrorId}] Source folder does not exist: {rule.SourceFolder}");
                    return;
                }

                if (isAllRule)
                {
                    // OTHERS rule: get all files, then filter by date criteria
                    _logger.Log(LogLevel.DEBUG, $"Processing OTHERS rule '{rule.Name}' - matching all file extensions");
                    files = Directory.GetFiles(rule.SourceFolder)
                        .Where(f => IsFileProcessable(f))
                        .Where(f => MatchesDateCriteria(f, rule))
                        .ToList();
                }
                else
                {
                    // Regular rule: filter by specific extensions
                    files = Directory.GetFiles(rule.SourceFolder)
                        .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                        .Where(f => IsFileProcessable(f))
                        .Where(f => MatchesDateCriteria(f, rule))
                        .ToList();
                }
            }
            catch (Exception dirEx)
            {
                _logger.Log(LogLevel.ERROR, $"[{ruleErrorId}] Error accessing source directory '{rule.SourceFolder}': {dirEx.Message}");
                return;
            }

            foreach (var sourceFile in files)
            {
                // Check if processing has been paused or if in config error mode before starting new file
                if (_processingPaused || _configErrorMode)
                {
                    if (_configErrorMode)
                        _logger.Log(LogLevel.INFO, $"Configuration error mode active, stopping new file transfers for rule '{rule.Name}'");
                    else
                        _logger.Log(LogLevel.DEBUG, $"Processing paused, stopping new file transfers for rule '{rule.Name}'");
                    break;
                }


                var fileName = Path.GetFileName(sourceFile);

                // Check if file is blacklisted
                if (_recentActivityService.IsFileBlacklisted(rule.SourceFolder, fileName))
                {
                    _logger.Log(LogLevel.DEBUG, $"Skipping blacklisted file: {fileName}");
                    continue;
                }

                // Check if file is already being processed by another rule
                if (!_processingFiles.TryAdd(sourceFile, true))
                {
                    _logger.Log(LogLevel.DEBUG, $"File already being processed by another rule: {fileName}");
                    continue;
                }

                // Update status only when we actually start processing a file
                if (fileCount == 0) // First file being processed
                {
                    OnStatusUpdated($"Processing {rule.Name}...");
                    OnIconUpdated(IconNames.Active);
                }

                // Start tracking this file transfer
                var processingId = Guid.NewGuid().ToString()[..8];

                // Check if file still exists before getting size (may have been moved by another rule)
                if (!File.Exists(sourceFile))
                {
                    _logger.Log(LogLevel.DEBUG, $"File no longer exists (moved by another process): {fileName}");
                    continue;
                }

                var fileInfo = new FileInfo(sourceFile);
                var fileSizeBytes = fileInfo.Length; // Get size before file operations - needed for notifications
                _logger.Log(LogLevel.INFO, $"[{processingId}] Starting file processing: {fileName} from rule '{rule.Name}'");
                var activityEntry = _recentActivityService.StartFileTransfer(fileName, rule.Name, fileSizeBytes, rule.SourceFolder, rule.DestinationFolder);

                bool transferSuccess = false;
                int maxRetries = 5;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        // Check if file still exists (may have been moved by another rule)
                        if (!File.Exists(sourceFile))
                        {
                            _logger.Log(LogLevel.DEBUG, $"[{processingId}] File no longer exists (moved by another process): {fileName}");
                            _recentActivityService.MarkTransferSuccess(activityEntry);
                            transferSuccess = true;
                            break;
                        }

                        _logger.Log(LogLevel.DEBUG, $"[{processingId}] File exists, proceeding with attempt {attempt}: {fileName}");

                        var currentFileInfo = new FileInfo(sourceFile);
                        // fileSizeBytes already captured above - use that for consistency
                        var destPath = Path.Combine(rule.DestinationFolder, fileName);

                        // Check FileExists policy
                        if (File.Exists(destPath) && rule.FileExists.ToLower() == "skip")
                        {
                            var sourceInfo = new FileInfo(sourceFile);
                            var fileKey = $"{rule.Name}:{fileName}";

                            // Check if file was previously skipped and hasn't changed
                            if (_skippedFiles.TryGetValue(fileKey, out var skippedInfo))
                            {
                                bool fileChanged = skippedInfo.Size != sourceInfo.Length ||
                                                 skippedInfo.LastWriteTime != sourceInfo.LastWriteTime;

                                if (!fileChanged)
                                {
                                    _logger.Log(LogLevel.TRACE, $"File already skipped and unchanged: {fileName}");
                                    // Don't add skipped files to recent files list
                                    transferSuccess = true;
                                    break;
                                }
                                else
                                {
                                    _logger.Log(LogLevel.INFO, $"File changed since last skip, reprocessing: {fileName}");
                                    _skippedFiles.TryRemove(fileKey, out _);
                                }
                            }
                            else
                            {
                                // First time encountering this file - skip and remember it
                                _skippedFiles[fileKey] = new SkippedFileInfo
                                {
                                    Size = sourceInfo.Length,
                                    LastWriteTime = sourceInfo.LastWriteTime,
                                    SkippedAt = DateTime.Now
                                };

                                _logger.Log(LogLevel.DEBUG, $"Skipping existing file (will track changes): {fileName}");
                                // Don't add skipped files to recent files list
                                transferSuccess = true;
                                break;
                            }
                        }

                        // Step 1: Request permission from tray before transferring file (if required)
                        bool requireApproval = _config.Application.RequireTrayApproval;

                        if (requireApproval)
                        {
                            // Legacy mode: require tray approval before moving files
                            if (_grpcServer != null)
                            {
                                try
                                {
                                    // Request permission from tray via service client
                                    var allowCopy = _serviceClient != null
                                        ? await _serviceClient.RequestFileCopyPermissionAsync(fileName, rule.SourceFolder, rule.DestinationFolder, rule.Name, fileSizeBytes)
                                        : false; // Deny if service client not available in approval mode

                                    if (!allowCopy)
                                    {
                                        _logger.Log(LogLevel.DEBUG, $"Transfer denied by tray: {fileName}");
                                        _recentActivityService.MarkTransferFailed(activityEntry, "Transfer denied by tray", attempt);
                                        break; // Don't retry if denied
                                    }

                                    _logger.Log(LogLevel.DEBUG, $"Transfer approved by tray: {fileName}");
                                }
                                catch (Exception permEx)
                                {
                                    _logger.Log(LogLevel.WARN, $"Failed to get permission from tray, denying transfer: {permEx.Message}");
                                    _recentActivityService.MarkTransferFailed(activityEntry, $"No tray permission: {permEx.Message}", attempt);
                                    break; // If tray doesn't respond, don't proceed
                                }
                            }
                            else
                            {
                                // No tray connection - deny transfer in approval mode
                                _logger.Log(LogLevel.WARN, $"No tray connection, denying transfer: {fileName}");
                                _recentActivityService.MarkTransferFailed(activityEntry, "No tray connection", attempt);
                                break;
                            }
                        }
                        else
                        {
                            // Autonomous mode: service operates independently without tray approval
                            _logger.Log(LogLevel.TRACE, $"Autonomous mode - proceeding without tray approval: {fileName}");
                        }

                        // Step 2: Check for config changes before EVERY move operation
                        if (_configValidator?.HasConfigChanged() == true)
                        {
                            if (!_configErrorMode)
                            {
                                _configErrorMode = true;
                                _logger.Log(LogLevel.INFO, $"Transfer of '{fileName}' aborted - configuration file changed externally");
                                _logger.Log(LogLevel.INFO, "Service entering configuration error mode - all file processing stopped");
                                _logger.Log(LogLevel.INFO, "Service will not process any files until restarted");

                                // Stop all timers immediately
                                StopAllTimers();

                                // Re-validate config to get detailed error message
                                var detailedError = await GetConfigChangeErrorMessageAsync();
                                OnStatusUpdated(detailedError);
                                OnIconUpdated(IconNames.Error);
                            }

                            // Mark this specific transfer as failed due to config change
                            _recentActivityService.MarkTransferFailed(activityEntry, "Transfer aborted - configuration changed", attempt);

                            // Break out of retry loop for this file
                            break;
                        }

                        // Step 3: Check if database record was created (already done in StartFileTransfer)
                        if (_activityHistoryService != null && _config.Application.ActivityHistoryEnabled)
                        {
                            if (!activityEntry.DatabaseRecordId.HasValue)
                            {
                                // CRITICAL: Cannot proceed without audit trail
                                _logger.Log(LogLevel.FATAL, $"============================================");
                                _logger.Log(LogLevel.FATAL, $"TRANSFER BLOCKED: {fileName}");
                                _logger.Log(LogLevel.FATAL, $"============================================");
                                _logger.Log(LogLevel.FATAL, $"Cannot move file without recording activity");
                                _logger.Log(LogLevel.FATAL, $"This is a safety mechanism to ensure accountability");
                                _logger.Log(LogLevel.FATAL, $"Fix database issues and restart service to resume");
                                _logger.Log(LogLevel.FATAL, $"============================================");

                                _recentActivityService.MarkTransferFailed(activityEntry, "Activity history write failed - transfer blocked for safety", attempt);

                                // Enter config error mode to stop all processing
                                _configErrorMode = true;
                                OnStatusUpdated("Database Error - Processing Stopped");
                                OnIconUpdated(IconNames.Error);
                                StopAllTimers();
                                break;
                            }

                            _logger.Log(LogLevel.DEBUG, $"Activity pre-recorded in database (ID: {activityEntry.DatabaseRecordId.Value})");
                        }

                        // Step 4: Transfer starting (config verified, activity recorded, proceeding)

                        // LOG: Starting file move
                        _logger.Log(LogLevel.INFO, $"Moving: {fileName} ({fileSizeBytes} bytes) [{rule.Name}]");

                        // Handle overwrite policy for File.Move
                        if (File.Exists(destPath))
                        {
                            if (rule.FileExists.ToLower() == "overwrite")
                            {
                                File.Delete(destPath); // Delete existing file first
                            }
                            else
                            {
                                // Skip policy already handled above, this shouldn't happen
                                throw new InvalidOperationException($"Destination file exists and policy is not overwrite: {destPath}");
                            }
                        }

                        // Step 5: Final config check immediately before file move
                        if (_configValidator?.HasConfigChanged() == true)
                        {
                            if (!_configErrorMode)
                            {
                                _configErrorMode = true;
                                _logger.Log(LogLevel.INFO, $"Transfer of '{fileName}' aborted just before move - configuration file changed externally");
                                _logger.Log(LogLevel.INFO, "Service entering configuration error mode - all file processing stopped");

                                // Stop all timers immediately
                                StopAllTimers();

                                // Re-validate config to get detailed error message
                                var detailedError = await GetConfigChangeErrorMessageAsync();
                                OnStatusUpdated(detailedError);
                                OnIconUpdated(IconNames.Error);
                                NotifyConfigError(detailedError);
                            }

                            // Mark this specific transfer as failed due to config change
                            _recentActivityService.MarkTransferFailed(activityEntry, "Transfer aborted - configuration changed just before move", attempt);
                            break;
                        }

                        // Step 6: Perform the actual file move
                        File.Move(sourceFile, destPath);

                        // File operations completed successfully
                        totalSize += fileSizeBytes; // Use pre-stored size
                        fileCount++;
                        processedFiles.Add(fileName);

                        // LOG: File move completed
                        _logger.Log(LogLevel.INFO, $"Completed: {fileName}");

                        // Mark transfer success (this also records to database internally)
                        _recentActivityService.MarkTransferSuccess(activityEntry);

                        // Step 7: Transfer completed successfully (recorded in recent files and database)

                        transferSuccess = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt < maxRetries)
                        {
                            // Wait RetryDelayMs before next attempt
                            if (_config.Application.RetryDelayMs > 0)
                            {
                                await Task.Delay(_config.Application.RetryDelayMs);
                            }
                        }
                        else
                        {
                            // LOG: File move failed (only after all retries)
                            _logger.Log(LogLevel.ERROR, $"Failed: {fileName} - {ex.Message}");

                            // Mark transfer failed (this also records to database internally)
                            _recentActivityService.MarkTransferFailed(activityEntry, ex.Message, attempt);

                            // Failure is recorded in recent files list and database
                        }
                    }
                }

                // Apply pause delay between files if configured
                if (transferSuccess && _config.Application.PauseDelayMs > 0)
                {
                    await Task.Delay(_config.Application.PauseDelayMs);
                }

                // Always remove file from processing dictionary when done
                _processingFiles.TryRemove(sourceFile, out _);
            }

            // Recent activity is now managed by RecentActivityService
            // Individual file operations are tracked there
            OnFileCountUpdated(fileCount);

            _logger.Log(LogLevel.INFO, $"Completed processing '{rule.Name}': {fileCount} files, {totalSize} bytes");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"[{ruleErrorId}] Error in ProcessFileRule for '{rule.Name}': {ex.Message}");
            _logger.Log(LogLevel.ERROR, $"[{ruleErrorId}] Stack: {ex.StackTrace}");

            try
            {
                OnStatusUpdated($"Error in {rule.Name}");
                OnIconUpdated(IconNames.Error);
            }
            catch { }
        }
        finally
        {
            // Remove processing flag to allow next cycle
            _processingRules.TryRemove(rule.Name, out _);

            // Only update status if no other rules are actively processing and we're not in config error mode
            if (_processingRules.IsEmpty && !_configErrorMode)
            {
                OnStatusUpdated("Waiting for files...");
                OnIconUpdated(IconNames.Waiting);
            }
        }
    }

    private void StopAllTimers()
    {
        var stopErrorId = Guid.NewGuid().ToString()[..8];
        _logger.Log(LogLevel.INFO, $"[{stopErrorId}] Stopping all timers due to configuration change");

        try
        {
            var timerCount = _timers.Count;
            foreach (var kvp in _timers.ToList())
            {
                try
                {
                    kvp.Value.Stop();
                    kvp.Value.Dispose();
                    _logger.Log(LogLevel.INFO, $"[{stopErrorId}] Stopped timer for rule '{kvp.Key}' due to config change");
                }
                catch (Exception timerEx)
                {
                    _logger.Log(LogLevel.INFO, $"[{stopErrorId}] Error stopping timer for rule '{kvp.Key}': {timerEx.Message}");
                }
            }
            _timers.Clear();
            _logger.Log(LogLevel.INFO, $"[{stopErrorId}] Stopped {timerCount} timers - service in configuration error mode");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.INFO, $"[{stopErrorId}] Error during StopAllTimers: {ex.Message}");
        }
    }

    public Task StopProcessingAsync()
    {
        var stopErrorId = Guid.NewGuid().ToString()[..8];
        _logger.Log(LogLevel.INFO, $"[{stopErrorId}] StopProcessingAsync initiated");

        try
        {
            var timerCount = _timers.Count;
            foreach (var kvp in _timers.ToList())
            {
                try
                {
                    kvp.Value.Stop();
                    kvp.Value.Dispose();
                    _logger.Log(LogLevel.DEBUG, $"[{stopErrorId}] Stopped timer for rule '{kvp.Key}'");
                }
                catch (Exception timerEx)
                {
                    _logger.Log(LogLevel.WARN, $"[{stopErrorId}] Error stopping timer for rule '{kvp.Key}': {timerEx.Message}");
                }
            }
            _timers.Clear();
            _logger.Log(LogLevel.INFO, $"[{stopErrorId}] Stopped {timerCount} timers successfully");

            // Report skipped file statistics
            var skippedCount = _skippedFiles.Count;
            if (skippedCount > 0)
            {
                _logger.Log(LogLevel.INFO, $"[{stopErrorId}] Session tracked {skippedCount} skipped files for change detection");
                _skippedFiles.Clear();
            }

            // Report zero-byte file statistics
            var zeroByteCount = _zeroByteFiles.Count;
            if (zeroByteCount > 0)
            {
                _logger.Log(LogLevel.INFO, $"[{stopErrorId}] Session tracked {zeroByteCount} zero-byte files for change detection");
                _zeroByteFiles.Clear();
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"[{stopErrorId}] Error during StopProcessingAsync: {ex.Message}");
        }

        // Dispose of activity history service
        _activityHistoryService?.Dispose();

        // Dispose of recent activity service
        _recentActivityService?.Dispose();

        return Task.CompletedTask;
    }

    public void ToggleProcessing(bool pause)
    {
        _processingPaused = pause;

        // Persist the paused state so it survives service restarts
        _runtimeState.IsProcessingPaused = pause;

        _logger.Log(LogLevel.INFO, $"Processing {(pause ? "paused" : "resumed")} - state persisted");

        // Update status and icon when toggling - let the main processing loop handle status updates when resuming
        if (pause)
        {
            OnStatusUpdated("Processing Paused...");
            OnIconUpdated(IconNames.Paused);
        }
        // When resuming, let the main processing loop naturally update the status/icon
        // This prevents rapid-fire gRPC updates that could disrupt the connection
    }

    protected virtual void OnStatusUpdated(string status)
    {
        StatusUpdated?.Invoke(this, new StatusUpdateEventArgs { Status = status });
    }

    protected virtual void OnIconUpdated(string iconName)
    {
        IconUpdated?.Invoke(this, new IconUpdateEventArgs { IconName = iconName });
    }

    protected virtual void OnRecentsUpdated(List<string> recents)
    {
        // Prevent duplicate broadcasts - only send if content has changed
        var currentContent = string.Join("|", recents);
        if (currentContent != _lastRecentsContent)
        {
            _lastRecentsContent = currentContent;
            RecentsUpdated?.Invoke(this, new RecentsUpdateEventArgs { Recents = recents });
        }
    }

    protected virtual void OnFileCountUpdated(int count)
    {
        FileCountUpdated?.Invoke(this, new FileCountUpdateEventArgs { Count = count });
    }

    public void NotifyConfigValid()
    {
        _logger.Log(LogLevel.INFO, "FileProcessor notifying tray: Config is VALID, service ready");

        // Send "ready" status immediately - this will update tray icon from error to waiting
        OnStatusUpdated("Service Ready");
        OnIconUpdated(IconNames.Waiting);

        _logger.Log(LogLevel.INFO, "Tray should now show 'waiting' icon - config is valid and service is ready");
    }

    public void NotifyConfigError(string errorMessage)
    {
        _logger.Log(LogLevel.INFO, $"FileProcessor notifying tray of config error: {errorMessage}");

        // Send detailed error message to tray (will be displayed in Error tab)
        OnStatusUpdated(errorMessage);
        OnIconUpdated(IconNames.Error);
        OnRecentsUpdated(new List<string> { $"CONFIG ERROR: {errorMessage}" });
    }

    /// <summary>
    /// Determines if a file should be processed by checking size and lock status
    /// </summary>
    /// <param name="filePath">Full path to the file</param>
    /// <returns>True if file is processable (>0 bytes and not locked)</returns>
    private bool IsFileProcessable(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);

            // Check zero-byte files with tracking
            if (fileInfo.Length == 0)
            {
                var fileKey = $"{filePath}";

                // Check if file was previously tracked as zero-byte and hasn't changed
                if (_zeroByteFiles.TryGetValue(fileKey, out var zeroByteInfo))
                {
                    bool fileChanged = zeroByteInfo.Size != fileInfo.Length ||
                                     zeroByteInfo.LastWriteTime != fileInfo.LastWriteTime;

                    if (!fileChanged)
                    {
                        // File is still zero-byte and unchanged - skip silently (no debug log spam)
                        return false;
                    }
                    else
                    {
                        // File changed since last check - remove from tracking and reprocess
                        _zeroByteFiles.TryRemove(fileKey, out _);
                        if (fileInfo.Length > 0)
                        {
                            _logger.Log(LogLevel.DEBUG, $"Zero-byte file now has content, processing: {Path.GetFileName(filePath)}");
                            return true; // File now has content, process it
                        }
                    }
                }

                // First time encountering this zero-byte file or it's still zero-byte after change
                _zeroByteFiles[fileKey] = new SkippedFileInfo
                {
                    Size = fileInfo.Length,
                    LastWriteTime = fileInfo.LastWriteTime,
                    SkippedAt = DateTime.Now
                };

                _logger.Log(LogLevel.DEBUG, $"Skipping zero-byte file (will track for changes): {Path.GetFileName(filePath)}");
                return false;
            }

            // Test if file is locked by trying to open it for exclusive access
            try
            {
                using var fileStream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.None);
                // If we can open exclusively, file is not locked
                return true;
            }
            catch (IOException)
            {
                // File is locked by another process
                _logger.Log(LogLevel.DEBUG, $"Skipping locked file: {Path.GetFileName(filePath)}");
                return false;
            }
        }
        catch (Exception ex)
        {
            // If we can't even check the file, skip it
            _logger.Log(LogLevel.DEBUG, $"Skipping file due to access error: {Path.GetFileName(filePath)} - {ex.Message}");
            return false;
        }
    }

    public void ClearRecentActivities()
    {
        _recentActivityService.ClearRecentActivities();
        _logger.Log(LogLevel.INFO, "Recent activities cleared by user request");
    }

    /// <summary>
    /// Checks if a file matches the date filter specified in the rule.
    /// Format: "TYPE:SIGN:MINUTES" (e.g., "LA:+43200" = Last Accessed, older than 43200 minutes)
    /// </summary>
    /// <param name="filePath">Full path to the file</param>
    /// <param name="rule">FileRule with optional DateFilter</param>
    /// <returns>True if file matches date filter (or if no date filter specified)</returns>
    private bool MatchesDateCriteria(string filePath, FileRule rule)
    {
        // If no date filter specified, file matches by default
        if (!rule.HasDateFilter)
        {
            return true;
        }

        try
        {
            var parsed = RJAutoMoverShared.Helpers.DateFilterHelper.Parse(rule.DateFilter);
            if (!parsed.IsValid)
            {
                _logger.Log(LogLevel.WARN, $"Invalid DateFilter for rule '{rule.Name}': {parsed.ErrorMessage}");
                return false;
            }

            var fileInfo = new FileInfo(filePath);
            var now = DateTime.Now;

            // Get the appropriate timestamp based on filter type
            DateTime fileTimestamp = parsed.Type switch
            {
                RJAutoMoverShared.Helpers.DateFilterHelper.FilterType.LastAccessed => fileInfo.LastAccessTime,
                RJAutoMoverShared.Helpers.DateFilterHelper.FilterType.LastModified => fileInfo.LastWriteTime,
                RJAutoMoverShared.Helpers.DateFilterHelper.FilterType.FileCreated => fileInfo.CreationTime,
                _ => now
            };

            var minutesSince = (now - fileTimestamp).TotalMinutes;

            // Check if file matches the filter criteria
            bool matches;
            if (parsed.Direction == RJAutoMoverShared.Helpers.DateFilterHelper.FilterDirection.OlderThan)
            {
                // File must be OLDER than X minutes
                matches = minutesSince >= parsed.Minutes;
            }
            else
            {
                // File must be WITHIN last X minutes
                matches = minutesSince <= parsed.Minutes;
            }

            // Log the result
            var typeStr = parsed.Type switch
            {
                RJAutoMoverShared.Helpers.DateFilterHelper.FilterType.LastAccessed => "accessed",
                RJAutoMoverShared.Helpers.DateFilterHelper.FilterType.LastModified => "modified",
                RJAutoMoverShared.Helpers.DateFilterHelper.FilterType.FileCreated => "created",
                _ => "unknown"
            };

            var directionStr = parsed.Direction == RJAutoMoverShared.Helpers.DateFilterHelper.FilterDirection.OlderThan
                ? $"older than {parsed.Minutes} min"
                : $"within last {parsed.Minutes} min";

            if (matches)
            {
                _logger.Log(LogLevel.TRACE, $"File MATCHES DateFilter: {Path.GetFileName(filePath)} ({typeStr} {minutesSince:F1} min ago, requires {directionStr})");
            }
            else
            {
                _logger.Log(LogLevel.TRACE, $"File does NOT match DateFilter: {Path.GetFileName(filePath)} ({typeStr} {minutesSince:F1} min ago, requires {directionStr})");
            }

            return matches;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.DEBUG, $"Error checking date filter for {Path.GetFileName(filePath)}: {ex.Message}");
            return false; // If we can't check date filter, don't process the file
        }
    }

    /// <summary>
    /// Re-validates the configuration when a change is detected and returns a detailed error message
    /// </summary>
    /// <returns>Detailed error message explaining what's wrong with the configuration</returns>
    private async Task<string> GetConfigChangeErrorMessageAsync()
    {
        try
        {
            if (_configValidator == null)
            {
                return "Configuration file changed externally. Unable to re-validate - ConfigValidator is null.";
            }

            // Re-validate the configuration to get detailed error information
            var validationResult = await _configValidator.ValidateConfigAsync();

            if (validationResult.IsValid)
            {
                // Config is actually valid, just changed
                return "Configuration file changed externally. Service requires restart for changes to take effect.";
            }
            else
            {
                // Config has validation errors - return detailed error message
                var detailedError = validationResult.Errors.Count > 0
                    ? validationResult.GetDetailedErrorMessage()
                    : (!string.IsNullOrEmpty(validationResult.ErrorMessage)
                        ? validationResult.ErrorMessage
                        : "Configuration file changed externally with validation errors.");

                _logger.Log(LogLevel.INFO, $"Config validation errors after change: {detailedError}");
                return detailedError;
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"Configuration file changed externally. Error during re-validation: {ex.Message}";
            _logger.Log(LogLevel.ERROR, errorMsg);
            return errorMsg;
        }
    }
}

public class StatusUpdateEventArgs : EventArgs
{
    public string Status { get; set; } = string.Empty;
}

public class IconUpdateEventArgs : EventArgs
{
    public string IconName { get; set; } = string.Empty;
}

public class RecentsUpdateEventArgs : EventArgs
{
    public List<string> Recents { get; set; } = new();
}

public class FileCountUpdateEventArgs : EventArgs
{
    public int Count { get; set; }
}
