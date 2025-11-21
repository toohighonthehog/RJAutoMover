using System.IO;
using RJAutoMoverShared.Models;
using Serilog;
using Serilog.Core;

namespace RJAutoMoverShared.Services;

/// <summary>
/// Unified logging service for both Service and Tray applications
/// Now powered by Serilog with automatic log rotation and retention
/// </summary>
public class LoggingService
{
    private readonly string _logPath;
    private readonly string _logFileName;
    private readonly Logger _logger;
    private const long MaxFileSizeBytes = 10_000_000; // 10MB per file

    /// <summary>
    /// Creates a new logging service instance
    /// </summary>
    /// <param name="applicationName">Name of the application (e.g., "RJAutoMoverService", "RJAutoMoverTray")</param>
    /// <param name="logFolder">Optional custom log folder path. If not provided, will try multiple fallback locations</param>
    public LoggingService(string applicationName, string? logFolder = null)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");

        // If logFolder is provided, try it first, otherwise use fallback paths
        var possiblePaths = !string.IsNullOrEmpty(logFolder)
            ? new[] { logFolder }
            : new[]
            {
                // 1. ProgramData shared location (writable by all users)
                Constants.Paths.GetSharedLogFolder(),

                // 2. User's AppData\Local folder (fallback if ProgramData lacks write permission)
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RJAutoMover", "Logs"),

                // 3. User's Documents folder
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RJAutoMover", "Logs"),

                // 4. Temp directory (last resort)
                Path.Combine(Path.GetTempPath(), "RJAutoMover", "Logs")
            };

        Exception? lastException = null;
        Logger? tempLogger = null;

        foreach (var logPath in possiblePaths)
        {
            try
            {
                _logPath = logPath;

                if (!Directory.Exists(_logPath))
                {
                    Directory.CreateDirectory(_logPath);
                }

                // Serilog file naming: {timestamp} {applicationName}.log
                // When rotation occurs, it becomes: {timestamp} {applicationName}_001.log, _002.log, etc.
                _logFileName = Path.Combine(_logPath, $"{timestamp} {applicationName}.log");

                // Configure Serilog with file rotation and retention
                tempLogger = new LoggerConfiguration()
                    .WriteTo.File(
                        path: _logFileName,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} | {Message:lj}{NewLine}{Exception}",
                        fileSizeLimitBytes: MaxFileSizeBytes,
                        rollOnFileSizeLimit: true,
                        retainedFileCountLimit: null, // We'll handle retention manually for more control
                        shared: true, // Allow multiple processes to read the log file while it's being written
                        flushToDiskInterval: TimeSpan.FromSeconds(1)
                    )
                    .CreateLogger();

                // Test write to verify permissions
                tempLogger.Information("Log file initialized for {ApplicationName}", applicationName);

                _logger = tempLogger;
                return; // Success! Exit the constructor
            }
            catch (Exception ex)
            {
                tempLogger?.Dispose();
                lastException = ex;
                continue; // Try next path
            }
        }

        // If all paths failed, create a no-op logger
        _logPath = "";
        _logFileName = "";

        #if DEBUG
        Console.WriteLine($"Warning: All log paths failed. Last error: {lastException?.Message}");
        #endif

        // Create a silent no-op logger
        _logger = new LoggerConfiguration()
            .CreateLogger();
    }

    /// <summary>
    /// Cleans up OLD log files based on retention period (automatic cleanup only).
    ///
    /// This method performs AUTOMATIC cleanup (deletes OLD files only, not ALL files).
    /// For manual cleanup (delete ALL files), see AboutWindow.ClearLogs_Click().
    ///
    /// Called in two scenarios:
    /// 1. On service startup (ServiceWorker.ExecuteAsync) - cleans up logs from previous sessions
    /// 2. Daily at 2:00 AM (ServiceWorker.PerformLogCleanup) - periodic maintenance cleanup
    ///
    /// Uses LastWriteTime (not CreationTime) to determine file age, which is more accurate for
    /// log files that are continuously appended to. Protects current session logs by checking
    /// if files are locked (in use) before deletion.
    ///
    /// Logs summary: "Cleaned up X log files older than Y days (Z in-use files skipped)"
    /// </summary>
    /// <param name="daysToKeep">Number of days to keep log files (from config.Application.LogRetentionDays, default: 7)</param>
    public void CleanupOldLogs(int daysToKeep = 7)
    {
        try
        {
            if (!Directory.Exists(_logPath))
                return;

            var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
            var logFiles = Directory.GetFiles(_logPath, "*.log");
            var deletedCount = 0;
            var skippedCount = 0;

            foreach (var logFile in logFiles)
            {
                try
                {
                    var fileInfo = new FileInfo(logFile);

                    // Use LastWriteTime instead of CreationTime for more accurate age detection
                    // LastWriteTime reflects the most recent write to the file, which is more
                    // accurate for continuously-appended log files
                    if (fileInfo.LastWriteTime < cutoffDate)
                    {
                        // Check if file is currently in use (current session log)
                        // This protects logs from the current service session, even if they're old
                        // (e.g., service has been running for 10+ days)
                        if (IsFileLocked(logFile))
                        {
                            skippedCount++;
                            continue; // Skip currently open log files
                        }

                        File.Delete(logFile);
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    // Log cleanup shouldn't fail the application, just skip problematic files
                    Log(LogLevel.WARN, $"Failed to delete old log file {Path.GetFileName(logFile)}: {ex.Message}");
                }
            }

            if (deletedCount > 0)
            {
                Log(LogLevel.INFO, $"Cleaned up {deletedCount:N0} log files older than {daysToKeep} days{(skippedCount > 0 ? $" ({skippedCount} in-use files skipped)" : "")}");
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.ERROR, $"Log cleanup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if a file is currently locked (in use) by another process.
    ///
    /// This method protects current session log files from deletion during cleanup.
    /// Attempts to open the file with exclusive access (FileShare.None). If this fails
    /// with an IOException, the file is locked and should be skipped.
    ///
    /// Used by CleanupOldLogs() to avoid deleting log files that are actively being written to.
    /// This ensures current session logs are never accidentally deleted, even if their LastWriteTime
    /// is older than the retention period (e.g., service started days ago but still running).
    /// </summary>
    /// <param name="filePath">Full path to the log file to check</param>
    /// <returns>True if file is locked (in use), false if accessible or on non-IO errors</returns>
    private bool IsFileLocked(string filePath)
    {
        try
        {
            // Attempt to open with exclusive access (no sharing)
            using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                return false; // Successfully opened = not locked
            }
        }
        catch (IOException)
        {
            // File is locked (current session log or in use by another process)
            return true;
        }
        catch
        {
            // Other errors (permissions, file not found, etc.) - assume file is accessible
            // This prevents cleanup failures from blocking the entire process
            return false;
        }
    }

    /// <summary>
    /// Logs a message with the specified level
    /// </summary>
    public void Log(LogLevel level, string message)
    {
        try
        {
            // Map custom log levels to Serilog levels and format message with custom prefix
            switch (level)
            {
                case LogLevel.gRPCOut:
                    _logger.Information("[gRPC>] {Message}", message);
                    break;
                case LogLevel.gRPCIn:
                    _logger.Information("[gRPC<] {Message}", message);
                    break;
                case LogLevel.DEBUG:
                    _logger.Debug("[DEBUG] {Message}", message);
                    break;
                case LogLevel.INFO:
                    _logger.Information("[INFO ] {Message}", message);
                    break;
                case LogLevel.WARN:
                    _logger.Warning("[WARN ] {Message}", message);
                    break;
                case LogLevel.ERROR:
                    _logger.Error("[ERROR] {Message}", message);
                    break;
                case LogLevel.FATAL:
                    _logger.Fatal("[FATAL] {Message}", message);
                    break;
                default:
                    _logger.Information("[{Level,-5}] {Message}", level.ToString(), message);
                    break;
            }
        }
        catch (Exception)
        {
            // If logging fails, silently continue - Serilog handles most edge cases
            #if DEBUG
            // In debug mode, we might want to know about logging failures
            // but still don't crash the app
            #endif
        }
    }

    /// <summary>
    /// Flushes any pending log operations
    /// </summary>
    public void Flush()
    {
        // Serilog automatically flushes with flushToDiskInterval (1 second)
        // For immediate flush on critical errors, we rely on Dispose() which closes the logger
        // This method is kept for API compatibility with existing code
    }

    /// <summary>
    /// Disposes the logger and releases resources
    /// </summary>
    public void Dispose()
    {
        try
        {
            _logger?.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }
    }

    /// <summary>
    /// Gets the current log file path
    /// </summary>
    public string GetLogFilePath() => _logFileName;

    /// <summary>
    /// Gets the log directory path
    /// </summary>
    public string GetLogDirectory() => _logPath;
}
