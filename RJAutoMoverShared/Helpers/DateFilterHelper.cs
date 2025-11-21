using System.Text.RegularExpressions;

namespace RJAutoMoverShared.Helpers;

/// <summary>
/// Helper class for parsing and formatting date filter strings
/// Format: "TYPE:SIGN:MINUTES"
/// - TYPE: LA (Last Accessed), LM (Last Modified), FC (File Created)
/// - SIGN: + (older than), - (within last)
/// - MINUTES: Integer value (1-5256000, representing up to 10 years)
/// </summary>
public static class DateFilterHelper
{
    private const int MaxMinutes = 5256000; // 10 years
    private const int MinMinutes = 1;

    public enum FilterType
    {
        None,
        LastAccessed,   // LA
        LastModified,   // LM
        FileCreated     // FC (created)
    }

    public enum FilterDirection
    {
        OlderThan,      // + sign
        WithinLast      // - sign
    }

    /// <summary>
    /// Parsed date filter result
    /// </summary>
    public class ParsedDateFilter
    {
        public FilterType Type { get; set; }
        public FilterDirection Direction { get; set; }
        public int Minutes { get; set; }
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parses a date filter string into its components
    /// </summary>
    public static ParsedDateFilter Parse(string? dateFilter)
    {
        if (string.IsNullOrWhiteSpace(dateFilter))
        {
            return new ParsedDateFilter
            {
                Type = FilterType.None,
                IsValid = true
            };
        }

        // Expected format: "TYPE:SIGN:MINUTES" (e.g., "LA:+43200")
        var regex = new Regex(@"^(LA|LM|FC):([+-])(\d+)$", RegexOptions.IgnoreCase);
        var match = regex.Match(dateFilter.Trim());

        if (!match.Success)
        {
            return new ParsedDateFilter
            {
                IsValid = false,
                ErrorMessage = $"Invalid date filter format. Expected 'TYPE:SIGN:MINUTES' (e.g., 'LA:+43200'), got '{dateFilter}'"
            };
        }

        var typeStr = match.Groups[1].Value.ToUpper();
        var sign = match.Groups[2].Value;
        var minutesStr = match.Groups[3].Value;

        // Parse type
        FilterType type = typeStr switch
        {
            "LA" => FilterType.LastAccessed,
            "LM" => FilterType.LastModified,
            "FC" => FilterType.FileCreated,
            _ => FilterType.None
        };

        // Parse direction
        var direction = sign == "+" ? FilterDirection.OlderThan : FilterDirection.WithinLast;

        // Parse minutes
        if (!int.TryParse(minutesStr, out var minutes))
        {
            return new ParsedDateFilter
            {
                IsValid = false,
                ErrorMessage = $"Invalid minutes value: {minutesStr}"
            };
        }

        // Validate range
        if (minutes < MinMinutes || minutes > MaxMinutes)
        {
            return new ParsedDateFilter
            {
                IsValid = false,
                ErrorMessage = $"Minutes must be between {MinMinutes} and {MaxMinutes} (got {minutes})"
            };
        }

        return new ParsedDateFilter
        {
            Type = type,
            Direction = direction,
            Minutes = minutes,
            IsValid = true
        };
    }

    /// <summary>
    /// Formats a date filter from its components
    /// </summary>
    public static string Format(FilterType type, FilterDirection direction, int minutes)
    {
        if (type == FilterType.None)
            return string.Empty;

        var typeStr = type switch
        {
            FilterType.LastAccessed => "LA",
            FilterType.LastModified => "LM",
            FilterType.FileCreated => "FC",
            _ => throw new ArgumentException($"Invalid filter type: {type}")
        };

        var sign = direction == FilterDirection.OlderThan ? "+" : "-";

        return $"{typeStr}:{sign}:{minutes}";
    }

    /// <summary>
    /// Gets a human-readable description of a date filter
    /// </summary>
    public static string GetDescription(string? dateFilter)
    {
        var parsed = Parse(dateFilter);
        if (!parsed.IsValid || parsed.Type == FilterType.None)
            return "None";

        var timeDesc = FormatMinutesToReadable(parsed.Minutes);
        var typeDesc = parsed.Type switch
        {
            FilterType.LastAccessed => "accessed",
            FilterType.LastModified => "modified",
            FilterType.FileCreated => "created",
            _ => "unknown"
        };

        if (parsed.Direction == FilterDirection.OlderThan)
        {
            return parsed.Type == FilterType.FileCreated
                ? $"Files created more than {timeDesc} ago (older files)"
                : $"Files NOT {typeDesc} in the last {timeDesc} (older files)";
        }
        else
        {
            return parsed.Type == FilterType.FileCreated
                ? $"Files created within the last {timeDesc}"
                : $"Files {typeDesc} within the last {timeDesc}";
        }
    }

    /// <summary>
    /// Formats minutes into a human-readable string with meaningful units
    /// </summary>
    private static string FormatMinutesToReadable(int minutes)
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

        // 30 days or more
        var months = minutes / 43200;
        var remainingWeeks = (minutes % 43200) / 10080;
        if (remainingWeeks == 0)
            return $"{months} month{(months != 1 ? "s" : "")} (approx)";
        return $"{months} month{(months != 1 ? "s" : "")}, {remainingWeeks} week{(remainingWeeks != 1 ? "s" : "")} (approx)";
    }

    /// <summary>
    /// Validates a date filter string
    /// </summary>
    public static (bool IsValid, string ErrorMessage) Validate(string? dateFilter)
    {
        var parsed = Parse(dateFilter);
        return (parsed.IsValid, parsed.ErrorMessage);
    }
}
