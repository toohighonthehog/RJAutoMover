namespace RJAutoMoverShared;

/// <summary>
/// Application-wide constants for RJAutoMover
/// </summary>
public static class Constants
{
    /// <summary>
    /// gRPC communication defaults
    /// </summary>
    public static class Grpc
    {
        /// <summary>
        /// Default port for the RJAutoMoverService gRPC server
        /// </summary>
        public const int DefaultServicePort = 60051;

        /// <summary>
        /// Default port for the RJAutoMoverTray gRPC server
        /// </summary>
        public const int DefaultTrayPort = 60052;

        /// <summary>
        /// Maximum message size for gRPC communication (16 MB)
        /// </summary>
        public const int MaxMessageSizeBytes = 16 * 1024 * 1024;

        /// <summary>
        /// Default localhost endpoint
        /// </summary>
        public const string LocalhostBase = "http://localhost";
    }

    /// <summary>
    /// Default timeout and retry values
    /// </summary>
    public static class Timeouts
    {
        /// <summary>
        /// Default retry delay in milliseconds
        /// </summary>
        public const int DefaultRetryDelayMs = 5000;

        /// <summary>
        /// Connection timeout in milliseconds
        /// </summary>
        public const int ConnectionTimeoutMs = 10000;

        /// <summary>
        /// Long operation timeout in milliseconds (30 seconds)
        /// </summary>
        public const int LongOperationTimeoutMs = 30000;

        /// <summary>
        /// Maximum retry delay in milliseconds (30 seconds)
        /// </summary>
        public const int MaxRetryDelayMs = 30000;

        /// <summary>
        /// Process graceful exit timeout (5 seconds)
        /// </summary>
        public const int ProcessExitTimeoutMs = 5000;

        /// <summary>
        /// Default reconnection wait time (2 seconds)
        /// </summary>
        public const int ReconnectionWaitMs = 2000;
    }

    /// <summary>
    /// Default application configuration values
    /// </summary>
    public static class Defaults
    {
        /// <summary>
        /// Default scan interval in milliseconds (30 seconds)
        /// </summary>
        public const int ScanIntervalMs = 30000;

        /// <summary>
        /// Default failure cooldown in milliseconds (3 minutes)
        /// </summary>
        public const int FailureCooldownMs = 180000;

        /// <summary>
        /// Default recheck service interval in milliseconds (30 seconds)
        /// </summary>
        public const int RecheckServiceMs = 30000;

        /// <summary>
        /// Default recheck tray interval in milliseconds (30 seconds)
        /// </summary>
        public const int RecheckTrayMs = 30000;

        /// <summary>
        /// Default service heartbeat interval in milliseconds (5 minutes)
        /// </summary>
        public const int ServiceHeartbeatMs = 300000;

        /// <summary>
        /// Default memory limit in MB
        /// </summary>
        public const int MemoryLimitMb = 512;

        /// <summary>
        /// Default memory check interval in milliseconds (1 minute)
        /// </summary>
        public const int MemoryCheckMs = 60000;
    }

    /// <summary>
    /// Validation limits for configuration values
    /// </summary>
    public static class Limits
    {
        /// <summary>
        /// Minimum scan interval in milliseconds (5 seconds)
        /// </summary>
        public const int MinScanIntervalMs = 5000;

        /// <summary>
        /// Maximum scan interval in milliseconds (15 minutes)
        /// </summary>
        public const int MaxScanIntervalMs = 900000;

        /// <summary>
        /// Minimum retry delay in milliseconds (1 second)
        /// </summary>
        public const int MinRetryDelayMs = 1000;

        /// <summary>
        /// Maximum retry delay in milliseconds (30 seconds)
        /// </summary>
        public const int MaxRetryDelayMs = 30000;

        /// <summary>
        /// Maximum failure cooldown in milliseconds (3 minutes)
        /// </summary>
        public const int MaxFailureCooldownMs = 180000;

        /// <summary>
        /// Minimum recheck interval in milliseconds (5 seconds)
        /// </summary>
        public const int MinRecheckMs = 5000;

        /// <summary>
        /// Maximum recheck interval in milliseconds (1 minute)
        /// </summary>
        public const int MaxRecheckMs = 60000;

        /// <summary>
        /// Minimum service heartbeat in milliseconds (1 minute)
        /// </summary>
        public const int MinServiceHeartbeatMs = 60000;

        /// <summary>
        /// Maximum service heartbeat in milliseconds (1 hour)
        /// </summary>
        public const int MaxServiceHeartbeatMs = 3600000;

        /// <summary>
        /// Minimum memory check interval in milliseconds (30 seconds)
        /// </summary>
        public const int MinMemoryCheckMs = 30000;

        /// <summary>
        /// Maximum memory check interval in milliseconds (5 minutes)
        /// </summary>
        public const int MaxMemoryCheckMs = 300000;
    }

    /// <summary>
    /// Default folder paths
    /// </summary>
    public static class Paths
    {
        /// <summary>
        /// Default log folder name (relative)
        /// </summary>
        public const string DefaultLogFolder = "Logs";

        /// <summary>
        /// Default data folder name (relative)
        /// </summary>
        public const string DefaultDataFolder = "Data";

        /// <summary>
        /// Application folder name in ProgramData
        /// </summary>
        public const string ProgramDataAppFolder = "RJAutoMover";

        /// <summary>
        /// Configuration file name
        /// </summary>
        public const string ConfigFileName = "config.yaml";

        /// <summary>
        /// Gets the shared log folder path in ProgramData
        /// Returns: C:\ProgramData\RJAutoMover\Logs
        /// </summary>
        public static string GetSharedLogFolder()
        {
            return System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
                ProgramDataAppFolder,
                DefaultLogFolder);
        }

        /// <summary>
        /// Gets the shared data folder path in ProgramData
        /// Returns: C:\ProgramData\RJAutoMover\Data
        /// </summary>
        public static string GetSharedDataFolder()
        {
            return System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
                ProgramDataAppFolder,
                DefaultDataFolder);
        }

        /// <summary>
        /// Gets the config file path with priority: Program Files first, then ProgramData fallback
        /// Priority:
        ///   1. C:\Program Files\RJAutoMover\config.yaml (or wherever app is installed)
        ///   2. C:\ProgramData\RJAutoMover\config.yaml
        /// </summary>
        /// <returns>The path to the config file (may or may not exist)</returns>
        public static string GetConfigPath()
        {
            // Check Program Files first (application installation directory)
            var programFilesPath = System.IO.Path.Combine(
                System.AppDomain.CurrentDomain.BaseDirectory,
                ConfigFileName);

            if (System.IO.File.Exists(programFilesPath))
            {
                return programFilesPath;
            }

            // Fallback to ProgramData location
            return System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
                ProgramDataAppFolder,
                ConfigFileName);
        }
    }
}
