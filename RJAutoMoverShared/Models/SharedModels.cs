using YamlDotNet.Serialization;

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
    /// Date filter specification in format: "TYPE:SIGN:MINUTES"
    /// - TYPE: LA (Last Accessed), LM (Last Modified), FC (File Created)
    /// - SIGN: + (older than), - (within last)
    /// - MINUTES: Integer value (1-5256000, representing up to 10 years)
    ///
    /// Examples:
    /// - "LA:+43200" = Files NOT accessed in last 43200 minutes (older files)
    /// - "LA:-1440" = Files accessed within last 1440 minutes (recent files)
    /// - "LM:+10080" = Files NOT modified in last 10080 minutes
    /// - "FC:+43200" = Files created more than 43200 minutes ago
    /// - "" or null = No date filter (process all files)
    /// </summary>
    public string DateFilter { get; set; } = string.Empty;

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
    [YamlIgnore]
    public string FileExistsAction => FileExists.Equals("skip", StringComparison.OrdinalIgnoreCase) ? "Skip" : "Overwrite";

    /// <summary>
    /// Helper property to check if rule has a date filter
    /// </summary>
    [YamlIgnore]
    public bool HasDateFilter => !string.IsNullOrWhiteSpace(DateFilter);

    /// <summary>
    /// Helper property to describe the date filter in human-readable format
    /// </summary>
    [YamlIgnore]
    public string DateFilterDescription => Helpers.DateFilterHelper.GetDescription(DateFilter);

    /// <summary>
    /// Alias for DateFilterDescription (for backward compatibility with Tray UI)
    /// </summary>
    [YamlIgnore]
    public string DateCriteria => DateFilterDescription;

    /// <summary>
    /// Helper property to display scan interval in human-readable format
    /// </summary>
    [YamlIgnore]
    public string ScanIntervalDisplay => FormatMinutesToReadable(ScanIntervalMs / 60000);

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
