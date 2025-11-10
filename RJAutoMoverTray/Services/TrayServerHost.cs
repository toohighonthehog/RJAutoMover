using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RJAutoMoverShared.Models;
using RJAutoMoverShared.Services;
using RJAutoMoverShared;
using LogLevel = RJAutoMoverShared.Models.LogLevel;

namespace RJAutoMoverTray.Services;

public class TrayServerHost
{
    private readonly RJAutoMoverShared.Services.LoggingService _logger;
    private readonly TrayGrpcServerService _grpcService;
    private IHost? _host;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly int _port;

    public TrayGrpcServerService GrpcService => _grpcService;

    public TrayServerHost(RJAutoMoverShared.Services.LoggingService logger, int? port = null)
    {
        _logger = logger;
        _port = port ?? Constants.Grpc.DefaultTrayPort;
        _grpcService = new TrayGrpcServerService(_logger);
    }

    public async Task StartAsync()
    {
        try
        {
            _logger.Log(LogLevel.INFO, $"Starting Tray gRPC server on port {_port}...");

            _host = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options =>
                    {
                        // Setup gRPC endpoint on localhost with configured port
                        options.ListenLocalhost(_port, listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http2;
                        });

                        // Configure faster shutdown
                        options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(5);
                        options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
                    });

                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGrpcService<TrayGrpcServerService>();
                        });
                    });

                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddSingleton(_logger);
                        services.AddSingleton(_grpcService);
                        services.AddGrpc(options =>
                        {
                            options.MaxReceiveMessageSize = Constants.Grpc.MaxMessageSizeBytes; // 16 MB
                            options.MaxSendMessageSize = Constants.Grpc.MaxMessageSizeBytes; // 16 MB
                        });

                        // Configure faster shutdown timeout
                        services.Configure<HostOptions>(options =>
                        {
                            options.ShutdownTimeout = TimeSpan.FromSeconds(5);
                        });
                    });

                    // Disable default logging to avoid conflicts
                    webBuilder.ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                    });
                })
                .Build();

            await _host.StartAsync(_cancellationTokenSource.Token);
            _logger.Log(LogLevel.INFO, $"Tray gRPC server started successfully on http://localhost:{_port}");
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
                _logger.Log(LogLevel.FATAL, "========================================");
                _logger.Log(LogLevel.FATAL, "PORT CONFLICT DETECTED");
                _logger.Log(LogLevel.FATAL, "========================================");
                _logger.Log(LogLevel.FATAL, $"Failed to bind to port {_port} - port is already in use by another application");
                _logger.Log(LogLevel.FATAL, "");
                _logger.Log(LogLevel.FATAL, "SOLUTIONS:");
                _logger.Log(LogLevel.FATAL, $"  1. Check if another instance of RJTray is running on port {_port}");
                _logger.Log(LogLevel.FATAL, $"  2. Use 'netstat -ano | findstr :{_port}' to find which process is using the port");
                _logger.Log(LogLevel.FATAL, "  3. Change TrayGrpcPort in config.yaml to a different port (1024-65535)");
                _logger.Log(LogLevel.FATAL, "  4. Restart both RJService AND RJTray after changing ports");
                _logger.Log(LogLevel.FATAL, "");
                _logger.Log(LogLevel.FATAL, "NOTE: Windows may reserve port ranges (e.g., 50052-50151 on some systems)");
                _logger.Log(LogLevel.FATAL, "      Use 'netsh interface ipv4 show excludedportrange protocol=tcp' to check");
                _logger.Log(LogLevel.FATAL, "");
                _logger.Log(LogLevel.FATAL, $"Error details: {ex.Message}");
                _logger.Log(LogLevel.FATAL, "========================================");
            }
            else
            {
                _logger.Log(LogLevel.ERROR, $"Failed to start tray gRPC server: {ex.Message}");
            }

            throw;
        }
    }

    public async Task StopAsync()
    {
        try
        {
            if (_host != null)
            {
                _logger.Log(LogLevel.INFO, "Stopping tray gRPC server...");
                _cancellationTokenSource.Cancel();
                await _host.StopAsync();
                _host.Dispose();
                _host = null;
                _logger.Log(LogLevel.INFO, "Tray gRPC server stopped");
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Error stopping tray gRPC server: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Dispose();
        _host?.Dispose();
    }
}
