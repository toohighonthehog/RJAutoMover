using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RJAutoMoverShared.Helpers;

/// <summary>
/// Provides folder permission validation utilities shared across all projects
/// </summary>
public static class FolderPermissionValidator
{
    /// <summary>
    /// Validates that a folder exists and has the required permissions
    /// </summary>
    /// <param name="folderPath">The folder path to validate</param>
    /// <param name="requireRead">Whether to check for read permission (default: true)</param>
    /// <param name="requireWrite">Whether to check for write permission (default: true)</param>
    /// <returns>FolderValidationResult with status and error message</returns>
    public static FolderValidationResult ValidateFolderPermissions(
        string folderPath,
        bool requireRead = true,
        bool requireWrite = true)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return new FolderValidationResult
            {
                IsValid = false,
                ErrorMessage = "Folder path cannot be empty"
            };
        }

        // Check if folder exists
        if (!Directory.Exists(folderPath))
        {
            return new FolderValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Folder does not exist: {folderPath}",
                FolderExists = false
            };
        }

        try
        {
            // Test read permission by attempting to enumerate
            if (requireRead)
            {
                try
                {
                    _ = Directory.GetFiles(folderPath);
                }
                catch (UnauthorizedAccessException)
                {
                    return new FolderValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"No read permission for folder: {folderPath}",
                        FolderExists = true,
                        HasReadAccess = false
                    };
                }
                catch (Exception ex)
                {
                    return new FolderValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Error checking read access: {ex.Message}",
                        FolderExists = true,
                        HasReadAccess = false
                    };
                }
            }

            // Test write permission by attempting to create a temp file
            if (requireWrite)
            {
                var testFilePath = Path.Combine(folderPath, $".rjautomover_test_{Guid.NewGuid():N}.tmp");
                try
                {
                    File.WriteAllText(testFilePath, "test");
                    File.Delete(testFilePath);
                }
                catch (UnauthorizedAccessException)
                {
                    return new FolderValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"No write permission for folder: {folderPath}",
                        FolderExists = true,
                        HasReadAccess = requireRead,
                        HasWriteAccess = false
                    };
                }
                catch (Exception ex)
                {
                    return new FolderValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Error checking write access: {ex.Message}",
                        FolderExists = true,
                        HasReadAccess = requireRead,
                        HasWriteAccess = false
                    };
                }
            }

            // All checks passed
            return new FolderValidationResult
            {
                IsValid = true,
                ErrorMessage = string.Empty,
                FolderExists = true,
                HasReadAccess = requireRead,
                HasWriteAccess = requireWrite
            };
        }
        catch (Exception ex)
        {
            return new FolderValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Unexpected error validating folder: {ex.Message}",
                FolderExists = true
            };
        }
    }

    /// <summary>
    /// Gets a user-friendly status message for folder validation
    /// </summary>
    public static string GetStatusMessage(FolderValidationResult result)
    {
        if (result.IsValid)
        {
            return "✓ Valid";
        }

        if (!result.FolderExists)
        {
            return "✗ Folder does not exist";
        }

        if (!result.HasReadAccess)
        {
            return "✗ No read permission";
        }

        if (!result.HasWriteAccess)
        {
            return "✗ No write permission";
        }

        return $"✗ {result.ErrorMessage}";
    }
}

/// <summary>
/// Result of folder permission validation
/// </summary>
public class FolderValidationResult
{
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public bool FolderExists { get; set; }
    public bool HasReadAccess { get; set; }
    public bool HasWriteAccess { get; set; }
}
