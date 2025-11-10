using RJAutoMoverShared.Services;
using RJAutoMoverShared.Protos;
using RJAutoMoverShared.Models;
using RJAutoMoverShared;
using LogLevel = RJAutoMoverShared.Models.LogLevel;

namespace RJAutoMoverTray.Services;

/// <summary>
/// V2 of Tray gRPC client using centralized ConnectionManager
/// </summary>
public class GrpcClientServiceV2 : IDisposable
{
    private readonly RJAutoMoverShared.Services.LoggingService _logger;
    private readonly ManagedServiceClient _client;
    private readonly int _servicePort;

    public event EventHandler<string>? StatusUpdated;
    public event EventHandler<string>? IconUpdated;
    public event EventHandler<List<string>>? RecentsUpdated;
    public event EventHandler<int>? FileCountUpdated;
    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<FileTransferNotification>? FileTransferNotified;
    public event EventHandler<string>? LogFolderReceived;
    public event EventHandler<string>? RegistrationRejected;
    public event EventHandler<DateTime>? ServiceStartTimeReceived;

    public bool IsConnected => _client.IsConnected;
    public DateTime? ServiceStartTime => _client.ServiceStartTime;

    public GrpcClientServiceV2(RJAutoMoverShared.Services.LoggingService logger, int? servicePort = null)
    {
        _logger = logger;
        _servicePort = servicePort ?? Constants.Grpc.DefaultServicePort;

        // Create managed client with logger wrapper that detects gRPC messages
        _client = new ManagedServiceClient(
            msg =>
            {
                // Detect gRPC messages and log them with appropriate level
                if (msg.StartsWith("[gRPC>]"))
                    _logger.Log(LogLevel.gRPCOut, msg.Substring(7).Trim()); // Remove [gRPC>] prefix
                else if (msg.StartsWith("[gRPC<]"))
                    _logger.Log(LogLevel.gRPCIn, msg.Substring(7).Trim()); // Remove [gRPC<] prefix
                else
                    _logger.Log(LogLevel.DEBUG, msg);
            },
            $"http://localhost:{_servicePort}");

        // Wire up events
        _client.StatusUpdated += (s, status) => StatusUpdated?.Invoke(this, status);
        _client.IconUpdated += (s, icon) => IconUpdated?.Invoke(this, icon);
        _client.RecentsUpdated += (s, recents) => RecentsUpdated?.Invoke(this, recents);
        _client.FileCountUpdated += (s, count) => FileCountUpdated?.Invoke(this, count);
        _client.FileTransferNotified += (s, notification) => FileTransferNotified?.Invoke(this, notification);
        _client.LogFolderReceived += (s, logFolder) => LogFolderReceived?.Invoke(this, logFolder);
        _client.RegistrationRejected += (s, conflictingUser) => RegistrationRejected?.Invoke(this, conflictingUser);
        _client.ServiceStartTimeReceived += (s, startTime) => ServiceStartTimeReceived?.Invoke(this, startTime);
        _client.ConnectionStateChanged += (s, isConnected) =>
        {
            if (isConnected)
                _logger.Log(LogLevel.INFO, "Tray gRPC client connected to service successfully");
            else
                _logger.Log(LogLevel.WARN, "Tray gRPC client disconnected from service");

            ConnectionStateChanged?.Invoke(this, isConnected);
        };
    }

    public async Task StartAsync()
    {
        _logger.Log(LogLevel.INFO, "Starting tray gRPC client...");
        _logger.Log(LogLevel.INFO, $"Attempting to connect to RJService at http://localhost:{_servicePort}");

        var result = await _client.StartAsync();

        if (result)
        {
            _logger.Log(LogLevel.INFO, "Tray gRPC client connected to service successfully");
        }
        else
        {
            _logger.Log(LogLevel.WARN, "Failed to connect to service - service may not be running");
        }
    }

    public async Task StopAsync()
    {
        _logger.Log(LogLevel.INFO, "Stopping tray gRPC client...");
        await _client.StopAsync();
        _logger.Log(LogLevel.INFO, "Tray gRPC client stopped");
    }

    public async Task TriggerReconnectionAsync()
    {
        await _client.TriggerReconnectionAsync();
    }

    public async Task<bool> ToggleProcessingAsync(bool pause)
    {
        try
        {
            return await _client.ToggleProcessingAsync(pause);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Failed to toggle processing: {ex.Message}");
            return false;
        }
    }

    public async Task<(bool success, bool isPaused)> GetServiceStatusAsync()
    {
        try
        {
            return await _client.GetServiceStatusAsync();
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Failed to get service status: {ex.Message}");
            return (false, false);
        }
    }

    public async Task<bool> ClearRecentActivitiesAsync()
    {
        try
        {
            return await _client.ClearRecentActivitiesAsync();
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Failed to clear recent activities: {ex.Message}");
            return false;
        }
    }

    public async Task<ServiceSystemInfoResponse?> GetServiceSystemInfoAsync()
    {
        try
        {
            return await _client.GetServiceSystemInfoAsync();
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Failed to get service system info: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
