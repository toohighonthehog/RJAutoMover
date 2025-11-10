using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RJAutoMoverS.Config;
using RJAutoMoverS.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Builder;
using RJAutoMoverShared.Models;
using RJAutoMoverShared.Services;
using RJAutoMoverShared.Helpers;
using RJAutoMoverShared;
using YamlDotNet.Serialization;

namespace RJAutoMoverS;

public class Program
{
    public static void Main(string[] args)
    {
        // Load configuration early to get LogFolder and retention days
        string logFolder = GetLogFolderFromConfig();
        int retentionDays = ConfigurationHelper.GetLogRetentionDays();
        var logger = new RJAutoMoverShared.Services.LoggingService("RJAutoMoverService", logFolder);

        // Clean up old log files on startup
        logger.CleanupOldLogs(retentionDays);

        // Setup global exception handlers for unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            var errorId = Guid.NewGuid().ToString()[..8];

            try
            {
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, $"[{errorId}] UNHANDLED EXCEPTION - Service will terminate!");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, $"[{errorId}] Exception: {ex?.Message ?? "Unknown"}");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, $"[{errorId}] Stack: {ex?.StackTrace ?? "None"}");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, $"[{errorId}] Is Terminating: {e.IsTerminating}");

                // Force flush logs
                logger.Flush();
            }
            catch
            {
                // File logging failed - try Windows Event Log as last resort
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        System.Diagnostics.EventLog.WriteEntry("RJAutoMoverService",
                            $"FATAL [{errorId}]: Unhandled exception - {ex?.Message}\n\nStack:\n{ex?.StackTrace}",
                            System.Diagnostics.EventLogEntryType.Error);
                    }
                }
                catch { /* Event log write failed - nothing more we can do */ }
            }
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            var errorId = Guid.NewGuid().ToString()[..8];

            try
            {
                logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"[{errorId}] UNOBSERVED TASK EXCEPTION");
                logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"[{errorId}] Exception: {e.Exception.Message}");
                logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"[{errorId}] Stack: {e.Exception.StackTrace}");

                // Mark exception as observed to prevent app termination
                e.SetObserved();
            }
            catch
            {
                // File logging failed - try Windows Event Log as fallback
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        System.Diagnostics.EventLog.WriteEntry("RJAutoMoverService",
                            $"ERROR [{errorId}]: Unobserved task exception - {e.Exception.Message}",
                            System.Diagnostics.EventLogEntryType.Warning);
                    }
                }
                catch { /* Event log write failed - nothing more we can do */ }
            }
        };

        try
        {
            // Log startup information for diagnostics
            logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"RJService starting - .NET Version: {Environment.Version}");
            logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Working Directory: {Environment.CurrentDirectory}");
            logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"User Interactive: {Environment.UserInteractive}");
            logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Command Line Args: {string.Join(" ", args)}");

            // Check if running interactively
            if (!Environment.UserInteractive || args.Contains("--service"))
            {
                logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, "Starting RJService host...");
                CreateHostBuilder(args, logger, logFolder).Build().Run();
            }
            else
            {
                logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, "RJService must be run as a Windows Service, not interactively.");
                Console.WriteLine("This application must be run as a Windows Service.");
                Console.WriteLine("Please install it as a service or run with --service flag for testing.");
                Environment.Exit(1);
            }
        }
        catch (Exception ex)
        {
            // Check for port binding errors
            bool isPortConflict = ex.Message.Contains("address already in use") ||
                                  ex.Message.Contains("Failed to bind to address") ||
                                  ex.InnerException?.Message.Contains("address already in use") == true ||
                                  ex.InnerException?.Message.Contains("Failed to bind to address") == true;

            if (isPortConflict)
            {
                var servicePort = GetServiceGrpcPortFromConfig();
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, "========================================");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, "PORT CONFLICT DETECTED");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, "========================================");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, $"Failed to bind to port {servicePort} - port is already in use by another application");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, "");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, "SOLUTIONS:");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, $"  1. Check if another instance of RJService is running on port {servicePort}");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, $"  2. Use 'netstat -ano | findstr :{servicePort}' to find which process is using the port");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, "  3. Change ServiceGrpcPort in config.yaml to a different port (1024-65535)");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, "  4. Restart both RJService AND RJTray after changing ports");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, "");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, $"Error details: {ex.Message}");
                logger.Log(RJAutoMoverShared.Models.LogLevel.FATAL, "========================================");
            }
            else
            {
                logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"Fatal error during service startup: {ex.Message}");
                logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"Stack trace: {ex.StackTrace}");
            }

            // Also write to Windows Event Log for service startup failures
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var eventMessage = isPortConflict
                        ? $"RJAutoMoverService failed to start - Port {GetServiceGrpcPortFromConfig()} is already in use. Check logs for details or change ServiceGrpcPort in config.yaml."
                        : $"Service startup failed: {ex.Message}\n\nStack trace: {ex.StackTrace}";

                    System.Diagnostics.EventLog.WriteEntry("RJAutoMoverService",
                        eventMessage,
                        System.Diagnostics.EventLogEntryType.Error);
                }
            }
            catch
            {
                // Event log write failed - no other logging options available
            }

            Environment.Exit(1);
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args, LoggingService logger, string logFolder)
    {
        // Load gRPC ports from config
        int serviceGrpcPort = GetServiceGrpcPortFromConfig();
        int trayGrpcPort = GetTrayGrpcPortFromConfig();
        logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Service gRPC port from config: {serviceGrpcPort}");
        logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Tray gRPC port from config: {trayGrpcPort}");

        return Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "RJService";
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton(logger);
                services.AddSingleton<ConfigValidator>();
                services.AddSingleton(sp => new RuntimeStateService(logger));
                services.AddSingleton(sp => new ServiceGrpcClientServiceV2(logger, trayGrpcPort));
                services.AddSingleton<FileProcessorService>();
                services.AddSingleton(sp => new GrpcServerServiceSimplified(logger, logFolder));
                services.AddHostedService<ServiceWorker>();

                // Configure faster shutdown timeout
                services.Configure<HostOptions>(options =>
                {
                    options.ShutdownTimeout = TimeSpan.FromSeconds(5);
                });

                services.AddGrpc(options =>
                {
                    options.MaxReceiveMessageSize = Constants.Grpc.MaxMessageSizeBytes; // 16 MB
                    options.MaxSendMessageSize = Constants.Grpc.MaxMessageSizeBytes; // 16 MB
                });
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureKestrel(options =>
                {
                    // Setup gRPC endpoint on localhost with configured port
                    options.ListenLocalhost(serviceGrpcPort, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http2;
                    });

                    // Configure faster shutdown
                    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(5);
                    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
                });

                // Add error handling for startup failures (including port conflicts)
                webBuilder.CaptureStartupErrors(true);
                webBuilder.UseSetting(WebHostDefaults.DetailedErrorsKey, "true");

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGrpcService<GrpcServerServiceSimplified>();
                    });
                });
            });
    }

    private static string GetLogFolderFromConfig()
    {
        var logFolder = ConfigurationHelper.GetLogFolder();

        // If the path is relative, resolve it relative to the application directory
        if (!Path.IsPathRooted(logFolder))
        {
            logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logFolder);
        }

        logFolder = Path.GetFullPath(logFolder);

        // Check if writable (service should have access, but fallback to ProgramData if not)
        if (!IsDirectoryWritable(logFolder))
        {
            // Use shared ProgramData folder (C:\ProgramData\RJAutoMover\Logs)
            // Consistent location for both service and tray logs
            var sharedLogFolder = RJAutoMoverShared.Constants.Paths.GetSharedLogFolder();

            // Service runs with system privileges, so this should always work
            Directory.CreateDirectory(sharedLogFolder);
            return sharedLogFolder;
        }

        return logFolder;
    }

    private static bool IsDirectoryWritable(string dirPath)
    {
        try
        {
            // Try to create the directory if it doesn't exist
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            // Test write access with a temporary file
            var testFile = Path.Combine(dirPath, $"servicelogtest_{Guid.NewGuid()}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int GetServiceGrpcPortFromConfig()
    {
        return ConfigurationHelper.GetServiceGrpcPort();
    }

    private static int GetTrayGrpcPortFromConfig()
    {
        return ConfigurationHelper.GetTrayGrpcPort();
    }
}
