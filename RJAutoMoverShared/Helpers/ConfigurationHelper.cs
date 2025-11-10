using RJAutoMoverShared.Models;
using YamlDotNet.Serialization;

namespace RJAutoMoverShared.Helpers;

/// <summary>
/// Centralized helper for reading configuration from config.yaml
/// </summary>
public static class ConfigurationHelper
{
    /// <summary>
    /// Loads the configuration from config.yaml
    /// Checks Program Files first, then ProgramData as fallback
    /// </summary>
    /// <param name="configPath">Optional path to config file. If null, uses priority lookup (Program Files → ProgramData).</param>
    /// <returns>Configuration object, or null if unable to load</returns>
    public static Configuration? LoadConfiguration(string? configPath = null)
    {
        try
        {
            // Use priority-based path resolution: Program Files first, then ProgramData
            configPath ??= Constants.Paths.GetConfigPath();

            if (!File.Exists(configPath))
            {
                return null;
            }

            var yamlContent = File.ReadAllText(configPath);
            var deserializer = new DeserializerBuilder().Build();
            return deserializer.Deserialize<Configuration>(yamlContent);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the log folder from configuration, with fallback to default
    /// </summary>
    /// <param name="config">Optional pre-loaded configuration. If null, will load from file.</param>
    /// <returns>Log folder path</returns>
    public static string GetLogFolder(Configuration? config = null)
    {
        try
        {
            config ??= LoadConfiguration();

            if (!string.IsNullOrWhiteSpace(config?.Application?.LogFolder))
            {
                return config.Application.LogFolder;
            }
        }
        catch
        {
            // Fall through to default
        }

        // Default to shared ProgramData location (writable by all users)
        return Constants.Paths.GetSharedLogFolder();
    }

    /// <summary>
    /// Gets the Service gRPC port from configuration, with fallback to default
    /// </summary>
    /// <param name="config">Optional pre-loaded configuration. If null, will load from file.</param>
    /// <returns>Service gRPC port</returns>
    public static int GetServiceGrpcPort(Configuration? config = null)
    {
        try
        {
            config ??= LoadConfiguration();

            if (config?.Application?.ServiceGrpcPort > 0)
            {
                return config.Application.ServiceGrpcPort;
            }
        }
        catch
        {
            // Fall through to default
        }

        return Constants.Grpc.DefaultServicePort;
    }

    /// <summary>
    /// Gets the Tray gRPC port from configuration, with fallback to default
    /// </summary>
    /// <param name="config">Optional pre-loaded configuration. If null, will load from file.</param>
    /// <returns>Tray gRPC port</returns>
    public static int GetTrayGrpcPort(Configuration? config = null)
    {
        try
        {
            config ??= LoadConfiguration();

            if (config?.Application?.TrayGrpcPort > 0)
            {
                return config.Application.TrayGrpcPort;
            }
        }
        catch
        {
            // Fall through to default
        }

        return Constants.Grpc.DefaultTrayPort;
    }

    /// <summary>
    /// Gets memory configuration from config file
    /// </summary>
    /// <param name="config">Optional pre-loaded configuration. If null, will load from file.</param>
    /// <returns>Tuple of (memoryLimitMb, memoryCheckMs)</returns>
    public static (int memoryLimitMb, int memoryCheckMs) GetMemoryConfig(Configuration? config = null)
    {
        try
        {
            config ??= LoadConfiguration();

            if (config?.Application != null)
            {
                return (config.Application.MemoryLimitMb, config.Application.MemoryCheckMs);
            }
        }
        catch
        {
            // Fall through to defaults
        }

        return (Constants.Defaults.MemoryLimitMb, Constants.Defaults.MemoryCheckMs); // Default values
    }

    /// <summary>
    /// Gets gRPC port configuration from config file
    /// </summary>
    /// <param name="config">Optional pre-loaded configuration. If null, will load from file.</param>
    /// <returns>Tuple of (servicePort, trayPort)</returns>
    public static (int servicePort, int trayPort) GetGrpcPorts(Configuration? config = null)
    {
        config ??= LoadConfiguration();
        return (GetServiceGrpcPort(config), GetTrayGrpcPort(config));
    }

    /// <summary>
    /// Gets the log retention days from configuration, with fallback to default
    /// </summary>
    /// <param name="config">Optional pre-loaded configuration. If null, will load from file.</param>
    /// <returns>Number of days to retain log files</returns>
    public static int GetLogRetentionDays(Configuration? config = null)
    {
        try
        {
            config ??= LoadConfiguration();

            if (config?.Application?.LogRetentionDays > 0)
            {
                return config.Application.LogRetentionDays;
            }
        }
        catch
        {
            // Fall through to default
        }

        return 7; // Default to 7 days
    }
}
