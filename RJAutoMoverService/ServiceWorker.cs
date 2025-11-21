using Microsoft.Extensions.Hosting;
using RJAutoMoverS.Config;
using RJAutoMoverS.Services;
using RJAutoMoverShared.Models;
using RJAutoMoverShared.Services;
using LogLevel = RJAutoMoverShared.Models.LogLevel;
using ValidationResult = RJAutoMoverS.Config.ValidationResult;

namespace RJAutoMoverS;

public class ServiceWorker : BackgroundService
{
    private readonly RJAutoMoverShared.Services.LoggingService _logger;
    private readonly ConfigValidator _configValidator;
    private readonly FileProcessorService _fileProcessor;
    private readonly GrpcServerServiceSimplified _grpcServer;
    private readonly ServiceGrpcClientServiceV2 _serviceClient;
    private Configuration? _config;
    private readonly DateTime _startTime;
    private readonly System.Timers.Timer _memoryTimer;
    private readonly System.Timers.Timer _logCleanupTimer;
    private bool _memoryErrorMode = false;

    public ServiceWorker(
        LoggingService logger,
        ConfigValidator configValidator,
        FileProcessorService fileProcessor,
        GrpcServerServiceSimplified grpcServer,
        ServiceGrpcClientServiceV2 serviceClient)
    {
        _logger = logger;
        _configValidator = configValidator;
        _fileProcessor = fileProcessor;
        _grpcServer = grpcServer;
        _serviceClient = serviceClient;
        _startTime = DateTime.Now;

        // Setup memory monitoring timer (initial interval - will be updated from config)
        _memoryTimer = new System.Timers.Timer(60000); // Default 1 minute
        _memoryTimer.Elapsed += LogMemoryUsage;
        _memoryTimer.AutoReset = true;

        // Setup log cleanup timer (runs daily at 2:00 AM)
        // This provides automatic cleanup of old log files in addition to the startup cleanup
        // AutoReset = false ensures we recalculate the interval after each execution (always targets 2:00 AM next day)
        _logCleanupTimer = new System.Timers.Timer(CalculateTimeUntil2AM());
        _logCleanupTimer.Elapsed += PerformLogCleanup;
        _logCleanupTimer.AutoReset = false; // Recalculate interval after each run to maintain 2:00 AM schedule
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Use the existing injected logger to avoid creating multiple log files
        try
        {
            _logger.Log(LogLevel.INFO, "=== SERVICE STARTUP INITIATED ===");

            // Log executable path and version
            var executablePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "Unknown";
            var versionInfo = GetExecutableVersionInfo(executablePath);
            _logger.Log(LogLevel.INFO, $"Executable: {executablePath}");
            _logger.Log(LogLevel.INFO, $"Version: {versionInfo}");

            _logger.Log(LogLevel.INFO, $"Process ID: {Environment.ProcessId}");
            _logger.Log(LogLevel.INFO, $"Start Time: {_startTime}");
            _logger.Log(LogLevel.INFO, $".NET Version: {Environment.Version}");

            // Check for application updates on startup (non-blocking)
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(executablePath) && System.IO.File.Exists(executablePath))
                    {
                        var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(executablePath);
                        var currentVersion = fileVersionInfo.FileVersion;

                        if (!string.IsNullOrEmpty(currentVersion))
                        {
                            var (versionStatus, latestVersion, installerUrl) = await VersionCheckerService.CheckForUpdatesAsync(currentVersion);

                            if (versionStatus > 0 && !string.IsNullOrEmpty(latestVersion))
                            {
                                _logger.Log(LogLevel.INFO, $"Update available: Current version {currentVersion}, Latest version {latestVersion}");
                            }
                            else
                            {
                                _logger.Log(LogLevel.INFO, $"Running latest version: {currentVersion}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.WARN, $"Failed to check for updates on startup: {ex.Message}");
                }
            });

            // Clean up old log files based on config retention setting
            int retentionDays = _config?.Application.LogRetentionDays ?? 7;
            _logger.CleanupOldLogs(retentionDays);

            // Force flush logs immediately
            await Task.Delay(100);

            _logger.Log(LogLevel.INFO, "=== SERVICE STARTUP COMPLETED ===");
        }
        catch (Exception ex)
        {
            // If even basic logging fails, write to Event Log
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    System.Diagnostics.EventLog.WriteEntry("RJAutoMoverService",
                        $"Critical: Service startup logging failed: {ex.Message}",
                        System.Diagnostics.EventLogEntryType.Error);
                }
            }
            catch { /* Event log write failed - no other logging options available */ }
        }

        // Start event-driven health monitoring (no polling)
        _logger?.Log(LogLevel.INFO, "Event-driven health monitoring enabled - no scheduled polling");

        // Service starts immediately and runs forever
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Try to initialize components in background
                await InitializeServiceAsync();

                // Run main service loop
                await RunServiceLoopAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                var errorId = Guid.NewGuid().ToString()[..8];
                try
                {
                    _logger?.Log(LogLevel.ERROR, $"[{errorId}] Service error: {ex.Message}");
                    _logger?.Log(LogLevel.ERROR, $"[{errorId}] Stack: {ex.StackTrace}");
                    _logger?.Log(LogLevel.ERROR, $"[{errorId}] Attempting recovery in 10 seconds...");
                }
                catch
                {
                    // Write to Event Log as fallback
                    try
                    {
                        if (OperatingSystem.IsWindows())
                        {
                            System.Diagnostics.EventLog.WriteEntry("RJAutoMoverService",
                                $"Error {errorId}: {ex.Message}\nRecovering...",
                                System.Diagnostics.EventLogEntryType.Warning);
                        }
                    }
                    catch { /* Event log write failed - nothing more we can do */ }
                }

                // Wait and retry - service never stops
                await Task.Delay(10000, stoppingToken);
            }
        }
    }

    private async Task InitializeServiceAsync()
    {
        try
        {
            // Try to initialize gRPC server first (always do this)
            _grpcServer?.SetServiceStartTime(_startTime);
            _grpcServer?.Initialize(_fileProcessor);

            // Wire up reactive reconnection: when tray contacts service, trigger immediate client reconnection
            if (_grpcServer != null && _serviceClient != null)
            {
                _grpcServer.TrayContactDetected += async (sender, e) =>
                {
                    try
                    {
                        await _serviceClient.TriggerReconnectionAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger?.Log(LogLevel.WARN, $"Error during reactive reconnection to tray: {ex.Message}");
                    }
                };
            }

            // Start service gRPC client to tray BEFORE config validation (bidirectional communication)
            _logger?.Log(LogLevel.INFO, "Initializing service gRPC client to tray...");
            var clientStarted = await _serviceClient?.StartAsync();
            if (clientStarted == true)
            {
                _logger?.Log(LogLevel.INFO, "Bidirectional gRPC communication established - Server:60051, Client:60052");
            }
            else
            {
                _logger?.Log(LogLevel.WARN, "Service gRPC client failed to connect to tray - will retry later");
            }

            // Now try to validate config
            ValidationResult validationResult;
            try
            {
                validationResult = _configValidator != null
                    ? await _configValidator.ValidateConfigAsync()
                    : new ValidationResult { IsValid = false, ErrorMessage = "ConfigValidator is null" };
            }
            catch (Exception configEx)
            {
                var configErrorMsg = $"Exception during config validation: {configEx.Message}";
                _logger?.Log(LogLevel.INFO, configErrorMsg);
                validationResult = new ValidationResult { IsValid = false, ErrorMessage = configErrorMsg };
            }

            if (validationResult.IsValid && validationResult.Configuration != null)
            {
                _config = validationResult.Configuration;
                _fileProcessor?.Initialize(_config, _configValidator, _grpcServer, _serviceClient);

                _logger?.Log(LogLevel.INFO, "Service initialized successfully");

                // CRITICAL: Immediately notify tray that config is VALID before starting any file processing
                _logger?.Log(LogLevel.INFO, "IMMEDIATELY notifying tray: Config valid, ready to process files");
                NotifyTrayOfValidConfig();

                // Update memory monitoring timer with config values
                _memoryTimer.Interval = _config.Application.MemoryCheckMs;
                _memoryTimer.Start();

                // Start log cleanup timer (runs daily at 2 AM)
                _logCleanupTimer.Start();
                _logger?.Log(LogLevel.INFO, $"Log cleanup scheduled daily at 2:00 AM (retention: {_config.Application.LogRetentionDays} days)");
                var intervalSec = _config.Application.MemoryCheckMs / 1000.0;
                _logger?.Log(LogLevel.INFO, $"Memory monitoring started (interval: {intervalSec:F0} seconds, limit: {_config.Application.MemoryLimitMb:N0} MB)");

                // Send initial health check to tray
                _ = Task.Run(async () => await SendHealthCheckToTrayAsync());
            }
            else
            {
                // Use detailed error message if available, otherwise fallback to standard message
                var errorMessage = validationResult.Errors.Count > 0
                    ? validationResult.GetDetailedErrorMessage()
                    : (!string.IsNullOrEmpty(validationResult.ErrorMessage)
                        ? validationResult.ErrorMessage
                        : "Unknown configuration error");

                _logger?.Log(LogLevel.INFO, $"CONFIGURATION ERROR: {errorMessage}");
                _logger?.Log(LogLevel.INFO, "=== ACTION REQUIRED ===");
                _logger?.Log(LogLevel.INFO, "Service will continue running but file processing is DISABLED until the configuration is corrected.");
                _logger?.Log(LogLevel.INFO, "======================");

                // Even with config errors, try to initialize ActivityHistory to show historical transfers
                // This allows users to see past activity even when config validation fails
                if (validationResult.Configuration != null)
                {
                    _logger?.Log(LogLevel.INFO, "Attempting to initialize ActivityHistory despite config validation failure...");
                    _fileProcessor?.InitializeActivityHistoryOnly(validationResult.Configuration);
                }

                // CRITICAL: Immediately notify tray about config error via gRPC (send detailed message)
                NotifyTrayOfConfigError(errorMessage);
            }
        }
        catch (Exception ex)
        {
            try
            {
                _logger?.Log(LogLevel.ERROR, $"Initialization error: {ex.Message}");
            }
            catch
            {
                // Initialization errors already logged above - safe to continue
            }
        }
    }

    private async Task RunServiceLoopAsync(CancellationToken stoppingToken)
    {
        if (_config != null && _fileProcessor != null)
        {
            // Try to run file processing
            await _fileProcessor.StartProcessingAsync(stoppingToken);
        }
        else
        {
            // No valid config - just wait
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private void NotifyTrayOfValidConfig()
    {
        try
        {
            if (_fileProcessor != null)
            {
                _logger?.Log(LogLevel.INFO, "Notifying tray that configuration is VALID and service is ready...");

                // Use the file processor to send valid config notification
                _fileProcessor.NotifyConfigValid();

                _logger?.Log(LogLevel.INFO, "Tray notified: Config valid, service ready");
            }
            else
            {
                _logger?.Log(LogLevel.WARN, "Cannot notify tray of valid config - file processor not available");
            }
        }
        catch (Exception ex)
        {
            _logger?.Log(LogLevel.WARN, $"Failed to notify tray of valid config: {ex.Message}");
        }
    }

    private void NotifyTrayOfConfigError(string errorMessage)
    {
        try
        {
            if (_fileProcessor != null)
            {
                // Notify tray that service has config error through file processor events
                _logger?.Log(LogLevel.INFO, "Notifying tray of configuration error...");

                // Use the file processor's method to trigger tray notifications
                _fileProcessor.NotifyConfigError(errorMessage);

                _logger?.Log(LogLevel.INFO, "Tray notified of configuration error via gRPC streaming");
            }
            else
            {
                _logger?.Log(LogLevel.WARN, "Cannot notify tray of config error - file processor not available");
            }
        }
        catch (Exception ex)
        {
            _logger?.Log(LogLevel.WARN, $"Failed to notify tray of config error: {ex.Message}");
        }
    }

    private async Task SendHealthCheckToTrayAsync()
    {
        try
        {
            var uptime = DateTime.Now - _startTime;
            var memoryUsage = GC.GetTotalMemory(false) / 1024 / 1024;

            // Send health check to tray via bidirectional gRPC
            bool trayHealthy = await _serviceClient.PerformHealthCheckAsync(
                (long)uptime.TotalMilliseconds,
                memoryUsage);

            if (trayHealthy)
            {
                _logger?.Log(LogLevel.DEBUG, $"Health check successful - Service uptime: {uptime:hh\\:mm\\:ss}, Memory: {memoryUsage:N0} MB");
            }
            else
            {
                _logger?.Log(LogLevel.WARN, "Tray health check failed - tray may be disconnected");
            }

            // Check for config changes (still needed)
            try
            {
                bool configChanged = _configValidator?.HasConfigChanged() ?? false;
                if (configChanged)
                {
                    _logger?.Log(LogLevel.ERROR, "Config file changed - restart required");
                }
            }
            catch (Exception configEx)
            {
                _logger?.Log(LogLevel.WARN, $"Error during config change check: {configEx.Message}");
            }

            // Simple memory monitoring - force GC if over 1GB
            if (memoryUsage > 1000)
            {
                _logger?.Log(LogLevel.WARN, $"High memory usage: {memoryUsage:N0} MB - running GC");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
        catch (Exception ex)
        {
            _logger?.Log(LogLevel.WARN, $"Health check error: {ex.Message}");
        }
    }

    private void LogMemoryUsage(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var workingSetMB = process.WorkingSet64 / 1024 / 1024;
            var privateMB = process.PrivateMemorySize64 / 1024 / 1024;
            var gcMemoryMB = GC.GetTotalMemory(false) / 1024 / 1024;
            var uptime = DateTime.Now - _startTime;

            _logger?.Log(LogLevel.INFO, $"Memory Usage - Working Set: {workingSetMB:N0} MB, Private: {privateMB:N0} MB, GC: {gcMemoryMB:N0} MB, Uptime: {uptime:hh\\:mm\\:ss}");

            // Check for critical memory usage using configured limit
            var memoryLimitMb = _config?.Application.MemoryLimitMb ?? 512; // Default to 512 if config not loaded

            if (workingSetMB > memoryLimitMb)
            {
                if (!_memoryErrorMode)
                {
                    _memoryErrorMode = true;
                    _logger?.Log(LogLevel.ERROR, $"CRITICAL: Memory limit exceeded ({workingSetMB:N0} MB > {memoryLimitMb:N0} MB) - entering error mode");
                    _logger?.Log(LogLevel.ERROR, "Service entering memory error mode - all processing stopped");
                    _logger?.Log(LogLevel.ERROR, "Service restart required to recover from memory error");

                    // Force aggressive garbage collection as last attempt
                    _logger?.Log(LogLevel.WARN, "Attempting emergency garbage collection before error mode");
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    // Notify tray and file processor of memory error
                    NotifyMemoryError($"Service memory usage exceeded {memoryLimitMb:N0} MB limit: {workingSetMB:N0} MB");
                }
                else
                {
                    _logger?.Log(LogLevel.ERROR, $"Memory error mode active - current usage: {workingSetMB:N0} MB (limit: {memoryLimitMb:N0} MB)");
                }
            }
            else if (_memoryErrorMode)
            {
                // Memory has dropped back below limit
                _logger?.Log(LogLevel.INFO, $"Memory usage returned to normal: {workingSetMB:N0} MB (limit: {memoryLimitMb:N0} MB)");
                _logger?.Log(LogLevel.INFO, "NOTE: Service restart still recommended after memory error");
            }
        }
        catch (Exception ex)
        {
            _logger?.Log(LogLevel.WARN, $"Error logging memory usage: {ex.Message}");
        }
    }

    private void NotifyMemoryError(string errorMessage)
    {
        try
        {
            _logger?.Log(LogLevel.ERROR, $"Notifying tray and file processor of memory error: {errorMessage}");

            // Stop file processing immediately
            if (_fileProcessor != null)
            {
                _fileProcessor.NotifyConfigError($"Memory Error: {errorMessage}");
                _logger?.Log(LogLevel.ERROR, "File processor notified of memory error");
            }
            else
            {
                _logger?.Log(LogLevel.WARN, "Cannot notify file processor of memory error - processor not available");
            }
        }
        catch (Exception ex)
        {
            _logger?.Log(LogLevel.WARN, $"Failed to notify components of memory error: {ex.Message}");
        }
    }

    /// <summary>
    /// Calculates milliseconds until next 2:00 AM for log cleanup scheduling.
    ///
    /// Used to schedule the daily log cleanup timer. If current time is before 2:00 AM today,
    /// returns time until 2:00 AM today. Otherwise, returns time until 2:00 AM tomorrow.
    ///
    /// Example: If called at 1:30 AM, returns ~30 minutes. If called at 3:00 PM, returns ~11 hours.
    /// </summary>
    /// <returns>Milliseconds until next 2:00 AM</returns>
    private double CalculateTimeUntil2AM()
    {
        var now = DateTime.Now;
        var next2AM = DateTime.Today.AddHours(2);

        // If 2 AM has already passed today, schedule for tomorrow
        if (now >= next2AM)
        {
            next2AM = next2AM.AddDays(1);
        }

        return (next2AM - now).TotalMilliseconds;
    }

    /// <summary>
    /// Performs periodic log cleanup (runs daily at 2:00 AM via timer).
    ///
    /// This method:
    /// 1. Cleans up old log files using LogRetentionDays from config
    /// 2. Recalculates the timer interval to target 2:00 AM the next day
    /// 3. Restarts the timer with the new interval
    ///
    /// If cleanup fails, retries in 1 hour instead of waiting until next day.
    /// This complements the startup cleanup (ExecuteAsync) to ensure logs don't accumulate.
    /// </summary>
    private void PerformLogCleanup(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            int retentionDays = _config?.Application.LogRetentionDays ?? 7;
            _logger?.Log(LogLevel.INFO, $"Performing scheduled log cleanup (retention: {retentionDays} days)");

            _logger?.CleanupOldLogs(retentionDays);

            // Recalculate interval for next day at 2 AM
            _logCleanupTimer.Interval = CalculateTimeUntil2AM();
            _logCleanupTimer.Start();
        }
        catch (Exception ex)
        {
            _logger?.Log(LogLevel.ERROR, $"Error during scheduled log cleanup: {ex.Message}");

            // Try again in 1 hour if cleanup fails
            _logCleanupTimer.Interval = TimeSpan.FromHours(1).TotalMilliseconds;
            _logCleanupTimer.Start();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Log(LogLevel.INFO, "=== SERVICE SHUTDOWN INITIATED ===");
        _logger.Log(LogLevel.INFO, "RJService stopping...");

        try
        {
            // Stop memory monitoring timer
            _memoryTimer?.Stop();
            _memoryTimer?.Dispose();
            _logger.Log(LogLevel.INFO, "Memory monitoring stopped");

            // Stop log cleanup timer
            _logCleanupTimer?.Stop();
            _logCleanupTimer?.Dispose();
            _logger.Log(LogLevel.INFO, "Log cleanup timer stopped");

            // Stop file processing immediately
            await _fileProcessor.StopProcessingAsync();
            _logger.Log(LogLevel.INFO, "File processor stopped");

            // Call base implementation with timeout protection
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await base.StopAsync(combinedCts.Token);
            _logger.Log(LogLevel.INFO, "RJService stopped successfully");
            _logger.Log(LogLevel.INFO, "=== SERVICE SHUTDOWN COMPLETED ===");
        }
        catch (OperationCanceledException)
        {
            _logger.Log(LogLevel.WARN, "Service stop operation timed out - forcing shutdown");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Error during service stop: {ex.Message}");
        }
        finally
        {
            // Event-driven monitoring requires no cleanup
        }
    }

    private static string GetExecutableVersionInfo(string executablePath)
    {
        try
        {
            if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
            {
                var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(executablePath);
                var fileVersion = versionInfo.FileVersion ?? "Unknown";
                var productVersion = versionInfo.ProductVersion ?? "Unknown";

                // Also try to get build date
                var buildDate = File.GetLastWriteTime(executablePath).ToString("yyyy-MM-dd HH:mm:ss");

                return $"{fileVersion} (Product: {productVersion}, Built: {buildDate})";
            }

            return "Unknown - executable not found";
        }
        catch (Exception ex)
        {
            return $"Unknown - error reading version: {ex.Message}";
        }
    }
}
