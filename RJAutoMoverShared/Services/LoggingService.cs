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
    /// Cleans up old log files
    /// </summary>
    /// <param name="daysToKeep">Number of days to keep log files (default: 7)</param>
    public void CleanupOldLogs(int daysToKeep = 7)
    {
        try
        {
            if (!Directory.Exists(_logPath))
                return;

            var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
            var logFiles = Directory.GetFiles(_logPath, "*.log");
            var deletedCount = 0;

            foreach (var logFile in logFiles)
            {
                try
                {
                    var fileInfo = new FileInfo(logFile);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
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
                Log(LogLevel.INFO, $"Cleaned up {deletedCount} log files older than {daysToKeep} days");
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.ERROR, $"Log cleanup failed: {ex.Message}");
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
