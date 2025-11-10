using System.Collections.Concurrent;
using RJAutoMoverShared.Models;
using RJAutoMoverShared.Services;
using LogLevel = RJAutoMoverShared.Models.LogLevel;

namespace RJAutoMoverS.Services;

public class RecentActivityService
{
    private readonly RJAutoMoverShared.Services.LoggingService _logger;
    private readonly List<RecentActivityEntry> _recentActivities = new();
    private readonly HashSet<string> _blacklistedFiles = new();
    private readonly object _lock = new();
    private string _lastBroadcastContent = "";
    private ActivityHistoryService? _activityHistoryService;
    public event EventHandler<RecentsUpdateEventArgs>? RecentsUpdated;

    public RecentActivityService(RJAutoMoverShared.Services.LoggingService logger)
    {
        _logger = logger;
        // Animation is handled entirely by the tray - no timer needed in service
    }

    /// <summary>
    /// Sets the activity history service for persistent storage
    /// </summary>
    public void SetActivityHistoryService(ActivityHistoryService? historyService)
    {
        _activityHistoryService = historyService;
        _logger.Log(LogLevel.INFO, $"Activity history service {(historyService != null ? "enabled" : "disabled")} for RecentActivityService");
    }

    /// <summary>
    /// Clears all recent activity entries (called on service start/restart)
    /// </summary>
    public void ClearRecentActivities()
    {
        lock (_lock)
        {
            _recentActivities.Clear();
            _blacklistedFiles.Clear();
            _lastBroadcastContent = "";

            _logger.Log(LogLevel.INFO, "Recent transfers list cleared on service start");
            NotifyRecentsUpdated();
        }
    }

    /// <summary>
    /// Starts a file transfer and adds it to recent activity with progress indicator
    /// </summary>
    public RecentActivityEntry StartFileTransfer(string fileName, string ruleName, long fileSizeBytes, string sourceFolder, string destinationFolder)
    {
        lock (_lock)
        {
            var entry = new RecentActivityEntry
            {
                Timestamp = DateTime.Now,
                FileName = fileName,
                RuleName = ruleName,
                FileSizeBytes = fileSizeBytes,
                SourceFolder = sourceFolder,
                DestinationFolder = destinationFolder
            };

            entry.StartTransfer();

            // Record InProgress status to database immediately
            if (_activityHistoryService != null)
            {
                var recordId = _activityHistoryService.RecordActivity(
                    fileName,
                    sourceFolder,
                    destinationFolder,
                    ruleName,
                    fileSizeBytes,
                    "InProgress",
                    null,
                    entry.AttemptCount);

                entry.DatabaseRecordId = recordId;

                if (recordId.HasValue)
                {
                    _logger.Log(LogLevel.INFO, $"Activity recorded to database with ID: {recordId.Value} - {fileName}");
                }
                else
                {
                    // Database write failed - this is a critical failure
                    _logger.Log(LogLevel.ERROR, "============================================");
                    _logger.Log(LogLevel.FATAL, "ACTIVITY DATABASE WRITE FAILED");
                    _logger.Log(LogLevel.ERROR, "============================================");
                    _logger.Log(LogLevel.ERROR, $"File: {fileName}");
                    _logger.Log(LogLevel.ERROR, $"Rule: {ruleName}");
                    _logger.Log(LogLevel.ERROR, $"Source: {sourceFolder}");
                    _logger.Log(LogLevel.ERROR, $"Destination: {destinationFolder}");
                    _logger.Log(LogLevel.FATAL, "File transfer BLOCKED - cannot proceed without database accountability");
                    _logger.Log(LogLevel.ERROR, "Check earlier logs for database initialization or write errors");
                    _logger.Log(LogLevel.ERROR, "Database location: C:\\ProgramData\\RJAutoMover\\Data\\ActivityHistory.db");
                    _logger.Log(LogLevel.ERROR, "============================================");

                    // Throw exception to abort the transfer
                    throw new InvalidOperationException(
                        $"Activity history database write failed for file '{fileName}'. " +
                        "File transfers are blocked when activity cannot be recorded to ensure accountability. " +
                        "Check service logs for database errors and ensure the database is writable at C:\\ProgramData\\RJAutoMover\\Data\\ActivityHistory.db");
                }
            }

            _recentActivities.Insert(0, entry);

            // Limit to 1000 entries
            while (_recentActivities.Count > 1000)
            {
                _recentActivities.RemoveAt(_recentActivities.Count - 1);
            }

            _logger.Log(LogLevel.INFO, $"Started transfer: {fileName} [{ruleName}]");
            NotifyRecentsUpdated();

            return entry;
        }
    }

    /// <summary>
    /// Marks a file transfer as successful
    /// </summary>
    public void MarkTransferSuccess(RecentActivityEntry entry)
    {
        lock (_lock)
        {
            entry.MarkSuccess();
            _logger.Log(LogLevel.INFO, $"Transfer successful: {entry.FileName}");

            // Update database record from InProgress to Success
            if (_activityHistoryService != null && entry.DatabaseRecordId.HasValue)
            {
                var updated = _activityHistoryService.UpdateActivity(
                    entry.DatabaseRecordId.Value,
                    "Success",
                    null,
                    entry.AttemptCount);

                if (updated)
                {
                    _logger.Log(LogLevel.INFO, $"Database record {entry.DatabaseRecordId.Value} updated to Success - {entry.FileName}");
                }
                else
                {
                    _logger.Log(LogLevel.WARN, $"Failed to update database record {entry.DatabaseRecordId.Value} to Success for {entry.FileName}");
                }
            }

            NotifyRecentsUpdated();
        }
    }

    /// <summary>
    /// Marks a file transfer as failed and manages retry logic
    /// </summary>
    public void MarkTransferFailed(RecentActivityEntry entry, string errorMessage, int attemptNumber)
    {
        lock (_lock)
        {
            entry.AttemptCount = attemptNumber;
            entry.MarkFailed(errorMessage);

            // Check if file should be blacklisted (5 failed attempts)
            if (entry.AttemptCount >= 5)
            {
                var filePath = GetFullFilePath(entry);
                _blacklistedFiles.Add(filePath);
                entry.MarkBlacklisted();
                _logger.Log(LogLevel.WARN, $"File blacklisted after {entry.AttemptCount} failed attempts: {entry.FileName}");
            }

            if (entry.AttemptCount >= 5)
            {
                _logger.Log(LogLevel.ERROR, $"Transfer failed permanently: {entry.FileName} - {errorMessage}");

                // Update database record to Failed (permanent failure after 5 attempts)
                if (_activityHistoryService != null && entry.DatabaseRecordId.HasValue)
                {
                    var updated = _activityHistoryService.UpdateActivity(
                        entry.DatabaseRecordId.Value,
                        "Failed",
                        errorMessage,
                        entry.AttemptCount);

                    if (updated)
                    {
                        _logger.Log(LogLevel.INFO, $"Database record {entry.DatabaseRecordId.Value} updated to Failed - {entry.FileName}");
                    }
                    else
                    {
                        _logger.Log(LogLevel.WARN, $"Failed to update database record {entry.DatabaseRecordId.Value} to Failed for {entry.FileName}");
                    }
                }
            }
            else
            {
                _logger.Log(LogLevel.WARN, $"Transfer failed (attempt {entry.AttemptCount}): {entry.FileName} - {errorMessage}");
                // Keep InProgress status in database for retry attempts
            }
            NotifyRecentsUpdated();
        }
    }

    /// <summary>
    /// Checks if a file is blacklisted
    /// </summary>
    public bool IsFileBlacklisted(string sourceFolder, string fileName)
    {
        var filePath = Path.Combine(sourceFolder, fileName);
        lock (_lock)
        {
            return _blacklistedFiles.Contains(filePath);
        }
    }

    /// <summary>
    /// Gets the current recent activities for display
    /// Processing transfers are shown at the top, followed by completed transfers
    /// </summary>
    public List<string> GetRecentActivitiesDisplay()
    {
        lock (_lock)
        {
            return _recentActivities
                .OrderByDescending(a => a.Status == ActivityStatus.InProgress ? 1 : 0) // In-progress first
                .ThenByDescending(a => a.Timestamp.Ticks) // Then by newest first (using Ticks for full precision)
                .Select(a => a.ToDisplayString())
                .ToList();
        }
    }

    /// <summary>
    /// Clears the blacklist (called on service restart)
    /// </summary>
    public void ClearBlacklist()
    {
        lock (_lock)
        {
            _blacklistedFiles.Clear();
            _logger.Log(LogLevel.INFO, "File blacklist cleared");
        }
    }


    /// <summary>
    /// Gets the full file path for blacklist tracking
    /// </summary>
    private string GetFullFilePath(RecentActivityEntry entry)
    {
        // We need to store the source folder in the entry to construct full path
        // For now, we'll use just the filename - this could be enhanced later
        return entry.FileName;
    }

    /// <summary>
    /// Notifies subscribers of recent activity updates
    /// </summary>
    private void NotifyRecentsUpdated()
    {
        var displayList = GetRecentActivitiesDisplay();

        // Create a content hash to detect changes
        var currentContent = string.Join("|", displayList);

        // Only broadcast if content has actually changed
        if (currentContent != _lastBroadcastContent)
        {
            _lastBroadcastContent = currentContent;

            if (displayList.Count > 0)
            {
                var latestActivity = displayList.FirstOrDefault();
                _logger.Log(LogLevel.gRPCOut, $"Sending recent activity update: {latestActivity} (total: {displayList.Count} items)");
            }
            RecentsUpdated?.Invoke(this, new RecentsUpdateEventArgs { Recents = displayList });
        }
    }

    /// <summary>
    /// Disposes resources
    /// </summary>
    public void Dispose()
    {
        // No resources to dispose - animation handled by tray
    }
}
