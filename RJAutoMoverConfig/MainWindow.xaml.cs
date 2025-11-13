using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RJAutoMoverConfig.Services;
using RJAutoMoverConfig.Windows;
using RJAutoMoverShared.Models;

namespace RJAutoMoverConfig;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ConfigEditorService _configService;
    private string _configFilePath;
    private bool _isConfigValid;
    private bool _hasUnsavedChanges;
    private Configuration? _currentConfig;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _configService = new ConfigEditorService();
        _configFilePath = ConfigEditorService.GetDefaultConfigPath();
        _isConfigValid = false;

        // Initialize collections
        FileRules = new ObservableCollection<FileRule>();
        ApplicationSettings = new ApplicationConfig();

        // Load app icon
        LoadAppIcon();

        // Check admin status
        IsAdministrator = ConfigEditorService.IsRunningAsAdministrator();

        // Load default config asynchronously after window loads
        Loaded += async (s, e) => await TryLoadDefaultConfig();
    }

    #region Properties

    public string ConfigFilePath
    {
        get => _configFilePath;
        set
        {
            _configFilePath = value;
            OnPropertyChanged();
        }
    }

    public bool IsConfigValid
    {
        get => _isConfigValid;
        set
        {
            _isConfigValid = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set
        {
            _hasUnsavedChanges = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public bool CanSave => IsConfigValid && HasUnsavedChanges;

    public bool IsAdministrator { get; }

    public ObservableCollection<FileRule> FileRules { get; }

    public ApplicationConfig ApplicationSettings { get; private set; }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Opens a file browser dialog to select a configuration file
    /// </summary>
    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Configuration File",
            Filter = "YAML files (*.yaml;*.yml)|*.yaml;*.yml|All files (*.*)|*.*",
            DefaultExt = ".yaml",
            InitialDirectory = Path.GetDirectoryName(ConfigFilePath)
        };

        if (dialog.ShowDialog() == true)
        {
            ConfigFilePath = dialog.FileName;
        }
    }

    /// <summary>
    /// Loads the configuration file specified in the path textbox
    /// </summary>
    private async void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadConfiguration();
    }

    /// <summary>
    /// Opens a dialog to create a new file rule
    /// </summary>
    private void AddRuleButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("AddRuleButton_Click: Starting...");

            var newRule = new FileRule
            {
                Name = "New Rule",
                SourceFolder = "",
                DestinationFolder = "",
                Extension = ".txt",
                ScanIntervalMs = 30000,
                FileExists = "skip",
                IsActive = false
            };

            System.Diagnostics.Debug.WriteLine("AddRuleButton_Click: Creating dialog...");

            var dialog = new FileRuleEditorDialog(newRule)
            {
                Owner = this
            };

            System.Diagnostics.Debug.WriteLine("AddRuleButton_Click: Showing dialog...");

            if (dialog.ShowDialog() == true && dialog.EditedRule != null)
            {
                FileRules.Add(dialog.EditedRule);
                IsConfigValid = false;
                HasUnsavedChanges = true;
                UpdateValidationStatus("Configuration modified - validation required", false);
            }

            System.Diagnostics.Debug.WriteLine("AddRuleButton_Click: Dialog closed.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AddRuleButton_Click: EXCEPTION: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

            var errorText = $"Error opening rule editor:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}\n\nInner Exception:\n{ex.InnerException?.Message ?? "None"}";

            // Copy to clipboard
            try
            {
                Clipboard.SetText(errorText);
            }
            catch { }

            MessageBox.Show(
                errorText + "\n\n(Error details copied to clipboard)",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Opens a dialog to edit an existing file rule
    /// </summary>
    private void EditRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is FileRule rule)
        {
            var dialog = new FileRuleEditorDialog(rule)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                // End any pending edit operations before refreshing
                FileRulesDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
                // Refresh the DataGrid
                FileRulesDataGrid.Items.Refresh();
                IsConfigValid = false;
                HasUnsavedChanges = true;
                UpdateValidationStatus("Configuration modified - validation required", false);
            }
        }
    }

    /// <summary>
    /// Creates a copy of an existing file rule (starts as inactive for safety)
    /// </summary>
    private void DuplicateRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is FileRule rule)
        {
            var duplicatedRule = new FileRule
            {
                Name = rule.Name + " (Copy)",
                SourceFolder = rule.SourceFolder,
                DestinationFolder = rule.DestinationFolder,
                Extension = rule.Extension,
                ScanIntervalMs = rule.ScanIntervalMs,
                FileExists = rule.FileExists,
                IsActive = false, // Start as inactive for safety
                LastAccessedMins = rule.LastAccessedMins,
                LastModifiedMins = rule.LastModifiedMins,
                AgeCreatedMins = rule.AgeCreatedMins
            };

            FileRules.Add(duplicatedRule);
            IsConfigValid = false;
            HasUnsavedChanges = true;
            UpdateValidationStatus("Configuration modified - validation required", false);
        }
    }

    /// <summary>
    /// Deletes a file rule after confirmation
    /// </summary>
    private void DeleteRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is FileRule rule)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to delete the rule '{rule.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                FileRules.Remove(rule);
                IsConfigValid = false;
                HasUnsavedChanges = true;
                UpdateValidationStatus("Configuration modified - validation required", false);
            }
        }
    }

    /// <summary>
    /// Validates the current configuration without saving
    /// </summary>
    private async void ValidateButton_Click(object sender, RoutedEventArgs e)
    {
        await ValidateConfiguration();
    }

    /// <summary>
    /// Saves the validated configuration to disk
    /// </summary>
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await SaveConfiguration();
    }

    /// <summary>
    /// Opens a save dialog to save the configuration to a new location
    /// </summary>
    private async void SaveAsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("SaveAsButton_Click: Starting...");

            var dialog = new SaveFileDialog
            {
                Title = "Save Configuration As",
                Filter = "YAML files (*.yaml)|*.yaml|All files (*.*)|*.*",
                DefaultExt = ".yaml",
                FileName = "config.yaml",
                InitialDirectory = Path.GetDirectoryName(ConfigFilePath)
            };

            System.Diagnostics.Debug.WriteLine("SaveAsButton_Click: Showing dialog...");

            if (dialog.ShowDialog() == true)
            {
                System.Diagnostics.Debug.WriteLine($"SaveAsButton_Click: File selected: {dialog.FileName}");
                ConfigFilePath = dialog.FileName;
                await SaveConfiguration();
                System.Diagnostics.Debug.WriteLine("SaveAsButton_Click: Save completed.");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("SaveAsButton_Click: Dialog cancelled.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveAsButton_Click: EXCEPTION: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

            var errorText = $"Error saving configuration:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}\n\nInner Exception:\n{ex.InnerException?.Message ?? "None"}";

            // Copy to clipboard
            try
            {
                Clipboard.SetText(errorText);
            }
            catch { }

            MessageBox.Show(
                errorText + "\n\n(Error details copied to clipboard)",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Exits the application after checking for unsaved changes
    /// </summary>
    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        // Check for unsaved changes
        if (!IsConfigValid)
        {
            var result = MessageBox.Show(
                "You have unsaved or unvalidated changes. Are you sure you want to exit?",
                "Unsaved Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        Close();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Attempts to load the default configuration file on startup
    /// </summary>
    private async Task TryLoadDefaultConfig()
    {
        if (File.Exists(ConfigFilePath))
        {
            await LoadConfiguration();
        }
        else
        {
            UpdateValidationStatus("No configuration file found. Create a new configuration or load an existing file.", false);
        }
    }

    /// <summary>
    /// Loads configuration from the file specified in ConfigFilePath
    /// </summary>
    private async Task LoadConfiguration()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
            {
                MessageBox.Show(
                    $"Configuration file not found: {ConfigFilePath}",
                    "File Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var config = _configService.LoadConfiguration(ConfigFilePath);
            if (config == null)
            {
                MessageBox.Show(
                    "Failed to load configuration file.",
                    "Load Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            _currentConfig = config;

            // Update FileRules collection
            FileRules.Clear();
            if (config.FileRules != null)
            {
                foreach (var rule in config.FileRules)
                {
                    FileRules.Add(rule);
                }
            }

            // Update ApplicationSettings
            ApplicationSettings = config.Application ?? new ApplicationConfig();
            OnPropertyChanged(nameof(ApplicationSettings));

            // Reset unsaved changes flag after successful load
            HasUnsavedChanges = false;

            // Automatically validate the loaded configuration (without showing message boxes)
            await ValidateConfiguration(showMessageBox: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error loading configuration:\n\n{ex.Message}",
                "Load Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Validates the current configuration and displays results to the user
    /// </summary>
    /// <param name="showMessageBox">Whether to show message boxes for validation results (default: true)</param>
    private async Task ValidateConfiguration(bool showMessageBox = true)
    {
        try
        {
            // Create configuration object from UI
            var config = new Configuration
            {
                FileRules = FileRules.ToList(),
                Application = ApplicationSettings
            };

            // Validate
            var result = await _configService.ValidateConfigurationAsync(ConfigFilePath, config);

            if (result.IsValid)
            {
                IsConfigValid = true;
                UpdateValidationStatus("Configuration is valid and ready to save!", true, switchToTab: showMessageBox);
                if (showMessageBox)
                {
                    MessageBox.Show(
                        "Configuration validation passed!\n\nYou can now save the configuration.",
                        "Validation Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            else
            {
                IsConfigValid = false;
                UpdateValidationStatus($"Validation failed:\n\n{result.ErrorMessage}", false, switchToTab: showMessageBox);
                if (showMessageBox)
                {
                    MessageBox.Show(
                        $"Configuration validation failed:\n\n{result.ErrorMessage}\n\nPlease fix the errors and try again.",
                        "Validation Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            IsConfigValid = false;
            UpdateValidationStatus($"Validation error: {ex.Message}", false, switchToTab: showMessageBox);
            if (showMessageBox)
            {
                MessageBox.Show(
                    $"Error during validation:\n\n{ex.Message}",
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Saves the current configuration to disk, optionally restarting the service if editing the active config
    /// </summary>
    private async Task SaveConfiguration()
    {
        try
        {
            // Validate first if not already validated
            if (!IsConfigValid)
            {
                await ValidateConfiguration();
                if (!IsConfigValid)
                {
                    return; // Validation failed
                }
            }

            // Check write permissions
            if (!IsAdministrator && ConfigFilePath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)))
            {
                MessageBox.Show(
                    "You need administrator privileges to save to the Program Files directory.\n\n" +
                    "Please either:\n" +
                    "1. Run this application as Administrator, or\n" +
                    "2. Use 'Save As' to save to a different location",
                    "Administrator Rights Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Create configuration object from UI
            var config = new Configuration
            {
                FileRules = FileRules.ToList(),
                Application = ApplicationSettings
            };

            // Save
            await _configService.SaveConfigurationAsync(ConfigFilePath, config);

            // Reset unsaved changes flag after successful save
            HasUnsavedChanges = false;

            UpdateValidationStatus("Configuration saved successfully!", true);

            // Check if this is the active config and offer to restart service
            var defaultConfigPath = ConfigEditorService.GetDefaultConfigPath();
            var isSavingActiveConfig = string.Equals(
                Path.GetFullPath(ConfigFilePath),
                Path.GetFullPath(defaultConfigPath),
                StringComparison.OrdinalIgnoreCase);

            if (isSavingActiveConfig)
            {
                var restartResult = MessageBox.Show(
                    $"Configuration saved successfully to:\n{ConfigFilePath}\n\n" +
                    "This is the active configuration file used by RJAutoMoverService.\n\n" +
                    "Would you like to restart the service now to apply the changes?",
                    "Restart Service?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (restartResult == MessageBoxResult.Yes)
                {
                    try
                    {
                        var processStartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "net",
                            Arguments = "stop RJAutoMoverService",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };

                        var stopProcess = System.Diagnostics.Process.Start(processStartInfo);
                        if (stopProcess != null)
                        {
                            await stopProcess.WaitForExitAsync();

                            // Wait a moment before starting
                            await Task.Delay(1000);

                            processStartInfo.Arguments = "start RJAutoMoverService";
                            var startProcess = System.Diagnostics.Process.Start(processStartInfo);
                            if (startProcess != null)
                            {
                                await startProcess.WaitForExitAsync();

                                MessageBox.Show(
                                    "Service restart initiated successfully.\n\n" +
                                    "The RJAutoMoverService should now be using the new configuration.",
                                    "Service Restarted",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                            }
                        }
                    }
                    catch (Exception restartEx)
                    {
                        MessageBox.Show(
                            $"Failed to restart service:\n\n{restartEx.Message}\n\n" +
                            "Please restart the service manually using Services (services.msc) or by running:\n" +
                            "net stop RJAutoMoverService\n" +
                            "net start RJAutoMoverService",
                            "Restart Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                else
                {
                    MessageBox.Show(
                        "Configuration saved successfully.\n\n" +
                        "The service has NOT been restarted. The RJAutoMoverService will continue using the old configuration until manually restarted.\n\n" +
                        "To restart manually, use Services (services.msc) or run:\n" +
                        "net stop RJAutoMoverService\n" +
                        "net start RJAutoMoverService",
                        "Manual Restart Required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show(
                    $"Configuration saved successfully to:\n{ConfigFilePath}\n\n" +
                    "Note: This is not the active configuration file.\n" +
                    "To use this configuration, copy it to:\n" +
                    defaultConfigPath,
                    "Save Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error saving configuration:\n\n{ex.Message}",
                "Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Updates the validation status UI and optionally switches to the Validation tab
    /// </summary>
    private void UpdateValidationStatus(string message, bool isValid, bool switchToTab = false)
    {
        // Update status indicator in the validation content area
        ValidationStatusIndicator.Fill = isValid
            ? new SolidColorBrush(Color.FromRgb(76, 175, 80))  // Green
            : new SolidColorBrush(Color.FromRgb(211, 47, 47)); // Red

        // Update tab indicator dot - same color as status indicator
        ValidationTabIndicator.Fill = isValid
            ? new SolidColorBrush(Color.FromRgb(76, 175, 80))  // Green
            : new SolidColorBrush(Color.FromRgb(211, 47, 47)); // Red

        ValidationStatusText.Text = message;

        // Show/hide validation messages
        if (!string.IsNullOrEmpty(message) && !isValid)
        {
            ValidationMessagesHeader.Visibility = Visibility.Visible;
            ValidationMessagesContainer.Visibility = Visibility.Visible;
            ValidationMessagesText.Text = message;
        }
        else if (isValid)
        {
            ValidationMessagesHeader.Visibility = Visibility.Visible;
            ValidationMessagesContainer.Visibility = Visibility.Visible;
            ValidationMessagesContainer.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233)); // Light green
            ValidationMessagesText.Text = message;
            ValidationMessagesText.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50)); // Dark green
        }
        else
        {
            ValidationMessagesHeader.Visibility = Visibility.Collapsed;
            ValidationMessagesContainer.Visibility = Visibility.Collapsed;
        }

        // Only switch to Validation tab if explicitly requested (e.g., when user clicks Validate button)
        if (switchToTab)
        {
            MainTabControl.SelectedIndex = 2;
        }
    }

    /// <summary>
    /// Loads the application icon from embedded resources
    /// </summary>
    private void LoadAppIcon()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames();
            var iconResource = resourceNames.FirstOrDefault(r => r.EndsWith("paused.ico"));

            if (iconResource != null)
            {
                using var stream = assembly.GetManifestResourceStream(iconResource);
                if (stream != null)
                {
                    var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.Default);
                    AppIcon.Source = decoder.Frames[0];
                }
            }
        }
        catch
        {
            // Icon loading is optional - failure is not critical
        }
    }

    /// <summary>
    /// Raises the PropertyChanged event for data binding updates
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Handles double-click on DataGrid row to edit the rule
    /// </summary>
    private void FileRulesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("MouseDoubleClick: Starting...");

            if (FileRulesDataGrid.SelectedItem is FileRule rule)
            {
                System.Diagnostics.Debug.WriteLine($"MouseDoubleClick: Rule selected: {rule.Name}");

                var dialog = new FileRuleEditorDialog(rule)
                {
                    Owner = this
                };

                System.Diagnostics.Debug.WriteLine("MouseDoubleClick: Showing dialog...");

                if (dialog.ShowDialog() == true)
                {
                    // End any pending edit operations before refreshing
                    FileRulesDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
                    FileRulesDataGrid.Items.Refresh();
                    IsConfigValid = false;
                    HasUnsavedChanges = true;
                    UpdateValidationStatus("Configuration modified - validation required", false);
                }

                System.Diagnostics.Debug.WriteLine("MouseDoubleClick: Dialog closed.");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("MouseDoubleClick: No rule selected or cast failed");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MouseDoubleClick: EXCEPTION: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

            var errorText = $"Error opening rule editor (double-click):\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}\n\nInner Exception:\n{ex.InnerException?.Message ?? "None"}";

            // Copy to clipboard
            try
            {
                Clipboard.SetText(errorText);
            }
            catch { }

            MessageBox.Show(
                errorText + "\n\n(Error details copied to clipboard)",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    #endregion
}

/// <summary>
/// Converter to split pipe-separated extensions into colored ExtensionTile objects
/// </summary>
public class ExtensionSplitterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string extensions && !string.IsNullOrWhiteSpace(extensions))
        {
            return ExtensionTile.CreateTiles(extensions);
        }
        return new List<ExtensionTile>();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Represents a single file extension tile with its deterministic display color.
/// Colors are assigned based on extension position to ensure consistency across app restarts.
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

    /// <summary>
    /// Creates a list of extension tiles with deterministic colors based on position.
    /// Extensions are colored in order using a cycling color palette.
    /// This ensures the same extension in the same position always gets the same color.
    /// </summary>
    /// <param name="extensionsString">Pipe-separated list of extensions (e.g., ".pdf|.docx|.txt")</param>
    /// <returns>List of ExtensionTile objects with assigned colors</returns>
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
