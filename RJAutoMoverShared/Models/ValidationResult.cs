namespace RJAutoMoverShared.Models;

/// <summary>
/// Result of configuration validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public Configuration? Configuration { get; set; }
}
