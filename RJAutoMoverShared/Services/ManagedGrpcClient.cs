using Grpc.Core;
using RJAutoMoverShared.Protos;

namespace RJAutoMoverShared.Services;

/// <summary>
/// Base class for managed gRPC clients that use ConnectionManager
/// </summary>
public abstract class ManagedGrpcClient<TClient> : IDisposable where TClient : ClientBase<TClient>
{
    protected readonly ConnectionManager ConnectionManager;
    protected readonly Action<string> Logger;
    private bool _disposed = false;

    protected TClient? Client => ConnectionManager.CreateClient<TClient>();
    public bool IsConnected => ConnectionManager.IsConnected;
    public ConnectionState ConnectionState => ConnectionManager.State;

    protected ManagedGrpcClient(ConnectionManager connectionManager, Action<string> logger)
    {
        ConnectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to connection state changes
        ConnectionManager.ConnectionStateChanged += OnConnectionStateChanged;
    }

    public async Task<bool> StartAsync()
    {
        Logger("Starting managed gRPC client...");
        return await ConnectionManager.StartAsync();
    }

    public async Task StopAsync()
    {
        Logger("Stopping managed gRPC client...");
        await ConnectionManager.StopAsync();
    }

    public async Task TriggerReconnectionAsync()
    {
        await ConnectionManager.TriggerReactiveReconnectionAsync();
    }

    protected virtual void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        Logger($"Connection state changed: {e.PreviousState} -> {e.CurrentState} ({e.Reason})");
    }

    /// <summary>
    /// Executes a gRPC call with automatic connection validation
    /// </summary>
    protected async Task<TResponse> ExecuteCallAsync<TResponse>(
        Func<TClient, Task<TResponse>> call,
        TResponse defaultValue,
        string operationName)
    {
        try
        {
            var client = Client;
            if (client == null || !ConnectionManager.IsConnected)
            {
                Logger($"{operationName}: Not connected to remote endpoint");
                return defaultValue;
            }

            return await call(client);
        }
        catch (RpcException ex)
        {
            Logger($"{operationName} RPC error: {ex.Status.StatusCode} - {ex.Status.Detail}");

            // Trigger reconnection on connection failures
            if (ex.StatusCode == StatusCode.Unavailable || ex.StatusCode == StatusCode.Cancelled)
            {
                _ = ConnectionManager.ConnectAsync();
            }

            return defaultValue;
        }
        catch (Exception ex)
        {
            Logger($"{operationName} error: {ex.Message}");
            return defaultValue;
        }
    }

    /// <summary>
    /// Executes a gRPC call that returns void
    /// </summary>
    protected async Task<bool> ExecuteCallAsync(
        Func<TClient, Task> call,
        string operationName)
    {
        try
        {
            var client = Client;
            if (client == null || !ConnectionManager.IsConnected)
            {
                Logger($"{operationName}: Not connected to remote endpoint");
                return false;
            }

            await call(client);
            return true;
        }
        catch (RpcException ex)
        {
            Logger($"{operationName} RPC error: {ex.Status.StatusCode} - {ex.Status.Detail}");

            // Trigger reconnection on connection failures
            if (ex.StatusCode == StatusCode.Unavailable || ex.StatusCode == StatusCode.Cancelled)
            {
                _ = ConnectionManager.ConnectAsync();
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger($"{operationName} error: {ex.Message}");
            return false;
        }
    }

    public virtual void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ConnectionManager.ConnectionStateChanged -= OnConnectionStateChanged;
        ConnectionManager.Dispose();
    }
}

/// <summary>
/// Managed client for Service -> Tray communication
/// </summary>
public class ManagedTrayClient : ManagedGrpcClient<ServiceClient.ServiceClientClient>
{
    public event EventHandler<bool>? ConnectionStateChanged;

    public ManagedTrayClient(Action<string> logger, string? endpoint = null)
        : base(new ConnectionManager(endpoint ?? $"{Constants.Grpc.LocalhostBase}:{Constants.Grpc.DefaultTrayPort}", logger), logger)
    {
    }

    protected override void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        base.OnConnectionStateChanged(sender, e);
        ConnectionStateChanged?.Invoke(this, e.CurrentState == RJAutoMoverShared.Services.ConnectionState.Connected);
    }

    public async Task<bool> RequestFileCopyPermissionAsync(
        string fileName,
        string sourceFolder,
        string destinationFolder,
        string ruleName,
        long fileSizeBytes)
    {
        Logger($"[gRPC>] RequestFileCopyPermission: {fileName} ({fileSizeBytes} bytes)");

        var response = await ExecuteCallAsync(
            client => client.RequestFileCopyPermissionAsync(new FileCopyRequest
            {
                FileName = fileName,
                SourceFolder = sourceFolder,
                DestinationFolder = destinationFolder,
                RuleName = ruleName,
                FileSizeBytes = fileSizeBytes
            }, deadline: DateTime.UtcNow.AddSeconds(10)).ResponseAsync,
            new FileCopyResponse { AllowCopy = false, Message = "Tray unavailable - safety check failed" }, // CRITICAL: Default to deny if tray unavailable
            "RequestFileCopyPermission");

        Logger($"[gRPC<] RequestFileCopyPermission response: allowCopy={response.AllowCopy}, message='{response.Message}'");
        return response.AllowCopy;
    }

    public async Task<bool> PerformHealthCheckAsync(long uptimeMs, long memoryUsageMB)
    {
        Logger($"[gRPC>] HealthCheck: uptime={uptimeMs}ms, memory={memoryUsageMB}MB");

        var response = await ExecuteCallAsync(
            client => client.HealthCheckAsync(new HealthCheckRequest
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ServiceVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown",
                UptimeMs = uptimeMs,
                MemoryUsageMB = memoryUsageMB
            }, deadline: DateTime.UtcNow.AddSeconds(10)).ResponseAsync,
            new HealthCheckResponse { IsHealthy = false },
            "HealthCheck");

        Logger($"[gRPC<] HealthCheck response: healthy={response.IsHealthy}");
        return response.IsHealthy;
    }

    public async Task NotifyFileTransferAsync(
        string fileName,
        string ruleName,
        long fileSizeBytes,
        TransferStatus status,
        string message = "")
    {
        Logger($"[gRPC>] NotifyFileTransfer: {fileName} status={status}");

        await ExecuteCallAsync(
            async client =>
            {
                await client.NotifyFileTransferAsync(new FileTransferNotification
                {
                    FileName = fileName,
                    RuleName = ruleName,
                    FileSizeBytes = fileSizeBytes,
                    Status = status,
                    Message = message,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }, deadline: DateTime.UtcNow.AddSeconds(5)).ResponseAsync;
            },
            "NotifyFileTransfer");
    }
}

/// <summary>
/// Managed client for Tray -> Service communication
/// </summary>
public class ManagedServiceClient : ManagedGrpcClient<TrayService.TrayServiceClient>
{
    private readonly string _trayId = Guid.NewGuid().ToString();
    private CancellationTokenSource? _streamCancellation;
    private System.Timers.Timer? _heartbeatTimer;
    private bool _isRegistered = false;
    private int _connectionCount = 0; // Track number of times we've connected
    private int _disconnectionCount = 0; // Track number of times we've disconnected
    private DateTime _startupTime = DateTime.Now;
    private DateTime _lastHeartbeatSent = DateTime.MinValue; // Track when we last sent a heartbeat

    public event EventHandler<string>? StatusUpdated;
    public event EventHandler<string>? IconUpdated;
    public event EventHandler<List<string>>? RecentsUpdated;
    public event EventHandler<int>? FileCountUpdated;
    public event EventHandler<FileTransferNotification>? FileTransferNotified;
    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<string>? LogFolderReceived;
    public event EventHandler<string>? RegistrationRejected; // New event for rejected registration
    public event EventHandler<DateTime>? ServiceStartTimeReceived;

    public DateTime? ServiceStartTime { get; private set; }

    public ManagedServiceClient(Action<string> logger, string? endpoint = null)
        : base(new ConnectionManager(endpoint ?? $"{Constants.Grpc.LocalhostBase}:{Constants.Grpc.DefaultServicePort}", logger), logger)
    {
        // Start heartbeat timer (sends heartbeat every 10 seconds)
        _heartbeatTimer = new System.Timers.Timer(10000);
        _heartbeatTimer.Elapsed += SendHeartbeatAsync;
        _heartbeatTimer.AutoReset = true;
    }

    protected override void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        base.OnConnectionStateChanged(sender, e);

        var isConnected = e.CurrentState == RJAutoMoverShared.Services.ConnectionState.Connected;
        ConnectionStateChanged?.Invoke(this, isConnected);

        if (isConnected)
        {
            _connectionCount++;
            var uptime = (DateTime.Now - _startupTime).TotalMinutes;
            Logger($"Connection established (#{_connectionCount}) - _isRegistered={_isRegistered}, TrayId={_trayId.Substring(0, 8)}, Uptime={uptime:F1}min");

            if (_connectionCount > 1)
            {
                Logger($"WARNING: This is reconnection #{_connectionCount - 1} since startup. Frequent reconnections may indicate network/service instability.");
            }

            // IMPORTANT: Only register if we're not already registered
            // This prevents re-registration attempts after reconnection which would be rejected
            // by the service (since it still has our active session)
            if (!_isRegistered)
            {
                Logger("Not registered - initiating registration, status fetch, and streaming");
                // Register and start streaming on successful connection
                _ = Task.Run(async () =>
                {
                    await RegisterWithServiceAsync();
                    await GetInitialStatusAsync();
                    _ = Task.Run(() => StreamUpdatesAsync());
                });
            }
            else
            {
                Logger("Already registered - skipping registration, just restarting stream and status fetch");
                // Already registered - just restart streaming and fetch current status
                _ = Task.Run(async () =>
                {
                    await GetInitialStatusAsync();
                    _ = Task.Run(() => StreamUpdatesAsync());
                });
            }
        }
        else
        {
            _disconnectionCount++;
            var uptime = (DateTime.Now - _startupTime).TotalMinutes;
            Logger($"Connection lost (#{_disconnectionCount}) - stopping stream but keeping registration (still registered: {_isRegistered}), Uptime={uptime:F1}min");

            if (_disconnectionCount > 5)
            {
                Logger($"WARNING: Excessive disconnections detected ({_disconnectionCount} times). Check network stability and service health.");
            }

            // Stop streaming on disconnect (but keep _isRegistered flag - we're still registered with the service)
            _streamCancellation?.Cancel();
        }
    }

    /// <summary>
    /// Gets the current username with multiple fallback methods to ensure we never return empty
    /// </summary>
    private string GetUsername()
    {
        // Try multiple methods to get username, in order of preference
        string? username = null;

        // Method 1: Environment.UserName (most common)
        try
        {
            username = Environment.UserName;
            if (!string.IsNullOrWhiteSpace(username))
            {
                Logger($"Username detected via Environment.UserName: {username}");
                return username;
            }
        }
        catch (Exception ex)
        {
            Logger($"Failed to get username from Environment.UserName: {ex.Message}");
        }

        // Method 2: WindowsIdentity (more reliable for Windows)
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            username = identity.Name;
            if (!string.IsNullOrWhiteSpace(username))
            {
                // Extract just the username from DOMAIN\Username format
                var parts = username.Split('\\');
                username = parts.Length > 1 ? parts[1] : username;
                Logger($"Username detected via WindowsIdentity: {username}");
                return username;
            }
        }
        catch (Exception ex)
        {
            Logger($"Failed to get username from WindowsIdentity: {ex.Message}");
        }

        // Method 3: Environment variable USERNAME
        try
        {
            username = Environment.GetEnvironmentVariable("USERNAME");
            if (!string.IsNullOrWhiteSpace(username))
            {
                Logger($"Username detected via USERNAME environment variable: {username}");
                return username;
            }
        }
        catch (Exception ex)
        {
            Logger($"Failed to get username from USERNAME environment variable: {ex.Message}");
        }

        // Method 4: Environment variable USER (for compatibility)
        try
        {
            username = Environment.GetEnvironmentVariable("USER");
            if (!string.IsNullOrWhiteSpace(username))
            {
                Logger($"Username detected via USER environment variable: {username}");
                return username;
            }
        }
        catch (Exception ex)
        {
            Logger($"Failed to get username from USER environment variable: {ex.Message}");
        }

        // If all methods fail, use a fallback value that's clearly identifiable
        var fallbackUsername = $"UnknownUser_{Environment.MachineName}";
        Logger($"WARNING: Could not detect username via any method - using fallback: {fallbackUsername}");
        return fallbackUsername;
    }

    private async Task RegisterWithServiceAsync()
    {
        // Get username with multiple fallback methods to ensure we never send empty username
        var username = GetUsername();
        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        var sessionId = currentProcess.SessionId;
        var processId = currentProcess.Id;

        // Check if running as administrator
        bool isAdmin = false;
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            // If we can't check, assume not admin
            isAdmin = false;
        }

        // Log environment information for diagnostics (helps identify PC-specific issues)
        try
        {
            Logger($"[ENV] OS: {Environment.OSVersion}, Machine: {Environment.MachineName}");
            Logger($"[ENV] .NET: {Environment.Version}, 64-bit OS: {Environment.Is64BitOperatingSystem}, 64-bit Process: {Environment.Is64BitProcess}");
            Logger($"[ENV] User: {username}, Domain: {Environment.UserDomainName}, Interactive: {Environment.UserInteractive}");
            Logger($"[ENV] Session ID: {sessionId}, Process ID: {processId}, IsAdmin: {isAdmin}");

            // Check if running under Terminal Services / Remote Desktop
            bool isRemoteSession = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SESSIONNAME")) &&
                                   Environment.GetEnvironmentVariable("SESSIONNAME") != "Console";
            Logger($"[ENV] Remote session: {isRemoteSession}, Session name: {Environment.GetEnvironmentVariable("SESSIONNAME") ?? "Unknown"}");
        }
        catch (Exception ex)
        {
            Logger($"[ENV] Failed to log environment info: {ex.Message}");
        }

        // Force registration if running as admin (allows admin override of existing sessions)
        bool forceRegister = isAdmin;

        Logger($"[gRPC>] RegisterTray: trayId={_trayId}, username={username}, sessionId={sessionId}, processId={processId}, isAdmin={isAdmin}, forceRegister={forceRegister}");

        var response = await ExecuteCallAsync(
            client => client.RegisterTrayAsync(new RegisterRequest
            {
                TrayId = _trayId,
                Username = username,
                WindowsSessionId = sessionId,
                ForceRegister = forceRegister,
                IsAdministrator = isAdmin,
                ProcessId = processId
            }).ResponseAsync,
            new RegisterResponse { Success = false },
            "RegisterTray");

        Logger($"[gRPC<] RegisterTray response: success={response.Success}, message='{response.Message}', logFolder='{response.LogFolder}'");

        if (!response.Success)
        {
            // Registration was rejected - stop heartbeat and notify listeners
            _heartbeatTimer?.Stop();
            _isRegistered = false;

            Logger($"REGISTRATION REJECTED: {response.Message}");

            // Only fire rejection event if there's an actual conflicting user
            // (not just a generic failure like service unavailable)
            if (!string.IsNullOrEmpty(response.ConflictingUser))
            {
                Logger($"Conflicting user: {response.ConflictingUser}");
                // Fire rejection event to trigger tray shutdown
                RegistrationRejected?.Invoke(this, response.ConflictingUser);
            }
            else
            {
                Logger($"Registration failed but no conflicting user - service may be unavailable");
            }

            return;
        }

        // Registration successful - start heartbeat
        _isRegistered = true;
        _heartbeatTimer?.Start();
        Logger("Heartbeat timer started (10s interval)");

        // Notify listeners of the log folder path
        if (!string.IsNullOrEmpty(response.LogFolder))
        {
            LogFolderReceived?.Invoke(this, response.LogFolder);
        }

        // Handle service start time
        if (response.ServiceStartTime > 0)
        {
            ServiceStartTime = DateTimeOffset.FromUnixTimeMilliseconds(response.ServiceStartTime).DateTime;
            Logger($"Service start time received: {ServiceStartTime}");
            ServiceStartTimeReceived?.Invoke(this, ServiceStartTime.Value);
        }
    }

    private async Task GetInitialStatusAsync()
    {
        Logger("[gRPC>] GetServiceStatus request (fetching fresh state after connection)");

        var status = await ExecuteCallAsync(
            client => client.GetServiceStatusAsync(new Empty()).ResponseAsync,
            new ServiceStatusResponse(),
            "GetServiceStatus");

        Logger($"[gRPC<] GetServiceStatus response: isRunning={status.IsRunning}, isPaused={status.IsPaused}, status='{status.CurrentStatus}', icon='{status.CurrentIcon}'");

        // Notify subscribers of initial state - this should update tray UI with fresh service state
        if (!string.IsNullOrEmpty(status.CurrentStatus))
        {
            Logger($"Updating tray status to: '{status.CurrentStatus}'");
            StatusUpdated?.Invoke(this, status.CurrentStatus);
        }
        if (!string.IsNullOrEmpty(status.CurrentIcon))
        {
            Logger($"Updating tray icon to: '{status.CurrentIcon}'");
            IconUpdated?.Invoke(this, status.CurrentIcon);
        }
        if (status.RecentItems.Count > 0)
            RecentsUpdated?.Invoke(this, status.RecentItems.ToList());
        FileCountUpdated?.Invoke(this, status.FileCount);

        // Handle service start time from status response
        if (status.ServiceStartTime > 0)
        {
            ServiceStartTime = DateTimeOffset.FromUnixTimeMilliseconds(status.ServiceStartTime).DateTime;
            Logger($"Service start time received from status: {ServiceStartTime}");
            ServiceStartTimeReceived?.Invoke(this, ServiceStartTime.Value);
        }
    }

    private async Task StreamUpdatesAsync()
    {
        _streamCancellation?.Cancel();
        _streamCancellation = new CancellationTokenSource();

        try
        {
            var client = Client;
            if (client == null || !ConnectionManager.IsConnected)
                return;

            Logger("[gRPC>] StreamUpdates request initiated");

            using var call = client.StreamUpdates(new Empty());

            await foreach (var update in call.ResponseStream.ReadAllAsync(_streamCancellation.Token))
            {
                switch (update.UpdateCase)
                {
                    case ServiceUpdate.UpdateOneofCase.Status:
                        Logger($"[gRPC<] StreamUpdates status: '{update.Status.Status}'");
                        StatusUpdated?.Invoke(this, update.Status.Status);
                        break;

                    case ServiceUpdate.UpdateOneofCase.Icon:
                        Logger($"[gRPC<] StreamUpdates icon: '{update.Icon.IconName}'");
                        IconUpdated?.Invoke(this, update.Icon.IconName);
                        break;

                    case ServiceUpdate.UpdateOneofCase.Recents:
                        var recentItems = update.Recents.RecentItems.ToList();
                        Logger($"[gRPC<] StreamUpdates recents: {recentItems.Count} items");
                        RecentsUpdated?.Invoke(this, recentItems);
                        break;

                    case ServiceUpdate.UpdateOneofCase.FileCount:
                        FileCountUpdated?.Invoke(this, update.FileCount.Count);
                        break;

                    case ServiceUpdate.UpdateOneofCase.FileTransfer:
                        Logger($"[gRPC<] File transfer: {update.FileTransfer.FileName} - {update.FileTransfer.Status}");
                        FileTransferNotified?.Invoke(this, update.FileTransfer);
                        break;

                    case ServiceUpdate.UpdateOneofCase.ServiceStartTime:
                        if (update.ServiceStartTime.StartTimeUnixMs > 0)
                        {
                            ServiceStartTime = DateTimeOffset.FromUnixTimeMilliseconds(update.ServiceStartTime.StartTimeUnixMs).DateTime;
                            Logger($"[gRPC<] Service start time update: {ServiceStartTime}");
                            ServiceStartTimeReceived?.Invoke(this, ServiceStartTime.Value);
                        }
                        break;
                }
            }

            // Stream ended normally (server closed) - this shouldn't happen unless service is shutting down
            Logger("Update stream ended - service may have closed connection");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            Logger("Update stream cancelled");
        }
        catch (OperationCanceledException)
        {
            Logger("Update stream cancelled");
        }
        catch (RpcException ex)
        {
            Logger($"Update stream RPC error: {ex.StatusCode} - {ex.Status.Detail}");

            // CRITICAL FIX: Stream died - force full reconnection (disconnect + connect) to re-establish stream and fetch fresh state
            if (ex.StatusCode == StatusCode.Unavailable || ex.StatusCode == StatusCode.Internal || ex.StatusCode == StatusCode.Aborted)
            {
                Logger("Stream died due to connection issue - forcing FULL RECONNECT (disconnect + connect) to re-establish stream and fetch fresh state");
                _ = ConnectionManager.ReconnectAsync(); // Use ReconnectAsync to force disconnect + reconnect
            }
        }
        catch (Exception ex)
        {
            Logger($"Update stream unexpected error: {ex.Message}");
            // Trigger full reconnection for unexpected errors too
            Logger("Triggering FULL RECONNECT (disconnect + connect) due to unexpected stream error");
            _ = ConnectionManager.ReconnectAsync(); // Use ReconnectAsync to force disconnect + reconnect
        }
    }

    public async Task<bool> ToggleProcessingAsync(bool pause)
    {
        Logger($"[gRPC>] ToggleProcessing: pauseProcessing={pause}");

        var response = await ExecuteCallAsync(
            client => client.ToggleProcessingAsync(new ToggleProcessingRequest { PauseProcessing = pause }).ResponseAsync,
            new ToggleProcessingResponse { Success = false },
            "ToggleProcessing");

        Logger($"[gRPC<] ToggleProcessing response: success={response.Success}");
        return response.Success;
    }

    public async Task<(bool success, bool isPaused)> GetServiceStatusAsync()
    {
        var status = await ExecuteCallAsync(
            client => client.GetServiceStatusAsync(new Empty()).ResponseAsync,
            new ServiceStatusResponse(),
            "GetServiceStatus");

        return (status.IsRunning, status.IsPaused);
    }

    public async Task<ServiceStatusResponse> GetFullServiceStatusAsync()
    {
        Logger($"[gRPC>] GetFullServiceStatus");

        var status = await ExecuteCallAsync(
            client => client.GetServiceStatusAsync(new Empty()).ResponseAsync,
            new ServiceStatusResponse(),
            "GetFullServiceStatus");

        Logger($"[gRPC<] GetFullServiceStatus response: isRunning={status.IsRunning}, isPaused={status.IsPaused}, recentItems={status.RecentItems.Count}");
        return status;
    }

    public async Task<bool> ClearRecentActivitiesAsync()
    {
        Logger($"[gRPC>] ClearRecentActivities");

        var response = await ExecuteCallAsync(
            client => client.ClearRecentActivitiesAsync(new Empty()).ResponseAsync,
            new ClearRecentActivitiesResponse { Success = false },
            "ClearRecentActivities");

        Logger($"[gRPC<] ClearRecentActivities response: success={response.Success}, message='{response.Message}'");
        return response.Success;
    }

    public async Task<ServiceSystemInfoResponse?> GetServiceSystemInfoAsync()
    {
        Logger($"[gRPC>] GetServiceSystemInfo");

        var response = await ExecuteCallAsync(
            client => client.GetServiceSystemInfoAsync(new Empty()).ResponseAsync,
            new ServiceSystemInfoResponse { MemoryUsageBytes = 0, PeakMemoryUsageBytes = 0, StartTimeUnixMs = 0, UptimeMs = 0 },
            "GetServiceSystemInfo");

        Logger($"[gRPC<] GetServiceSystemInfo response: memory={response.MemoryUsageBytes / 1024.0 / 1024.0:F2}MB, uptime={response.UptimeMs}ms");
        return response.MemoryUsageBytes > 0 ? response : null;
    }

    private async void SendHeartbeatAsync(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!_isRegistered || !ConnectionManager.IsConnected)
        {
            Logger($"[gRPC>] Heartbeat skipped: _isRegistered={_isRegistered}, IsConnected={ConnectionManager.IsConnected}");
            return;
        }

        // Check if we missed heartbeats due to timer suspension (sleep, RDP disconnect, etc.)
        var now = DateTime.Now;
        var timeSinceLastHeartbeat = (now - _lastHeartbeatSent).TotalSeconds;

        if (_lastHeartbeatSent != DateTime.MinValue && timeSinceLastHeartbeat > 20)
        {
            // Timer was suspended! We missed heartbeats
            Logger($"WARNING: Timer suspension detected! {timeSinceLastHeartbeat:F1}s since last heartbeat (expected ~10s)");
            Logger($"Possible causes: RDP disconnect, sleep, screen lock, or system hibernation");
            Logger($"Session may have been marked as stale by service - attempting to re-register");

            // Clear registration flag to force re-registration on next connection
            _isRegistered = false;
            _heartbeatTimer?.Stop();

            // Trigger immediate reconnection to re-register
            Logger("Triggering immediate reconnection to recover from timer suspension");
            _ = ConnectionManager.ReconnectAsync();
            return;
        }

        _lastHeartbeatSent = now;
        Logger($"[gRPC>] Sending heartbeat: TrayId={_trayId.Substring(0, 8)} ({timeSinceLastHeartbeat:F1}s since last)");

        var response = await ExecuteCallAsync(
            client => client.HeartbeatTrayAsync(new HeartbeatRequest
            {
                TrayId = _trayId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }).ResponseAsync,
            new HeartbeatResponse { Acknowledged = false, IsActiveTray = false },
            "HeartbeatTray");

        Logger($"[gRPC<] Heartbeat response: Acknowledged={response.Acknowledged}, IsActiveTray={response.IsActiveTray}, ActiveUser={response.ActiveUser}");

        if (response.Acknowledged && !response.IsActiveTray)
        {
            // We are not the active tray - session was cleared (likely due to missed heartbeats)
            Logger($"HEARTBEAT REJECTED: We are no longer the active tray (active user: {response.ActiveUser})");
            Logger($"HEARTBEAT REJECTED: Our TrayId={_trayId.Substring(0, 8)}, service says active user is '{response.ActiveUser}'");

            // Check if this is due to timer suspension
            if (response.ActiveUser == "None" || string.IsNullOrEmpty(response.ActiveUser))
            {
                Logger("DIAGNOSIS: ActiveUser is 'None' - service cleared our session, likely due to missed heartbeats from timer suspension");
                Logger("RECOVERY: Will attempt to re-register on next connection");
            }
            else
            {
                Logger("Shutting down gracefully - another tray instance has taken control");
            }

            _heartbeatTimer?.Stop();
            _isRegistered = false;

            // Only fire shutdown event if there's an actual conflicting user
            // If ActiveUser is "None", just trigger reconnection to re-register
            if (response.ActiveUser == "None" || string.IsNullOrEmpty(response.ActiveUser))
            {
                Logger("Triggering reconnection to re-register (no conflicting user)");
                _ = ConnectionManager.ReconnectAsync();
            }
            else
            {
                // Fire rejection event to trigger application shutdown
                RegistrationRejected?.Invoke(this, response.ActiveUser ?? "Unknown");
            }
        }
        else if (response.Acknowledged && response.IsActiveTray)
        {
            Logger($"[gRPC<] Heartbeat acknowledged - we are the active tray");
        }
        else if (!response.Acknowledged)
        {
            Logger($"[gRPC<] Heartbeat not acknowledged - possible service issue");
        }
    }

    public override void Dispose()
    {
        _streamCancellation?.Cancel();
        _streamCancellation?.Dispose();
        _heartbeatTimer?.Stop();
        _heartbeatTimer?.Dispose();
        base.Dispose();
    }
}
