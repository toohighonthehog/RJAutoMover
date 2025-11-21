using Grpc.Core;
using RJAutoMoverShared.Protos;
using RJAutoMoverShared.Models;
using RJAutoMoverShared.Services;
using LogLevel = RJAutoMoverShared.Models.LogLevel;

namespace RJAutoMoverS.Services;

public class GrpcServerServiceSimplified : TrayService.TrayServiceBase
{
    private readonly RJAutoMoverShared.Services.LoggingService _logger;
    private readonly List<IServerStreamWriter<ServiceUpdate>> _activeStreams = new();
    private readonly object _lock = new();
    private readonly object _sessionLock = new();
    private FileProcessorService? _fileProcessor;
    private string _currentStatus = "Initializing...";
    private string _currentIcon = IconNames.Waiting;
    private List<string> _recentItems = new();
    private int _fileCount = 0;
    private DateTime _lastTrayContact = DateTime.MinValue;
    private string _logFolder = "";
    private TraySessionInfo? _activeTraySession = null;
    private System.Timers.Timer? _sessionMonitorTimer;
    private DateTime _serviceStartTime = DateTime.Now;

    public event EventHandler? TrayContactDetected;

    public GrpcServerServiceSimplified(LoggingService logger, string logFolder)
    {
        _logger = logger;
        _logFolder = logFolder;

        // Start session monitor timer (checks for stale sessions every 30 seconds)
        _sessionMonitorTimer = new System.Timers.Timer(30000);
        _sessionMonitorTimer.Elapsed += MonitorStaleSessions;
        _sessionMonitorTimer.AutoReset = true;
        _sessionMonitorTimer.Start();
    }

    private void OnTrayContact()
    {
        var now = DateTime.Now;
        // Only fire event if it's been more than 5 seconds since last contact
        // This prevents rapid-fire events during normal operation
        if (now - _lastTrayContact > TimeSpan.FromSeconds(5))
        {
            _lastTrayContact = now;
            _logger.Log(LogLevel.DEBUG, "Tray contact detected - triggering reactive reconnection");
            TrayContactDetected?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetServiceStartTime(DateTime startTime)
    {
        _serviceStartTime = startTime;
        _logger.Log(LogLevel.DEBUG, $"Service start time set to: {_serviceStartTime}");
    }

    public void Initialize(FileProcessorService fileProcessor)
    {
        _fileProcessor = fileProcessor;

        // Unsubscribe first to prevent duplicate event handlers if Initialize is called multiple times
        // (can happen during service restart/recovery scenarios)
        _fileProcessor.StatusUpdated -= OnStatusUpdated;
        _fileProcessor.IconUpdated -= OnIconUpdated;
        _fileProcessor.RecentsUpdated -= OnRecentsUpdated;
        _fileProcessor.FileCountUpdated -= OnFileCountUpdated;

        // Subscribe to file processor events
        _fileProcessor.StatusUpdated += OnStatusUpdated;
        _fileProcessor.IconUpdated += OnIconUpdated;
        _fileProcessor.RecentsUpdated += OnRecentsUpdated;
        _fileProcessor.FileCountUpdated += OnFileCountUpdated;

        // NOTE: Don't load historical data here - ActivityHistoryService isn't initialized yet!
        // It will be loaded via RefreshHistoricalCache() after ActivityHistoryService is ready
    }

    /// <summary>
    /// Refreshes the recent items cache with historical data from the database.
    /// Should be called after ActivityHistoryService is fully initialized.
    /// </summary>
    public void RefreshHistoricalCache()
    {
        if (_fileProcessor == null)
        {
            _logger.Log(LogLevel.WARN, "Cannot refresh historical cache - file processor not initialized");
            return;
        }

        _recentItems = _fileProcessor.GetActivitiesWithHistory(1000);
        _logger.Log(LogLevel.INFO, $"Refreshed recent items cache with {_recentItems.Count} activities (including database history)");
    }

    public override Task<RegisterResponse> RegisterTray(RegisterRequest request, ServerCallContext context)
    {
        OnTrayContact(); // Trigger reactive reconnection
        _logger.Log(LogLevel.gRPCIn, $"RegisterTray request from trayId: {request.TrayId}, user: {request.Username}, sessionId: {request.WindowsSessionId}, processId: {request.ProcessId}, forceRegister: {request.ForceRegister}, isAdmin: {request.IsAdministrator}");

        // Validate username is not empty (defensive check)
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            _logger.Log(LogLevel.ERROR, $"CRITICAL: Registration attempted with empty username from trayId: {request.TrayId}, sessionId: {request.WindowsSessionId}");

            var errorResponse = new RegisterResponse
            {
                Success = false,
                Message = "Registration failed: Username cannot be empty. This is a critical error - please restart the tray application.",
                LogFolder = _logFolder,
                ConflictingUser = ""
            };

            return Task.FromResult(errorResponse);
        }

        lock (_sessionLock)
        {
            // Check if there's an existing active session
            if (_activeTraySession != null)
            {
                var secondsSinceLastHeartbeat = (DateTime.Now - _activeTraySession.LastHeartbeat).TotalSeconds;
                _logger.Log(LogLevel.INFO, $"Existing session found: {_activeTraySession.GetDescription()}, last heartbeat {secondsSinceLastHeartbeat:F1}s ago");

                // Check if the existing session is still alive (30 second timeout for faster stale detection)
                if (_activeTraySession.IsAlive(30, msg => _logger.Log(LogLevel.DEBUG, msg)))
                {
                    _logger.Log(LogLevel.INFO, $"Existing session IS ALIVE (heartbeat within 30s and process still running)");

                    // Check for admin override
                    if (request.ForceRegister && request.IsAdministrator)
                    {
                        // Admin is forcing takeover
                        _logger.Log(LogLevel.WARN, $"ADMIN OVERRIDE: {request.Username} (Administrator) is forcefully taking control from {_activeTraySession.GetDescription()}");
                        _logger.Log(LogLevel.INFO, $"Terminating existing session for admin takeover");

                        // Forcefully clean up the existing session
                        CleanupStaleStreams();
                        _activeTraySession = null;

                        // Continue to create new session below
                    }
                    else
                    {
                        // Active session exists - reject this registration
                        _logger.Log(LogLevel.WARN, $"Registration REJECTED for {request.Username} (PID {request.ProcessId}) - Active tray session already exists: {_activeTraySession.GetDescription()}");

                        // Defensive: Ensure username is never empty in error message
                        var conflictingUsername = string.IsNullOrWhiteSpace(_activeTraySession.Username)
                            ? "Unknown User"
                            : _activeTraySession.Username;

                        var rejectResponse = new RegisterResponse
                        {
                            Success = false,
                            Message = $"Another user ({conflictingUsername}) already has the tray application connected. Only one tray instance can run at a time.\n\nIf you are an administrator and need to take control, restart the tray application with administrator privileges.\n\nIf you believe this is an error and no other tray is running, wait 30 seconds and try again.",
                            LogFolder = _logFolder,
                            ConflictingUser = conflictingUsername
                        };

                        _logger.Log(LogLevel.gRPCOut, $"RegisterTray response: success=false, conflictingUser='{conflictingUsername}'");
                        return Task.FromResult(rejectResponse);
                    }
                }
                else
                {
                    // Session is stale - clean it up
                    var staleDuration = (DateTime.Now - _activeTraySession.LastHeartbeat).TotalSeconds;
                    _logger.Log(LogLevel.WARN, $"Detected STALE tray session: {_activeTraySession.GetDescription()} (last heartbeat {staleDuration:F0}s ago)");
                    _logger.Log(LogLevel.INFO, $"Removing stale session to allow new registration from {request.Username}");

                    // Remove stale streams
                    CleanupStaleStreams();

                    _activeTraySession = null;
                }
            }

            // Create new session
            _activeTraySession = new TraySessionInfo
            {
                TrayId = request.TrayId,
                Username = request.Username,
                WindowsSessionId = request.WindowsSessionId,
                ProcessId = request.ProcessId,
                RegistrationTime = DateTime.Now,
                LastHeartbeat = DateTime.Now
            };

            _logger.Log(LogLevel.INFO, $"Tray session ESTABLISHED: {_activeTraySession.GetDescription()}");

            var response = new RegisterResponse
            {
                Success = true,
                Message = "Tray registered successfully",
                LogFolder = _logFolder,
                ConflictingUser = "",
                ServiceStartTime = new DateTimeOffset(_serviceStartTime).ToUnixTimeMilliseconds()
            };

            _logger.Log(LogLevel.gRPCOut, $"RegisterTray response: success=true, message='{response.Message}', logFolder='{response.LogFolder}', startTime={_serviceStartTime}");
            return Task.FromResult(response);
        }
    }

    public override Task<ToggleProcessingResponse> ToggleProcessing(ToggleProcessingRequest request, ServerCallContext context)
    {
        OnTrayContact(); // Trigger reactive reconnection
        _logger.Log(LogLevel.gRPCIn, $"ToggleProcessing: pauseProcessing={request.PauseProcessing}");

        if (_fileProcessor != null)
        {
            _fileProcessor.ToggleProcessing(request.PauseProcessing);
            _logger.Log(LogLevel.INFO, $"Processing toggled: {(request.PauseProcessing ? "paused" : "resumed")}");

            var response = new ToggleProcessingResponse
            {
                Success = true,
                CurrentStatus = _fileProcessor.ProcessingPaused
            };

            _logger.Log(LogLevel.gRPCOut, $"ToggleProcessing response: success={response.Success}, currentStatus={response.CurrentStatus}");
            return Task.FromResult(response);
        }

        var failureResponse = new ToggleProcessingResponse
        {
            Success = false,
            CurrentStatus = false
        };

        _logger.Log(LogLevel.gRPCOut, $"ToggleProcessing response: success={failureResponse.Success}, currentStatus={failureResponse.CurrentStatus}");
        return Task.FromResult(failureResponse);
    }

    public override Task<ServiceStatusResponse> GetServiceStatus(Empty request, ServerCallContext context)
    {
        OnTrayContact(); // Trigger reactive reconnection

        // Get activities with historical data if available
        var activitiesWithHistory = _fileProcessor?.GetActivitiesWithHistory(1000) ?? _recentItems;

        var response = new ServiceStatusResponse
        {
            IsRunning = true,
            IsPaused = _fileProcessor?.ProcessingPaused ?? false,
            CurrentStatus = _currentStatus,
            RecentItems = { activitiesWithHistory }, // Include historical data
            CurrentIcon = _currentIcon,
            FileCount = _fileCount,
            ServiceStartTime = new DateTimeOffset(_serviceStartTime).ToUnixTimeMilliseconds()
        };

        return Task.FromResult(response);
    }

    public async Task NotifyFileTransferAsync(string fileName, string ruleName, long fileSizeBytes, TransferStatus status, string message)
    {
        var update = new ServiceUpdate
        {
            FileTransfer = new FileTransferNotification
            {
                FileName = fileName,
                RuleName = ruleName,
                FileSizeBytes = fileSizeBytes,
                Status = status,
                Message = message,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }
        };

        await BroadcastUpdate(update);
        _logger.Log(LogLevel.gRPCOut, $"File transfer notification: {fileName} - {status} ({message})");
    }

    public override async Task StreamUpdates(Empty request, IServerStreamWriter<ServiceUpdate> responseStream, ServerCallContext context)
    {
        OnTrayContact(); // Trigger reactive reconnection
        _logger.Log(LogLevel.gRPCIn, "StreamUpdates client connected");
        _logger.Log(LogLevel.INFO, "Client connected to update stream");

        // Register this stream for updates
        lock (_lock)
        {
            _activeStreams.Add(responseStream);
        }

        try
        {
            // Send immediate initial update
            await responseStream.WriteAsync(new ServiceUpdate
            {
                Status = new StatusUpdate
                {
                    Status = _currentStatus,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            });

            await responseStream.WriteAsync(new ServiceUpdate
            {
                Icon = new IconUpdate
                {
                    IconName = _currentIcon,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            });

            // Send activities with historical data if available
            var activitiesWithHistory = _fileProcessor?.GetActivitiesWithHistory(1000) ?? _recentItems;
            await responseStream.WriteAsync(new ServiceUpdate
            {
                Recents = new RecentsUpdate
                {
                    RecentItems = { activitiesWithHistory },
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            });

            await responseStream.WriteAsync(new ServiceUpdate
            {
                FileCount = new FileCountUpdate
                {
                    Count = _fileCount,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            });

            await responseStream.WriteAsync(new ServiceUpdate
            {
                ServiceStartTime = new ServiceStartTimeUpdate
                {
                    StartTimeUnixMs = new DateTimeOffset(_serviceStartTime).ToUnixTimeMilliseconds()
                }
            });

            _logger.Log(LogLevel.gRPCOut, $"StreamUpdates initial data sent: status='{_currentStatus}', icon='{_currentIcon}', fileCount={_fileCount}, recentItems={_recentItems.Count}, serviceStartTime={_serviceStartTime}");

            // Keep connection alive - no heartbeat needed, just wait for cancellation
            while (!context.CancellationToken.IsCancellationRequested)
            {
                await Task.Delay(30000, context.CancellationToken); // Wait 30 seconds
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Log(LogLevel.INFO, "Update stream cancelled by client");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Error in update stream: {ex.Message}");
        }
        finally
        {
            // Remove stream from active list
            lock (_lock)
            {
                _activeStreams.Remove(responseStream);
            }
        }
    }

    public override Task<ClearRecentActivitiesResponse> ClearRecentActivities(Empty request, ServerCallContext context)
    {
        OnTrayContact();
        _logger.Log(LogLevel.gRPCIn, "ClearRecentActivities request received");

        try
        {
            // This will trigger RecentsUpdated event which will broadcast the empty list
            _fileProcessor?.ClearRecentActivities();

            _logger.Log(LogLevel.gRPCOut, "ClearRecentActivities response: success=True");
            return Task.FromResult(new ClearRecentActivitiesResponse
            {
                Success = true,
                Message = "Recent activities cleared successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Error clearing recent activities: {ex.Message}");
            return Task.FromResult(new ClearRecentActivitiesResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            });
        }
    }

    public override Task<ServiceSystemInfoResponse> GetServiceSystemInfo(Empty request, ServerCallContext context)
    {
        OnTrayContact();
        _logger.Log(LogLevel.gRPCIn, "GetServiceSystemInfo request received");

        try
        {
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var memoryUsageBytes = currentProcess.WorkingSet64;
            var peakMemoryUsageBytes = currentProcess.PeakWorkingSet64;
            var startTimeUnixMs = new DateTimeOffset(currentProcess.StartTime).ToUnixTimeMilliseconds();
            var uptimeMs = (long)(DateTime.Now - currentProcess.StartTime).TotalMilliseconds;

            // Get database statistics from file processor's activity history service
            DatabaseStatistics? dbStats = null;
            if (_fileProcessor?.ActivityHistory != null)
            {
                dbStats = _fileProcessor.ActivityHistory.GetDatabaseStatistics();
            }

            var uptimeSec = uptimeMs / 1000.0;
            _logger.Log(LogLevel.gRPCOut, $"GetServiceSystemInfo response: memory={memoryUsageBytes / 1024.0 / 1024.0:F2} MB, uptime={uptimeSec:F0} sec, dbRecords={dbStats?.TotalRecords ?? 0:N0}");

            return Task.FromResult(new ServiceSystemInfoResponse
            {
                MemoryUsageBytes = memoryUsageBytes,
                PeakMemoryUsageBytes = peakMemoryUsageBytes,
                StartTimeUnixMs = startTimeUnixMs,
                UptimeMs = uptimeMs,
                DatabasePath = dbStats?.DatabasePath ?? "",
                DatabaseEnabled = dbStats?.IsEnabled ?? false,
                DatabaseConnected = dbStats?.IsConnected ?? false,
                DatabaseRecordCount = dbStats?.TotalRecords ?? 0,
                DatabaseLastTransfer = dbStats?.LastTransferTimestamp?.ToString("o") ?? ""
            });
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Error getting service system info: {ex.Message}");
            // Return zeros on error
            return Task.FromResult(new ServiceSystemInfoResponse
            {
                MemoryUsageBytes = 0,
                PeakMemoryUsageBytes = 0,
                StartTimeUnixMs = 0,
                UptimeMs = 0,
                DatabasePath = "",
                DatabaseEnabled = false,
                DatabaseConnected = false,
                DatabaseRecordCount = 0,
                DatabaseLastTransfer = ""
            });
        }
    }

    // Event handlers for file processor events
    private async void OnStatusUpdated(object? sender, StatusUpdateEventArgs e)
    {
        if (_currentStatus != e.Status)
        {
            _currentStatus = e.Status;
            _logger.Log(LogLevel.gRPCOut, $"Status update: '{e.Status}'");
            await BroadcastUpdate(new ServiceUpdate
            {
                Status = new StatusUpdate
                {
                    Status = e.Status,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            });
        }
    }

    private async void OnIconUpdated(object? sender, IconUpdateEventArgs e)
    {
        if (_currentIcon != e.IconName)
        {
            _currentIcon = e.IconName;
            _logger.Log(LogLevel.gRPCOut, $"Icon update: '{e.IconName}'");
            await BroadcastUpdate(new ServiceUpdate
            {
                Icon = new IconUpdate
                {
                    IconName = e.IconName,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            });
        }
    }

    private async void OnRecentsUpdated(object? sender, RecentsUpdateEventArgs e)
    {
        // Get activities with historical data if available (current session + database history)
        var activitiesWithHistory = _fileProcessor?.GetActivitiesWithHistory(1000) ?? e.Recents;

        // Only broadcast if recent items actually changed
        var newContent = string.Join("|", activitiesWithHistory);
        var oldContent = string.Join("|", _recentItems);

        if (newContent != oldContent)
        {
            _recentItems = activitiesWithHistory;
            _logger.Log(LogLevel.gRPCOut, $"Recent items update: {activitiesWithHistory.Count} items (includes history)");
            await BroadcastUpdate(new ServiceUpdate
            {
                Recents = new RecentsUpdate
                {
                    RecentItems = { activitiesWithHistory },
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            });
        }
    }

    private async void OnFileCountUpdated(object? sender, FileCountUpdateEventArgs e)
    {
        // Only broadcast if file count actually changed
        if (_fileCount != e.Count)
        {
            _fileCount = e.Count;
            _logger.Log(LogLevel.gRPCOut, $"File count update: {e.Count}");
            await BroadcastUpdate(new ServiceUpdate
            {
                FileCount = new FileCountUpdate
                {
                    Count = e.Count,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            });
        }
    }

    private async Task BroadcastUpdate(ServiceUpdate update)
    {
        List<IServerStreamWriter<ServiceUpdate>> activeStreamsCopy;

        // Copy active streams list to avoid holding lock during async operations
        lock (_lock)
        {
            activeStreamsCopy = new List<IServerStreamWriter<ServiceUpdate>>(_activeStreams);
        }

        if (activeStreamsCopy.Count == 0) return;

        List<IServerStreamWriter<ServiceUpdate>> streamsToRemove = new();

        // Broadcast to all streams
        var broadcastTasks = activeStreamsCopy.Select(async stream =>
        {
            try
            {
                await stream.WriteAsync(update);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.DEBUG, $"Failed to write to stream: {ex.Message}");
                lock (streamsToRemove)
                {
                    if (!streamsToRemove.Contains(stream))
                        streamsToRemove.Add(stream);
                }
            }
        });

        await Task.WhenAll(broadcastTasks);

        // Remove failed streams
        if (streamsToRemove.Count > 0)
        {
            lock (_lock)
            {
                foreach (var stream in streamsToRemove)
                {
                    _activeStreams.Remove(stream);
                }
            }
        }
    }

    public override Task<HeartbeatResponse> HeartbeatTray(HeartbeatRequest request, ServerCallContext context)
    {
        lock (_sessionLock)
        {
            if (_activeTraySession != null && _activeTraySession.TrayId == request.TrayId)
            {
                // Update heartbeat timestamp
                var previousHeartbeat = _activeTraySession.LastHeartbeat;
                _activeTraySession.LastHeartbeat = DateTime.Now;
                var timeSinceLastHeartbeat = (DateTime.Now - previousHeartbeat).TotalSeconds;

                _logger.Log(LogLevel.DEBUG, $"[gRPC<] Heartbeat received from active tray {_activeTraySession.TrayId.Substring(0, 8)} ({_activeTraySession.Username}, PID {_activeTraySession.ProcessId}) - {timeSinceLastHeartbeat:F1}s since last heartbeat");

                return Task.FromResult(new HeartbeatResponse
                {
                    Acknowledged = true,
                    IsActiveTray = true,
                    ActiveUser = _activeTraySession.Username
                });
            }
            else
            {
                // This tray is not the registered one
                var activeUser = _activeTraySession?.Username ?? "None";
                var activeTrayId = _activeTraySession?.TrayId.Substring(0, 8) ?? "None";
                _logger.Log(LogLevel.WARN, $"[gRPC<] Heartbeat from UNREGISTERED tray: {request.TrayId.Substring(0, 8)} (Active tray: {activeTrayId} / {activeUser})");

                return Task.FromResult(new HeartbeatResponse
                {
                    Acknowledged = true,
                    IsActiveTray = false,
                    ActiveUser = activeUser
                });
            }
        }
    }

    private void MonitorStaleSessions(object? sender, System.Timers.ElapsedEventArgs e)
    {
        lock (_sessionLock)
        {
            // Use 30 second timeout for faster stale detection (matches registration timeout)
            if (_activeTraySession != null)
            {
                _logger.Log(LogLevel.DEBUG, $"Session monitor checking if session is alive: {_activeTraySession.GetDescription()}");
                bool isAlive = _activeTraySession.IsAlive(30, msg => _logger.Log(LogLevel.DEBUG, msg));

                if (!isAlive)
                {
                    var secondsSinceLastHeartbeat = (DateTime.Now - _activeTraySession.LastHeartbeat).TotalSeconds;
                    _logger.Log(LogLevel.WARN, $"Session monitor detected STALE session: {_activeTraySession.GetDescription()} (last heartbeat {secondsSinceLastHeartbeat:F0}s ago)");
                    _logger.Log(LogLevel.INFO, "Automatically cleaning up stale session");

                    CleanupStaleStreams();
                    _activeTraySession = null;
                }
                else
                {
                    _logger.Log(LogLevel.DEBUG, $"Session monitor: Session is still alive");
                }
            }
        }
    }

    private void CleanupStaleStreams()
    {
        lock (_lock)
        {
            if (_activeStreams.Count > 0)
            {
                _logger.Log(LogLevel.DEBUG, $"Cleaning up {_activeStreams.Count} stale stream(s)");
                _activeStreams.Clear();
            }
        }
    }
}
