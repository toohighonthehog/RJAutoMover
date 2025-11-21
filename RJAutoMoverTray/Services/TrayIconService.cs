using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using RJAutoMoverTray.Windows;
using RJAutoMoverShared.Models;
using RJAutoMoverShared.Services;
using LogLevel = RJAutoMoverShared.Models.LogLevel;

namespace RJAutoMoverTray.Services;

/// <summary>
/// Manages the system tray icon and its interactions with the RJAutoMover service.
/// Handles status updates, icon changes, context menu, and communication with the About window.
/// </summary>
public class TrayIconService
{
    // Core services
    private readonly RJAutoMoverShared.Services.LoggingService _logger;
    private GrpcClientServiceV2? _grpcClient;

    // UI components
    private TaskbarIcon? _trayIcon;
    private MenuItem? _statusMenuItem;
    private MenuItem? _recentsMenuItem;
    private MenuItem? _toggleProcessingMenuItem;
    private AboutWindow? _aboutWindow;

    // State tracking
    private string _currentIconName = "base.ico";
    private bool _isPaused = false;
    private string _currentStatus = "Service Disconnected";
    private string _errorDetails = string.Empty;
    private List<string> _cachedRecents = new List<string>();

    /// <summary>
    /// Event fired when the tray icon changes, allowing other components to react to state changes.
    /// </summary>
    public event EventHandler<string>? IconChanged;

    /// <summary>
    /// Event fired when the connection state to the service changes (connected/disconnected).
    /// </summary>
    public event EventHandler<bool>? ConnectionStateChanged;

    /// <summary>
    /// Initializes a new instance of the TrayIconService.
    /// </summary>
    /// <param name="logger">The logging service for recording events and errors.</param>
    /// <param name="grpcClient">Optional gRPC client for communicating with the background service.</param>
    public TrayIconService(RJAutoMoverShared.Services.LoggingService logger, GrpcClientServiceV2? grpcClient)
    {
        _logger = logger;
        _grpcClient = grpcClient;

        // Subscribe to gRPC events if client is provided
        if (_grpcClient != null)
        {
            SubscribeToGrpcEvents();
        }
    }

    /// <summary>
    /// Sets or updates the gRPC client used for communication with the background service.
    /// Automatically unsubscribes from the old client and subscribes to the new one.
    /// </summary>
    /// <param name="grpcClient">The new gRPC client to use.</param>
    public void SetGrpcClient(GrpcClientServiceV2 grpcClient)
    {
        // Unsubscribe from old client if it exists
        if (_grpcClient != null)
        {
            UnsubscribeFromGrpcEvents();
        }

        _grpcClient = grpcClient;
        SubscribeToGrpcEvents();
    }

    /// <summary>
    /// Subscribes to all gRPC client events to receive updates from the background service.
    /// </summary>
    private void SubscribeToGrpcEvents()
    {
        if (_grpcClient == null) return;

        _grpcClient.StatusUpdated += OnStatusUpdated;
        _grpcClient.IconUpdated += OnIconUpdated;
        _grpcClient.RecentsUpdated += OnRecentsUpdated;
        _grpcClient.ConnectionStateChanged += OnConnectionStateChanged;
        _grpcClient.FileTransferNotified += OnFileTransferNotified;
    }

    /// <summary>
    /// Unsubscribes from all gRPC client events when changing clients.
    /// </summary>
    private void UnsubscribeFromGrpcEvents()
    {
        if (_grpcClient == null) return;

        _grpcClient.StatusUpdated -= OnStatusUpdated;
        _grpcClient.IconUpdated -= OnIconUpdated;
        _grpcClient.RecentsUpdated -= OnRecentsUpdated;
        _grpcClient.ConnectionStateChanged -= OnConnectionStateChanged;
        _grpcClient.FileTransferNotified -= OnFileTransferNotified;
    }

    /// <summary>
    /// Creates and initializes the system tray icon with its context menu.
    /// The icon starts in a "stopped" state until the service connection is established.
    /// </summary>
    /// <returns>The initialized TaskbarIcon instance.</returns>
    public TaskbarIcon CreateTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "RJService Tray",
            Icon = LoadIcon("stopped.ico"), // Start with stopped icon until service status is verified
            Visibility = Visibility.Visible,
            ContextMenu = CreateContextMenu()
        };

        _currentIconName = "stopped.ico";

        // Initialize status to show service is not yet connected
        if (_statusMenuItem != null)
        {
            _statusMenuItem.Header = "Status: Service Disconnected";
        }

        // Initially disable menu items since service is not connected
        UpdateMenuItemStates(false);

        // Set initial tooltip
        UpdateTooltip();

        return _trayIcon;
    }

    /// <summary>
    /// Creates the context menu for the tray icon with all menu items.
    /// Includes: Status (read-only), Pause/Resume Processing, View Recent Transfers, and About.
    /// </summary>
    /// <returns>The configured ContextMenu instance.</returns>
    private ContextMenu CreateContextMenu()
    {
        var contextMenu = new ContextMenu();

        // Status
        _statusMenuItem = new MenuItem
        {
            Header = "Status: Waiting...",
            IsEnabled = false,
            FontWeight = FontWeights.Bold
        };
        contextMenu.Items.Add(_statusMenuItem);
        contextMenu.Items.Add(new Separator());

        // Toggle Processing
        _toggleProcessingMenuItem = new MenuItem
        {
            Header = "Pause Processing"
        };
        _toggleProcessingMenuItem.Click += async (s, e) => await ToggleProcessing();
        contextMenu.Items.Add(_toggleProcessingMenuItem);
        contextMenu.Items.Add(new Separator());

        // View Recent
        _recentsMenuItem = new MenuItem
        {
            Header = "View Recent Transfers..."
        };
        _recentsMenuItem.Click += (s, e) => ShowRecentTransfersWindow();
        contextMenu.Items.Add(_recentsMenuItem);
        contextMenu.Items.Add(new Separator());

        // About
        var aboutMenuItem = new MenuItem
        {
            Header = "About..."
        };
        aboutMenuItem.Click += (s, e) => ShowAboutWindow();
        contextMenu.Items.Add(aboutMenuItem);

        return contextMenu;
    }

    /// <summary>
    /// Toggles the file processing state (pause/resume) by sending a request to the background service.
    /// The UI updates will be handled via event callbacks when the service confirms the state change.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ToggleProcessing()
    {
        var currentPauseState = _isPaused;
        var newPauseState = !currentPauseState;

        _logger.Log(LogLevel.INFO, $"Requesting toggle processing to: {(newPauseState ? "Paused" : "Active")}");
        var success = _grpcClient != null ? await _grpcClient.ToggleProcessingAsync(newPauseState) : false;

        if (success)
        {
            _logger.Log(LogLevel.INFO, $"Toggle processing request successful - waiting for service confirmation");
            // Don't update anything here - let the service broadcast the status/icon changes
            // The OnStatusUpdated and OnIconUpdated methods will handle the actual UI updates
        }
        else
        {
            _logger.Log(LogLevel.ERROR, "Failed to toggle processing");
        }
    }

    /// <summary>
    /// Gets the initial transfer history from the service for display in the About window.
    /// Requests fresh status from service which includes database history, not just cached data.
    /// </summary>
    /// <returns>A list of transfer history strings including database records.</returns>
    public async Task<List<string>> GetInitialTransferHistoryAsync()
    {
        _logger.Log(LogLevel.DEBUG, $"GetInitialTransferHistoryAsync: Cached items={_cachedRecents.Count}");

        try
        {
            // Request fresh status from service which includes historical database records
            _logger.Log(LogLevel.DEBUG, "GetInitialTransferHistoryAsync: Calling GetFullServiceStatusAsync...");
            var status = await _grpcClient.GetFullServiceStatusAsync();

            if (status == null)
            {
                _logger.Log(LogLevel.WARN, "GetInitialTransferHistoryAsync: GetFullServiceStatusAsync returned null");
                return _cachedRecents.ToList();
            }

            _logger.Log(LogLevel.DEBUG, $"GetInitialTransferHistoryAsync: Status received - IsRunning={status.IsRunning}, IsPaused={status.IsPaused}, RecentItems.Count={status.RecentItems?.Count ?? 0}");

            if (status.RecentItems != null && status.RecentItems.Count > 0)
            {
                _logger.Log(LogLevel.INFO, $"GetInitialTransferHistoryAsync: Retrieved {status.RecentItems.Count} items from service (includes database history)");

                // Log first few items for debugging
                for (int i = 0; i < Math.Min(3, status.RecentItems.Count); i++)
                {
                    _logger.Log(LogLevel.DEBUG, $"  Item {i}: {status.RecentItems[i].Substring(0, Math.Min(100, status.RecentItems[i].Length))}...");
                }

                // Update cache with fresh data including history
                _cachedRecents = status.RecentItems.ToList();

                return _cachedRecents;
            }
            else
            {
                _logger.Log(LogLevel.WARN, "GetInitialTransferHistoryAsync: Service returned no activities (RecentItems is null or empty)");
                return _cachedRecents.ToList(); // Fall back to cache if service returns nothing
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"GetInitialTransferHistoryAsync: Error fetching from service: {ex.Message}");
            _logger.Log(LogLevel.ERROR, $"GetInitialTransferHistoryAsync: Stack trace: {ex.StackTrace}");
            // Fall back to cached data on error
            return _cachedRecents.ToList();
        }
    }

    /// <summary>
    /// Gets database statistics from the service via gRPC.
    /// </summary>
    public async Task<DatabaseStatistics?> GetDatabaseStatistics()
    {
        try
        {
            var systemInfo = await _grpcClient.GetServiceSystemInfoAsync();

            // Parse database statistics from system info response
            var stats = new DatabaseStatistics
            {
                DatabasePath = systemInfo.DatabasePath ?? "(unknown)",
                IsEnabled = systemInfo.DatabaseEnabled,
                IsConnected = systemInfo.DatabaseConnected,
                TotalRecords = systemInfo.DatabaseRecordCount,
                LastTransferTimestamp = !string.IsNullOrEmpty(systemInfo.DatabaseLastTransfer)
                    ? DateTime.Parse(systemInfo.DatabaseLastTransfer)
                    : null
            };

            return stats;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Failed to get database statistics: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Updates the toggle menu item text to reflect the current pause state.
    /// </summary>
    /// <param name="isPaused">True if processing is paused, false if active.</param>
    private void UpdateToggleMenuItem(bool isPaused)
    {
        if (_toggleProcessingMenuItem != null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _toggleProcessingMenuItem.Header = isPaused ? "Resume Processing" : "Pause Processing";
            });
        }
    }

    /// <summary>
    /// Updates the enabled/disabled state of menu items based on connection and error states.
    /// The toggle processing menu is disabled when disconnected or in an error state.
    /// </summary>
    /// <param name="isConnected">True if connected to the background service.</param>
    private void UpdateMenuItemStates(bool isConnected)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_toggleProcessingMenuItem != null)
            {
                // Disable if not connected OR if in error state
                bool isInError = IsInErrorState(_currentStatus);
                _toggleProcessingMenuItem.IsEnabled = isConnected && !isInError;
            }

            if (_recentsMenuItem != null)
            {
                _recentsMenuItem.IsEnabled = true; // Always enabled - shows window regardless of connection
            }
        });

        // Update the About window toggle button state
        bool isInError = IsInErrorState(_currentStatus);
        _aboutWindow?.SetTransfersToggleButtonEnabled(isConnected && !isInError);

        _logger.Log(LogLevel.DEBUG, $"Menu items {(isConnected ? "enabled" : "disabled")} - connection state: {isConnected}");
    }

    /// <summary>
    /// Handles status updates from the background service.
    /// Updates the status menu item, tooltip, and error details as needed.
    /// </summary>
    private void OnStatusUpdated(object? sender, string status)
    {
        string oldStatus = _currentStatus;

        // Parse status format: "Short message|||Detailed message"
        string displayStatus = status;
        string detailedError = status;

        if (status.Contains("|||"))
        {
            var parts = status.Split(new[] { "|||" }, StringSplitOptions.None);
            displayStatus = parts[0]; // Short message for tray icon
            detailedError = parts.Length > 1 ? parts[1] : status; // Detailed message for Error tab
            _currentStatus = displayStatus; // Store only the short message
        }
        else
        {
            _currentStatus = status;

            // Legacy handling: Show generic message for validation errors in menu item
            if (status.StartsWith("Configuration validation failed:", StringComparison.OrdinalIgnoreCase))
            {
                displayStatus = "Status: Configuration validation failed";
            }
        }

        if (_statusMenuItem != null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _statusMenuItem.Header = displayStatus.StartsWith("Status:") ? displayStatus : $"Status: {displayStatus}";
            });
        }

        // Update tooltip with new status
        UpdateTooltip();

        // Send tray state update to service if error state changed
        bool oldErrorState = IsInErrorState(oldStatus);
        bool newErrorState = IsInErrorState(_currentStatus);

        if (oldErrorState != newErrorState)
        {
            UpdateTrayState();

            // Clear error details if we're no longer in an error state
            if (!newErrorState)
            {
                _errorDetails = string.Empty;

                // Update About window if it's open to remove the error tab
                _aboutWindow?.UpdateErrorStatus(null, false);
            }
            else
            {
                // Store detailed error for Error tab
                _errorDetails = detailedError;

                // Update About window if it's open to show the Error tab with detailed message
                _aboutWindow?.UpdateErrorStatus(detailedError, true);
            }

            // Update menu item states when error state changes
            UpdateMenuItemStates(_grpcClient?.IsConnected ?? false);
        }

        // Don't update icon here - let OnIconUpdated handle all icon changes
        // This prevents status updates from overriding explicit icon updates from the service
    }

    /// <summary>
    /// Handles icon change notifications from the background service.
    /// Updates the tray icon and infers the pause state from the icon name.
    /// </summary>
    private void OnIconUpdated(object? sender, string iconName)
    {
        _logger.Log(LogLevel.DEBUG,
            $"[ICON UPDATE] Received icon update from service: {iconName}");

        _currentIconName = iconName;
        UpdateIcon(iconName);

        bool oldPausedState = _isPaused;

        // Update pause state based on icon
        if (iconName == "paused.ico")
        {
            _isPaused = true;
            UpdateToggleMenuItem(true);
            _aboutWindow?.SetProcessingPaused(true);
        }
        else if (iconName == "active.ico" || iconName == "waiting.ico")
        {
            _isPaused = false;
            UpdateToggleMenuItem(false);
            _aboutWindow?.SetProcessingPaused(false);
        }

        // Send tray state update to service if paused state changed
        if (oldPausedState != _isPaused)
        {
            UpdateTrayState();
        }
    }

    /// <summary>
    /// Handles recent transfer updates from the background service.
    /// Logs transfer activity and updates the About window's Transfers tab if open.
    /// </summary>
    private void OnRecentsUpdated(object? sender, List<string> recents)
    {
        // Cache the recents for when the About window is opened
        _cachedRecents = new List<string>(recents);

        // Log recent activity updates
        if (recents.Count > 0)
        {
            var latestActivity = recents.FirstOrDefault();
            if (!string.IsNullOrEmpty(latestActivity))
            {
                _logger.Log(LogLevel.INFO, $"Received transfer update: {latestActivity}");

                // If this is a config error message, store it for the About window
                if (latestActivity.StartsWith("CONFIG ERROR:", StringComparison.OrdinalIgnoreCase))
                {
                    _errorDetails = latestActivity.Substring("CONFIG ERROR:".Length).Trim();
                }
            }
        }

        // Update the About window if it's open
        _aboutWindow?.UpdateTransfers(recents);
    }

    /// <summary>
    /// Shows the About window with the Transfers tab selected.
    /// Called when the user clicks "View Recent Transfers" in the context menu.
    /// </summary>
    private void ShowRecentTransfersWindow()
    {
        ShowAboutWindow("Transfers");
    }

    /// <summary>
    /// Shows the About window, optionally opening to a specific tab.
    /// If the window is already open, it will be activated instead of creating a new instance.
    /// </summary>
    /// <param name="initialTab">Optional tab name to open (e.g., "Transfers", "Version"). Defaults to "Version" if null.</param>
    private void ShowAboutWindow(string? initialTab = null)
    {
        // If window already exists and is visible, just activate it
        if (_aboutWindow != null && _aboutWindow.IsVisible)
        {
            _aboutWindow.Activate();
            if (_aboutWindow.WindowState == WindowState.Minimized)
            {
                _aboutWindow.WindowState = WindowState.Normal;
            }
            return;
        }

        // Determine if we're in an error state and get appropriate error message
        bool hasError = IsInErrorState(_currentStatus);
        string errorMessage = hasError && !string.IsNullOrEmpty(_errorDetails)
            ? _errorDetails
            : _currentStatus;

        // Create the About window with error info and initial tab selection
        _aboutWindow = new AboutWindow(errorMessage, hasError, initialTab);

        // Provide the window with access to the gRPC client and tray service
        if (_grpcClient != null)
        {
            _aboutWindow.SetGrpcClient(_grpcClient);
        }
        _aboutWindow.SetTrayIconService(this);

        // Sync the window's state with current tray state
        _aboutWindow.SetProcessingPaused(_isPaused);

        // Update toggle button state based on connection and error state
        bool isConnected = _grpcClient?.IsConnected ?? false;
        bool isInError = IsInErrorState(_currentStatus);
        _aboutWindow.SetTransfersToggleButtonEnabled(isConnected && !isInError);

        // Populate the window with cached recent transfers
        if (_cachedRecents.Count > 0)
        {
            _aboutWindow.UpdateTransfers(_cachedRecents);
        }

        // Clean up reference when window is closed
        _aboutWindow.Closed += (s, e) => _aboutWindow = null;
        _aboutWindow.ShowDialog();
    }


    /// <summary>
    /// Handles file transfer notifications from the service and displays balloon tooltips.
    /// Shows notifications for transfer start, completion, and failures.
    /// </summary>
    private void OnFileTransferNotified(object? sender, RJAutoMoverShared.Protos.FileTransferNotification notification)
    {
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                switch (notification.Status)
                {
                    case RJAutoMoverShared.Protos.TransferStatus.TransferStarted:
                        _logger.Log(LogLevel.INFO, $"File transfer starting: {notification.FileName} ({notification.FileSizeBytes} bytes) [{notification.RuleName}]");

                        // Show tray balloon notification for transfer start
                        if (_trayIcon != null)
                        {
                            _trayIcon.ShowBalloonTip("File Transfer Starting",
                                $"Moving: {notification.FileName}\nRule: {notification.RuleName}",
                                BalloonIcon.Info);
                        }
                        break;

                    case RJAutoMoverShared.Protos.TransferStatus.TransferCompleted:
                        _logger.Log(LogLevel.INFO, $"File transfer completed: {notification.FileName}");

                        // Show tray balloon notification for success
                        if (_trayIcon != null)
                        {
                            _trayIcon.ShowBalloonTip("File Transfer Completed",
                                $"Successfully moved: {notification.FileName}",
                                BalloonIcon.Info);
                        }
                        break;

                    case RJAutoMoverShared.Protos.TransferStatus.TransferFailed:
                        _logger.Log(LogLevel.ERROR, $"File transfer failed: {notification.FileName} - {notification.Message}");

                        // Show tray balloon notification for failure
                        if (_trayIcon != null)
                        {
                            _trayIcon.ShowBalloonTip("File Transfer Failed",
                                $"Failed to move: {notification.FileName}\nError: {notification.Message}",
                                BalloonIcon.Error);
                        }
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Error handling file transfer notification: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles connection state changes to the background service.
    /// Updates menu states and icon based on whether service is connected or disconnected.
    /// </summary>
    private void OnConnectionStateChanged(object? sender, bool isConnected)
    {
        _logger.Log(LogLevel.DEBUG, $"OnConnectionStateChanged called - isConnected={isConnected}, sender={sender?.GetType().Name ?? "null"}");

        // Update menu item states based on connection
        UpdateMenuItemStates(isConnected);

        if (!isConnected)
        {
            _logger.Log(LogLevel.WARN, "[CONNECTION LOST] Lost connection to service - updating tray to disconnected state (stopped.ico)");
            UpdateIcon("stopped.ico");
            OnStatusUpdated(null, "Service Disconnected");
        }
        else
        {
            _logger.Log(LogLevel.INFO, "[CONNECTION ESTABLISHED] Connected to service - tray will now receive status updates");

            // Send initial tray state to service upon connection
            UpdateTrayState();
        }

        // Fire connection state changed event for other components (like AboutWindow)
        ConnectionStateChanged?.Invoke(this, isConnected);
    }

    /// <summary>
    /// Updates the tray icon and fires the IconChanged event for other components.
    /// </summary>
    /// <param name="iconName">The name of the icon to load (e.g., "active.ico", "paused.ico").</param>
    private void UpdateIcon(string iconName)
    {
        if (_trayIcon != null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                string oldIcon = _currentIconName ?? "(none)";

                // Log icon changes with caller information
                var stackTrace = new System.Diagnostics.StackTrace(1, true);
                var callerFrame = stackTrace.GetFrame(0);
                string callerInfo = callerFrame != null
                    ? $"{callerFrame.GetMethod()?.Name}:{callerFrame.GetFileLineNumber()}"
                    : "Unknown";

                _logger.Log(LogLevel.INFO,
                    $"[ICON CHANGE] {oldIcon} → {iconName} | Caller: {callerInfo}");

                _trayIcon.Icon = LoadIcon(iconName);
                IconChanged?.Invoke(this, iconName);
            });
        }
    }

    /// <summary>
    /// Gets the name of the currently displayed tray icon.
    /// </summary>
    /// <returns>The icon file name (e.g., "active.ico").</returns>
    public string? GetCurrentIconName()
    {
        return _currentIconName;
    }

    /// <summary>
    /// Updates the tray icon tooltip to display the current status message.
    /// For validation errors, shows a generic message instead of the full error details.
    /// </summary>
    private void UpdateTooltip()
    {
        if (_trayIcon != null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // If this is a validation error, show generic message in tooltip
                // Full details are available in the Error tab
                string tooltipText = _currentStatus;
                if (_currentStatus.StartsWith("Configuration validation failed:", StringComparison.OrdinalIgnoreCase))
                {
                    tooltipText = "Configuration validation failed";
                }

                _trayIcon.ToolTipText = tooltipText;
            });
        }
    }

    /// <summary>
    /// Loads an icon from embedded resources.
    /// Falls back to a base icon if the requested icon is not found.
    /// </summary>
    /// <param name="iconName">The name of the icon file to load.</param>
    /// <returns>The loaded icon, or a system default icon if loading fails.</returns>
    private System.Drawing.Icon LoadIcon(string iconName)
    {
        try
        {
            // Load icon from embedded resources
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = $"RJAutoMoverTray.{iconName}";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                return new System.Drawing.Icon(stream);
            }

            // Fallback to base icon
            resourceName = "RJAutoMoverTray.base.ico";
            using var fallbackStream = assembly.GetManifestResourceStream(resourceName);
            if (fallbackStream != null)
            {
                return new System.Drawing.Icon(fallbackStream);
            }

            // Last resort - create a default icon
            _logger.Log(LogLevel.WARN, $"Could not find embedded icon {iconName}, using system default");
            return System.Drawing.SystemIcons.Application;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Failed to load embedded icon {iconName}: {ex.Message}");
            return System.Drawing.SystemIcons.Application;
        }
    }


    /// <summary>
    /// Determines if the current status indicates an error state.
    /// Checks for keywords like "error", "failed", "disconnected", or "config changed".
    /// </summary>
    /// <param name="status">The status message to check.</param>
    /// <returns>True if the status indicates an error state.</returns>
    private bool IsInErrorState(string status)
    {
        if (string.IsNullOrEmpty(status))
            return false;

        return status.ToLower().Contains("error") ||
               status.ToLower().Contains("failed") ||
               status.ToLower().Contains("disconnected") ||
               status.ToLower().Contains("config changed");
    }

    /// <summary>
    /// Logs the current tray state for debugging purposes.
    /// The service tracks tray state automatically via events.
    /// </summary>
    private void UpdateTrayState()
    {
        try
        {
            if (_grpcClient == null)
            {
                _logger.Log(LogLevel.DEBUG, "Cannot send tray state update - gRPC client not available");
                return;
            }

            // Tray state is now tracked automatically by the service via event subscriptions
            _logger.Log(LogLevel.DEBUG, $"Tray state: paused={_isPaused}, status='{_currentStatus}'");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.WARN, $"Failed to send tray state update: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the current state of the tray icon for external components.
    /// Used by the gRPC server to query tray state.
    /// </summary>
    /// <returns>A TrayState object containing current pause state, error state, status, and icon name.</returns>
    public TrayState GetCurrentState()
    {
        return new TrayState
        {
            IsPaused = _isPaused,
            IsInErrorState = IsInErrorState(_currentStatus),
            Status = _currentStatus,
            CurrentIconName = _currentIconName
        };
    }

}

/// <summary>
/// Represents the current state of the tray icon.
/// Used for communication between the tray service and other components.
/// </summary>
public class TrayState
{
    /// <summary>
    /// True if file processing is currently paused.
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>
    /// True if the service is in an error state (disconnected, config error, etc.).
    /// </summary>
    public bool IsInErrorState { get; set; }

    /// <summary>
    /// The current status message displayed in the tray menu.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// The name of the currently displayed icon (e.g., "active.ico", "paused.ico").
    /// </summary>
    public string CurrentIconName { get; set; } = string.Empty;
}
