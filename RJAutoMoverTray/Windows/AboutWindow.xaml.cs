using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using RJAutoMoverShared.Models;
using RJAutoMoverShared.Services;
using RJAutoMoverTray.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RJAutoMoverTray.Windows;

/// <summary>
/// The About window displays application information across multiple tabs:
/// Transfers (recent file transfers), Config (application settings), Logs (service and tray logs),
/// System (installation and resource info), .NET (build and runtime info), and Version (app versions).
/// Also displays an Error tab when the service is in an error state.
/// </summary>
public partial class AboutWindow : Window
{
    // Window state
    private string? _errorStatus;
    private bool _hasError;
    private readonly string? _initialTab;

    // Service references
    private GrpcClientServiceV2? _grpcClient;
    private Services.TrayIconService? _trayIconService;

    // Transfers tab state
    private System.Windows.Threading.DispatcherTimer? _transfersAnimationTimer;
    private readonly string[] _brailleFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
    private System.Collections.ObjectModel.ObservableCollection<TransferDisplayItem> _transfers = new();
    private List<TransferDisplayItem> _allTransfers = new(); // Source data for filtering/sorting
    private bool _isProcessingPaused = false;
    private DateTime _serviceStartTime = DateTime.MinValue;
    private string _currentSortColumn = "Time";
    private bool _sortAscending = false;
    private string _transfersSearchText = "";
    private string _selectedSessionFilter = "All";

    // Version checking timer
    private System.Windows.Threading.DispatcherTimer? _versionCheckTimer;

    /// <summary>
    /// Initializes the About window with optional error information and initial tab selection.
    /// </summary>
    /// <param name="errorStatus">Optional error message to display if hasError is true.</param>
    /// <param name="hasError">True to show the Error tab and display error information.</param>
    /// <param name="initialTab">Optional name of tab to open (e.g., "Transfers"). Defaults to "Version" if null.</param>
    public AboutWindow(string? errorStatus = null, bool hasError = false, string? initialTab = null)
    {
        try
        {
            _errorStatus = errorStatus;
            _hasError = hasError;
            _initialTab = initialTab;

            InitializeComponent();
            LoadAboutInformation();
            LoadLogs(); // Load logs initially

            var trayService = App.Current.Properties["TrayIconService"] as Services.TrayIconService;
            _trayIconService = trayService;
            UpdateHeaderColor(trayService?.GetCurrentIconName());

            // Set up the transfers tab
            TransfersItemsControl.ItemsSource = _transfers;

            // Set database path display
            var databasePath = System.IO.Path.Combine(
                RJAutoMoverShared.Constants.Paths.GetSharedDataFolder(),
                "ActivityHistory.db");
            if (DatabasePathText != null)
            {
                DatabasePathText.Text = $"Activity history database: {databasePath}";
            }

            // Setup animation timer for transfers
            _transfersAnimationTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _transfersAnimationTimer.Tick += TransfersAnimationTimer_Tick;
            _transfersAnimationTimer.Start();

            // Setup periodic version check timer (refresh every minute)
            _versionCheckTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _versionCheckTimer.Tick += async (s, e) => await RefreshVersionTab();
            _versionCheckTimer.Start();

            // Show error tab if in error state
            if (_hasError && ErrorTab != null)
            {
                ErrorTab.Visibility = Visibility.Visible;
                TabControl.SelectedItem = ErrorTab;
                LoadErrorInformation();
            }
            else if (ErrorTab != null)
            {
                ErrorTab.Visibility = Visibility.Collapsed;
            }

            // Select initial tab if specified, otherwise default to Version tab
            if (!string.IsNullOrEmpty(_initialTab))
            {
                SelectTabByName(_initialTab);
            }
            else if (!_hasError)
            {
                // Default to Version tab when opening normally (not from error state)
                SelectTabByName("Version");
            }

            // Subscribe to icon changes
            if (trayService != null)
            {
                trayService.IconChanged += (s, iconName) => UpdateHeaderColor(iconName);
                // Subscribe to connection state changes to refresh config when service reconnects
                trayService.ConnectionStateChanged += OnServiceConnectionStateChanged;
            }

            // Subscribe to tab changes to refresh Version tab when opened
            TabControl.SelectionChanged += TabControl_SelectionChanged;

            // Load initial transfer history after window content is rendered
            // NOTE: Service start time is loaded in SetGrpcClient() which is called BEFORE this event
            if (trayService != null)
            {
                this.ContentRendered += async (s, e) =>
                {
                    await LoadInitialTransferHistory(trayService);
                };
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error initializing About window: {ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                "About Window Error", MessageBoxButton.OK, MessageBoxImage.Error);
            throw; // Re-throw to prevent window from opening in bad state
        }
    }

    /// <summary>
    /// Ensures the service start time is loaded before parsing transfers.
    /// This is critical for correctly determining IsCurrentSession in ParseTransferItem().
    /// </summary>
    private async Task EnsureServiceStartTimeLoaded()
    {
        try
        {
            if (_grpcClient == null)
            {
                System.Diagnostics.Debug.WriteLine("EnsureServiceStartTimeLoaded: GrpcClient is null");
                return;
            }

            // Get service system info to retrieve start time
            var serviceInfo = await _grpcClient.GetServiceSystemInfoAsync();
            if (serviceInfo != null)
            {
                var serviceStartTime = DateTimeOffset.FromUnixTimeMilliseconds(serviceInfo.StartTimeUnixMs).LocalDateTime;
                _serviceStartTime = serviceStartTime;
                System.Diagnostics.Debug.WriteLine($"EnsureServiceStartTimeLoaded: Set service start time to {_serviceStartTime:yyyy-MM-dd HH:mm:ss}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("EnsureServiceStartTimeLoaded: GetServiceSystemInfoAsync returned null");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading service start time: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads initial transfer history when the About window opens.
    /// Called in ContentRendered event to ensure UI is fully initialized.
    /// </summary>
    private async Task LoadInitialTransferHistory(Services.TrayIconService trayService)
    {
        try
        {
            var transfers = await trayService.GetInitialTransferHistoryAsync();
            System.Diagnostics.Debug.WriteLine($"LoadInitialTransferHistory: Retrieved {transfers?.Count ?? 0} transfers");

            if (transfers != null && transfers.Count > 0)
            {
                UpdateTransfers(transfers);
                System.Diagnostics.Debug.WriteLine($"LoadInitialTransferHistory: Called UpdateTransfers with {transfers.Count} items");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("LoadInitialTransferHistory: No transfers to display");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading initial transfer history: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Loads database statistics from the service via gRPC.
    /// </summary>
    private async Task LoadDatabaseStatistics()
    {
        try
        {
            if (_trayIconService == null)
            {
                SystemDatabasePathText.Text = "(unavailable)";
                SystemDatabaseStatusText.Text = "Service not connected";
                SystemDatabaseRecordCountText.Text = "(unavailable)";
                SystemDatabaseLastTransferText.Text = "(unavailable)";
                return;
            }

            var stats = await _trayIconService.GetDatabaseStatistics();

            if (stats == null)
            {
                SystemDatabasePathText.Text = "(unavailable)";
                SystemDatabaseStatusText.Text = "Failed to retrieve statistics";
                SystemDatabaseRecordCountText.Text = "(unavailable)";
                SystemDatabaseLastTransferText.Text = "(unavailable)";
                return;
            }

            // Display database path
            SystemDatabasePathText.Text = stats.DatabasePath;

            // Display status with color coding
            if (!stats.IsEnabled)
            {
                SystemDatabaseStatusText.Text = "Disabled";
                SystemDatabaseStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)); // Gray
            }
            else if (!stats.IsConnected)
            {
                SystemDatabaseStatusText.Text = "Not Connected";
                SystemDatabaseStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00)); // Red
            }
            else
            {
                SystemDatabaseStatusText.Text = "Connected";
                SystemDatabaseStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00)); // Green
            }

            // Display record count
            SystemDatabaseRecordCountText.Text = stats.TotalRecords.ToString("N0");

            // Display last transfer timestamp
            if (stats.LastTransferTimestamp.HasValue)
            {
                var lastTransfer = stats.LastTransferTimestamp.Value;
                var timeSince = DateTime.Now - lastTransfer;

                if (timeSince.TotalDays >= 1)
                {
                    SystemDatabaseLastTransferText.Text = $"{lastTransfer:yyyy-MM-dd HH:mm:ss} ({(int)timeSince.TotalDays} days ago)";
                }
                else if (timeSince.TotalHours >= 1)
                {
                    SystemDatabaseLastTransferText.Text = $"{lastTransfer:yyyy-MM-dd HH:mm:ss} ({(int)timeSince.TotalHours} hours ago)";
                }
                else
                {
                    SystemDatabaseLastTransferText.Text = $"{lastTransfer:yyyy-MM-dd HH:mm:ss} ({(int)timeSince.TotalMinutes} minutes ago)";
                }
            }
            else
            {
                SystemDatabaseLastTransferText.Text = "(no transfers recorded)";
            }

            System.Diagnostics.Debug.WriteLine($"Database statistics loaded: Path={stats.DatabasePath}, Records={stats.TotalRecords}, Connected={stats.IsConnected}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading database statistics: {ex.Message}");
            SystemDatabasePathText.Text = "(error)";
            SystemDatabaseStatusText.Text = $"Error: {ex.Message}";
            SystemDatabaseRecordCountText.Text = "(error)";
            SystemDatabaseLastTransferText.Text = "(error)";
        }
    }

    /// <summary>
    /// Handles tab selection changes to refresh content when tabs are opened.
    /// </summary>
    private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl)
        {
            var selectedTab = TabControl.SelectedItem as TabItem;
            if (selectedTab?.Header?.ToString() == "Version")
            {
                // Refresh version information when Version tab is opened
                // Fire and forget - don't block tab opening on version check
                _ = RefreshVersionTab();
            }
            else if (selectedTab?.Header?.ToString() == "Status")
            {
                // Load database statistics when Status tab is opened
                await LoadDatabaseStatistics();
            }
        }
    }

    /// <summary>
    /// Refreshes all version information including update checks.
    /// </summary>
    private async System.Threading.Tasks.Task RefreshVersionTab()
    {
        // Yield immediately to allow UI to render
        await System.Threading.Tasks.Task.Yield();

        // Refresh tray version
        var (trayVersion, trayBuildDate) = GetTrayVersionInfo();
        TrayVersionText.Text = trayVersion;
        TrayBuildDateText.Text = trayBuildDate;

        // Refresh service version
        await LoadServiceVersionInfo();

        // Check for tray updates
        if (!string.IsNullOrEmpty(trayVersion) && trayVersion != "Unknown")
        {
            try
            {
                var (versionStatus, latestVersion, installerUrl) = await VersionCheckerService.CheckForUpdatesAsync(trayVersion);
                if (!string.IsNullOrEmpty(latestVersion))
                {
                    if (versionStatus > 0)
                    {
                        TrayNewVersionRun.Text = latestVersion?.Trim();
                        TrayUpdateLink.NavigateUri = new Uri(installerUrl);
                        TrayUpdateLinkText.Visibility = Visibility.Visible;
                        TrayLatestVersionText.Visibility = Visibility.Collapsed;
                        TrayPreReleaseText.Visibility = Visibility.Collapsed;
                        TrayUnableToConfirmText.Visibility = Visibility.Collapsed;
                    }
                    else if (versionStatus < 0)
                    {
                        TrayUpdateLinkText.Visibility = Visibility.Collapsed;
                        TrayLatestVersionText.Visibility = Visibility.Collapsed;
                        TrayPreReleaseText.Visibility = Visibility.Visible;
                        TrayUnableToConfirmText.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        TrayUpdateLinkText.Visibility = Visibility.Collapsed;
                        TrayLatestVersionText.Visibility = Visibility.Visible;
                        TrayPreReleaseText.Visibility = Visibility.Collapsed;
                        TrayUnableToConfirmText.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    TrayUpdateLinkText.Visibility = Visibility.Collapsed;
                    TrayLatestVersionText.Visibility = Visibility.Collapsed;
                    TrayPreReleaseText.Visibility = Visibility.Collapsed;
                    TrayUnableToConfirmText.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                TrayUpdateLinkText.Visibility = Visibility.Collapsed;
                TrayLatestVersionText.Visibility = Visibility.Collapsed;
                TrayPreReleaseText.Visibility = Visibility.Collapsed;
                TrayUnableToConfirmText.Visibility = Visibility.Visible;
            }
        }
    }


    private void LoadAboutInformation()
    {
        try
        {
            // Load the icon
            LoadApplicationIcon();

            // Load version information
            LoadVersionInformation();

            // Load memory usage
            LoadMemoryUsage();

            // Load system information
            LoadSystemInformation();

            // Load .NET build information
            LoadDotNetInformation();

            // Load configuration
            LoadConfiguration();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading about information: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadErrorInformation()
    {
        if (ErrorMessageText != null)
        {
            if (!string.IsNullOrEmpty(_errorStatus))
            {
                // Check if this is a detailed config validation error (starts with "Configuration validation failed:")
                bool isDetailedConfigError = _errorStatus.Contains("Configuration validation failed:");

                if (isDetailedConfigError)
                {
                    // Display the detailed validation errors with better formatting
                    var lines = _errorStatus.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    var formattedLines = new List<string>();

                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            formattedLines.Add(trimmed);
                        }
                    }

                    ErrorMessageText.Text = string.Join("\n", formattedLines) + "\n\n" +
                        "HOW TO FIX:\n" +
                        "1. Open the configuration file:\n" +
                        "   C:\\Program Files\\RJAutoMover\\config.yaml\n\n" +
                        "2. Fix the errors listed above\n\n" +
                        "3. Restart the RJAutoMoverService:\n" +
                        "   • Press Win+R\n" +
                        "   • Type: services.msc\n" +
                        "   • Find 'RJAutoMover Service'\n" +
                        "   • Right-click → Restart\n\n" +
                        "Check service logs for more details:\n" +
                        "C:\\ProgramData\\RJAutoMover\\Logs\\";
                }
                else
                {
                    // Check if this is a generic status message
                    bool isGenericStatus = _errorStatus.Equals("Config Error", StringComparison.OrdinalIgnoreCase) ||
                                           _errorStatus.Equals("Service Disconnected", StringComparison.OrdinalIgnoreCase) ||
                                           _errorStatus.ToLower().Contains("error") && _errorStatus.Length < 50;

                    if (isGenericStatus)
                    {
                        // Show generic troubleshooting info for generic status messages
                        ErrorMessageText.Text = $"Status: {_errorStatus}\n\n" +
                            "The service is in an error state. Common causes include:\n\n" +
                            "• Invalid configuration file (config.yaml)\n" +
                            "• Missing or inaccessible folders\n" +
                            "• Port conflicts (default ports: 60051, 60052)\n" +
                            "• Insufficient permissions\n" +
                            "• Service not running or crashed\n\n" +
                            "Check the service log files for detailed error information:\n" +
                            "C:\\ProgramData\\RJAutoMover\\Logs\\";
                    }
                    else
                    {
                        // Display the specific error message
                        var errorLines = _errorStatus.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        var formattedError = string.Join("\n", errorLines.Select(line => line.Trim()));
                        ErrorMessageText.Text = formattedError + "\n\n" +
                            "Check service logs for more details:\n" +
                            "C:\\ProgramData\\RJAutoMover\\Logs\\";
                    }
                }
            }
            else
            {
                ErrorMessageText.Text = "The service is in an error state, but no specific error message is available.\n\n" +
                    "Common causes:\n" +
                    "• Invalid configuration file (config.yaml)\n" +
                    "• Missing or inaccessible folders\n" +
                    "• Port conflicts\n" +
                    "• Insufficient permissions\n\n" +
                    "Check the service log files for detailed error information:\n" +
                    "C:\\ProgramData\\RJAutoMover\\Logs\\";
            }
        }
    }

    private void LoadApplicationIcon()
    {
        try
        {
            // Load base.ico from embedded resources
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "RJAutoMoverTray.base.ico";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                // Convert ICO to BitmapImage for WPF Image control
                var icon = new System.Drawing.Icon(stream);
                using var bitmap = icon.ToBitmap();
                using var memory = new MemoryStream();

                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                AppIcon.Source = bitmapImage;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load icon: {ex.Message}");
        }
    }

    private async void LoadVersionInformation()
    {
        // Tray version
        var (trayVersion, trayBuildDate) = GetTrayVersionInfo();
        TrayVersionText.Text = trayVersion;
        TrayBuildDateText.Text = trayBuildDate;

        // Service version
        var (serviceVersion, serviceBuildDate) = GetServiceVersionInfo();
        ServiceVersionText.Text = serviceVersion;
        ServiceBuildDateText.Text = serviceBuildDate;

        // Service account
        var serviceAccount = GetServiceAccount();
        ServiceAccountText.Text = serviceAccount;

        // Check for updates (async, non-blocking)
        await CheckForUpdatesAsync(trayVersion, serviceVersion);
    }

    /// <summary>
    /// Checks for updates from GitHub and displays update notification if available.
    /// </summary>
    private async System.Threading.Tasks.Task CheckForUpdatesAsync(string trayVersion, string serviceVersion)
    {
        // Check tray version
        if (!string.IsNullOrEmpty(trayVersion) && trayVersion != "Unknown")
        {
            try
            {
                var (versionStatus, latestVersion, installerUrl) = await VersionCheckerService.CheckForUpdatesAsync(trayVersion);
                if (!string.IsNullOrEmpty(latestVersion))
                {
                    // Successfully retrieved version info
                    if (versionStatus > 0)
                    {
                        // Update available
                        TrayNewVersionRun.Text = latestVersion?.Trim();
                        TrayUpdateLink.NavigateUri = new Uri(installerUrl);
                        TrayUpdateLinkText.Visibility = Visibility.Visible;
                        TrayLatestVersionText.Visibility = Visibility.Collapsed;
                        TrayPreReleaseText.Visibility = Visibility.Collapsed;
                        TrayUnableToConfirmText.Visibility = Visibility.Collapsed;
                    }
                    else if (versionStatus < 0)
                    {
                        // Pre-release (current is newer than latest)
                        TrayUpdateLinkText.Visibility = Visibility.Collapsed;
                        TrayLatestVersionText.Visibility = Visibility.Collapsed;
                        TrayPreReleaseText.Visibility = Visibility.Visible;
                        TrayUnableToConfirmText.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        // Current version is the latest
                        TrayUpdateLinkText.Visibility = Visibility.Collapsed;
                        TrayLatestVersionText.Visibility = Visibility.Visible;
                        TrayPreReleaseText.Visibility = Visibility.Collapsed;
                        TrayUnableToConfirmText.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    // Unable to retrieve version info (null response)
                    TrayUpdateLinkText.Visibility = Visibility.Collapsed;
                    TrayLatestVersionText.Visibility = Visibility.Collapsed;
                    TrayPreReleaseText.Visibility = Visibility.Collapsed;
                    TrayUnableToConfirmText.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                // Version checking failed (network error, etc.)
                TrayUpdateLinkText.Visibility = Visibility.Collapsed;
                TrayLatestVersionText.Visibility = Visibility.Collapsed;
                TrayPreReleaseText.Visibility = Visibility.Collapsed;
                TrayUnableToConfirmText.Visibility = Visibility.Visible;
            }
        }

        // Check service version
        if (!string.IsNullOrEmpty(serviceVersion) && serviceVersion != "Unknown" && serviceVersion != "Unavailable")
        {
            try
            {
                var (versionStatus, latestVersion, installerUrl) = await VersionCheckerService.CheckForUpdatesAsync(serviceVersion);
                if (!string.IsNullOrEmpty(latestVersion))
                {
                    // Successfully retrieved version info
                    if (versionStatus > 0)
                    {
                        // Update available
                        ServiceNewVersionRun.Text = latestVersion?.Trim();
                        ServiceUpdateLink.NavigateUri = new Uri(installerUrl);
                        ServiceUpdateLinkText.Visibility = Visibility.Visible;
                        ServiceLatestVersionText.Visibility = Visibility.Collapsed;
                        ServicePreReleaseText.Visibility = Visibility.Collapsed;
                        ServiceUnableToConfirmText.Visibility = Visibility.Collapsed;
                    }
                    else if (versionStatus < 0)
                    {
                        // Pre-release (current is newer than latest)
                        ServiceUpdateLinkText.Visibility = Visibility.Collapsed;
                        ServiceLatestVersionText.Visibility = Visibility.Collapsed;
                        ServicePreReleaseText.Visibility = Visibility.Visible;
                        ServiceUnableToConfirmText.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        // Current version is the latest
                        ServiceUpdateLinkText.Visibility = Visibility.Collapsed;
                        ServiceLatestVersionText.Visibility = Visibility.Visible;
                        ServicePreReleaseText.Visibility = Visibility.Collapsed;
                        ServiceUnableToConfirmText.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    // Unable to retrieve version info (null response)
                    ServiceUpdateLinkText.Visibility = Visibility.Collapsed;
                    ServiceLatestVersionText.Visibility = Visibility.Collapsed;
                    ServicePreReleaseText.Visibility = Visibility.Collapsed;
                    ServiceUnableToConfirmText.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                // Version checking failed (network error, etc.)
                ServiceUpdateLinkText.Visibility = Visibility.Collapsed;
                ServiceLatestVersionText.Visibility = Visibility.Collapsed;
                ServicePreReleaseText.Visibility = Visibility.Collapsed;
                ServiceUnableToConfirmText.Visibility = Visibility.Visible;
            }
        }
    }

    /// <summary>
    /// Handles hyperlink navigation to open URLs in the default browser.
    /// </summary>
    private void UpdateLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open URL: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private (string version, string buildDate) GetTrayVersionInfo()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var assemblyPath = assembly.Location;

            // If Location is empty (single-file published), try to find RJAutoMoverTray.exe
            if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
            {
                var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                var possiblePaths = new[]
                {
                    Path.Combine(currentDir, "RJAutoMoverTray.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RJAutoMover", "RJAutoMoverTray.exe"),
                    Process.GetCurrentProcess().MainModule?.FileName
                };

                foreach (var path in possiblePaths)
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        assemblyPath = path;
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(assemblyPath) && File.Exists(assemblyPath))
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(assemblyPath);
                var version = versionInfo.FileVersion ?? "1.0.0.0";
                var buildDate = File.GetLastWriteTime(assemblyPath).ToString("MMMM dd, yyyy HH:mm");
                return (version, buildDate);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting tray version: {ex.Message}");
        }

        return ("Unknown", "Unknown");
    }

    private (string version, string buildDate) GetServiceVersionInfo()
    {
        try
        {
            var possiblePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RJAutoMoverService.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RJAutoMover", "RJAutoMoverService.exe")
            };

            foreach (var servicePath in possiblePaths)
            {
                if (File.Exists(servicePath))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(servicePath);
                    var version = versionInfo.FileVersion ?? "1.0.0.0";
                    var buildDate = File.GetLastWriteTime(servicePath).ToString("MMMM dd, yyyy HH:mm");
                    return (version, buildDate);
                }
            }

            // Try version.txt fallback
            var versionTxtPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RJAutoMover", "version.txt")
            };

            foreach (var versionFile in versionTxtPaths)
            {
                if (File.Exists(versionFile))
                {
                    var version = File.ReadAllText(versionFile).Trim();
                    var buildDate = File.GetLastWriteTime(versionFile).ToString("MMMM dd, yyyy HH:mm");
                    return (version, buildDate);
                }
            }
        }
        catch { }

        return ("Unavailable", "Unavailable");
    }

    private string GetServiceAccount()
    {
        try
        {
            // Use PowerShell to query the service account
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"(Get-WmiObject Win32_Service -Filter \\\"Name='RJAutoMoverService'\\\").StartName\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                process.WaitForExit(3000); // 3 second timeout
                var output = process.StandardOutput.ReadToEnd().Trim();

                if (!string.IsNullOrEmpty(output))
                {
                    // Handle common service account names
                    if (output.Equals("LocalSystem", StringComparison.OrdinalIgnoreCase))
                        return "Local System";
                    else if (output.Equals("NT AUTHORITY\\LocalService", StringComparison.OrdinalIgnoreCase))
                        return "Local Service";
                    else if (output.Equals("NT AUTHORITY\\NetworkService", StringComparison.OrdinalIgnoreCase))
                        return "Network Service";
                    else
                        return output; // Custom account
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting service account: {ex.Message}");
        }

        return "Unknown";
    }

    private void RefreshSystemInfo_Click(object sender, RoutedEventArgs e)
    {
        LoadMemoryUsage();
    }

    private async void RefreshDatabaseStats_Click(object sender, RoutedEventArgs e)
    {
        await LoadDatabaseStatistics();
    }

    private async void RefreshVersionInfo_Click(object sender, RoutedEventArgs e)
    {
        await LoadServiceVersionInfo();
    }

    private async System.Threading.Tasks.Task LoadServiceVersionInfo()
    {
        // Service version
        var (serviceVersion, serviceBuildDate) = GetServiceVersionInfo();
        ServiceVersionText.Text = serviceVersion;
        ServiceBuildDateText.Text = serviceBuildDate;

        // Service account
        ServiceAccountText.Text = GetServiceAccount();

        // Check for updates
        if (!string.IsNullOrEmpty(serviceVersion) && serviceVersion != "Unknown" && serviceVersion != "Unavailable")
        {
            try
            {
                var (versionStatus, latestVersion, installerUrl) = await VersionCheckerService.CheckForUpdatesAsync(serviceVersion);
                if (!string.IsNullOrEmpty(latestVersion))
                {
                    if (versionStatus > 0)
                    {
                        ServiceNewVersionRun.Text = latestVersion?.Trim();
                        ServiceUpdateLink.NavigateUri = new Uri(installerUrl);
                        ServiceUpdateLinkText.Visibility = Visibility.Visible;
                        ServiceLatestVersionText.Visibility = Visibility.Collapsed;
                        ServicePreReleaseText.Visibility = Visibility.Collapsed;
                        ServiceUnableToConfirmText.Visibility = Visibility.Collapsed;
                    }
                    else if (versionStatus < 0)
                    {
                        ServiceUpdateLinkText.Visibility = Visibility.Collapsed;
                        ServiceLatestVersionText.Visibility = Visibility.Collapsed;
                        ServicePreReleaseText.Visibility = Visibility.Visible;
                        ServiceUnableToConfirmText.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        ServiceUpdateLinkText.Visibility = Visibility.Collapsed;
                        ServiceLatestVersionText.Visibility = Visibility.Visible;
                        ServicePreReleaseText.Visibility = Visibility.Collapsed;
                        ServiceUnableToConfirmText.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    ServiceUpdateLinkText.Visibility = Visibility.Collapsed;
                    ServiceLatestVersionText.Visibility = Visibility.Collapsed;
                    ServicePreReleaseText.Visibility = Visibility.Collapsed;
                    ServiceUnableToConfirmText.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                ServiceUpdateLinkText.Visibility = Visibility.Collapsed;
                ServiceLatestVersionText.Visibility = Visibility.Collapsed;
                ServicePreReleaseText.Visibility = Visibility.Collapsed;
                ServiceUnableToConfirmText.Visibility = Visibility.Visible;
            }
        }
    }

    private async void LoadMemoryUsage()
    {
        // Tray memory usage
        var trayProcess = Process.GetCurrentProcess();
        var trayMemoryMB = trayProcess.WorkingSet64 / 1024.0 / 1024.0;
        var trayPeakMemoryMB = trayProcess.PeakWorkingSet64 / 1024.0 / 1024.0;
        var trayUptime = DateTime.Now - trayProcess.StartTime;
        var trayStartTime = trayProcess.StartTime;

        TrayMemoryText.Text = $"{trayMemoryMB:F2} MB ({trayPeakMemoryMB:F2} MB peak)";
        TrayUptimeText.Text = $"{FormatUptime(trayUptime)} [started {trayStartTime:yyyy-MM-dd HH:mm:ss}]";

        // Service memory usage - request from service via gRPC
        try
        {
            if (_grpcClient != null)
            {
                var serviceInfo = await _grpcClient.GetServiceSystemInfoAsync();
                if (serviceInfo != null && serviceInfo.MemoryUsageBytes > 0)
                {
                    var serviceMemoryMB = serviceInfo.MemoryUsageBytes / 1024.0 / 1024.0;
                    var servicePeakMemoryMB = serviceInfo.PeakMemoryUsageBytes / 1024.0 / 1024.0;
                    var serviceStartTime = DateTimeOffset.FromUnixTimeMilliseconds(serviceInfo.StartTimeUnixMs).LocalDateTime;
                    var serviceUptime = TimeSpan.FromMilliseconds(serviceInfo.UptimeMs);

                    // Store service start time for transfer highlighting
                    _serviceStartTime = serviceStartTime;

                    ServiceMemoryText.Text = $"{serviceMemoryMB:F2} MB ({servicePeakMemoryMB:F2} MB peak)";
                    ServiceUptimeText.Text = $"{FormatUptime(serviceUptime)} [started {serviceStartTime:yyyy-MM-dd HH:mm:ss}]";
                }
                else
                {
                    ServiceMemoryText.Text = "Unavailable (No Response)";
                    ServiceUptimeText.Text = "Unavailable (No Response)";
                }
            }
            else
            {
                ServiceMemoryText.Text = "Unavailable (Not Connected)";
                ServiceUptimeText.Text = "Unavailable (Not Connected)";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading service memory via gRPC: {ex.Message}");
            ServiceMemoryText.Text = $"Unavailable (Error)";
            ServiceUptimeText.Text = $"Unavailable (Error)";
        }
    }

    private string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        }
        else if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s";
        }
        else if (uptime.TotalMinutes >= 1)
        {
            return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
        }
        else
        {
            return $"{uptime.Seconds}s";
        }
    }

    private void LoadSystemInformation()
    {
        // Install location
        var installPath = AppDomain.CurrentDomain.BaseDirectory;
        InstallPathText.Text = installPath;

        // OS Version
        var osVersion = RuntimeInformation.OSDescription;
        var architecture = RuntimeInformation.OSArchitecture;
        OSVersionText.Text = $"{osVersion} ({architecture})";
    }

    private void LoadDotNetInformation()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();

            // SDK Version - read from assembly metadata
            var sdkVersion = GetAssemblyMetadata(assembly, "BuildSdkVersion");
            if (string.IsNullOrEmpty(sdkVersion))
            {
                // Fallback to runtime version
                sdkVersion = Environment.Version.ToString();
            }
            SdkVersionText.Text = sdkVersion;

            // Current Runtime Version - what's actually running right now
            // For self-contained apps, this should be the embedded runtime
            var currentRuntime = RuntimeInformation.FrameworkDescription;
            var runtimeVersion = Environment.Version.ToString();

            // Check if this is a self-contained deployment
            var isSelfContained = string.IsNullOrEmpty(assembly.Location) ||
                                  !Path.GetDirectoryName(assembly.Location)?.Contains("dotnet") == true;

            if (isSelfContained)
            {
                CurrentRuntimeVersionText.Text = $"{currentRuntime} (Self-Contained: {runtimeVersion})";
            }
            else
            {
                CurrentRuntimeVersionText.Text = $"{currentRuntime} (Shared)";
            }

            // Package Versions
            var packageVersions = GetPackageVersions();
            PackageVersionsText.Text = packageVersions;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading .NET information: {ex.Message}");
            SdkVersionText.Text = "Error";
            CurrentRuntimeVersionText.Text = "Error";
            PackageVersionsText.Text = "Error loading package information";
        }
    }

    private string GetAssemblyMetadata(Assembly assembly, string key)
    {
        var attribute = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key);
        return attribute?.Value ?? string.Empty;
    }

    private string GetPackageVersions()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();

            // Try to get package versions from assembly metadata first (embedded at build time)
            var packageVersionsMetadata = GetAssemblyMetadata(assembly, "PackageVersions");

            if (!string.IsNullOrEmpty(packageVersionsMetadata))
            {
                // Parse the semicolon-separated package list
                var packageList = packageVersionsMetadata.Split(';')
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .OrderBy(p => p)
                    .ToList();

                if (packageList.Count > 0)
                {
                    return string.Join("\n", packageList.Select(p => p.Replace(":", ": ")));
                }
            }

            // Fallback: try to get from loaded assemblies (works when not using PublishSingleFile)
            var packages = new Dictionary<string, string>();
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .ToList();

            var packageNames = new[]
            {
                "Grpc.AspNetCore",
                "Grpc.Net.Client",
                "Grpc.Core.Api",
                "Google.Protobuf",
                "Hardcodet.NotifyIcon.Wpf",
                "CommunityToolkit.Mvvm",
                "Microsoft.Data.Sqlite",
                "YamlDotNet",
                "Serilog",
                "Serilog.Sinks.File"
            };

            foreach (var packageName in packageNames)
            {
                var asm = loadedAssemblies.FirstOrDefault(a =>
                    a.GetName().Name?.Equals(packageName, StringComparison.OrdinalIgnoreCase) == true);

                if (asm != null)
                {
                    var version = asm.GetName().Version?.ToString() ?? "Unknown";
                    // Remove trailing .0.0 for cleaner display
                    if (version.EndsWith(".0.0"))
                        version = version.Substring(0, version.Length - 4);
                    packages[packageName] = version;
                }
            }

            if (packages.Count == 0)
                return "Package information not available\n(Build with updated version to see package details)";

            return string.Join("\n", packages.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting package versions: {ex.Message}");
            return "Error loading package versions";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        StopLogUpdates();
        StopVersionCheckTimer();
        Close();
    }

    private void StopVersionCheckTimer()
    {
        if (_versionCheckTimer != null)
        {
            _versionCheckTimer.Stop();
            _versionCheckTimer = null;
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to exit RJAutoMover?",
            "Exit Application",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            Application.Current.Shutdown();
        }
    }

    private string? _configFilePath;
    private string? _rawConfigYaml;

    private void LoadConfiguration()
    {
        try
        {
            // Find config.yaml
            // Priority: 1) Program Files directory (where executables are), 2) ProgramData directory
            var possibleConfigPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RJAutoMover", "config.yaml"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RJAutoMover", "config.yaml")
            };

            string? configPath = null;
            foreach (var path in possibleConfigPaths)
            {
                if (File.Exists(path))
                {
                    configPath = path;
                    break;
                }
            }

            if (configPath == null)
            {
                var errorSettings = new List<AppSettingViewModel>
                {
                    new AppSettingViewModel { DisplayName = "Error", YamlFieldName = "", Value = "Config file not found", DefaultIndicator = "" }
                };
                AppSettingsItemsControl.ItemsSource = errorSettings;
                RawConfigTextBox.Text = "Config file not found";
                _configFilePath = null;
                return;
            }

            // Load and parse YAML
            _configFilePath = configPath;
            _rawConfigYaml = File.ReadAllText(configPath);
            var yaml = _rawConfigYaml;
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(PascalCaseNamingConvention.Instance)
                .Build();

            var config = deserializer.Deserialize<Configuration>(yaml);

            // Load File Rules
            var fileRuleViewModels = config.FileRules.Select(rule => new FileRuleViewModel
            {
                Name = rule.Name,
                SourceFolder = rule.SourceFolder,
                DestinationFolder = rule.DestinationFolder,
                Extension = rule.Extension,
                Extensions = ExtensionTile.CreateTiles(rule.Extension),
                ScanIntervalDisplay = FormatInterval(rule.ScanIntervalMs),
                FileExists = char.ToUpper(rule.FileExists[0]) + rule.FileExists.Substring(1),
                StatusText = rule.IsActive ? "ACTIVE" : "INACTIVE",
                StatusColor = rule.IsActive ? new SolidColorBrush(Color.FromRgb(92, 184, 92)) : new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                DateCriteria = FormatDateCriteria(rule)
            }).ToList();

            FileRulesItemsControl.ItemsSource = fileRuleViewModels;

            // Load Application Settings with defaults
            var defaults = new ApplicationConfig();
            var appSettings = new List<AppSettingViewModel>
            {
                new AppSettingViewModel
                {
                    DisplayName = "Processing Paused",
                    YamlFieldName = "ProcessingPaused",
                    Value = config.Application.ProcessingPaused ? "Yes" : "No",
                    DefaultIndicator = config.Application.ProcessingPaused == defaults.ProcessingPaused ? " [default]" : $" [default = {(defaults.ProcessingPaused ? "Yes" : "No")}]"
                },
                new AppSettingViewModel
                {
                    DisplayName = "Retry Delay",
                    YamlFieldName = "RetryDelayMs",
                    Value = FormatInterval(config.Application.RetryDelayMs),
                    DefaultIndicator = config.Application.RetryDelayMs == defaults.RetryDelayMs ? " [default]" : $" [default = {FormatInterval(defaults.RetryDelayMs)}]"
                },
                new AppSettingViewModel
                {
                    DisplayName = "Failure Cooldown",
                    YamlFieldName = "FailureCooldownMs",
                    Value = FormatInterval(config.Application.FailureCooldownMs),
                    DefaultIndicator = config.Application.FailureCooldownMs == defaults.FailureCooldownMs ? " [default]" : $" [default = {FormatInterval(defaults.FailureCooldownMs)}]"
                },
                new AppSettingViewModel
                {
                    DisplayName = "Service Recheck Interval",
                    YamlFieldName = "RecheckServiceMs",
                    Value = FormatInterval(config.Application.RecheckServiceMs),
                    DefaultIndicator = config.Application.RecheckServiceMs == defaults.RecheckServiceMs ? " [default]" : $" [default = {FormatInterval(defaults.RecheckServiceMs)}]"
                },
                new AppSettingViewModel
                {
                    DisplayName = "Tray Recheck Interval",
                    YamlFieldName = "RecheckTrayMs",
                    Value = FormatInterval(config.Application.RecheckTrayMs),
                    DefaultIndicator = config.Application.RecheckTrayMs == defaults.RecheckTrayMs ? " [default]" : $" [default = {FormatInterval(defaults.RecheckTrayMs)}]"
                },
                new AppSettingViewModel
                {
                    DisplayName = "Pause Delay",
                    YamlFieldName = "PauseDelayMs",
                    Value = FormatInterval(config.Application.PauseDelayMs),
                    DefaultIndicator = config.Application.PauseDelayMs == defaults.PauseDelayMs ? " [default]" : $" [default = {FormatInterval(defaults.PauseDelayMs)}]"
                },
                new AppSettingViewModel
                {
                    DisplayName = "Service Heartbeat Interval",
                    YamlFieldName = "ServiceHeartbeatMs",
                    Value = FormatInterval(config.Application.ServiceHeartbeatMs),
                    DefaultIndicator = config.Application.ServiceHeartbeatMs == defaults.ServiceHeartbeatMs ? " [default]" : $" [default = {FormatInterval(defaults.ServiceHeartbeatMs)}]"
                },
                new AppSettingViewModel
                {
                    DisplayName = "Memory Limit",
                    YamlFieldName = "MemoryLimitMb",
                    Value = $"{config.Application.MemoryLimitMb} MB",
                    DefaultIndicator = config.Application.MemoryLimitMb == defaults.MemoryLimitMb ? " [default]" : $" [default = {defaults.MemoryLimitMb} MB]"
                },
                new AppSettingViewModel
                {
                    DisplayName = "Memory Check Interval",
                    YamlFieldName = "MemoryCheckMs",
                    Value = FormatInterval(config.Application.MemoryCheckMs),
                    DefaultIndicator = config.Application.MemoryCheckMs == defaults.MemoryCheckMs ? " [default]" : $" [default = {FormatInterval(defaults.MemoryCheckMs)}]"
                },
                new AppSettingViewModel
                {
                    DisplayName = "Log Folder",
                    YamlFieldName = "LogFolder",
                    Value = App.ServiceLogFolder ?? RJAutoMoverShared.Constants.Paths.GetSharedLogFolder(),
                    DefaultIndicator = string.IsNullOrEmpty(config.Application.LogFolder) ? " [using default path]" : (config.Application.LogFolder == defaults.LogFolder ? " [default]" : " [from config]")
                },
                new AppSettingViewModel
                {
                    DisplayName = "Service gRPC Port",
                    YamlFieldName = "ServiceGrpcPort",
                    Value = config.Application.ServiceGrpcPort.ToString(),
                    DefaultIndicator = config.Application.ServiceGrpcPort == defaults.ServiceGrpcPort ? " [default]" : $" [default = {defaults.ServiceGrpcPort}]"
                },
                new AppSettingViewModel
                {
                    DisplayName = "Tray gRPC Port",
                    YamlFieldName = "TrayGrpcPort",
                    Value = config.Application.TrayGrpcPort.ToString(),
                    DefaultIndicator = config.Application.TrayGrpcPort == defaults.TrayGrpcPort ? " [default]" : $" [default = {defaults.TrayGrpcPort}]"
                },
                new AppSettingViewModel
                {
                    DisplayName = "Require Tray Approval",
                    YamlFieldName = "RequireTrayApproval",
                    Value = config.Application.RequireTrayApproval ? "Yes" : "No",
                    DefaultIndicator = config.Application.RequireTrayApproval == defaults.RequireTrayApproval ? " [default]" : $" [default = {(defaults.RequireTrayApproval ? "Yes" : "No")}]"
                },
                new AppSettingViewModel
                {
                    DisplayName = "Activity History Enabled",
                    YamlFieldName = "ActivityHistoryEnabled",
                    Value = config.Application.ActivityHistoryEnabled ? "Yes" : "No",
                    DefaultIndicator = config.Application.ActivityHistoryEnabled == defaults.ActivityHistoryEnabled ? " [default]" : $" [default = {(defaults.ActivityHistoryEnabled ? "Yes" : "No")}]"
                },
                new AppSettingViewModel
                {
                    DisplayName = "Activity History Max Records",
                    YamlFieldName = "ActivityHistoryMaxRecords",
                    Value = config.Application.ActivityHistoryMaxRecords.ToString(),
                    DefaultIndicator = config.Application.ActivityHistoryMaxRecords == defaults.ActivityHistoryMaxRecords ? " [default]" : $" [default = {defaults.ActivityHistoryMaxRecords}]"
                },
                new AppSettingViewModel
                {
                    DisplayName = "Activity History Retention Days",
                    YamlFieldName = "ActivityHistoryRetentionDays",
                    Value = config.Application.ActivityHistoryRetentionDays.ToString(),
                    DefaultIndicator = config.Application.ActivityHistoryRetentionDays == defaults.ActivityHistoryRetentionDays ? " [default]" : $" [default = {defaults.ActivityHistoryRetentionDays}]"
                }
            };

            AppSettingsItemsControl.ItemsSource = appSettings;

            // Load raw YAML into the text box
            if (RawConfigTextBox != null && _rawConfigYaml != null)
            {
                RawConfigTextBox.Text = _rawConfigYaml;
            }

            // Update config file path display
            if (ConfigFilePathTextBlock != null && _configFilePath != null)
            {
                ConfigFilePathTextBlock.Text = $"Config: {_configFilePath}";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading configuration: {ex.Message}");
            var errorSettings = new List<AppSettingViewModel>
            {
                new AppSettingViewModel { DisplayName = "Error", YamlFieldName = "", Value = $"Error loading config: {ex.Message}", DefaultIndicator = "" }
            };
            AppSettingsItemsControl.ItemsSource = errorSettings;
            if (RawConfigTextBox != null)
            {
                RawConfigTextBox.Text = $"Error loading config: {ex.Message}";
            }

            // Clear config file path display on error
            if (ConfigFilePathTextBlock != null)
            {
                ConfigFilePathTextBlock.Text = _configFilePath != null ? $"Config: {_configFilePath} (Error loading)" : "";
            }
        }
    }

    private void ShowParsedConfig_Click(object sender, RoutedEventArgs e)
    {
        if (ParsedConfigView != null && RawConfigView != null)
        {
            ParsedConfigView.Visibility = Visibility.Visible;
            RawConfigView.Visibility = Visibility.Collapsed;
            ShowParsedConfigButton.Background = new SolidColorBrush(Color.FromRgb(74, 144, 226));
            ShowParsedConfigButton.Foreground = new SolidColorBrush(Colors.White);
            ShowParsedConfigButton.FontWeight = FontWeights.Bold;
            ShowRawConfigButton.Background = new SolidColorBrush(Color.FromRgb(221, 221, 221));
            ShowRawConfigButton.Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51));
            ShowRawConfigButton.FontWeight = FontWeights.Normal;
        }
    }

    private void ShowRawConfig_Click(object sender, RoutedEventArgs e)
    {
        if (ParsedConfigView != null && RawConfigView != null)
        {
            ParsedConfigView.Visibility = Visibility.Collapsed;
            RawConfigView.Visibility = Visibility.Visible;
            ShowRawConfigButton.Background = new SolidColorBrush(Color.FromRgb(74, 144, 226));
            ShowRawConfigButton.Foreground = new SolidColorBrush(Colors.White);
            ShowRawConfigButton.FontWeight = FontWeights.Bold;
            ShowParsedConfigButton.Background = new SolidColorBrush(Color.FromRgb(221, 221, 221));
            ShowParsedConfigButton.Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51));
            ShowParsedConfigButton.FontWeight = FontWeights.Normal;
        }
    }

    private void ShowVersionInfo_Click(object sender, RoutedEventArgs e)
    {
        if (VersionInfoView != null && DotNetInfoView != null)
        {
            VersionInfoView.Visibility = Visibility.Visible;
            DotNetInfoView.Visibility = Visibility.Collapsed;
            ShowVersionInfoButton.Background = new SolidColorBrush(Color.FromRgb(74, 144, 226));
            ShowVersionInfoButton.Foreground = new SolidColorBrush(Colors.White);
            ShowVersionInfoButton.FontWeight = FontWeights.Bold;
            ShowDotNetInfoButton.Background = new SolidColorBrush(Color.FromRgb(221, 221, 221));
            ShowDotNetInfoButton.Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51));
            ShowDotNetInfoButton.FontWeight = FontWeights.Normal;
        }
    }

    private void ShowDotNetInfo_Click(object sender, RoutedEventArgs e)
    {
        if (VersionInfoView != null && DotNetInfoView != null)
        {
            VersionInfoView.Visibility = Visibility.Collapsed;
            DotNetInfoView.Visibility = Visibility.Visible;
            ShowDotNetInfoButton.Background = new SolidColorBrush(Color.FromRgb(74, 144, 226));
            ShowDotNetInfoButton.Foreground = new SolidColorBrush(Colors.White);
            ShowDotNetInfoButton.FontWeight = FontWeights.Bold;
            ShowVersionInfoButton.Background = new SolidColorBrush(Color.FromRgb(221, 221, 221));
            ShowVersionInfoButton.Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51));
            ShowVersionInfoButton.FontWeight = FontWeights.Normal;
        }
    }

    private string FormatInterval(int milliseconds)
    {
        if (milliseconds < 1000)
            return $"{milliseconds} ms";

        var seconds = milliseconds / 1000;
        if (seconds < 60)
            return $"{seconds} sec";

        var minutes = seconds / 60;
        if (minutes < 60)
            return $"{minutes} min";

        var hours = minutes / 60;
        return $"{hours} hr";
    }

    private string? FormatDateCriteria(FileRule rule)
    {
        if (rule.LastAccessedMins.HasValue)
        {
            var value = rule.LastAccessedMins.Value;
            if (value > 0)
            {
                return $"Last Accessed: older than {FormatMinutes(value)}";
            }
            else
            {
                return $"Last Accessed: within {FormatMinutes(Math.Abs(value))}";
            }
        }
        else if (rule.LastModifiedMins.HasValue)
        {
            var value = rule.LastModifiedMins.Value;
            if (value > 0)
            {
                return $"Last Modified: older than {FormatMinutes(value)}";
            }
            else
            {
                return $"Last Modified: within {FormatMinutes(Math.Abs(value))}";
            }
        }
        else if (rule.AgeCreatedMins.HasValue)
        {
            var value = rule.AgeCreatedMins.Value;
            if (value > 0)
            {
                return $"Age: older than {FormatMinutes(value)}";
            }
            else
            {
                return $"Age: within {FormatMinutes(Math.Abs(value))}";
            }
        }
        return null;
    }

    private string FormatMinutes(int minutes)
    {
        if (minutes < 60)
            return $"{minutes} min";

        var hours = minutes / 60;
        if (hours < 24)
            return $"{hours} hr";

        var days = hours / 24;
        if (days < 365)
            return $"{days} day{(days > 1 ? "s" : "")}";

        var years = days / 365;
        return $"{years} year{(years > 1 ? "s" : "")}";
    }

    // Log viewer fields
    private List<string> _allLogLines = new();
    private string _currentLogSource = "Service";
    private string _currentLogLevel = "ALL";
    private string _currentSearchText = "";
    private System.Windows.Threading.DispatcherTimer? _logUpdateTimer;
    private string _currentLogFile = "";

    private void LogSourceComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LogSourceComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            _currentLogSource = selectedItem.Tag?.ToString() ?? "Service";
            LoadLogs();
        }
    }

    private void LogLevelFilter_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LogLevelFilterComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            _currentLogLevel = selectedItem.Tag?.ToString() ?? "ALL";
            FilterAndDisplayLogs();
        }
    }

    private void LogSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (LogSearchTextBox.Foreground == System.Windows.Media.Brushes.Gray)
            return; // Ignore if placeholder text

        _currentSearchText = LogSearchTextBox.Text;
        FilterAndDisplayLogs();
    }

    private void LogSearchTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (LogSearchTextBox.Text == "Search logs...")
        {
            LogSearchTextBox.Text = "";
            LogSearchTextBox.Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51));
        }
    }

    private void LogSearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LogSearchTextBox.Text))
        {
            LogSearchTextBox.Text = "Search logs...";
            LogSearchTextBox.Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153));
        }
    }

    private void LoadLogs()
    {
        try
        {
            // Check if UI controls are initialized
            if (LogTextBox == null)
                return;

            _allLogLines.Clear();

            // Use service-provided log folder or fall back to shared log folder constant
            var logFolder = App.ServiceLogFolder ?? RJAutoMoverShared.Constants.Paths.GetSharedLogFolder();

            // Update UI to show log folder path
            if (LogFolderPathText != null)
            {
                LogFolderPathText.Text = $"Log folder: {logFolder}";
            }

            if (!Directory.Exists(logFolder))
            {
                LogTextBox.Text = $"Log folder not found: {logFolder}";
                StopLogUpdates();
                return;
            }

            // Find the most recent log file for the selected source
            var searchPattern = _currentLogSource == "Service"
                ? "*RJAutoMoverService.log"
                : "*RJAutoMoverTray.log";

            var logFiles = Directory.GetFiles(logFolder, searchPattern)
                .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                .ToArray();

            if (logFiles.Length == 0)
            {
                LogTextBox.Text = $"No {_currentLogSource} log files found in {logFolder}";
                StopLogUpdates();
                return;
            }

            // Read the most recent log file
            var mostRecentLog = logFiles[0];
            _currentLogFile = mostRecentLog;

            // Parse log entries, combining multi-line entries
            // Use shared file access to allow reading while Serilog is writing
            _allLogLines = ParseLogEntries(ReadSharedLogFile(mostRecentLog));

            // Display filtered logs (newest first)
            FilterAndDisplayLogs();

            // Start live updates
            StartLogUpdates();
        }
        catch (Exception ex)
        {
            if (LogTextBox != null)
                LogTextBox.Text = $"Error loading logs: {ex.Message}";
            StopLogUpdates();
        }
    }

    private void StartLogUpdates()
    {
        // Stop existing timer if any
        StopLogUpdates();

        // Create timer that updates logs every 2 seconds
        _logUpdateTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _logUpdateTimer.Tick += (s, e) => RefreshLogs();
        _logUpdateTimer.Start();
    }

    private void StopLogUpdates()
    {
        if (_logUpdateTimer != null)
        {
            _logUpdateTimer.Stop();
            _logUpdateTimer = null;
        }
    }

    private void RefreshLogs()
    {
        try
        {
            // Find the current most recent log file (in case a new one was created)
            // Use service-provided log folder or fall back to shared log folder constant
            var logFolder = App.ServiceLogFolder ?? RJAutoMoverShared.Constants.Paths.GetSharedLogFolder();
            if (!Directory.Exists(logFolder))
                return;

            var searchPattern = _currentLogSource == "Service"
                ? "*RJAutoMoverService.log"
                : "*RJAutoMoverTray.log";

            var logFiles = Directory.GetFiles(logFolder, searchPattern)
                .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                .ToArray();

            if (logFiles.Length == 0)
                return;

            var mostRecentLogFile = logFiles[0];

            // If we're now looking at a different file, update the reference
            if (_currentLogFile != mostRecentLogFile)
            {
                _currentLogFile = mostRecentLogFile;
            }

            // Re-read the log file and parse entries
            // Use shared file access to allow reading while Serilog is writing
            var newLines = ParseLogEntries(ReadSharedLogFile(_currentLogFile));

            // Only update if content changed
            if (newLines.Count != _allLogLines.Count || !newLines.SequenceEqual(_allLogLines))
            {
                _allLogLines = newLines;
                FilterAndDisplayLogs();
            }
        }
        catch
        {
            // Silently ignore errors during refresh
        }
    }

    private void FilterAndDisplayLogs()
    {
        // Check if UI controls are initialized
        if (LogTextBox == null)
            return;

        if (_allLogLines.Count == 0)
        {
            LogTextBox.Text = "No log entries found.";
            return;
        }

        var filteredLines = _allLogLines.AsEnumerable();

        // Filter by log level
        if (_currentLogLevel != "ALL")
        {
            if (_currentLogLevel == "GRPC")
            {
                // Show gRPC-specific logs (lines with [gRPC>] or [gRPC<])
                filteredLines = filteredLines.Where(line =>
                    line.Contains("[gRPC>]") || line.Contains("[gRPC<]"));
            }
            else
            {
                filteredLines = filteredLines.Where(line =>
                    line.Contains($"[{_currentLogLevel}"));
            }
        }

        // Filter by search text with AND/OR logic
        if (!string.IsNullOrWhiteSpace(_currentSearchText) && _currentSearchText != "Search logs...")
        {
            filteredLines = filteredLines.Where(line => MatchesSearchFilter(line, _currentSearchText));
        }

        var filteredList = filteredLines.ToList();

        if (filteredList.Count == 0)
        {
            LogTextBox.Text = "No log entries match the current filters.";
        }
        else
        {
            // Reverse the list to show newest entries first
            filteredList.Reverse();
            LogTextBox.Text = string.Join(Environment.NewLine, filteredList);
        }
    }

    private bool MatchesSearchFilter(string line, string searchText)
    {
        // Split by | for OR groups
        var orGroups = searchText.Split('|');

        // If any OR group matches, the line matches
        foreach (var orGroup in orGroups)
        {
            var trimmedGroup = orGroup.Trim();
            if (string.IsNullOrWhiteSpace(trimmedGroup))
                continue;

            // Split by space for AND terms within this OR group
            var andTerms = trimmedGroup.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Check if all AND terms are present (case-insensitive)
            bool allTermsMatch = andTerms.All(term =>
                line.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase));

            if (allTermsMatch)
                return true; // This OR group matched, so the line matches
        }

        return false; // No OR groups matched
    }

    /// <summary>
    /// Reads a log file with shared access, allowing reading while Serilog is writing.
    /// </summary>
    private string[] ReadSharedLogFile(string filePath)
    {
        try
        {
            // Open file with FileShare.ReadWrite to allow reading while Serilog writes
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);
            var lines = new List<string>();

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lines.Add(line);
            }

            return lines.ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reading log file with shared access: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private List<string> ParseLogEntries(string[] rawLines)
    {
        var entries = new List<string>();
        var currentEntry = new System.Text.StringBuilder();

        foreach (var line in rawLines)
        {
            // Check if line starts with a timestamp pattern (YYYY-MM-DD HH:MM:SS.fff)
            // This indicates the start of a new log entry
            if (line.Length >= 23 &&
                char.IsDigit(line[0]) &&
                line[4] == '-' &&
                line[7] == '-' &&
                line[10] == ' ' &&
                line[13] == ':' &&
                line[16] == ':')
            {
                // Save previous entry if it exists
                if (currentEntry.Length > 0)
                {
                    entries.Add(currentEntry.ToString());
                    currentEntry.Clear();
                }

                // Start new entry
                currentEntry.Append(line);
            }
            else
            {
                // Continuation of previous entry
                if (currentEntry.Length > 0)
                {
                    currentEntry.AppendLine();
                    currentEntry.Append(line);
                }
                else
                {
                    // Orphaned line (shouldn't happen normally)
                    currentEntry.Append(line);
                }
            }
        }

        // Add the last entry
        if (currentEntry.Length > 0)
        {
            entries.Add(currentEntry.ToString());
        }

        return entries;
    }

    private void UpdateHeaderColor(string? iconName)
    {
        // Header color is static - no need to update based on icon state
        // The gradient is defined in XAML as HeaderGradient resource
    }

    #region Transfers Tab Support Methods

    /// <summary>
    /// Selects a tab by its header name (case-insensitive).
    /// </summary>
    /// <param name="tabName">The name of the tab to select (e.g., "Transfers", "Version").</param>
    private void SelectTabByName(string tabName)
    {
        foreach (TabItem tab in TabControl.Items)
        {
            if (tab.Header.ToString()?.Equals(tabName, StringComparison.OrdinalIgnoreCase) == true)
            {
                TabControl.SelectedItem = tab;
                break;
            }
        }
    }

    /// <summary>
    /// Sets the gRPC client for the Transfers tab to use for pause/resume functionality.
    /// Called by TrayIconService when the window is opened.
    /// </summary>
    /// <param name="grpcClient">The gRPC client to use for service communication.</param>
    public async void SetGrpcClient(GrpcClientServiceV2 grpcClient)
    {
        _grpcClient = grpcClient;

        // CRITICAL: Load service start time FIRST before loading transfer history
        // This ensures ParseTransferItem() can correctly determine IsCurrentSession
        await EnsureServiceStartTimeLoaded();

        // Refresh all service-related information now that we have the gRPC client
        LoadMemoryUsage();
        LoadConfiguration();  // Refresh config in case service restarted with new config
    }

    /// <summary>
    /// Sets the tray icon service reference for the Transfers tab's pause/resume button.
    /// Called by TrayIconService when the window is opened.
    /// </summary>
    /// <param name="trayIconService">The tray icon service instance.</param>
    public void SetTrayIconService(Services.TrayIconService trayIconService)
    {
        _trayIconService = trayIconService;
    }

    /// <summary>
    /// Handles service connection state changes to refresh configuration and other data.
    /// Called when the service connects/disconnects (e.g., when service restarts).
    /// </summary>
    /// <param name="sender">The sender of the event.</param>
    /// <param name="isConnected">True if service is connected, false if disconnected.</param>
    private void OnServiceConnectionStateChanged(object? sender, bool isConnected)
    {
        if (isConnected)
        {
            // Service reconnected - refresh all service-dependent data
            Application.Current.Dispatcher.Invoke(() =>
            {
                LoadConfiguration();  // Refresh config in case service restarted with new config
                LoadMemoryUsage();    // Refresh memory usage
                LoadSystemInformation(); // Refresh system info
            });
        }
    }

    /// <summary>
    /// Updates the Transfers tab with recent file transfer data from the service.
    /// Parses transfer strings and displays them with animated status indicators.
    /// </summary>
    /// <param name="recentItems">List of transfer strings from the service (format: "timestamp - filename size status [rule]").</param>
    public void UpdateTransfers(List<string> recentItems)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Store all transfers in source list
            _allTransfers = recentItems
                .Select(ParseTransferItem)
                .Where(t => t != null)
                .Select(t => t!)
                .ToList();

            // Apply filtering and sorting
            RefreshTransfersDisplay();
        });
    }

    /// <summary>
    /// Parses a transfer string from the service into a TransferDisplayItem.
    /// Expected format: "YYYY-MM-DD HH:mm:ss - filename size indicator [ruleName]"
    /// Where indicator can be: braille spinner (in progress), ✓ (success), ✗ (failed), or ⚠ (blacklisted).
    /// </summary>
    /// <param name="item">The transfer string to parse.</param>
    /// <returns>A TransferDisplayItem or null if parsing fails.</returns>
    private TransferDisplayItem? ParseTransferItem(string item)
    {
        try
        {
            // New format: "YYYY-MM-DD HH:mm:ss - filename size indicator [ruleName] {DestinationFolder}|FileSizeBytes^SourceFolder"
            long fileSizeBytes = 0;
            string sourceFolder = "";
            var parts = item.Split('^');
            var mainContent = parts[0];
            if (parts.Length > 1)
            {
                sourceFolder = parts[1];
            }

            var mainParts = mainContent.Split('|');
            mainContent = mainParts[0];

            if (mainParts.Length > 1)
            {
                long.TryParse(mainParts[1], out fileSizeBytes);
            }

            // Split on " - " to separate timestamp from file info
            parts = mainContent.Split(" - ", 2);
            if (parts.Length != 2) return null;

            var timestampStr = parts[0];
            var remainder = parts[1];

            // Extract destination folder from curly braces at the end (if present)
            string destinationFolder = "";
            var lastBraceStart = remainder.LastIndexOf('{');
            var lastBraceEnd = remainder.LastIndexOf('}');

            if (lastBraceStart >= 0 && lastBraceEnd > lastBraceStart)
            {
                destinationFolder = remainder.Substring(lastBraceStart + 1, lastBraceEnd - lastBraceStart - 1);
                remainder = remainder.Substring(0, lastBraceStart).Trim();
            }

            // Extract rule name from brackets at the end
            var lastBracketStart = remainder.LastIndexOf('[');
            var lastBracketEnd = remainder.LastIndexOf(']');

            if (lastBracketStart < 0 || lastBracketEnd < 0 || lastBracketEnd <= lastBracketStart)
                return null;

            var ruleName = remainder.Substring(lastBracketStart + 1, lastBracketEnd - lastBracketStart - 1);
            var fileSizeAndIndicator = remainder.Substring(0, lastBracketStart).Trim();

            // Split to get: filename, size, and status indicator
            var parts2 = fileSizeAndIndicator.Split(' ');
            if (parts2.Length < 3) return null;

            var indicator = parts2[parts2.Length - 1];  // Last element is status icon
            var fileSize = parts2[parts2.Length - 2];   // Second-to-last is file size
            var fileName = string.Join(" ", parts2.Take(parts2.Length - 2));  // Rest is filename

            // Determine status based on indicator
            bool isInProgress = _brailleFrames.Contains(indicator);
            string errorInfo = "";

            if (indicator == "✗")
            {
                errorInfo = "Failed";
            }
            else if (indicator == "⚠")
            {
                errorInfo = "Blacklisted";
            }

            DateTime timestampDateTime = DateTime.MinValue;
            if (DateTime.TryParse(timestampStr, out DateTime parsedDateTime))
            {
                timestampDateTime = parsedDateTime;
            }

            var displayTime = timestampDateTime != DateTime.MinValue
                ? timestampDateTime.ToString("HH:mm:ss [dd MMM]")
                : timestampStr;

            // Determine if this transfer is from the current service session by comparing timestamps
            // If the transfer timestamp is greater than or equal to service start time, it's current session
            bool isCurrentSession = false;
            if (timestampDateTime != DateTime.MinValue && _serviceStartTime != DateTime.MinValue)
            {
                isCurrentSession = timestampDateTime >= _serviceStartTime;
                Debug.WriteLine($"[ParseTransferItem] File: {fileName}, Transfer: {timestampDateTime:yyyy-MM-dd HH:mm:ss}, ServiceStart: {_serviceStartTime:yyyy-MM-dd HH:mm:ss}, IsCurrentSession: {isCurrentSession}");
            }

            return new TransferDisplayItem
            {
                Timestamp = displayTime,
                TimestampDateTime = timestampDateTime,
                FileName = fileName,
                FileSize = fileSize,
                FileSizeBytes = fileSizeBytes,
                RuleName = $"[{ruleName}]",
                StatusIcon = indicator,
                IsInProgress = isInProgress,
                ErrorInfo = errorInfo,
                IsCurrentSession = isCurrentSession,
                DestinationFolder = destinationFolder,
                SourceFolder = sourceFolder
            };
        }
        catch
        {
            return null;
        }
    }

    private void TransfersAnimationTimer_Tick(object? sender, EventArgs e)
    {
        foreach (var transfer in _transfers.Where(t => t.IsInProgress))
        {
            AdvanceBrailleAnimation(transfer);
        }

        // Status text removed - no longer displaying transfer counts
    }

    private void AdvanceBrailleAnimation(TransferDisplayItem transfer)
    {
        var currentIndex = Array.IndexOf(_brailleFrames, transfer.StatusIcon);
        if (currentIndex >= 0)
        {
            var nextIndex = (currentIndex + 1) % _brailleFrames.Length;
            transfer.StatusIcon = _brailleFrames[nextIndex];
        }
    }

    /// <summary>
    /// Handles the Transfers tab's Pause/Resume button click.
    /// Sends a toggle processing request to the background service via the tray service.
    /// </summary>
    private async void TransfersToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_trayIconService == null)
        {
            return;
        }

        try
        {
            await _trayIconService.ToggleProcessing();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error toggling processing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Updates the pause state and refreshes the toggle button text.
    /// Called by TrayIconService when the service's pause state changes.
    /// </summary>
    /// <param name="isPaused">True if processing is paused, false if active.</param>
    public void SetProcessingPaused(bool isPaused)
    {
        _isProcessingPaused = isPaused;
        UpdateTransfersToggleButton();
    }

    /// <summary>
    /// Updates the Transfers tab toggle button text based on the current pause state.
    /// </summary>
    private void UpdateTransfersToggleButton()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (TransfersToggleButton != null)
            {
                TransfersToggleButton.Content = _isProcessingPaused ? "Resume Processing" : "Pause Processing";

                // Update button appearance for better visibility when paused
                if (_isProcessingPaused)
                {
                    // Yellow background with dark text for paused state
                    TransfersToggleButton.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)); // Yellow
                    TransfersToggleButton.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)); // Dark text
                    TransfersToggleButton.FontWeight = FontWeights.Bold;
                }
                else
                {
                    // Gray background for normal state
                    TransfersToggleButton.Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
                    TransfersToggleButton.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                    TransfersToggleButton.FontWeight = FontWeights.Normal;
                }
            }
        });
    }

    /// <summary>
    /// Handles column header clicks to toggle sorting.
    /// </summary>
    private void SortColumn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is TextBlock header && header.Tag is string column)
        {
            // Toggle sort direction if clicking same column, otherwise default to descending
            if (_currentSortColumn == column)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _currentSortColumn = column;
                _sortAscending = false;
            }

            // Update column headers
            UpdateColumnHeaders();

            // Re-sort the transfers
            RefreshTransfersDisplay();
        }
    }

    /// <summary>
    /// Updates column header text to show sort indicators.
    /// </summary>
    private void UpdateColumnHeaders()
    {
        var arrow = _sortAscending ? " ▲" : " ▼";

        if (TimeColumnHeader != null)
            TimeColumnHeader.Text = _currentSortColumn == "Time" ? $"Time{arrow}" : "Time";
        if (SizeColumnHeader != null)
            SizeColumnHeader.Text = _currentSortColumn == "Size" ? $"Size{arrow}" : "Size";
        if (FilenameColumnHeader != null)
            FilenameColumnHeader.Text = _currentSortColumn == "Filename" ? $"Filename{arrow}" : "Filename";
        if (RuleColumnHeader != null)
            RuleColumnHeader.Text = _currentSortColumn == "Rule" ? $"Rule{arrow}" : "Rule";
    }

    /// <summary>
    /// Handles session filter changes.
    /// </summary>
    private void SessionFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SessionFilterComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _selectedSessionFilter = tag;
            RefreshTransfersDisplay();
        }
    }

    /// <summary>
    /// Handles search text changes.
    /// </summary>
    private void TransfersSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TransfersSearchTextBox != null && TransfersSearchTextBox.Foreground.ToString() != "#FF999999")
        {
            _transfersSearchText = TransfersSearchTextBox.Text.ToLower();
            RefreshTransfersDisplay();
        }
    }

    private void TransfersSearchTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TransfersSearchTextBox != null && TransfersSearchTextBox.Text == "Search transfers...")
        {
            TransfersSearchTextBox.Text = "";
            TransfersSearchTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        }
    }

    private void TransfersSearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (TransfersSearchTextBox != null && string.IsNullOrWhiteSpace(TransfersSearchTextBox.Text))
        {
            TransfersSearchTextBox.Text = "Search transfers...";
            TransfersSearchTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            _transfersSearchText = "";
            RefreshTransfersDisplay();
        }
    }

    /// <summary>
    /// Refreshes the transfers display with current sorting, filtering, and search.
    /// </summary>
    private void RefreshTransfersDisplay()
    {
        if (_allTransfers == null) return;

        var filtered = _allTransfers.AsEnumerable();

        // Apply session filter
        if (_selectedSessionFilter == "Current")
        {
            filtered = filtered.Where(t => t.IsCurrentSession);
        }
        else if (_selectedSessionFilter == "Previous")
        {
            filtered = filtered.Where(t => !t.IsCurrentSession);
        }

        var afterSessionFilter = filtered.ToList();

        // Apply search filter (ignore placeholder text)
        if (!string.IsNullOrEmpty(_transfersSearchText) && _transfersSearchText != "search transfers...")
        {
            filtered = afterSessionFilter.Where(t =>
                t.FileName.ToLower().Contains(_transfersSearchText) ||
                t.RuleName.ToLower().Contains(_transfersSearchText) ||
                t.SourceFolder.ToLower().Contains(_transfersSearchText) ||
                t.DestinationFolder.ToLower().Contains(_transfersSearchText));
        }
        else
        {
            filtered = afterSessionFilter;
        }

        // Apply sorting
        IOrderedEnumerable<TransferDisplayItem> sorted;
        sorted = _currentSortColumn switch
        {
            "Time" => _sortAscending ? filtered.OrderBy(t => t.TimestampDateTime) : filtered.OrderByDescending(t => t.TimestampDateTime),
            "Size" => _sortAscending ? filtered.OrderBy(t => t.FileSizeBytes) : filtered.OrderByDescending(t => t.FileSizeBytes),
            "Filename" => _sortAscending ? filtered.OrderBy(t => t.FileName) : filtered.OrderByDescending(t => t.FileName),
            "Rule" => _sortAscending ? filtered.OrderBy(t => t.RuleName) : filtered.OrderByDescending(t => t.RuleName),
            _ => filtered.OrderByDescending(t => t.TimestampDateTime)
        };

        // Keep in-progress items at top
        var result = sorted.OrderBy(t => t.IsInProgress ? 0 : 1).ToList();

        // Update the observable collection
        _transfers.Clear();
        foreach (var item in result)
        {
            _transfers.Add(item);
        }
    }

    /// <summary>
    /// Enables or disables the Transfers tab toggle button.
    /// Called by TrayIconService based on connection and error states.
    /// </summary>
    /// <param name="isEnabled">True to enable the button, false to disable.</param>
    public void SetTransfersToggleButtonEnabled(bool isEnabled)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (TransfersToggleButton != null)
            {
                TransfersToggleButton.IsEnabled = isEnabled;
            }
        });
    }

    /// <summary>
    /// Updates the error status dynamically after the window has been created.
    /// Called by TrayIconService when the service recovers from an error state.
    /// </summary>
    /// <param name="errorStatus">The new error status message (empty/null if no error).</param>
    /// <param name="hasError">True if the service is currently in an error state.</param>
    public void UpdateErrorStatus(string? errorStatus, bool hasError)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _errorStatus = errorStatus;
            _hasError = hasError;

            // Show or hide the Error tab based on the current error state
            if (ErrorTab != null)
            {
                if (_hasError)
                {
                    ErrorTab.Visibility = Visibility.Visible;
                    LoadErrorInformation();
                }
                else
                {
                    ErrorTab.Visibility = Visibility.Collapsed;
                }
            }
        });
    }

    #endregion



}

#region ViewModels

/// <summary>
/// ViewModel for displaying file rules in the Config tab.
/// </summary>
public class FileRuleViewModel
{
    public string Name { get; set; } = string.Empty;
    public string SourceFolder { get; set; } = string.Empty;
    public string DestinationFolder { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public List<ExtensionTile> Extensions { get; set; } = new();
    public string ScanIntervalDisplay { get; set; } = string.Empty;
    public string FileExists { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public SolidColorBrush StatusColor { get; set; } = new SolidColorBrush(Colors.Gray);
    public string? DateCriteria { get; set; } = null; // Display date filtering criteria if specified
}

/// <summary>
/// Represents a single file extension tile with its display color.
/// </summary>
public class ExtensionTile
{
    public string Extension { get; set; } = string.Empty;
    public SolidColorBrush Color { get; set; } = new SolidColorBrush(Colors.Gray);

    private static readonly SolidColorBrush[] TileColors = new[]
    {
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 152, 219)),  // Blue
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 204, 113)),  // Green
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(155, 89, 182)),  // Purple
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 126, 34)),  // Orange
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(231, 76, 60)),   // Red
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 188, 156)),  // Teal
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 196, 15)),  // Yellow
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(149, 165, 166)), // Gray
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 73, 94)),    // Dark Blue
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(142, 68, 173))   // Violet
    };

    public static List<ExtensionTile> CreateTiles(string extensionsString)
    {
        // Check if this is an OTHERS extension rule (special handling)
        if (extensionsString.Trim().Equals("OTHERS", StringComparison.OrdinalIgnoreCase))
        {
            return new List<ExtensionTile>
            {
                new ExtensionTile
                {
                    Extension = "OTHERS",
                    Color = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)) // Dark gray for OTHERS
                }
            };
        }

        var extensions = extensionsString.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .ToList();

        var tiles = new List<ExtensionTile>();
        for (int i = 0; i < extensions.Count; i++)
        {
            tiles.Add(new ExtensionTile
            {
                Extension = extensions[i],
                Color = TileColors[i % TileColors.Length]
            });
        }

        return tiles;
    }
}

/// <summary>
/// ViewModel for displaying application settings in the Config tab.
/// </summary>
public class AppSettingViewModel
{
    public string DisplayName { get; set; } = string.Empty;
    public string YamlFieldName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string DefaultIndicator { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel for displaying individual file transfer items in the Transfers tab.
/// Implements INotifyPropertyChanged to support animated status icon updates.
/// </summary>
public class TransferDisplayItem : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private string _statusIcon = "";
    private string _errorInfo = "";
    private bool _isCurrentSession;

    /// <summary>Display time of the transfer (HH:mm:ss [dd MMM] format, e.g., "14:32:45 [28 Oct]").</summary>
    public string Timestamp { get; set; } = "";

    /// <summary>Parsed DateTime for sorting purposes.</summary>
    public DateTime TimestampDateTime { get; set; } = DateTime.MinValue;

    /// <summary>Name of the file being transferred.</summary>
    public string FileName { get; set; } = "";

    /// <summary>Human-readable file size (e.g., "1.5MB").</summary>
    public string FileSize { get; set; } = "";

    /// <summary>Name of the rule that triggered this transfer (with brackets).</summary>
    public string RuleName { get; set; } = "";

    /// <summary>True if the transfer is currently in progress (shows animated spinner).</summary>
    public bool IsInProgress { get; set; }

    /// <summary>True if this transfer occurred in the current service session.</summary>
    public bool IsCurrentSession
    {
        get => _isCurrentSession;
        set
        {
            _isCurrentSession = value;
            OnPropertyChanged(nameof(IsCurrentSession));
        }
    }

    /// <summary>Destination folder where the file was moved.</summary>
    public string DestinationFolder { get; set; } = "";

    /// <summary>Source folder where the file was moved from.</summary>
    public string SourceFolder { get; set; } = "";

    /// <summary>Raw file size in bytes.</summary>
    public long FileSizeBytes { get; set; } = 0;

    /// <summary>Tooltip for the timestamp column.</summary>
    public string TimestampTooltip => TimestampDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>Tooltip for the file size column.</summary>
    public string FileSizeTooltip => $"{FileSizeBytes:N0} bytes";

    /// <summary>Tooltip for the file name column.</summary>
    public string FullPathTooltip => Path.Combine(SourceFolder, FileName);

    /// <summary>Tooltip for the rule column.</summary>
    public string RuleTooltip => Path.Combine(DestinationFolder, FileName);

    /// <summary>
    /// Status icon character (braille spinner, ✓, ✗, or ⚠).
    /// Notifies UI of changes to support animation.
    /// </summary>
    public string StatusIcon
    {
        get => _statusIcon;
        set
        {
            _statusIcon = value;
            OnPropertyChanged(nameof(StatusIcon));
        }
    }

    /// <summary>
    /// Error information text if the transfer failed.
    /// </summary>
    public string ErrorInfo
    {
        get => _errorInfo;
        set
        {
            _errorInfo = value;
            OnPropertyChanged(nameof(ErrorInfo));
        }
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

}

/// <summary>
/// Converts null or empty string values to Collapsed visibility, otherwise Visible.
/// Used to conditionally show date criteria fields in the Config tab.
/// </summary>
public class NullToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value == null || (value is string str && string.IsNullOrEmpty(str)))
        {
            return System.Windows.Visibility.Collapsed;
        }
        return System.Windows.Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

#endregion
