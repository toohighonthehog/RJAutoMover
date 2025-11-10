using RJAutoMoverShared.Services;
using RJAutoMoverShared.Protos;
using RJAutoMoverShared.Models;
using RJAutoMoverShared;

namespace RJAutoMoverS.Services;

/// <summary>
/// V2 of Service gRPC client using centralized ConnectionManager
/// </summary>
public class ServiceGrpcClientServiceV2 : IDisposable
{
    private readonly RJAutoMoverShared.Services.LoggingService _logger;
    private readonly ManagedTrayClient _client;
    private bool _disposed = false;
    private readonly int _trayPort;

    public bool IsConnected => _client.IsConnected;

    public ServiceGrpcClientServiceV2(RJAutoMoverShared.Services.LoggingService logger, int? trayPort = null)
    {
        _logger = logger;
        _trayPort = trayPort ?? Constants.Grpc.DefaultTrayPort;

        // Create managed client with logger wrapper that detects gRPC messages
        _client = new ManagedTrayClient(
            msg =>
            {
                // Detect gRPC messages and log them with appropriate level
                if (msg.StartsWith("[gRPC>]"))
                    _logger.Log(RJAutoMoverShared.Models.LogLevel.gRPCOut, msg.Substring(7).Trim()); // Remove [gRPC>] prefix
                else if (msg.StartsWith("[gRPC<]"))
                    _logger.Log(RJAutoMoverShared.Models.LogLevel.gRPCIn, msg.Substring(7).Trim()); // Remove [gRPC<] prefix
                else
                    _logger.Log(RJAutoMoverShared.Models.LogLevel.DEBUG, msg);
            },
            $"http://localhost:{_trayPort}");

        // Subscribe to connection state changes
        _client.ConnectionStateChanged += OnConnectionStateChanged;
    }

    private void OnConnectionStateChanged(object? sender, bool isConnected)
    {
        if (isConnected)
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, "Service gRPC client connected to tray successfully");
        }
        else
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.WARN, "Service gRPC client disconnected from tray");
        }
    }

    public async Task<bool> StartAsync()
    {
        try
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, "Connecting to tray gRPC server...");
            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, $"Attempting to connect to tray at http://localhost:{_trayPort}");

            var result = await _client.StartAsync();

            if (result)
            {
                _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, "Service gRPC client connected to tray successfully");
            }
            else
            {
                _logger.Log(RJAutoMoverShared.Models.LogLevel.WARN, "Failed to connect to tray - tray may not be running");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"Failed to start service gRPC client: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RequestFileCopyPermissionAsync(
        string fileName,
        string sourceFolder,
        string destinationFolder,
        string ruleName,
        long fileSizeBytes)
    {
        try
        {
            return await _client.RequestFileCopyPermissionAsync(
                fileName, sourceFolder, destinationFolder, ruleName, fileSizeBytes);
        }
        catch (Exception ex)
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"Error requesting file copy permission: {ex.Message}");
            return true; // Allow copy if request fails
        }
    }

    public async Task NotifyFileTransferAsync(
        string fileName,
        string ruleName,
        long fileSizeBytes,
        TransferStatus status,
        string message = "")
    {
        try
        {
            await _client.NotifyFileTransferAsync(fileName, ruleName, fileSizeBytes, status, message);
        }
        catch (Exception ex)
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"Error notifying file transfer: {ex.Message}");
        }
    }

    public async Task<bool> PerformHealthCheckAsync(long uptimeMs, long memoryUsageMB)
    {
        try
        {
            return await _client.PerformHealthCheckAsync(uptimeMs, memoryUsageMB);
        }
        catch (Exception ex)
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.WARN, $"Health check failed: {ex.Message}");
            return false;
        }
    }

    public async Task TriggerReconnectionAsync()
    {
        try
        {
            await _client.TriggerReconnectionAsync();
        }
        catch (Exception ex)
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"Error during reactive reconnection: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        try
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, "Stopping service gRPC client...");
            await _client.StopAsync();
            _logger.Log(RJAutoMoverShared.Models.LogLevel.INFO, "Service gRPC client stopped");
        }
        catch (Exception ex)
        {
            _logger.Log(RJAutoMoverShared.Models.LogLevel.ERROR, $"Error stopping service gRPC client: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _client.Dispose();
            _disposed = true;
        }
    }
}
