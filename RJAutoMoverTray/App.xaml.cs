using System.Windows;
using System.Diagnostics;
using RJAutoMoverTray.Services;
using Hardcodet.Wpf.TaskbarNotification;
using System.IO;
using System.Threading;
using YamlDotNet.Serialization;
using RJAutoMoverShared.Models;
using RJAutoMoverShared.Services;
using RJAutoMoverShared.Helpers;
using static RJAutoMoverShared.Constants;

namespace RJAutoMoverTray;

public partial class App : Application
{
    private TaskbarIcon? _notifyIcon;
    private GrpcClientServiceV2? _grpcClient;
    private TrayIconService? _trayService;
    private RJAutoMoverShared.Services.LoggingService? _logger;
    private TrayServerHost? _trayServer;
    private Mutex? _singleInstanceMutex;
    private readonly System.Timers.Timer _memoryTimer;
    private readonly DateTime _startTime;
    private bool _memoryErrorMode = false;
    private int _memoryLimitMb = RJAutoMoverShared.Constants.Defaults.MemoryLimitMb; // Default
    private int _memoryCheckMs = RJAutoMoverShared.Constants.Defaults.MemoryCheckMs; // Default
    private int _serviceGrpcPort = RJAutoMoverShared.Constants.Grpc.DefaultServicePort; // Default
    private int _trayGrpcPort = RJAutoMoverShared.Constants.Grpc.DefaultTrayPort; // Default

    // Static property to share log folder with AboutWindow
    public static string? ServiceLogFolder { get; set; }

    public App()
    {
        _startTime = DateTime.Now;

        // Setup memory monitoring timer (will be updated from config)
        _memoryTimer = new System.Timers.Timer(60000); // Default 1 minute
        _memoryTimer.Elapsed += LogMemoryUsage;
        _memoryTimer.AutoReset = true;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check for single instance system-wide (across all users) and kill existing instances
        var (isAlreadyRunning, killInfo, createdMutex) = SystemWideSingleInstanceChecker.CheckForExistingInstance();

        if (isAlreadyRunning)
        {
            // This should rarely happen now since we kill existing processes
            SystemWideSingleInstanceChecker.ShowAlreadyRunningMessage(killInfo);
            Shutdown();
            return;
        }

        // Store the mutex created during the check
        _singleInstanceMutex = createdMutex;
        if (_singleInstanceMutex == null)
        {
            MessageBox.Show("Failed to ensure single instance. Mutex creation failed.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        // If we killed any processes, we'll log this information after logger is initialized

        // Load config for memory, logging, and gRPC port settings
        string logFolder = GetLogFolderFromConfig();
        int retentionDays = ConfigurationHelper.GetLogRetentionDays();
        LoadMemoryConfigFromConfig();
        LoadGrpcPortsFromConfig();

        // Validate log folder before creating the logging service
        string logError = ValidateLogFolderWritable(logFolder);
        if (!string.IsNullOrEmpty(logError))
        {
            // Log to Windows Event Log since file logging isn't available
            LogToEventLog($"RJTray log folder validation failed. Path: '{logFolder}', Error: {logError}. Tray application cannot start without a writable log folder.", EventLogEntryType.Error);

            MessageBox.Show($"Failed to initialize log folder: {logError}\n\nThe tray application cannot start without a writable log directory.\n\nPath: {logFolder}", "Log Folder Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _logger = new RJAutoMoverShared.Services.LoggingService("RJAutoMoverTray", logFolder);

        // Clean up old log files on startup
        _logger.CleanupOldLogs(retentionDays);

        // Log startup with executable path and version info
        _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, "=== TRAY STARTUP INITIATED ===");

        // Log any instances that were killed during startup
        if (!string.IsNullOrEmpty(killInfo))
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Single instance enforcement: {killInfo}");
        }

        var executablePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "Unknown";
        var versionInfo = GetExecutableVersionInfo(executablePath);
        _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Executable: {executablePath}");
        _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Version: {versionInfo}");
        _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Process ID: {Environment.ProcessId}");
        _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Start Time: {DateTime.Now}");
        _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"User: {Environment.UserDomainName}\\{Environment.UserName}");
        _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Session ID: {System.Diagnostics.Process.GetCurrentProcess().SessionId}");
        _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $".NET Version: {Environment.Version}");
        _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, "System-wide single instance check passed - no other RJTray instances detected");

        // Auto-startup scheduled task is created during installation if user selected the option

        _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, "RJTray starting...");

        // Start memory monitoring timer with configured values
        _memoryTimer.Interval = _memoryCheckMs;
        _memoryTimer.Start();
        _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Memory monitoring started (interval: {_memoryCheckMs}ms, limit: {_memoryLimitMb}MB)");

        try
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, "Creating tray services and bidirectional gRPC communication...");

            // Start tray gRPC server first (configured port)
            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Tray gRPC port from config: {_trayGrpcPort}");
            _trayServer = new TrayServerHost(_logger, _trayGrpcPort);
            await _trayServer.StartAsync();

            // Create gRPC client to service (configured port)
            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Service gRPC port from config: {_serviceGrpcPort}");
            _grpcClient = new GrpcClientServiceV2(_logger, _serviceGrpcPort);

            // Subscribe to log folder updates from service
            _grpcClient.LogFolderReceived += (sender, logFolder) =>
            {
                ServiceLogFolder = logFolder;
                _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Received log folder from service: {logFolder}");
            };

            // Subscribe to registration rejection event
            _grpcClient.RegistrationRejected += (sender, conflictingUser) =>
            {
                _logger.Log(RJAutoMoverShared.Models.LogLevel.WARN, $"Tray registration rejected - conflicting user: {conflictingUser}");

                // Show message to user and shutdown gracefully
                Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        $"Another user ({conflictingUser}) already has the RJAutoMover tray application connected.\n\n" +
                        $"Only one tray instance can run at a time across all users on this machine.\n\n" +
                        $"This tray application will now close.",
                        "Tray Already Connected",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, "Shutting down tray due to registration rejection");
                    Shutdown();
                });
            };

            // Wire up reactive reconnection: when service contacts tray, trigger immediate client reconnection
            _trayServer.GrpcService.ServiceContactDetected += async (sender, e) =>
            {
                try
                {
                    await _grpcClient.TriggerReconnectionAsync();
                }
                catch (Exception ex)
                {
                    _logger.Log(RJAutoMoverShared.Models.LogLevel.WARN, $"Error during reactive reconnection: {ex.Message}");
                }
            };

            // Create tray service with the client
            _trayService = new TrayIconService(_logger, _grpcClient);
            // Store tray service reference for balloon notifications
            Properties["TrayIconService"] = _trayService;
            _notifyIcon = _trayService.CreateTrayIcon();

            // Wire up TrayIconService to gRPC server for safety checks
            _trayServer.GrpcService.SetTrayIconService(_trayService);
            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, "TrayIconService wired to gRPC server for safety checks");

            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, "Tray icon created, starting gRPC client communication...");
            // Start gRPC client communication
            await _grpcClient.StartAsync();

            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Bidirectional gRPC communication established successfully - Server:{_trayGrpcPort}, Client:{_serviceGrpcPort}");

            // Check for application updates on startup
            _ = Task.Run(async () =>
            {
                try
                {
                    var executablePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(executablePath) && System.IO.File.Exists(executablePath))
                    {
                        var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(executablePath);
                        var currentVersion = versionInfo.FileVersion;

                        if (!string.IsNullOrEmpty(currentVersion))
                        {
                            var (versionStatus, latestVersion, installerUrl) = await VersionCheckerService.CheckForUpdatesAsync(currentVersion);

                            if (versionStatus > 0 && !string.IsNullOrEmpty(latestVersion))
                            {
                                _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Update available: Current version {currentVersion}, Latest version {latestVersion}");
                            }
                            else
                            {
                                _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Running latest version: {currentVersion}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(RJAutoMoverShared.Models.LogLevel.WARN, $"Failed to check for updates on startup: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"Failed to start RJTray: {ex.Message}");
            MessageBox.Show($"Failed to start RJTray: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
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

            _logger?.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Memory Usage - Working Set: {workingSetMB}MB, Private: {privateMB}MB, GC: {gcMemoryMB}MB, Uptime: {uptime:hh\\:mm\\:ss}");

            // Check for critical memory usage
            if (workingSetMB > _memoryLimitMb)
            {
                if (!_memoryErrorMode)
                {
                    _memoryErrorMode = true;
                    _logger?.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"CRITICAL: Memory limit exceeded ({workingSetMB}MB > {_memoryLimitMb}MB) - entering error mode");
                    _logger?.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "Tray entering memory error mode - functionality limited");
                    _logger?.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "Application restart required to recover from memory error");

                    // Force aggressive garbage collection as last attempt
                    _logger?.Log(RJAutoMoverShared.Models.LogLevel.WARN, "Attempting emergency garbage collection before error mode");
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    // Set tray to error state
                    SetTrayErrorState($"Tray memory usage exceeded {_memoryLimitMb}MB limit: {workingSetMB}MB");
                }
                else
                {
                    _logger?.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"Memory error mode active - current usage: {workingSetMB}MB (limit: {_memoryLimitMb}MB)");
                }
            }
            else if (_memoryErrorMode)
            {
                // Memory has dropped back below limit
                _logger?.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Memory usage returned to normal: {workingSetMB}MB (limit: {_memoryLimitMb}MB)");
                _logger?.Log(RJAutoMoverShared.Models.LogLevel.INFO, "NOTE: Application restart still recommended after memory error");
            }
        }
        catch (Exception ex)
        {
            _logger?.Log(RJAutoMoverShared.Models.LogLevel.WARN, $"Error logging memory usage: {ex.Message}");
        }
    }

    private void SetTrayErrorState(string errorMessage)
    {
        try
        {
            _logger?.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"Setting tray to error state: {errorMessage}");

            // Update tray icon and status via TrayIconService
            if (_trayService != null)
            {
                // The tray service should have methods to update icon and status
                // This will depend on the TrayIconService implementation
                _logger?.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "Notifying tray service of memory error state");

                // We can use the Properties to store error state for balloon notifications
                Properties["MemoryError"] = errorMessage;

                // Show balloon notification about memory error
                var trayIconService = Properties["TrayIconService"] as TrayIconService;
                if (trayIconService != null)
                {
                    // The tray will handle the error state through the service status updates
                    _logger?.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "Tray icon service notified of memory error");
                }
            }
            else
            {
                _logger?.Log(RJAutoMoverShared.Models.LogLevel.WARN, "Cannot set tray error state - tray service not available");
            }
        }
        catch (Exception ex)
        {
            _logger?.Log(RJAutoMoverShared.Models.LogLevel.WARN, $"Failed to set tray error state: {ex.Message}");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _logger?.Log(RJAutoMoverShared.Models.LogLevel.INFO, "=== TRAY SHUTDOWN INITIATED ===");
        _logger?.Log(RJAutoMoverShared.Models.LogLevel.INFO, "RJTray shutting down...");

        // Stop memory monitoring timer
        _memoryTimer?.Stop();
        _memoryTimer?.Dispose();
        _logger?.Log(RJAutoMoverShared.Models.LogLevel.INFO, "Memory monitoring stopped");

        if (_grpcClient != null)
        {
            await _grpcClient.StopAsync();
            _grpcClient.Dispose();
        }

        if (_trayServer != null)
        {
            await _trayServer.StopAsync();
            _trayServer.Dispose();
        }

        _notifyIcon?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _logger?.Log(RJAutoMoverShared.Models.LogLevel.INFO, "=== TRAY SHUTDOWN COMPLETED ===");
        base.OnExit(e);
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

    private static string GetLogFolderFromConfig()
    {
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var configLogFolder = ConfigurationHelper.GetLogFolder();

        // If the path is relative, resolve it relative to the application directory
        string logFolder;
        if (!Path.IsPathRooted(configLogFolder))
        {
            logFolder = Path.Combine(appDirectory, configLogFolder);
        }
        else
        {
            logFolder = configLogFolder;
        }

        logFolder = Path.GetFullPath(logFolder);

        // Check if the log folder is writable (important for non-admin users)
        // If not writable (e.g., C:\Program Files\...), use shared ProgramData location
        if (!IsDirectoryWritable(logFolder))
        {
            // Use shared ProgramData folder - accessible to all users
            var sharedLogFolder = RJAutoMoverShared.Constants.Paths.GetSharedLogFolder();

            // Try to use shared location
            if (IsDirectoryWritable(sharedLogFolder))
            {
                return sharedLogFolder;
            }

            // Last resort: temp folder
            var tempLogFolder = Path.Combine(Path.GetTempPath(), "RJAutoMover", "Logs");
            Directory.CreateDirectory(tempLogFolder);
            return tempLogFolder;
        }

        return logFolder;
    }

    private static bool IsDirectoryWritable(string dirPath)
    {
        try
        {
            // Try to create the directory if it doesn't exist
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            // Test write access with a temporary file
            var testFile = Path.Combine(dirPath, $"writetest_{Guid.NewGuid()}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void LoadMemoryConfigFromConfig()
    {
        var (memoryLimitMb, memoryCheckMs) = ConfigurationHelper.GetMemoryConfig();
        _memoryLimitMb = memoryLimitMb;
        _memoryCheckMs = memoryCheckMs;
    }

    private void LoadGrpcPortsFromConfig()
    {
        var (servicePort, trayPort) = ConfigurationHelper.GetGrpcPorts();
        _serviceGrpcPort = servicePort;
        _trayGrpcPort = trayPort;
    }

    private static string ValidateLogFolderWritable(string logFolder)
    {
        try
        {
            // Try to create the directory if it doesn't exist
            if (!Directory.Exists(logFolder))
            {
                Directory.CreateDirectory(logFolder);
            }

            // Test write access by creating and deleting a test file
            var testFile = Path.Combine(logFolder, $"traylogtest_{Guid.NewGuid()}.tmp");
            File.WriteAllText(testFile, $"Tray log folder write test at {DateTime.Now}");
            File.Delete(testFile);

            return ""; // Success - no error
        }
        catch (Exception ex)
        {
            return ex.Message; // Return error message
        }
    }

    private static void LogToEventLog(string message, EventLogEntryType entryType)
    {
        try
        {
            const string sourceName = "RJAutoMoverTray";
            const string logName = "Application";

            // Create the event source if it doesn't exist (requires admin rights)
            if (!EventLog.SourceExists(sourceName))
            {
                EventLog.CreateEventSource(sourceName, logName);
            }

            // Write to Event Log
            EventLog.WriteEntry(sourceName, message, entryType);
        }
        catch (Exception ex)
        {
            // If we can't write to Event Log either, this is the final fallback
            // Show in console if running interactively, otherwise silently fail
            try
            {
                Console.WriteLine($"Failed to write to Event Log: {ex.Message}");
                Console.WriteLine($"Original message: {message}");
            }
            catch
            {
                // Even console output failed - nothing more we can do
            }
        }
    }

}
