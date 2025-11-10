using Grpc.Core;
using Grpc.Net.Client;
using System.Net.Http;

namespace RJAutoMoverShared.Services;

/// <summary>
/// Connection state for a gRPC connection
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

/// <summary>
/// Connection event arguments
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionState PreviousState { get; init; }
    public ConnectionState CurrentState { get; init; }
    public string? Reason { get; init; }
    public Exception? Exception { get; init; }
}

/// <summary>
/// Centralized connection manager for bidirectional gRPC communication.
/// Handles connection lifecycle, reconnection logic, and reactive reconnection triggers.
/// </summary>
public class ConnectionManager : IDisposable
{
    private readonly string _endpointAddress;
    private readonly Action<string> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();

    private GrpcChannel? _channel;
    private ConnectionState _state = ConnectionState.Disconnected;
    private int _reconnectionAttempts = 0;
    private DateTime _lastConnectionAttempt = DateTime.MinValue;
    private DateTime _lastSuccessfulConnection = DateTime.MinValue;
    private DateTime _lastReactiveContact = DateTime.MinValue;
    private bool _disposed = false;
    private Task? _reconnectionTask;

    // Configuration
    private readonly int _initialRetryDelayMs;
    private readonly int _maxRetryDelayMs;
    private readonly int _reactiveDebounceSeconds;
    private readonly int _connectionTimeoutSeconds;

    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public ConnectionState State => _state;
    public bool IsConnected => _state == ConnectionState.Connected;
    public GrpcChannel? Channel => _channel;
    public int ReconnectionAttempts => _reconnectionAttempts;
    public DateTime LastSuccessfulConnection => _lastSuccessfulConnection;

    /// <summary>
    /// Creates a new ConnectionManager instance
    /// </summary>
    /// <param name="endpointAddress">The gRPC endpoint address (e.g., "http://localhost:50051")</param>
    /// <param name="logger">Logging action for diagnostics</param>
    /// <param name="initialRetryDelayMs">Initial retry delay in milliseconds (default: 2000)</param>
    /// <param name="maxRetryDelayMs">Maximum retry delay in milliseconds (default: 30000)</param>
    /// <param name="reactiveDebounceSeconds">Debounce time for reactive reconnection triggers (default: 5)</param>
    /// <param name="connectionTimeoutSeconds">Timeout for connection attempts (default: 5)</param>
    public ConnectionManager(
        string endpointAddress,
        Action<string> logger,
        int initialRetryDelayMs = 2000,
        int maxRetryDelayMs = 30000,
        int reactiveDebounceSeconds = 5,
        int connectionTimeoutSeconds = 5)
    {
        _endpointAddress = endpointAddress ?? throw new ArgumentNullException(nameof(endpointAddress));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _initialRetryDelayMs = initialRetryDelayMs;
        _maxRetryDelayMs = maxRetryDelayMs;
        _reactiveDebounceSeconds = reactiveDebounceSeconds;
        _connectionTimeoutSeconds = connectionTimeoutSeconds;
    }

    /// <summary>
    /// Starts the connection manager and initiates first connection attempt
    /// </summary>
    public async Task<bool> StartAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ConnectionManager));

        _logger($"ConnectionManager starting for endpoint: {_endpointAddress}");
        return await ConnectAsync();
    }

    /// <summary>
    /// Attempts to establish a connection to the gRPC endpoint
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        if (_disposed)
            return false;

        // Prevent concurrent connection attempts
        if (!await _connectionLock.WaitAsync(0))
        {
            _logger("Connection attempt already in progress, skipping");
            return false;
        }

        try
        {
            _lastConnectionAttempt = DateTime.UtcNow;

            // If already connected, return success
            if (_state == ConnectionState.Connected && _channel != null)
            {
                _logger("Already connected, skipping connection attempt");
                return true;
            }

            UpdateState(ConnectionState.Connecting, "Initiating connection");

            // Clean up existing channel if any
            if (_channel != null)
            {
                try
                {
                    await _channel.ShutdownAsync();
                    _channel.Dispose();
                }
                catch
                {
                    // Suppress exceptions during cleanup - channel disposal failures are not critical
                }
                _channel = null;
            }

            _logger($"Attempting connection to {_endpointAddress}...");

            // Create new channel with optimized settings
            var options = new GrpcChannelOptions
            {
                MaxReceiveMessageSize = Constants.Grpc.MaxMessageSizeBytes, // 16 MB
                MaxSendMessageSize = Constants.Grpc.MaxMessageSizeBytes, // 16 MB
                HttpHandler = new SocketsHttpHandler
                {
                    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
                    KeepAlivePingDelay = TimeSpan.FromSeconds(10),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(5),
                    EnableMultipleHttp2Connections = false,
                    ConnectTimeout = TimeSpan.FromSeconds(_connectionTimeoutSeconds)
                }
            };

            _channel = GrpcChannel.ForAddress(_endpointAddress, options);

            // Connection successful
            _reconnectionAttempts = 0;
            _lastSuccessfulConnection = DateTime.UtcNow;
            UpdateState(ConnectionState.Connected, "Connection established");

            _logger($"Successfully connected to {_endpointAddress}");
            return true;
        }
        catch (Exception ex)
        {
            _reconnectionAttempts++;
            _logger($"Connection attempt #{_reconnectionAttempts} failed: {ex.Message}");

            // Clean up failed channel
            if (_channel != null)
            {
                try { _channel.Dispose(); } catch { /* Suppress disposal exceptions during error handling */ }
                _channel = null;
            }

            UpdateState(ConnectionState.Failed, $"Connection failed: {ex.Message}", ex);
            ScheduleReconnection();
            return false;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Triggers an immediate reconnection attempt if not currently connected.
    /// Used for reactive reconnection when the remote peer initiates contact.
    /// </summary>
    public async Task TriggerReactiveReconnectionAsync()
    {
        if (_disposed)
            return;

        var now = DateTime.UtcNow;

        // Debounce reactive reconnection triggers to prevent rapid-fire attempts
        if ((now - _lastReactiveContact).TotalSeconds < _reactiveDebounceSeconds)
        {
            _logger($"Reactive reconnection debounced (within {_reactiveDebounceSeconds}s window)");
            return;
        }

        _lastReactiveContact = now;

        // If already connected, no need to reconnect
        if (_state == ConnectionState.Connected)
        {
            _logger("Reactive reconnection triggered but already connected - ignoring");
            return;
        }

        _logger("Reactive reconnection triggered by remote peer contact - attempting immediate connection");
        _reconnectionAttempts = 0; // Reset attempts for fast recovery
        await ConnectAsync();
    }

    /// <summary>
    /// Manually triggers a disconnect and reconnect cycle
    /// </summary>
    public async Task ReconnectAsync()
    {
        if (_disposed)
            return;

        _logger("Manual reconnection requested");
        await DisconnectAsync();
        await ConnectAsync();
    }

    /// <summary>
    /// Disconnects the current connection
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_disposed)
            return;

        await _connectionLock.WaitAsync();
        try
        {
            if (_channel != null)
            {
                _logger("Disconnecting...");
                UpdateState(ConnectionState.Disconnected, "Manual disconnect");

                try
                {
                    await _channel.ShutdownAsync();
                }
                catch
                {
                    // Suppress shutdown exceptions during disconnect - not critical
                }
                finally
                {
                    _channel.Dispose();
                    _channel = null;
                }

                _logger("Disconnected");
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Stops the connection manager and cancels any pending reconnection attempts
    /// </summary>
    public async Task StopAsync()
    {
        if (_disposed)
            return;

        _logger("ConnectionManager stopping...");
        _shutdownCts.Cancel();

        // Wait for any pending reconnection task to complete
        if (_reconnectionTask != null && !_reconnectionTask.IsCompleted)
        {
            try
            {
                await Task.WhenAny(_reconnectionTask, Task.Delay(2000));
            }
            catch
            {
                // Suppress exceptions during shutdown - task cancellation expected
            }
        }

        await DisconnectAsync();
        _logger("ConnectionManager stopped");
    }

    /// <summary>
    /// Creates a gRPC client from the current channel
    /// </summary>
    public TClient? CreateClient<TClient>() where TClient : ClientBase<TClient>
    {
        if (_channel == null || _state != ConnectionState.Connected)
            return null;

        try
        {
            return (TClient?)Activator.CreateInstance(typeof(TClient), _channel);
        }
        catch (Exception ex)
        {
            _logger($"Failed to create client: {ex.Message}");
            return null;
        }
    }

    private void ScheduleReconnection()
    {
        if (_shutdownCts.Token.IsCancellationRequested)
        {
            _logger("Shutdown in progress, skipping reconnection");
            return;
        }

        // Use exponential backoff with jitter
        var baseDelay = Math.Min(_initialRetryDelayMs * Math.Pow(2, _reconnectionAttempts - 1), _maxRetryDelayMs);
        var jitter = new Random().Next(0, 1000); // Add 0-1s jitter to prevent thundering herd
        var delayMs = (int)baseDelay + jitter;

        _logger($"Scheduling reconnection attempt #{_reconnectionAttempts + 1} in {delayMs / 1000.0:F1} seconds...");
        UpdateState(ConnectionState.Reconnecting, $"Reconnecting in {delayMs / 1000.0:F1}s");

        _reconnectionTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, _shutdownCts.Token);

                if (!_shutdownCts.Token.IsCancellationRequested)
                {
                    _logger($"Attempting scheduled reconnection #{_reconnectionAttempts + 1}...");
                    await ConnectAsync();
                }
            }
            catch (OperationCanceledException)
            {
                _logger("Reconnection cancelled due to shutdown");
            }
            catch (Exception ex)
            {
                _logger($"Error during scheduled reconnection: {ex.Message}");
            }
        }, _shutdownCts.Token);
    }

    private void UpdateState(ConnectionState newState, string reason, Exception? exception = null)
    {
        var previousState = _state;
        _state = newState;

        _logger($"Connection state: {previousState} -> {newState} ({reason})");

        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs
        {
            PreviousState = previousState,
            CurrentState = newState,
            Reason = reason,
            Exception = exception
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAsync().Wait(3000);
        _shutdownCts.Dispose();
        _connectionLock.Dispose();
    }
}
