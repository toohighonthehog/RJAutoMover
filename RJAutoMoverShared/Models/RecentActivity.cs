using System;

namespace RJAutoMoverShared.Models;

public class RecentActivityEntry
{
    public DateTime Timestamp { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public ActivityStatus Status { get; set; }
    public string ProgressIndicator { get; set; } = string.Empty;
    public int AttemptCount { get; set; } = 0;
    public string ErrorMessage { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; } = 0;

    // Database tracking fields
    public long? DatabaseRecordId { get; set; } = null; // ID of the record in the Activities table
    public string SourceFolder { get; set; } = string.Empty;
    public string DestinationFolder { get; set; } = string.Empty;

    /// <summary>
    /// Generates the display text for the tray menu
    /// Format: "HH:mm:ss - filename.ext 1.23MB ⠋ [RuleName] {DestinationFolder}"
    /// </summary>
    public string ToDisplayString()
    {
        // Include date and time so tray can properly parse and filter by timestamp
        var timeStr = Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        var sizeStr = FormatFileSize(FileSizeBytes);
        var indicator = GetCurrentIndicator();
        var destFolder = !string.IsNullOrEmpty(DestinationFolder) ? $" {{{DestinationFolder}}}" : "";
        return $"{timeStr} - {FileName} {sizeStr} {indicator} [{RuleName}]{destFolder}|{FileSizeBytes}^{SourceFolder}";
    }

    /// <summary>
    /// Gets the current progress indicator based on status
    /// </summary>
    private string GetCurrentIndicator()
    {
        return Status switch
        {
            ActivityStatus.InProgress => ProgressIndicator,
            ActivityStatus.Success => "✓",
            ActivityStatus.Failed => "✗",
            ActivityStatus.Blacklisted => "⚠",
            _ => "?"
        };
    }

    /// <summary>
    /// Formats file size with appropriate units (KB, MB, GB) with 2 decimal places
    /// </summary>
    private string FormatFileSize(long bytes)
    {
        if (bytes == 0) return "0B";

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        if (unitIndex == 0) // Bytes
        {
            return $"{(int)size}B";
        }
        else
        {
            return $"{size:F2}{units[unitIndex]}";
        }
    }


    /// <summary>
    /// Marks the transfer as starting
    /// </summary>
    public void StartTransfer()
    {
        Status = ActivityStatus.InProgress;
        ProgressIndicator = "⠋"; // Static braille - animation handled by tray
        AttemptCount++;
    }

    /// <summary>
    /// Marks the transfer as successful
    /// </summary>
    public void MarkSuccess()
    {
        Status = ActivityStatus.Success;
        ProgressIndicator = "✓";
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// Marks the transfer as failed
    /// </summary>
    public void MarkFailed(string errorMessage)
    {
        Status = ActivityStatus.Failed;
        ProgressIndicator = "✗";
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Marks the file as blacklisted (too many failures)
    /// </summary>
    public void MarkBlacklisted()
    {
        Status = ActivityStatus.Blacklisted;
        ProgressIndicator = "⚠";
    }
}

public enum ActivityStatus
{
    InProgress,
    Success,
    Failed,
    Blacklisted
}
