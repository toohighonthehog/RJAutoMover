namespace RJAutoMoverShared.Models;

public class FileRule
{
    public string SourceFolder { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string DestinationFolder { get; set; } = string.Empty;
    public int ScanIntervalMs { get; set; } = Constants.Defaults.ScanIntervalMs;
    public bool IsActive { get; set; } = false;
    public string Name { get; set; } = string.Empty;
    public string FileExists { get; set; } = "skip";

    /// <summary>
    /// Optional: Only move files that were last accessed within the specified number of minutes.
    /// Must be positive. Maximum: 5256000 minutes (10 years). Mutually exclusive with LastModifiedMins and AgeCreatedMins.
    /// </summary>
    public int? LastAccessedMins { get; set; } = null;

    /// <summary>
    /// Optional: Only move files that were last modified within the specified number of minutes.
    /// Must be positive. Maximum: 5256000 minutes (10 years). Mutually exclusive with LastAccessedMins and AgeCreatedMins.
    /// </summary>
    public int? LastModifiedMins { get; set; } = null;

    /// <summary>
    /// Optional: Only move files that are older than the specified number of minutes (created at least X minutes ago).
    /// Must be positive. Maximum: 5256000 minutes (10 years). Mutually exclusive with LastAccessedMins and LastModifiedMins.
    /// </summary>
    public int? AgeCreatedMins { get; set; } = null;

    public List<string> GetExtensions()
    {
        return Extension.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().ToLower())
            .ToList();
    }

    /// <summary>
    /// Checks if this rule is an OTHERS extension rule (matches all file types)
    /// </summary>
    public bool IsAllExtensionRule()
    {
        return Extension.Trim().Equals("OTHERS", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Constant for OTHERS extension keyword
    /// </summary>
    public const string AllExtensionKeyword = "OTHERS";

    /// <summary>
    /// Helper property for displaying file exists action in UI
    /// </summary>
    public string FileExistsAction => FileExists.Equals("skip", StringComparison.OrdinalIgnoreCase) ? "Skip" : "Overwrite";

    /// <summary>
    /// Helper property to check if rule has a date filter
    /// </summary>
    public bool HasDateFilter => LastAccessedMins.HasValue || LastModifiedMins.HasValue || AgeCreatedMins.HasValue;

    /// <summary>
    /// Helper property to describe the date filter in human-readable format
    /// </summary>
    public string DateFilterDescription
    {
        get
        {
            if (LastAccessedMins.HasValue)
            {
                if (LastAccessedMins.Value > 0)
                    return $"Files NOT accessed in the last {FormatMinutesToReadable(LastAccessedMins.Value)} (older files)";
                else
                    return $"Files accessed within the last {FormatMinutesToReadable(Math.Abs(LastAccessedMins.Value))}";
            }

            if (LastModifiedMins.HasValue)
            {
                if (LastModifiedMins.Value > 0)
                    return $"Files NOT modified in the last {FormatMinutesToReadable(LastModifiedMins.Value)} (older files)";
                else
                    return $"Files modified within the last {FormatMinutesToReadable(Math.Abs(LastModifiedMins.Value))}";
            }

            if (AgeCreatedMins.HasValue)
            {
                if (AgeCreatedMins.Value > 0)
                    return $"Files created more than {FormatMinutesToReadable(AgeCreatedMins.Value)} ago (older files)";
                else
                    return $"Files created within the last {FormatMinutesToReadable(Math.Abs(AgeCreatedMins.Value))}";
            }

            return "None";
        }
    }

    /// <summary>
    /// Formats minutes into a human-readable string with meaningful units
    /// </summary>
    private string FormatMinutesToReadable(int minutes)
    {
        // Convert to the most meaningful unit
        if (minutes < 60)
            return $"{minutes} minute{(minutes != 1 ? "s" : "")}";

        if (minutes < 1440) // Less than 1 day
        {
            var hours = minutes / 60;
            var remainingMins = minutes % 60;
            if (remainingMins == 0)
                return $"{hours} hour{(hours != 1 ? "s" : "")}";
            return $"{hours}h {remainingMins}m";
        }

        if (minutes < 10080) // Less than 1 week (7 days)
        {
            var days = minutes / 1440;
            var remainingHours = (minutes % 1440) / 60;
            if (remainingHours == 0)
                return $"{days} day{(days != 1 ? "s" : "")}";
            return $"{days} day{(days != 1 ? "s" : "")}, {remainingHours} hour{(remainingHours != 1 ? "s" : "")}";
        }

        if (minutes < 43200) // Less than 30 days
        {
            var weeks = minutes / 10080;
            var remainingDays = (minutes % 10080) / 1440;
            if (remainingDays == 0)
                return $"{weeks} week{(weeks != 1 ? "s" : "")}";
            return $"{weeks} week{(weeks != 1 ? "s" : "")}, {remainingDays} day{(remainingDays != 1 ? "s" : "")}";
        }

        if (minutes < 525600) // Less than 1 year (365 days)
        {
            var months = minutes / 43200; // Approximate 30 days per month
            var remainingDays = (minutes % 43200) / 1440;
            if (remainingDays == 0)
                return $"{months} month{(months != 1 ? "s" : "")}";
            return $"{months} month{(months != 1 ? "s" : "")}, {remainingDays} day{(remainingDays != 1 ? "s" : "")}";
        }

        // Years
        var years = minutes / 525600;
        var remainingMonths = (minutes % 525600) / 43200;
        if (remainingMonths == 0)
            return $"{years} year{(years != 1 ? "s" : "")}";
        return $"{years} year{(years != 1 ? "s" : "")}, {remainingMonths} month{(remainingMonths != 1 ? "s" : "")}";
    }
}

public class ApplicationConfig
{
    public bool ProcessingPaused { get; set; } = false;
    public int RetryDelayMs { get; set; } = Constants.Timeouts.DefaultRetryDelayMs;
    public int FailureCooldownMs { get; set; } = Constants.Defaults.FailureCooldownMs;
    public int RecheckServiceMs { get; set; } = Constants.Defaults.RecheckServiceMs;
    public int RecheckTrayMs { get; set; } = Constants.Defaults.RecheckTrayMs;
    public int PauseDelayMs { get; set; } = 0;
    public int ServiceHeartbeatMs { get; set; } = Constants.Defaults.ServiceHeartbeatMs;
    public int MemoryLimitMb { get; set; } = Constants.Defaults.MemoryLimitMb;
    public int MemoryCheckMs { get; set; } = Constants.Defaults.MemoryCheckMs;
    public string LogFolder { get; set; } = string.Empty;
    public int LogRetentionDays { get; set; } = 7; // Number of days to retain log files
    public int ServiceGrpcPort { get; set; } = Constants.Grpc.DefaultServicePort;
    public int TrayGrpcPort { get; set; } = Constants.Grpc.DefaultTrayPort;

    // New: Activity history settings
    public bool RequireTrayApproval { get; set; } = false; // Service operates autonomously by default
    public bool ActivityHistoryEnabled { get; set; } = true;
    public int ActivityHistoryMaxRecords { get; set; } = 5000;
    public int ActivityHistoryRetentionDays { get; set; } = 90;
}

public class Configuration
{
    public List<FileRule> FileRules { get; set; } = new();
    public ApplicationConfig Application { get; set; } = new();
}

public enum LogLevel
{
    FATAL,
    ERROR,
    WARN,
    INFO,
    DEBUG,
    TRACE,
    gRPCOut,  // [gRPC>] - outgoing gRPC messages
    gRPCIn    // [gRPC<] - incoming gRPC messages
}

public static class IconNames
{
    public const string Active = "active.ico";
    public const string Base = "base.ico";
    public const string Error = "error.ico";
    public const string Paused = "paused.ico";
    public const string Stopped = "stopped.ico";
    public const string Waiting = "waiting.ico";
}
