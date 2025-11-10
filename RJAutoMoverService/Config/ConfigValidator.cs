using System.IO;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Diagnostics;
using YamlDotNet.Serialization;
using YamlDotNet.RepresentationModel;
using RJAutoMoverShared.Models;
using RJAutoMoverShared.Services;

namespace RJAutoMoverS.Config;

public class ValidationError
{
    public string RuleName { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? FieldName { get; set; }
    public string? FieldValue { get; set; }

    public override string ToString()
    {
        if (!string.IsNullOrEmpty(RuleName))
        {
            return $"Rule '{RuleName}': {Message}";
        }
        return Message;
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public List<ValidationError> Errors { get; set; } = new();
    public Configuration? Configuration { get; set; }

    public string GetDetailedErrorMessage()
    {
        if (Errors.Count == 0)
        {
            return ErrorMessage;
        }

        var lines = new List<string>();
        lines.Add("Configuration validation failed:");
        lines.Add("");

        foreach (var error in Errors)
        {
            lines.Add($"• {error}");
        }

        return string.Join("\n", lines);
    }
}

public class ConfigSourceInfo
{
    public HashSet<string> ExplicitApplicationKeys { get; set; } = new();
    public Dictionary<int, HashSet<string>> ExplicitFileRuleKeys { get; set; } = new();
    public Dictionary<string, string> RawApplicationValues { get; set; } = new();
    public Dictionary<int, Dictionary<string, string>> RawFileRuleValues { get; set; } = new();
}

public class ConfigValidator
{
    private readonly RJAutoMoverShared.Services.LoggingService _logger;
    private readonly string _configPath;
    private string? _lastConfigHash = null;
    private bool _configChangedErrorMode = false;

    public ConfigValidator(RJAutoMoverShared.Services.LoggingService logger)
    {
        _logger = logger;

        // Use priority-based config path: Program Files first, then ProgramData fallback
        _configPath = RJAutoMoverShared.Constants.Paths.GetConfigPath();
    }

    public async Task<ValidationResult> ValidateConfigAsync()
    {
        var validationId = Guid.NewGuid().ToString()[..8];

        try
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"[{validationId}] Validating configuration: {_configPath}");

            if (!File.Exists(_configPath))
            {
                var errorMsg = $"Configuration file not found at {_configPath}";
                _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"[{validationId}] {errorMsg}");
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = errorMsg
                };
            }

            var yamlContent = await File.ReadAllTextAsync(_configPath);
            var sourceInfo = ParseYamlStructure(yamlContent, validationId);

            Configuration config;
            try
            {
                // Try standard deserialization first
                var deserializer = new DeserializerBuilder().Build();
                config = deserializer.Deserialize<Configuration>(yamlContent);
            }
            catch (YamlDotNet.Core.YamlException)
            {
                // If standard deserialization fails due to type conversion errors,
                // create a default config and let validation catch the issues
                _logger.Log(RJAutoMoverShared.Models.LogLevel.WARN, $"[{validationId}] YAML deserialization failed, using defaults for validation");
                config = new Configuration
                {
                    Application = new ApplicationConfig(),
                    FileRules = new List<FileRule>()
                };

                // Try to at least get the FileRules structure if possible
                try
                {
                    var yaml = new YamlStream();
                    yaml.Load(new StringReader(yamlContent));
                    var rootNode = (YamlMappingNode)yaml.Documents[0].RootNode;

                    if (rootNode.Children.ContainsKey(new YamlScalarNode("FileRules")))
                    {
                        var fileRulesNode = (YamlSequenceNode)rootNode.Children[new YamlScalarNode("FileRules")];
                        for (int i = 0; i < fileRulesNode.Children.Count; i++)
                        {
                            var ruleNode = (YamlMappingNode)fileRulesNode.Children[i];
                            var rule = new FileRule();

                            // Extract string values safely
                            if (ruleNode.Children.ContainsKey(new YamlScalarNode("Name")))
                                rule.Name = ruleNode.Children[new YamlScalarNode("Name")].ToString();
                            if (ruleNode.Children.ContainsKey(new YamlScalarNode("SourceFolder")))
                                rule.SourceFolder = ruleNode.Children[new YamlScalarNode("SourceFolder")].ToString();
                            if (ruleNode.Children.ContainsKey(new YamlScalarNode("DestinationFolder")))
                                rule.DestinationFolder = ruleNode.Children[new YamlScalarNode("DestinationFolder")].ToString();
                            if (ruleNode.Children.ContainsKey(new YamlScalarNode("Extension")))
                                rule.Extension = ruleNode.Children[new YamlScalarNode("Extension")].ToString();
                            if (ruleNode.Children.ContainsKey(new YamlScalarNode("FileExists")))
                                rule.FileExists = ruleNode.Children[new YamlScalarNode("FileExists")].ToString();

                            // Extract numeric values safely
                            if (ruleNode.Children.ContainsKey(new YamlScalarNode("ScanIntervalMs")))
                            {
                                var scanValue = ruleNode.Children[new YamlScalarNode("ScanIntervalMs")].ToString();
                                if (int.TryParse(scanValue, out int scanInterval))
                                    rule.ScanIntervalMs = scanInterval;
                            }

                            // Extract boolean values safely
                            if (ruleNode.Children.ContainsKey(new YamlScalarNode("IsActive")))
                            {
                                var activeValue = ruleNode.Children[new YamlScalarNode("IsActive")].ToString();
                                if (bool.TryParse(activeValue, out bool isActive))
                                    rule.IsActive = isActive;
                            }

                            config.FileRules.Add(rule);
                        }
                    }
                }
                catch
                {
                    // If we can't even parse the structure, validation will catch it
                }
            }

            if (config == null)
            {
                var errorMsg = "Failed to deserialize configuration - config object is null";
                _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"[{validationId}] {errorMsg}");
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = errorMsg
                };
            }

            // Validate and display results
            var fileRuleValidation = ValidateFileRules(config.FileRules, validationId, sourceInfo);
            var appValidation = ValidateApplicationConfig(config.Application, validationId, sourceInfo);

            // Collect all validation errors for detailed reporting
            var allErrors = new List<ValidationError>();
            if (!fileRuleValidation.IsValid)
            {
                allErrors.Add(new ValidationError
                {
                    ErrorType = "FileRules",
                    Message = fileRuleValidation.ErrorMessage
                });
            }
            if (!appValidation.IsValid)
            {
                allErrors.Add(new ValidationError
                {
                    ErrorType = "Application",
                    Message = appValidation.ErrorMessage
                });
            }

            // Create concise error summary
            string? validationError = null;
            if (!fileRuleValidation.IsValid || !appValidation.IsValid)
            {
                var errorParts = new List<string>();

                if (!fileRuleValidation.IsValid && !string.IsNullOrEmpty(fileRuleValidation.ErrorMessage))
                {
                    // Split multiple rule names and put each in square brackets
                    var ruleNames = fileRuleValidation.ErrorMessage.Split(", ");
                    var brackedRules = ruleNames.Select(name => $"[{name}]");
                    errorParts.AddRange(brackedRules);
                }

                if (!appValidation.IsValid && !string.IsNullOrEmpty(appValidation.ErrorMessage))
                {
                    errorParts.Add(appValidation.ErrorMessage);
                }

                validationError = string.Join(", ", errorParts) + " - fix and restart service.";

                // Log detailed validation errors with proper formatting
                _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "========================================");
                _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "CONFIGURATION VALIDATION FAILED");
                _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "========================================");
                _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"Config file: {_configPath}");
                _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "");

                if (!fileRuleValidation.IsValid)
                {
                    _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "FILE RULES ERROR:");
                    // Split multi-line error messages and log each line separately
                    var errorLines = fileRuleValidation.ErrorMessage.Split('\n');
                    foreach (var line in errorLines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"  {line.Trim()}");
                        }
                    }
                    _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "");
                }

                if (!appValidation.IsValid)
                {
                    _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "APPLICATION SETTINGS ERROR:");
                    var errorLines = appValidation.ErrorMessage.Split('\n');
                    foreach (var line in errorLines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"  {line.Trim()}");
                        }
                    }
                    _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "");
                }

                _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "NEXT STEPS:");
                _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"  1. Fix the errors in {_configPath}");
                _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "  2. Restart the RJAutoMoverService");
                _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "========================================");
            }

            // Display comprehensive summary
            DisplayConfigurationSummary(config, validationId, sourceInfo, validationError);

            // If there are any validation errors, return the combined formatted error message with details
            if (!fileRuleValidation.IsValid || !appValidation.IsValid)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = validationError ?? "Configuration validation failed",
                    Errors = allErrors,
                    Configuration = config // Include config even on failure so ActivityHistory can be initialized
                };
            }

            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"[{validationId}] Configuration validation completed successfully");
            return new ValidationResult
            {
                IsValid = true,
                Configuration = config
            };
        }
        catch (YamlDotNet.Core.YamlException yamlEx)
        {
            var errorMsg = $"YAML parsing error: {yamlEx.Message}";
            _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"[{validationId}] {errorMsg}");
            _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"[{validationId}] YAML error details: Line {yamlEx.Start.Line}, Column {yamlEx.Start.Column}");

            // Check if this might be due to invalid numeric values
            if (yamlEx.Message.Contains("unable to parse") || yamlEx.Message.Contains("invalid") || yamlEx.Message.ToLower().Contains("type"))
            {
                errorMsg = $"Configuration contains invalid values. Please check that all numeric fields contain only numbers. YAML error: {yamlEx.Message}";
            }

            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = errorMsg
            };
        }
        catch (Exception ex)
        {
            var errorMsg = $"Unexpected error during configuration validation: {ex.Message}";
            _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"[{validationId}] {errorMsg}");
            _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"[{validationId}] Stack trace: {ex.StackTrace}");
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = errorMsg
            };
        }
    }

    private ValidationResult ValidateFileRules(List<FileRule> fileRules, string validationId = "", ConfigSourceInfo? sourceInfo = null)
    {
        if (fileRules == null || !fileRules.Any())
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = "No FileRules defined in configuration"
            };
        }

        var sourceExtensions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var allRulesByFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase); // Track OTHERS rules per folder
        var inactiveRuleIssues = new List<string>(); // Track issues with inactive rules for display
        var allSourceFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in fileRules)
        {
            // Validate all rules but only fail validation for active rules with issues
            bool isActiveRule = rule.IsActive;

            // Validate required fields
            if (string.IsNullOrWhiteSpace(rule.SourceFolder))
            {
                return new ValidationResult 
                { 
                    IsValid = false, 
                    ErrorMessage = $"SourceFolder is required for rule '{rule.Name}'" 
                };
            }

            if (string.IsNullOrWhiteSpace(rule.DestinationFolder))
            {
                return new ValidationResult 
                { 
                    IsValid = false, 
                    ErrorMessage = $"DestinationFolder is required for rule '{rule.Name}'" 
                };
            }

            if (string.IsNullOrWhiteSpace(rule.Extension))
            {
                return new ValidationResult 
                { 
                    IsValid = false, 
                    ErrorMessage = $"Extension is required for rule '{rule.Name}'" 
                };
            }

            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                return new ValidationResult 
                { 
                    IsValid = false, 
                    ErrorMessage = "Name is required for all FileRules" 
                };
            }

            // Validate Name (alphanumeric up to 32 characters)
            if (!Regex.IsMatch(rule.Name, @"^[a-zA-Z0-9\s]{1,32}$"))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Name '{rule.Name}' must be alphanumeric and up to 32 characters"
                };
            }

            // Validate folders don't contain wildcards
            if (ContainsWildcards(rule.SourceFolder))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"SourceFolder cannot contain wildcards (* or ?): {rule.SourceFolder}"
                };
            }

            if (ContainsWildcards(rule.DestinationFolder))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"DestinationFolder cannot contain wildcards (* or ?): {rule.DestinationFolder}"
                };
            }

            // Collect all source folders for later validation
            if (!string.IsNullOrWhiteSpace(rule.SourceFolder))
            {
                allSourceFolders.Add(Path.GetFullPath(rule.SourceFolder));
            }

            // Validate folders exist and are accessible for all rules
            // But only fail validation for active rules with issues
            var sourceCheck = CheckDirectoryAccess(rule.SourceFolder);
            if (!sourceCheck.IsAccessible)
            {
                if (isActiveRule)
                {
                    var error = new ValidationError
                    {
                        RuleName = rule.Name,
                        ErrorType = "SourceFolder",
                        Message = sourceCheck.ErrorDetail,
                        FieldName = "SourceFolder",
                        FieldValue = rule.SourceFolder
                    };

                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Rule '{rule.Name}': {sourceCheck.ErrorDetail}",
                        Errors = new List<ValidationError> { error }
                    };
                }
                else
                {
                    inactiveRuleIssues.Add($"Rule '{rule.Name}': {sourceCheck.ErrorDetail}");
                }
            }

            var destCheck = CheckDirectoryAccess(rule.DestinationFolder);
            if (!destCheck.IsAccessible)
            {
                if (isActiveRule)
                {
                    var error = new ValidationError
                    {
                        RuleName = rule.Name,
                        ErrorType = "DestinationFolder",
                        Message = destCheck.ErrorDetail,
                        FieldName = "DestinationFolder",
                        FieldValue = rule.DestinationFolder
                    };

                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Rule '{rule.Name}': {destCheck.ErrorDetail}",
                        Errors = new List<ValidationError> { error }
                    };
                }
                else
                {
                    inactiveRuleIssues.Add($"Rule '{rule.Name}': {destCheck.ErrorDetail}");
                }
            }

            // Validate extensions don't contain wildcards
            if (ContainsWildcards(rule.Extension))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Extension cannot contain wildcards (* or ?): {rule.Extension}"
                };
            }

            // Check if this is an OTHERS extension rule (special handling)
            bool isAllRule = rule.IsAllExtensionRule();

            // Validate OTHERS extension rules
            if (isAllRule)
            {
                // OTHERS rules MUST have exactly one date criteria
                var dateCriteriaCountForAll = 0;
                if (rule.LastAccessedMins.HasValue) dateCriteriaCountForAll++;
                if (rule.LastModifiedMins.HasValue) dateCriteriaCountForAll++;
                if (rule.AgeCreatedMins.HasValue) dateCriteriaCountForAll++;

                if (dateCriteriaCountForAll == 0)
                {
                    if (isActiveRule)
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"Rule '{rule.Name}': Extension 'OTHERS' rules MUST have a date criteria (LastAccessedMins, LastModifiedMins, or AgeCreatedMins)"
                        };
                    }
                    else
                    {
                        inactiveRuleIssues.Add($"Rule '{rule.Name}': Extension 'OTHERS' rules MUST have a date criteria");
                    }
                }

                // Check for only one OTHERS rule per source folder (among active rules)
                if (rule.IsActive)
                {
                    var normalizedSource = Path.GetFullPath(rule.SourceFolder);

                    // Track OTHERS rules by source folder
                    if (!allRulesByFolder.ContainsKey(normalizedSource))
                    {
                        allRulesByFolder[normalizedSource] = new List<string>();
                    }

                    allRulesByFolder[normalizedSource].Add(rule.Name);
                }
            }

            // Validate extensions (skip for OTHERS rules)
            if (!isAllRule)
            {
                var extensions = rule.GetExtensions();
                foreach (var ext in extensions)
                {
                    if (!Regex.IsMatch(ext, @"^\.[a-zA-Z0-9]{1,10}$"))
                    {
                        if (isActiveRule)
                        {
                            return new ValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Invalid extension format '{ext}' in rule '{rule.Name}'"
                            };
                        }
                        else
                        {
                            inactiveRuleIssues.Add($"Rule '{rule.Name}': Invalid extension format '{ext}'");
                        }
                    }

                    // Check for extension clashes within same source folder (using normalized paths)
                    // Skip this check for ALL rules
                    if (rule.IsActive)
                    {
                        var normalizedSource = Path.GetFullPath(rule.SourceFolder);

                        if (!sourceExtensions.ContainsKey(normalizedSource))
                        {
                            sourceExtensions[normalizedSource] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        }

                        if (sourceExtensions[normalizedSource].Contains(ext))
                        {
                            return new ValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Extension '{ext}' is duplicated for source folder '{rule.SourceFolder}'"
                            };
                        }

                        sourceExtensions[normalizedSource].Add(ext);
                    }
                }
            }

            // Validate ScanIntervalMs
            if (rule.ScanIntervalMs < 5000 || rule.ScanIntervalMs > 900000)
            {
                if (isActiveRule)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"ScanIntervalMs must be between 5000 and 900000 for rule '{rule.Name}'"
                    };
                }
                else
                {
                    inactiveRuleIssues.Add($"Rule '{rule.Name}': ScanIntervalMs must be between 5000 and 900000");
                }
            }

            // Validate FileExists
            if (!new[] { "skip", "overwrite" }.Contains(rule.FileExists.ToLower()))
            {
                if (isActiveRule)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"FileExists must be 'skip' or 'overwrite' for rule '{rule.Name}'"
                    };
                }
                else
                {
                    inactiveRuleIssues.Add($"Rule '{rule.Name}': FileExists must be 'skip' or 'overwrite'");
                }
            }

            // Validate date criteria (LastAccessedMins, LastModifiedMins, AgeCreatedMins)
            // Only one can be set per rule - mutual exclusivity check
            var dateCriteriaCount = 0;
            var dateCriteriaNames = new List<string>();

            if (rule.LastAccessedMins.HasValue)
            {
                dateCriteriaCount++;
                dateCriteriaNames.Add("LastAccessedMins");
            }
            if (rule.LastModifiedMins.HasValue)
            {
                dateCriteriaCount++;
                dateCriteriaNames.Add("LastModifiedMins");
            }
            if (rule.AgeCreatedMins.HasValue)
            {
                dateCriteriaCount++;
                dateCriteriaNames.Add("AgeCreatedMins");
            }

            // Check mutual exclusivity
            if (dateCriteriaCount > 1)
            {
                if (isActiveRule)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Rule '{rule.Name}': Only one date criteria can be specified. Found: {string.Join(", ", dateCriteriaNames)}. Remove all but one date criteria."
                    };
                }
                else
                {
                    inactiveRuleIssues.Add($"Rule '{rule.Name}': Only one date criteria can be specified. Found: {string.Join(", ", dateCriteriaNames)}");
                }
            }

            // Validate individual date criteria values (allow negative, range: -5256000 to +5256000 minutes = +/- 10 years)
            // Positive = older than X minutes, Negative = within last X minutes, Zero = not allowed
            const int maxMinutes = 5256000; // 10 years in minutes

            if (rule.LastAccessedMins.HasValue)
            {
                if (rule.LastAccessedMins.Value == 0)
                {
                    if (isActiveRule)
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"LastAccessedMins cannot be zero for rule '{rule.Name}'. Use positive (older than) or negative (within last) values."
                        };
                    }
                    else
                    {
                        inactiveRuleIssues.Add($"Rule '{rule.Name}': LastAccessedMins cannot be zero");
                    }
                }
                else if (Math.Abs(rule.LastAccessedMins.Value) > maxMinutes)
                {
                    if (isActiveRule)
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"LastAccessedMins must be between -{maxMinutes} and +{maxMinutes} (+/- 10 years) for rule '{rule.Name}' (found: {rule.LastAccessedMins.Value})"
                        };
                    }
                    else
                    {
                        inactiveRuleIssues.Add($"Rule '{rule.Name}': LastAccessedMins must be between -{maxMinutes} and +{maxMinutes} (found: {rule.LastAccessedMins.Value})");
                    }
                }
            }

            if (rule.LastModifiedMins.HasValue)
            {
                if (rule.LastModifiedMins.Value == 0)
                {
                    if (isActiveRule)
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"LastModifiedMins cannot be zero for rule '{rule.Name}'. Use positive (older than) or negative (within last) values."
                        };
                    }
                    else
                    {
                        inactiveRuleIssues.Add($"Rule '{rule.Name}': LastModifiedMins cannot be zero");
                    }
                }
                else if (Math.Abs(rule.LastModifiedMins.Value) > maxMinutes)
                {
                    if (isActiveRule)
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"LastModifiedMins must be between -{maxMinutes} and +{maxMinutes} (+/- 10 years) for rule '{rule.Name}' (found: {rule.LastModifiedMins.Value})"
                        };
                    }
                    else
                    {
                        inactiveRuleIssues.Add($"Rule '{rule.Name}': LastModifiedMins must be between -{maxMinutes} and +{maxMinutes} (found: {rule.LastModifiedMins.Value})");
                    }
                }
            }

            if (rule.AgeCreatedMins.HasValue)
            {
                if (rule.AgeCreatedMins.Value == 0)
                {
                    if (isActiveRule)
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"AgeCreatedMins cannot be zero for rule '{rule.Name}'. Use positive (older than) or negative (within last) values."
                        };
                    }
                    else
                    {
                        inactiveRuleIssues.Add($"Rule '{rule.Name}': AgeCreatedMins cannot be zero");
                    }
                }
                else if (Math.Abs(rule.AgeCreatedMins.Value) > maxMinutes)
                {
                    if (isActiveRule)
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"AgeCreatedMins must be between -{maxMinutes} and +{maxMinutes} (+/- 10 years) for rule '{rule.Name}' (found: {rule.AgeCreatedMins.Value})"
                        };
                    }
                    else
                    {
                        inactiveRuleIssues.Add($"Rule '{rule.Name}': AgeCreatedMins must be between -{maxMinutes} and +{maxMinutes} (found: {rule.AgeCreatedMins.Value})");
                    }
                }
            }
        }

        // Check for multiple ALL rules per source folder (only one allowed)
        foreach (var kvp in allRulesByFolder)
        {
            if (kvp.Value.Count > 1)
            {
                var allRuleNames = string.Join(", ", kvp.Value.Select(r => $"'{r}'"));
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Source folder '{kvp.Key}' has multiple active OTHERS extension rules ({allRuleNames}). Only one OTHERS rule per source folder is allowed."
                };
            }
        }

        // Check for destination folders that match any source folder (would create infinite loops)
        foreach (var rule in fileRules)
        {
            if (!string.IsNullOrWhiteSpace(rule.DestinationFolder))
            {
                var destinationPath = Path.GetFullPath(rule.DestinationFolder);
                if (allSourceFolders.Contains(destinationPath))
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"DestinationFolder '{rule.DestinationFolder}' for rule '{rule.Name}' matches a SourceFolder from another rule. This would create an infinite processing loop."
                    };
                }
            }
        }

        // Check #5: Source and destination cannot be the same
        foreach (var rule in fileRules)
        {
            if (!string.IsNullOrWhiteSpace(rule.SourceFolder) && !string.IsNullOrWhiteSpace(rule.DestinationFolder))
            {
                var srcFull = Path.GetFullPath(rule.SourceFolder);
                var dstFull = Path.GetFullPath(rule.DestinationFolder);

                if (srcFull.Equals(dstFull, StringComparison.OrdinalIgnoreCase))
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Rule '{rule.Name}': SourceFolder and DestinationFolder cannot be the same"
                    };
                }
            }
        }

        // Check #1: Circular move chains (multi-hop loops)
        var cycleDetection = DetectCircularChains(fileRules.Where(r => r.IsActive).ToList(), validationId);
        if (!cycleDetection.IsValid)
        {
            return cycleDetection;
        }

        // Check #14: Duplicate rule names
        var ruleNames = fileRules.Select(r => r.Name?.Trim()).Where(n => !string.IsNullOrEmpty(n)).ToList();
        var duplicateNames = ruleNames.GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateNames.Any())
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Duplicate rule names found: {string.Join(", ", duplicateNames)}"
            };
        }

        // Check #11: Reserved Windows folder names
        foreach (var rule in fileRules)
        {
            var reservedCheck = ValidateNoReservedNames(rule.SourceFolder, $"Rule '{rule.Name}' SourceFolder");
            if (!reservedCheck.IsValid)
                return reservedCheck;

            reservedCheck = ValidateNoReservedNames(rule.DestinationFolder, $"Rule '{rule.Name}' DestinationFolder");
            if (!reservedCheck.IsValid)
                return reservedCheck;
        }

        // Check if all rules have critical issues that would prevent them from working
        var workableRules = 0;
        foreach (var rule in fileRules)
        {
            var hasCriticalIssues = string.IsNullOrWhiteSpace(rule.SourceFolder) ||
                                   string.IsNullOrWhiteSpace(rule.DestinationFolder) ||
                                   string.IsNullOrWhiteSpace(rule.Extension) ||
                                   (!string.IsNullOrWhiteSpace(rule.SourceFolder) && !Directory.Exists(rule.SourceFolder)) ||
                                   (!string.IsNullOrWhiteSpace(rule.DestinationFolder) && !Directory.Exists(rule.DestinationFolder));

            if (!hasCriticalIssues)
            {
                workableRules++;
            }
        }

        // If no rules are potentially workable, fail validation regardless of active status
        if (workableRules == 0)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = "No file rules are workable - all rules have critical configuration issues"
            };
        }

        // Check for rules with invalid numeric values for the error summary
        var rulesWithIssues = new List<string>();
        if (sourceInfo != null)
        {
            for (int i = 0; i < fileRules.Count; i++)
            {
                var rule = fileRules[i];
                if (sourceInfo.RawFileRuleValues.TryGetValue(i, out var ruleRawValues) &&
                    ruleRawValues.TryGetValue("ScanIntervalMs", out var scanIntervalRaw))
                {
                    if (!int.TryParse(scanIntervalRaw, out _))
                    {
                        rulesWithIssues.Add(rule.Name);
                    }
                }
            }
        }

        if (rulesWithIssues.Any())
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = string.Join(", ", rulesWithIssues)
            };
        }

        return new ValidationResult { IsValid = true };
    }

    private ValidationResult ValidateApplicationConfig(ApplicationConfig appConfig, string validationId = "", ConfigSourceInfo? sourceInfo = null)
    {
        var errorFields = new List<string>();

        // Helper function to check for invalid numeric values
        bool HasInvalidNumericValue(string fieldName, object deserializedValue)
        {
            if (sourceInfo?.RawApplicationValues?.TryGetValue(fieldName, out var rawValue) == true)
            {
                if (!int.TryParse(rawValue, out int parsedValue))
                {
                    errorFields.Add(fieldName);
                    return true;
                }
            }
            return false;
        }

        // Check for invalid numeric values first, then range validation
        if (!HasInvalidNumericValue("RetryDelayMs", appConfig.RetryDelayMs) &&
            (appConfig.RetryDelayMs < 1000 || appConfig.RetryDelayMs > 30000))
        {
            errorFields.Add("RetryDelayMs");
        }

        if (!HasInvalidNumericValue("FailureCooldownMs", appConfig.FailureCooldownMs) &&
            (appConfig.FailureCooldownMs < 0 || appConfig.FailureCooldownMs > 180000))
        {
            errorFields.Add("FailureCooldownMs");
        }

        if (!HasInvalidNumericValue("RecheckServiceMs", appConfig.RecheckServiceMs) &&
            (appConfig.RecheckServiceMs < 5000 || appConfig.RecheckServiceMs > 60000))
        {
            errorFields.Add("RecheckServiceMs");
        }

        if (!HasInvalidNumericValue("RecheckTrayMs", appConfig.RecheckTrayMs) &&
            (appConfig.RecheckTrayMs < 5000 || appConfig.RecheckTrayMs > 60000))
        {
            errorFields.Add("RecheckTrayMs");
        }

        if (!HasInvalidNumericValue("PauseDelayMs", appConfig.PauseDelayMs) &&
            (appConfig.PauseDelayMs < 0 || appConfig.PauseDelayMs > 60000))
        {
            errorFields.Add("PauseDelayMs");
        }

        if (!HasInvalidNumericValue("ServiceHeartbeatMs", appConfig.ServiceHeartbeatMs) &&
            (appConfig.ServiceHeartbeatMs < 60000 || appConfig.ServiceHeartbeatMs > 3600000))
        {
            errorFields.Add("ServiceHeartbeatMs");
        }

        if (!HasInvalidNumericValue("MemoryLimitMb", appConfig.MemoryLimitMb) &&
            (appConfig.MemoryLimitMb < 256 || appConfig.MemoryLimitMb > 1024))
        {
            errorFields.Add("MemoryLimitMb");
        }

        if (!HasInvalidNumericValue("MemoryCheckMs", appConfig.MemoryCheckMs) &&
            (appConfig.MemoryCheckMs < 30000 || appConfig.MemoryCheckMs > 300000))
        {
            errorFields.Add("MemoryCheckMs");
        }

        // Validate ServiceGrpcPort - valid port range 1024-65535, excluding system ports and Windows reserved ranges
        if (!HasInvalidNumericValue("ServiceGrpcPort", appConfig.ServiceGrpcPort) &&
            (appConfig.ServiceGrpcPort < 1024 || appConfig.ServiceGrpcPort > 65535))
        {
            errorFields.Add("ServiceGrpcPort");
        }

        // Validate TrayGrpcPort - valid port range 1024-65535, excluding system ports and Windows reserved ranges
        if (!HasInvalidNumericValue("TrayGrpcPort", appConfig.TrayGrpcPort) &&
            (appConfig.TrayGrpcPort < 1024 || appConfig.TrayGrpcPort > 65535))
        {
            errorFields.Add("TrayGrpcPort");
        }

        // Ensure ports are not the same
        if (appConfig.ServiceGrpcPort == appConfig.TrayGrpcPort)
        {
            errorFields.Add("ServiceGrpcPort/TrayGrpcPort (ports must be different)");
        }

        // Validate LogFolder - ensure it exists and is writable
        var logFolder = GetEffectiveLogFolder(appConfig.LogFolder);
        if (!ValidateLogFolder(logFolder, validationId))
        {
            errorFields.Add("LogFolder");
        }

        if (errorFields.Any())
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = string.Join(", ", errorFields)
            };
        }

        return new ValidationResult { IsValid = true };
    }


    private string GetEffectiveLogFolder(string configuredLogFolder)
    {
        if (string.IsNullOrEmpty(configuredLogFolder))
        {
            // Default to shared ProgramData location (writable by all users)
            return RJAutoMoverShared.Constants.Paths.GetSharedLogFolder();
        }

        return configuredLogFolder;
    }

    private bool ValidateLogFolder(string logFolder, string validationId)
    {
        try
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"[{validationId}] Validating log folder: {logFolder}");

            // Try to create the directory if it doesn't exist
            if (!Directory.Exists(logFolder))
            {
                Directory.CreateDirectory(logFolder);
                _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"[{validationId}] Created log directory: {logFolder}");
            }

            // Test write access by creating and deleting a test file
            var testFile = Path.Combine(logFolder, $"logtest_{Guid.NewGuid()}.tmp");
            File.WriteAllText(testFile, $"Log folder write test at {DateTime.Now}");
            File.Delete(testFile);

            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"[{validationId}] Log folder validation successful: {logFolder}");
            return true;
        }
        catch (Exception ex)
        {
            // Log to file first (if possible), then to Event Log as fallback
            try
            {
                _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"[{validationId}] Log folder validation failed for '{logFolder}': {ex.Message}");
                _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"[{validationId}] Service cannot start without a writable log folder");
            }
            catch
            {
                // File logging failed, which is expected if log folder isn't writable
            }

            // Always log critical validation failures to Windows Event Log
            LogToEventLog($"RJService log folder validation failed. Path: '{logFolder}', Error: {ex.Message}. Service cannot start without a writable log folder.", EventLogEntryType.Error);

            return false;
        }
    }

    private void LogToEventLog(string message, EventLogEntryType entryType)
    {
        try
        {
            const string sourceName = "RJAutoMoverService";
            const string logName = "Application";

            // Create the event source if it doesn't exist (requires admin rights)
            if (!EventLog.SourceExists(sourceName))
            {
                EventLog.CreateEventSource(sourceName, logName);
            }

            // Write to Event Log
            EventLog.WriteEntry(sourceName, message, entryType);
        }
        catch (Exception ex)
        {
            // If we can't write to Event Log either, there's not much we can do
            // This could happen due to permissions, but we don't want to crash the validation
            Console.WriteLine($"Failed to write to Event Log: {ex.Message}");
            Console.WriteLine($"Original message: {message}");
        }
    }

    private bool IsDirectoryAccessible(string path)
    {
        try
        {
            // Try to create a test file to check write access
            var testFile = Path.Combine(path, $"test_{Guid.NewGuid()}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private (bool IsAccessible, string ErrorDetail) CheckDirectoryAccess(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return (false, $"Directory does not exist: {path}");
            }

            // Try to enumerate files (read access)
            try
            {
                var files = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                var isAdmin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

                return (false,
                    $"ACCESS DENIED - No read permission for: {path}\n" +
                    $"  Service account: {currentUser}\n" +
                    $"  Running as admin: {isAdmin}\n" +
                    $"  SOLUTION: Either run the RJAutoMoverService as Local System, or grant '{currentUser}' read/write permissions to this folder.");
            }

            // Try to create a test file (write access)
            try
            {
                var testFile = Path.Combine(path, $".rjautomover_test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
            catch (UnauthorizedAccessException)
            {
                var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                var isAdmin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

                return (false,
                    $"ACCESS DENIED - No write permission for: {path}\n" +
                    $"  Service account: {currentUser}\n" +
                    $"  Running as admin: {isAdmin}\n" +
                    $"  SOLUTION: Either run the RJAutoMoverService as Local System, or grant '{currentUser}' read/write permissions to this folder.");
            }

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"Error accessing directory: {path}\n  Details: {ex.Message}");
        }
    }

    private void DisplayConfigurationSummary(Configuration config, string validationId, ConfigSourceInfo sourceInfo, string? validationError = null)
    {
        var activeRules = config.FileRules?.Count(r => r.IsActive) ?? 0;
        var totalRules = config.FileRules?.Count ?? 0;
        var status = string.IsNullOrEmpty(validationError) ? "VALID" : "INVALID";

        // Display ALL file rules with consistent formatting
        if (config.FileRules?.Any() == true)
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"[{validationId}] File Rules:");

            for (int ruleIndex = 0; ruleIndex < config.FileRules.Count; ruleIndex++)
            {
                var rule = config.FileRules[ruleIndex];
                var explicitKeys = sourceInfo.ExplicitFileRuleKeys.GetValueOrDefault(ruleIndex, new HashSet<string>());

                var extensions = $"[{string.Join(", ", rule.GetExtensions())}]";
                var issues = new List<string>();

                // Check for all validation issues
                if (string.IsNullOrWhiteSpace(rule.SourceFolder)) issues.Add("NO_SOURCE");
                if (string.IsNullOrWhiteSpace(rule.DestinationFolder)) issues.Add("NO_DEST");
                if (string.IsNullOrWhiteSpace(rule.Extension)) issues.Add("NO_EXT");
                if (!string.IsNullOrWhiteSpace(rule.SourceFolder) && ContainsWildcards(rule.SourceFolder)) issues.Add("SRC_WILDCARDS");
                if (!string.IsNullOrWhiteSpace(rule.DestinationFolder) && ContainsWildcards(rule.DestinationFolder)) issues.Add("DEST_WILDCARDS");
                if (!string.IsNullOrWhiteSpace(rule.Extension) && ContainsWildcards(rule.Extension)) issues.Add("EXT_WILDCARDS");
                if (!string.IsNullOrWhiteSpace(rule.SourceFolder) && !Directory.Exists(rule.SourceFolder)) issues.Add("SRC_MISSING");
                if (!string.IsNullOrWhiteSpace(rule.DestinationFolder) && !Directory.Exists(rule.DestinationFolder)) issues.Add("DEST_MISSING");
                if (!string.IsNullOrWhiteSpace(rule.SourceFolder) && !IsDirectoryAccessible(rule.SourceFolder)) issues.Add("SRC_ACCESS");
                if (!string.IsNullOrWhiteSpace(rule.DestinationFolder) && !IsDirectoryAccessible(rule.DestinationFolder)) issues.Add("DEST_ACCESS");

                // Check for invalid ScanIntervalMs value
                bool scanIntervalInvalid = false;
                string scanIntervalDisplay = rule.ScanIntervalMs.ToString() + "ms";

                if (sourceInfo.RawFileRuleValues.TryGetValue(ruleIndex, out var ruleRawValues) &&
                    ruleRawValues.TryGetValue("ScanIntervalMs", out var scanIntervalRaw))
                {
                    if (!int.TryParse(scanIntervalRaw, out int parsedValue))
                    {
                        scanIntervalInvalid = true;
                        scanIntervalDisplay = scanIntervalRaw; // Show the invalid value without 'ms' suffix
                        issues.Add($"SCAN_INVALID({scanIntervalRaw})");
                    }
                    else if (parsedValue != rule.ScanIntervalMs)
                    {
                        // This shouldn't happen with our current logic, but safety check
                        scanIntervalInvalid = true;
                        scanIntervalDisplay = scanIntervalRaw; // Show the invalid value without 'ms' suffix
                        issues.Add($"SCAN_INVALID({scanIntervalRaw})");
                    }
                }

                if (!scanIntervalInvalid && (rule.ScanIntervalMs < 5000 || rule.ScanIntervalMs > 900000))
                {
                    issues.Add("SCAN_RANGE");
                }
                if (!new[] { "skip", "overwrite" }.Contains(rule.FileExists.ToLower())) issues.Add("FILEEXISTS_INVALID");

                // Determine status and log level
                var ruleStatus = rule.IsActive ? "ACTIVE" : "INACTIVE";
                var issueText = issues.Any() ? $" [ISSUES: {string.Join(", ", issues)}]" : "";

                // Only use ERROR level for active rules with issues that would prevent processing
                var logLevel = (rule.IsActive && issues.Any()) ? RJAutoMoverShared.Models.LogLevel.ERROR :
                              (!rule.IsActive && issues.Any()) ? RJAutoMoverShared.Models.LogLevel.WARN :
                              RJAutoMoverShared.Models.LogLevel.INFO;

                _logger.Log(logLevel, $"[{validationId}]   {rule.Name} [{ruleStatus}]: {rule.SourceFolder} -> {rule.DestinationFolder} {extensions} | {scanIntervalDisplay} | {rule.FileExists}{issueText}");
            }
        }

        // Show ALL application settings with source indicators and range info
        var allSettings = new List<(string name, object value, string rawValue, bool isExplicit, bool isValid, bool isNumericInvalid, string rangeInfo)>();

        // Helper function to check if a numeric field has invalid input
        bool IsNumericFieldInvalid(string fieldName, object deserializedValue, out string rawValue, out string rangeInfo)
        {
            rawValue = "";
            rangeInfo = "";

            if (!sourceInfo.RawApplicationValues.TryGetValue(fieldName, out var tempRawValue))
                return false;

            rawValue = tempRawValue ?? "";

            // Get the valid range for this field
            var ranges = new Dictionary<string, string>
            {
                { "RetryDelayMs", "1000-30000" },
                { "FailureCooldownMs", "0-180000" },
                { "RecheckServiceMs", "5000-60000" },
                { "RecheckTrayMs", "5000-60000" },
                { "PauseDelayMs", "0-60000" },
                { "ServiceHeartbeatMs", "60000-3600000" },
                { "MemoryLimitMb", "256-1024" },
                { "MemoryCheckMs", "30000-300000" },
                { "ServiceGrpcPort", "1024-65535" },
                { "TrayGrpcPort", "1024-65535" }
            };
            rangeInfo = ranges.GetValueOrDefault(fieldName, "");

            // Check if the raw value is not a valid integer
            if (!int.TryParse(rawValue, out int parsedValue))
                return true;

            // Check if parsed value doesn't match deserialized (shouldn't happen but safety check)
            return parsedValue.ToString() != deserializedValue.ToString();
        }

        // Check each numeric field
        var retryDelayInvalid = IsNumericFieldInvalid("RetryDelayMs", config.Application.RetryDelayMs, out string retryDelayRaw, out string retryDelayRange);
        var failureCooldownInvalid = IsNumericFieldInvalid("FailureCooldownMs", config.Application.FailureCooldownMs, out string failureCooldownRaw, out string failureCooldownRange);
        var recheckServiceInvalid = IsNumericFieldInvalid("RecheckServiceMs", config.Application.RecheckServiceMs, out string recheckServiceRaw, out string recheckServiceRange);
        var recheckTrayInvalid = IsNumericFieldInvalid("RecheckTrayMs", config.Application.RecheckTrayMs, out string recheckTrayRaw, out string recheckTrayRange);
        var pauseDelayInvalid = IsNumericFieldInvalid("PauseDelayMs", config.Application.PauseDelayMs, out string pauseDelayRaw, out string pauseDelayRange);
        var serviceHeartbeatInvalid = IsNumericFieldInvalid("ServiceHeartbeatMs", config.Application.ServiceHeartbeatMs, out string serviceHeartbeatRaw, out string serviceHeartbeatRange);
        var memoryLimitInvalid = IsNumericFieldInvalid("MemoryLimitMb", config.Application.MemoryLimitMb, out string memoryLimitRaw, out string memoryLimitRange);
        var memoryCheckInvalid = IsNumericFieldInvalid("MemoryCheckMs", config.Application.MemoryCheckMs, out string memoryCheckRaw, out string memoryCheckRange);
        var serviceGrpcPortInvalid = IsNumericFieldInvalid("ServiceGrpcPort", config.Application.ServiceGrpcPort, out string serviceGrpcPortRaw, out string serviceGrpcPortRange);
        var trayGrpcPortInvalid = IsNumericFieldInvalid("TrayGrpcPort", config.Application.TrayGrpcPort, out string trayGrpcPortRaw, out string trayGrpcPortRange);

        allSettings.AddRange(new (string name, object value, string rawValue, bool isExplicit, bool isValid, bool isNumericInvalid, string rangeInfo)[]
        {
            ("ProcessingPaused", config.Application.ProcessingPaused, sourceInfo.RawApplicationValues.GetValueOrDefault("ProcessingPaused", ""), sourceInfo.ExplicitApplicationKeys.Contains("ProcessingPaused"), true, false, ""),
            ("RetryDelayMs", config.Application.RetryDelayMs, retryDelayRaw ?? "", sourceInfo.ExplicitApplicationKeys.Contains("RetryDelayMs"),
                !retryDelayInvalid && config.Application.RetryDelayMs >= 1000 && config.Application.RetryDelayMs <= 30000, retryDelayInvalid, retryDelayRange),
            ("FailureCooldownMs", config.Application.FailureCooldownMs, failureCooldownRaw ?? "", sourceInfo.ExplicitApplicationKeys.Contains("FailureCooldownMs"),
                !failureCooldownInvalid && config.Application.FailureCooldownMs >= 0 && config.Application.FailureCooldownMs <= 180000, failureCooldownInvalid, failureCooldownRange),
            ("RecheckServiceMs", config.Application.RecheckServiceMs, recheckServiceRaw ?? "", sourceInfo.ExplicitApplicationKeys.Contains("RecheckServiceMs"),
                !recheckServiceInvalid && config.Application.RecheckServiceMs >= 5000 && config.Application.RecheckServiceMs <= 60000, recheckServiceInvalid, recheckServiceRange),
            ("RecheckTrayMs", config.Application.RecheckTrayMs, recheckTrayRaw ?? "", sourceInfo.ExplicitApplicationKeys.Contains("RecheckTrayMs"),
                !recheckTrayInvalid && config.Application.RecheckTrayMs >= 5000 && config.Application.RecheckTrayMs <= 60000, recheckTrayInvalid, recheckTrayRange),
            ("PauseDelayMs", config.Application.PauseDelayMs, pauseDelayRaw ?? "", sourceInfo.ExplicitApplicationKeys.Contains("PauseDelayMs"),
                !pauseDelayInvalid && config.Application.PauseDelayMs >= 0 && config.Application.PauseDelayMs <= 60000, pauseDelayInvalid, pauseDelayRange),
            ("ServiceHeartbeatMs", config.Application.ServiceHeartbeatMs, serviceHeartbeatRaw ?? "", sourceInfo.ExplicitApplicationKeys.Contains("ServiceHeartbeatMs"),
                !serviceHeartbeatInvalid && config.Application.ServiceHeartbeatMs >= 60000 && config.Application.ServiceHeartbeatMs <= 3600000, serviceHeartbeatInvalid, serviceHeartbeatRange),
            ("MemoryLimitMb", config.Application.MemoryLimitMb, memoryLimitRaw ?? "", sourceInfo.ExplicitApplicationKeys.Contains("MemoryLimitMb"),
                !memoryLimitInvalid && config.Application.MemoryLimitMb >= 256 && config.Application.MemoryLimitMb <= 1024, memoryLimitInvalid, memoryLimitRange),
            ("MemoryCheckMs", config.Application.MemoryCheckMs, memoryCheckRaw ?? "", sourceInfo.ExplicitApplicationKeys.Contains("MemoryCheckMs"),
                !memoryCheckInvalid && config.Application.MemoryCheckMs >= 30000 && config.Application.MemoryCheckMs <= 300000, memoryCheckInvalid, memoryCheckRange),
            ("ServiceGrpcPort", config.Application.ServiceGrpcPort, serviceGrpcPortRaw ?? "", sourceInfo.ExplicitApplicationKeys.Contains("ServiceGrpcPort"),
                !serviceGrpcPortInvalid && config.Application.ServiceGrpcPort >= 1024 && config.Application.ServiceGrpcPort <= 65535 && config.Application.ServiceGrpcPort != config.Application.TrayGrpcPort, serviceGrpcPortInvalid, serviceGrpcPortRange),
            ("TrayGrpcPort", config.Application.TrayGrpcPort, trayGrpcPortRaw ?? "", sourceInfo.ExplicitApplicationKeys.Contains("TrayGrpcPort"),
                !trayGrpcPortInvalid && config.Application.TrayGrpcPort >= 1024 && config.Application.TrayGrpcPort <= 65535 && config.Application.TrayGrpcPort != config.Application.ServiceGrpcPort, trayGrpcPortInvalid, trayGrpcPortRange)
        });

        // Only show non-default settings or settings with issues
        var settingsToShow = allSettings.Where(s => s.isExplicit || !s.isValid || s.isNumericInvalid).ToList();

        if (settingsToShow.Any())
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"[{validationId}] Application Settings:");
            foreach (var setting in settingsToShow)
            {
                var sourceText = setting.isExplicit ? "[from config]" : "[default]";

                string issueText = "";
                string displayValue = setting.value?.ToString() ?? "";

                if (setting.isNumericInvalid)
                {
                    displayValue = setting.rawValue; // Show the invalid value, not the default
                    issueText = $" [* Invalid value '{setting.rawValue}' * | range: {setting.rangeInfo}]";
                }
                else if (!setting.isValid)
                {
                    issueText = $" [* Out of range * | range: {setting.rangeInfo}]";
                }

                var logLevel = (setting.isNumericInvalid || !setting.isValid) ? RJAutoMoverShared.Models.LogLevel.ERROR : RJAutoMoverShared.Models.LogLevel.INFO;

                _logger.Log(logLevel, $"[{validationId}]   {setting.name}={displayValue} {sourceText}{issueText}");
            }
        }

        // Display final status summary
        _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"[{validationId}] Config Status: {status} | Rules: {activeRules}/{totalRules} active | Processing: {(config.Application?.ProcessingPaused == true ? "PAUSED" : "ENABLED")}");
    }

    private ConfigSourceInfo ParseYamlStructure(string yamlContent, string validationId)
    {
        var sourceInfo = new ConfigSourceInfo();

        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(yamlContent));

            var rootNode = (YamlMappingNode)yaml.Documents[0].RootNode;

            // Check Application section
            if (rootNode.Children.ContainsKey(new YamlScalarNode("Application")))
            {
                var appNode = (YamlMappingNode)rootNode.Children[new YamlScalarNode("Application")];
                foreach (var kvp in appNode.Children)
                {
                    var key = ((YamlScalarNode)kvp.Key).Value;
                    var value = kvp.Value.ToString();
                    if (key != null)
                    {
                        sourceInfo.ExplicitApplicationKeys.Add(key);
                        sourceInfo.RawApplicationValues[key] = value ?? "";
                    }
                }
            }

            // Check FileRules section
            if (rootNode.Children.ContainsKey(new YamlScalarNode("FileRules")))
            {
                var fileRulesNode = (YamlSequenceNode)rootNode.Children[new YamlScalarNode("FileRules")];
                for (int i = 0; i < fileRulesNode.Children.Count; i++)
                {
                    var ruleNode = (YamlMappingNode)fileRulesNode.Children[i];
                    var ruleKeys = new HashSet<string>();
                    var ruleValues = new Dictionary<string, string>();

                    foreach (var kvp in ruleNode.Children)
                    {
                        var key = ((YamlScalarNode)kvp.Key).Value;
                        var value = kvp.Value.ToString();
                        if (key != null)
                        {
                            ruleKeys.Add(key);
                            ruleValues[key] = value ?? "";
                        }
                    }

                    sourceInfo.ExplicitFileRuleKeys[i] = ruleKeys;
                    sourceInfo.RawFileRuleValues[i] = ruleValues;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.WARN, $"[{validationId}] Failed to parse YAML structure for source tracking: {ex.Message}");
        }

        return sourceInfo;
    }

    /// <summary>
    /// Checks if a string contains wildcard characters (* or ?)
    /// </summary>
    /// <param name="input">The string to check</param>
    /// <returns>True if the string contains wildcard characters</returns>
    private static bool ContainsWildcards(string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        return input.Contains('*') || input.Contains('?');
    }

    /// <summary>
    /// Checks if the configuration file has changed since the last validation
    /// </summary>
    /// <returns>True if config file has changed or this is the first check</returns>
    public bool HasConfigChanged()
    {
        // If already in config changed error mode, stay there
        if (_configChangedErrorMode)
        {
            return true;
        }

        try
        {
            if (!File.Exists(_configPath))
                return _lastConfigHash != null; // Config file was deleted

            var currentHash = CalculateFileHash(_configPath);

            if (_lastConfigHash == null)
            {
                // First time checking - store hash but don't report as changed
                _lastConfigHash = currentHash;
                return false;
            }

            bool hasChanged = _lastConfigHash != currentHash;
            if (hasChanged)
            {
                var changeId = Guid.NewGuid().ToString()[..8];
                _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Configuration file changed externally - service entering error mode");
                _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Configuration file: {_configPath}");
                _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Previous hash: {_lastConfigHash}");
                _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Current hash:  {currentHash}");
                _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Service will stop processing files until restarted");
                _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Please restart the service to apply configuration changes");

                // Enter permanent error mode - don't update hash, don't allow recovery
                _configChangedErrorMode = true;
            }

            return hasChanged;
        }
        catch (Exception ex)
        {
            var errorId = Guid.NewGuid().ToString()[..8];
            _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"[{errorId}] Error checking config file changes: {ex.Message}");
            return false; // Don't trigger error mode due to check failure
        }
    }

    /// <summary>
    /// Calculates MD5 hash of a file for change detection purposes.
    /// Note: MD5 is used intentionally for performance (faster than SHA256) and is acceptable here
    /// since this is for file change detection only, not cryptographic security.
    /// </summary>
    /// <param name="filePath">Path to file</param>
    /// <returns>Hash string</returns>
    private static string CalculateFileHash(string filePath)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = md5.ComputeHash(stream);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Detects circular move chains (multi-hop loops) in file rules
    /// </summary>
    private ValidationResult DetectCircularChains(List<FileRule> activeRules, string validationId = "")
    {
        // Build a directed graph: folder -> list of destination folders
        var graph = new Dictionary<string, List<(string destination, string ruleName)>>(StringComparer.OrdinalIgnoreCase);
        var pathToCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // Maps normalized paths to canonical paths

        foreach (var rule in activeRules)
        {
            if (string.IsNullOrWhiteSpace(rule.SourceFolder) || string.IsNullOrWhiteSpace(rule.DestinationFolder))
                continue;

            var src = Path.GetFullPath(rule.SourceFolder);
            var dst = Path.GetFullPath(rule.DestinationFolder);

            // Try to resolve to canonical paths (handles mapped drives, UNC paths, junctions, symlinks)
            var srcCanonical = TryGetCanonicalPath(src);
            var dstCanonical = TryGetCanonicalPath(dst);

            // Track path aliases if detected
            if (!srcCanonical.Equals(src, StringComparison.OrdinalIgnoreCase))
            {
                pathToCanonical[src] = srcCanonical;
            }
            if (!dstCanonical.Equals(dst, StringComparison.OrdinalIgnoreCase))
            {
                pathToCanonical[dst] = dstCanonical;
            }

            if (!graph.ContainsKey(srcCanonical))
                graph[srcCanonical] = new List<(string, string)>();

            graph[srcCanonical].Add((dstCanonical, rule.Name));
        }

        // Log any detected path aliases
        if (pathToCanonical.Any() && !string.IsNullOrEmpty(validationId))
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.WARN, $"[{validationId}] Path aliasing detected - multiple paths may reference the same physical location:");
            foreach (var alias in pathToCanonical)
            {
                _logger.Log(RJAutoMoverShared.Models.LogLevel.WARN, $"[{validationId}]   '{alias.Key}' → '{alias.Value}' (canonical)");
            }
            _logger.Log(RJAutoMoverShared.Models.LogLevel.WARN, $"[{validationId}] If using mapped drives (e.g., P:) and UNC paths (e.g., \\\\server\\share) to the same location,");
            _logger.Log(RJAutoMoverShared.Models.LogLevel.WARN, $"[{validationId}] this may cause unexpected behavior. Consider using consistent path types across all rules.");
        }

        // Use DFS to detect cycles
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recursionStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pathStack = new List<string>();

        foreach (var node in graph.Keys)
        {
            if (!visited.Contains(node))
            {
                var cycle = DetectCycleDFS(node, graph, visited, recursionStack, pathStack);
                if (cycle != null)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Circular move chain detected: {string.Join(" → ", cycle)}"
                    };
                }
            }
        }

        return new ValidationResult { IsValid = true };
    }

    /// <summary>
    /// Attempts to resolve a path to its canonical form, handling mapped drives, UNC paths, junctions, and symlinks.
    /// This helps detect when different paths (e.g., P:\ and \\server\share) point to the same physical location.
    /// </summary>
    private string TryGetCanonicalPath(string path)
    {
        try
        {
            // First normalize the path
            var normalizedPath = Path.GetFullPath(path);

            // If it's a mapped drive, try to resolve to UNC path
            if (normalizedPath.Length >= 2 && normalizedPath[1] == ':')
            {
                var driveLetter = normalizedPath.Substring(0, 2);
                var uncPath = GetUncPathFromMappedDrive(driveLetter);

                if (!string.IsNullOrEmpty(uncPath))
                {
                    // Combine UNC root with the rest of the path
                    var remainder = normalizedPath.Length > 3 ? normalizedPath.Substring(3) : "";
                    return Path.Combine(uncPath, remainder);
                }
            }

            // Try to resolve symbolic links, junctions, or directory symlinks
            if (Directory.Exists(normalizedPath))
            {
                var dirInfo = new DirectoryInfo(normalizedPath);

                // Check if it's a reparse point (junction, symlink, etc.)
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    // Get the actual target (this works for junctions and symlinks)
                    var target = dirInfo.LinkTarget;
                    if (!string.IsNullOrEmpty(target))
                    {
                        return Path.GetFullPath(target);
                    }
                }
            }

            return normalizedPath;
        }
        catch
        {
            // If any resolution fails, return the normalized path
            return Path.GetFullPath(path);
        }
    }

    /// <summary>
    /// Attempts to resolve a mapped drive letter to its UNC path
    /// </summary>
    private string GetUncPathFromMappedDrive(string driveLetter)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "net",
                Arguments = $"use {driveLetter}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                process.WaitForExit(2000); // 2 second timeout
                var output = process.StandardOutput.ReadToEnd();

                // Parse output to find the UNC path
                // Format: "Remote name       \\server\share"
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("Remote name") || line.Contains("Remote"))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            if (part.StartsWith("\\\\"))
                            {
                                return part;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Silently fail - we'll just use the drive letter as-is
        }

        return string.Empty;
    }

    /// <summary>
    /// DFS helper for cycle detection
    /// </summary>
    private List<string>? DetectCycleDFS(
        string node,
        Dictionary<string, List<(string destination, string ruleName)>> graph,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        List<string> pathStack)
    {
        visited.Add(node);
        recursionStack.Add(node);
        pathStack.Add(node);

        if (graph.ContainsKey(node))
        {
            foreach (var (neighbor, ruleName) in graph[node])
            {
                if (!visited.Contains(neighbor))
                {
                    var cycle = DetectCycleDFS(neighbor, graph, visited, recursionStack, pathStack);
                    if (cycle != null)
                        return cycle;
                }
                else if (recursionStack.Contains(neighbor))
                {
                    // Found a cycle - build the cycle path
                    var cycleStartIndex = pathStack.IndexOf(neighbor);
                    var cyclePath = pathStack.Skip(cycleStartIndex).ToList();
                    cyclePath.Add(neighbor); // Close the loop
                    return cyclePath;
                }
            }
        }

        recursionStack.Remove(node);
        pathStack.RemoveAt(pathStack.Count - 1);
        return null;
    }

    /// <summary>
    /// Validates that a path doesn't contain reserved Windows names
    /// </summary>
    private ValidationResult ValidateNoReservedNames(string path, string fieldDescription)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new ValidationResult { IsValid = true };

        var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        try
        {
            var pathParts = path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in pathParts)
            {
                // Strip extension if present (e.g., "CON.txt" is still reserved)
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(part);
                if (string.IsNullOrWhiteSpace(nameWithoutExtension))
                    nameWithoutExtension = part;

                if (reservedNames.Contains(nameWithoutExtension))
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"{fieldDescription} contains reserved Windows name '{nameWithoutExtension}'"
                    };
                }
            }
        }
        catch
        {
            // If path parsing fails, let other validations catch it
        }

        return new ValidationResult { IsValid = true };
    }

}
