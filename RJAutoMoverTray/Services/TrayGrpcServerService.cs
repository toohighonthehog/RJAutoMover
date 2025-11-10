using Grpc.Core;
using RJAutoMoverShared.Protos;
using System.Windows;
using RJAutoMoverShared.Models;
using RJAutoMoverShared.Services;
using LogLevel = RJAutoMoverShared.Models.LogLevel;

namespace RJAutoMoverTray.Services;

public class TrayGrpcServerService : ServiceClient.ServiceClientBase
{
    private readonly RJAutoMoverShared.Services.LoggingService _logger;
    private DateTime _lastServiceContact = DateTime.MinValue;
    private TrayIconService? _trayIconService;

    public event EventHandler? ServiceContactDetected;

    public TrayGrpcServerService(RJAutoMoverShared.Services.LoggingService logger)
    {
        _logger = logger;
    }

    public void SetTrayIconService(TrayIconService trayIconService)
    {
        _trayIconService = trayIconService;
    }

    private void OnServiceContact()
    {
        var now = DateTime.Now;
        // Only fire event if it's been more than 5 seconds since last contact
        // This prevents rapid-fire events during normal operation
        if (now - _lastServiceContact > TimeSpan.FromSeconds(5))
        {
            _lastServiceContact = now;
            _logger.Log(LogLevel.DEBUG, "Service contact detected - triggering reactive reconnection");
            ServiceContactDetected?.Invoke(this, EventArgs.Empty);
        }
    }

    public override Task<FileCopyResponse> RequestFileCopyPermission(FileCopyRequest request, ServerCallContext context)
    {
        try
        {
            OnServiceContact(); // Trigger reactive reconnection
            _logger.Log(LogLevel.gRPCIn, $"RequestFileCopyPermission: {request.FileName} from {request.SourceFolder} to {request.DestinationFolder}");

            // CRITICAL SAFETY CHECK: Verify tray state before allowing file operations
            if (_trayIconService == null)
            {
                _logger.Log(LogLevel.ERROR, "SAFETY VIOLATION PREVENTED: Tray icon service not available - denying file copy");
                return Task.FromResult(new FileCopyResponse
                {
                    AllowCopy = false,
                    Message = "Tray not ready - file copy denied for safety"
                });
            }

            var trayState = _trayIconService.GetCurrentState();

            // Deny if tray is in error state
            if (trayState.IsInErrorState)
            {
                _logger.Log(LogLevel.ERROR, $"SAFETY VIOLATION PREVENTED: Tray in error state ('{trayState.Status}') - denying file copy for {request.FileName}");
                return Task.FromResult(new FileCopyResponse
                {
                    AllowCopy = false,
                    Message = $"Tray in error state - file copy denied for safety"
                });
            }

            // Deny if tray is paused
            if (trayState.IsPaused)
            {
                _logger.Log(LogLevel.WARN, $"Tray paused - denying file copy for {request.FileName}");
                return Task.FromResult(new FileCopyResponse
                {
                    AllowCopy = false,
                    Message = "Processing paused - file copy denied"
                });
            }

            // All safety checks passed - allow the copy
            var response = new FileCopyResponse
            {
                AllowCopy = true,
                Message = "File copy approved by tray"
            };

            _logger.Log(LogLevel.gRPCOut, $"RequestFileCopyPermission response: allowCopy={response.AllowCopy}");
            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Error in RequestFileCopyPermission: {ex.Message}");
            return Task.FromResult(new FileCopyResponse
            {
                AllowCopy = false,
                Message = $"Error processing request: {ex.Message}"
            });
        }
    }

    public override Task<Empty> NotifyFileTransfer(FileTransferNotification request, ServerCallContext context)
    {
        try
        {
            OnServiceContact(); // Trigger reactive reconnection
            _logger.Log(LogLevel.gRPCIn, $"NotifyFileTransfer: {request.FileName} status={request.Status}");

            // File transfer notifications are logged but no popup notifications
            // (popups were too annoying for frequent file operations)

            return Task.FromResult(new Empty());
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Error in NotifyFileTransfer: {ex.Message}");
            return Task.FromResult(new Empty());
        }
    }

    public override Task<HealthCheckResponse> HealthCheck(HealthCheckRequest request, ServerCallContext context)
    {
        try
        {
            OnServiceContact(); // Trigger reactive reconnection
            _logger.Log(LogLevel.gRPCIn, $"HealthCheck from service: uptime={request.UptimeMs}ms, memory={request.MemoryUsageMB}MB");

            // Tray is healthy if it can respond
            var response = new HealthCheckResponse
            {
                IsHealthy = true,
                TrayVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown",
                ResponseTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _logger.Log(LogLevel.gRPCOut, $"HealthCheck response: healthy={response.IsHealthy}");
            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Error in HealthCheck: {ex.Message}");
            return Task.FromResult(new HealthCheckResponse
            {
                IsHealthy = false,
                TrayVersion = "Error",
                ResponseTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
    }
}
