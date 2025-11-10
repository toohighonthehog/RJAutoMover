using System.IO;
using RJAutoMoverShared.Models;
using RJAutoMoverShared.Helpers;
using RJAutoMoverS.Config;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RJAutoMoverConfig.Services;

/// <summary>
/// Service for loading, saving, and validating configuration files
/// </summary>
public class ConfigEditorService
{
    private readonly RJAutoMoverShared.Services.LoggingService _loggingService;
    private readonly ConfigValidator _validator;

    public ConfigEditorService()
    {
        // Create a logging service for validation
        _loggingService = new RJAutoMoverShared.Services.LoggingService("ConfigEditor");
        _validator = new ConfigValidator(_loggingService);
    }

    /// <summary>
    /// Gets the default config file path with priority: Program Files first, then ProgramData fallback
    /// </summary>
    public static string GetDefaultConfigPath()
    {
        return RJAutoMoverShared.Constants.Paths.GetConfigPath();
    }

    /// <summary>
    /// Loads configuration from a YAML file
    /// </summary>
    public Configuration? LoadConfiguration(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var yamlContent = File.ReadAllText(filePath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(PascalCaseNamingConvention.Instance)
                .Build();

            return deserializer.Deserialize<Configuration>(yamlContent);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load configuration from {filePath}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Saves configuration to a YAML file
    /// </summary>
    public async Task<bool> SaveConfigurationAsync(string filePath, Configuration config)
    {
        try
        {
            // Validate before saving
            var validationResult = await ValidateConfigurationAsync(filePath, config);
            if (!validationResult.IsValid)
            {
                throw new InvalidOperationException($"Configuration is invalid: {validationResult.ErrorMessage}");
            }

            // Create backup of existing file
            if (File.Exists(filePath))
            {
                CreateBackup(filePath);
            }

            // Serialize configuration with comments
            var yaml = SerializeWithComments(config);

            // Save to file
            await File.WriteAllTextAsync(filePath, yaml);

            return true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save configuration to {filePath}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Validates a configuration without saving
    /// </summary>
    public async Task<ValidationResult> ValidateConfigurationAsync(string filePath, Configuration config)
    {
        try
        {
            // Write to temp file for validation
            var tempFile = Path.GetTempFileName();
            try
            {
                var yaml = SerializeWithComments(config);
                await File.WriteAllTextAsync(tempFile, yaml);

                // Validate the configuration object structure directly
                var fileRuleValidation = ValidateFileRulesStructure(config.FileRules);
                if (!fileRuleValidation.IsValid)
                {
                    return fileRuleValidation;
                }

                var appValidation = ValidateApplicationConfigStructure(config.Application);
                if (!appValidation.IsValid)
                {
                    return appValidation;
                }

                return new ValidationResult { IsValid = true, Configuration = config };
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
        catch (Exception ex)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Validation error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Creates a backup of the configuration file
    /// </summary>
    private void CreateBackup(string filePath)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = $"{filePath}.{timestamp}.bak";
            File.Copy(filePath, backupPath, overwrite: true);
        }
        catch
        {
            // Backup failure shouldn't prevent saving
        }
    }

    /// <summary>
    /// Serializes configuration to YAML with helpful comments
    /// </summary>
    private string SerializeWithComments(Configuration config)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build();

        var yaml = serializer.Serialize(config);

        // Add header comment
        var header = @"# ==============================================================================
# RJAutoMover Configuration File
# ==============================================================================
# This file was generated by RJAutoMoverConfig
#
# IMPORTANT: After editing this file, you MUST restart the RJAutoMoverService
# for changes to take effect (Win+R -> services.msc -> Restart service)
#
# Documentation: See README.md for detailed explanations
# ==============================================================================

";
        return header + yaml;
    }

    /// <summary>
    /// Basic structural validation for file rules (simplified version)
    /// </summary>
    private ValidationResult ValidateFileRulesStructure(List<FileRule> fileRules)
    {
        if (fileRules == null || !fileRules.Any())
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = "No FileRules defined in configuration"
            };
        }

        foreach (var rule in fileRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "All FileRules must have a Name"
                };
            }

            if (string.IsNullOrWhiteSpace(rule.SourceFolder))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Rule '{rule.Name}': SourceFolder is required"
                };
            }

            if (string.IsNullOrWhiteSpace(rule.DestinationFolder))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Rule '{rule.Name}': DestinationFolder is required"
                };
            }

            if (string.IsNullOrWhiteSpace(rule.Extension))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Rule '{rule.Name}': Extension is required"
                };
            }

            // Validate date criteria mutual exclusivity
            var dateCriteriaCount = 0;
            if (rule.LastAccessedMins.HasValue) dateCriteriaCount++;
            if (rule.LastModifiedMins.HasValue) dateCriteriaCount++;
            if (rule.AgeCreatedMins.HasValue) dateCriteriaCount++;

            if (dateCriteriaCount > 1)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Rule '{rule.Name}': Only one date criteria allowed (LastAccessedMins, LastModifiedMins, or AgeCreatedMins)"
                };
            }

            // Validate zero values
            if (rule.LastAccessedMins.HasValue && rule.LastAccessedMins.Value == 0)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Rule '{rule.Name}': LastAccessedMins cannot be zero"
                };
            }

            if (rule.LastModifiedMins.HasValue && rule.LastModifiedMins.Value == 0)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Rule '{rule.Name}': LastModifiedMins cannot be zero"
                };
            }

            if (rule.AgeCreatedMins.HasValue && rule.AgeCreatedMins.Value == 0)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Rule '{rule.Name}': AgeCreatedMins cannot be zero"
                };
            }

            // Validate OTHERS rules have date criteria
            if (rule.IsAllExtensionRule() && dateCriteriaCount == 0)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Rule '{rule.Name}': Extension 'OTHERS' rules MUST have a date criteria"
                };
            }
        }

        return new ValidationResult { IsValid = true };
    }

    /// <summary>
    /// Basic structural validation for application config
    /// </summary>
    private ValidationResult ValidateApplicationConfigStructure(ApplicationConfig appConfig)
    {
        if (appConfig == null)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = "Application configuration is missing"
            };
        }

        // Validate port numbers
        if (appConfig.ServiceGrpcPort < 1024 || appConfig.ServiceGrpcPort > 65535)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = "ServiceGrpcPort must be between 1024 and 65535"
            };
        }

        if (appConfig.TrayGrpcPort < 1024 || appConfig.TrayGrpcPort > 65535)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = "TrayGrpcPort must be between 1024 and 65535"
            };
        }

        if (appConfig.ServiceGrpcPort == appConfig.TrayGrpcPort)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = "ServiceGrpcPort and TrayGrpcPort must be different"
            };
        }

        return new ValidationResult { IsValid = true };
    }

    /// <summary>
    /// Checks if the current user has admin rights
    /// </summary>
    public static bool IsRunningAsAdministrator()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
