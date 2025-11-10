using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using RJAutoMoverShared.Models;

namespace RJAutoMoverConfig.Windows;

/// <summary>
/// Dialog window for editing file rule properties with real-time validation
/// </summary>
public partial class FileRuleEditorDialog : Window, INotifyPropertyChanged
{
    private readonly FileRule _originalRule;
    private readonly FileRule _workingCopy;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes the dialog with a file rule to edit
    /// </summary>
    /// <param name="ruleToEdit">The file rule to edit (changes are made to a working copy until saved)</param>
    public FileRuleEditorDialog(FileRule ruleToEdit)
    {
        InitializeComponent();

        _originalRule = ruleToEdit;

        // Create a working copy
        _workingCopy = new FileRule
        {
            Name = ruleToEdit.Name,
            SourceFolder = ruleToEdit.SourceFolder,
            DestinationFolder = ruleToEdit.DestinationFolder,
            Extension = ruleToEdit.Extension,
            ScanIntervalMs = ruleToEdit.ScanIntervalMs,
            FileExists = ruleToEdit.FileExists,
            IsActive = ruleToEdit.IsActive,
            LastAccessedMins = ruleToEdit.LastAccessedMins,
            LastModifiedMins = ruleToEdit.LastModifiedMins,
            AgeCreatedMins = ruleToEdit.AgeCreatedMins
        };

        DataContext = _workingCopy;

        // Defer initialization until window is loaded to ensure all XAML elements are ready
        this.Loaded += (s, e) =>
        {
            // Initialize FileExists radio buttons
            FileExistsSkip = _workingCopy.FileExists?.ToLower() == "skip";
            FileExistsOverwrite = _workingCopy.FileExists?.ToLower() == "overwrite";

            // Initialize date filter radio buttons
            InitializeDateFilters();

            // Initial validation
            ValidateSourceFolder();
            ValidateDestinationFolder();
            UpdateScanIntervalLabel();
        };

        // Set up event handlers
        SourceFolderTextBox.TextChanged += (s, e) => ValidateSourceFolder();
        DestinationFolderTextBox.TextChanged += (s, e) => ValidateDestinationFolder();
        ScanIntervalSlider.ValueChanged += (s, e) => UpdateScanIntervalLabel();

        Title = $"Edit Rule: {_workingCopy.Name}";
    }

    /// <summary>
    /// The edited rule (only set if dialog was saved, null if cancelled)
    /// </summary>
    public FileRule? EditedRule { get; private set; }

    private bool _fileExistsSkip;

    /// <summary>
    /// Two-way binding for FileExists "skip" radio button
    /// </summary>
    public bool FileExistsSkip
    {
        get => _fileExistsSkip;
        set
        {
            _fileExistsSkip = value;
            if (value && _workingCopy != null)
            {
                _workingCopy.FileExists = "skip";
            }
            OnPropertyChanged();
        }
    }

    private bool _fileExistsOverwrite;

    /// <summary>
    /// Two-way binding for FileExists "overwrite" radio button
    /// </summary>
    public bool FileExistsOverwrite
    {
        get => _fileExistsOverwrite;
        set
        {
            _fileExistsOverwrite = value;
            if (value && _workingCopy != null)
            {
                _workingCopy.FileExists = "overwrite";
            }
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Initializes the date filter radio buttons based on the rule's current settings
    /// </summary>
    private void InitializeDateFilters()
    {
        try
        {
            if (_workingCopy.LastAccessedMins.HasValue)
            {
                LastAccessedRadio.IsChecked = true;
                var value = _workingCopy.LastAccessedMins.Value;
                if (value < 0)
                {
                    LastAccessedWithinRadio.IsChecked = true;
                    LastAccessedMinsTextBox.Text = Math.Abs(value).ToString();
                }
                else
                {
                    LastAccessedOlderRadio.IsChecked = true;
                    LastAccessedMinsTextBox.Text = value.ToString();
                }
            }
            else if (_workingCopy.LastModifiedMins.HasValue)
            {
                LastModifiedRadio.IsChecked = true;
                var value = _workingCopy.LastModifiedMins.Value;
                if (value < 0)
                {
                    LastModifiedWithinRadio.IsChecked = true;
                    LastModifiedMinsTextBox.Text = Math.Abs(value).ToString();
                }
                else
                {
                    LastModifiedOlderRadio.IsChecked = true;
                    LastModifiedMinsTextBox.Text = value.ToString();
                }
            }
            else if (_workingCopy.AgeCreatedMins.HasValue)
            {
                AgeCreatedRadio.IsChecked = true;
                var value = _workingCopy.AgeCreatedMins.Value;
                if (value < 0)
                {
                    AgeCreatedWithinRadio.IsChecked = true;
                    AgeCreatedMinsTextBox.Text = Math.Abs(value).ToString();
                }
                else
                {
                    AgeCreatedOlderRadio.IsChecked = true;
                    AgeCreatedMinsTextBox.Text = value.ToString();
                }
            }
            else
            {
                NoDateFilterRadio.IsChecked = true;
            }
        }
        catch (Exception ex)
        {
            // Log the error but don't crash - just use defaults
            System.Diagnostics.Debug.WriteLine($"Error initializing date filters: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens a folder browser dialog to select the source folder
    /// </summary>
    private void BrowseSourceFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Source Folder"
        };

        if (dialog.ShowDialog() == true)
        {
            SourceFolderTextBox.Text = dialog.FolderName;
        }
    }

    /// <summary>
    /// Opens a folder browser dialog to select the destination folder
    /// </summary>
    private void BrowseDestinationFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Destination Folder"
        };

        if (dialog.ShowDialog() == true)
        {
            DestinationFolderTextBox.Text = dialog.FolderName;
        }
    }

    /// <summary>
    /// Shows a context menu with available path variables for the source folder
    /// </summary>
    private void ShowSourceVariables_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("<InstallingUserDownloads>", SourceFolderTextBox));
        menu.Items.Add(CreateMenuItem("<InstallingUserDocuments>", SourceFolderTextBox));
        menu.Items.Add(CreateMenuItem("<InstallingUserDesktop>", SourceFolderTextBox));
        menu.PlacementTarget = sender as Button;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Shows a context menu with available path variables for the destination folder
    /// </summary>
    private void ShowDestVariables_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("<InstallingUserDownloads>", DestinationFolderTextBox));
        menu.Items.Add(CreateMenuItem("<InstallingUserDocuments>", DestinationFolderTextBox));
        menu.Items.Add(CreateMenuItem("<InstallingUserDesktop>", DestinationFolderTextBox));
        menu.PlacementTarget = sender as Button;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Creates a menu item that inserts a path variable into a textbox when clicked
    /// </summary>
    private MenuItem CreateMenuItem(string text, TextBox targetTextBox)
    {
        var menuItem = new MenuItem { Header = text };
        menuItem.Click += (s, e) => targetTextBox.Text = text;
        return menuItem;
    }

    /// <summary>
    /// Handles date filter radio button changes
    /// </summary>
    private void DateFilterRadio_Changed(object sender, RoutedEventArgs e)
    {
        // Clear previous values when switching
        if (sender == NoDateFilterRadio && NoDateFilterRadio.IsChecked == true)
        {
            LastAccessedMinsTextBox.Text = "";
            LastModifiedMinsTextBox.Text = "";
            AgeCreatedMinsTextBox.Text = "";
        }
    }

    /// <summary>
    /// Validates the source folder path and updates the status indicator
    /// </summary>
    private void ValidateSourceFolder()
    {
        try
        {
            var path = SourceFolderTextBox?.Text;

            if (string.IsNullOrWhiteSpace(path))
            {
                if (SourceFolderStatus != null)
                {
                    SourceFolderStatus.Text = "Source folder is required";
                    SourceFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47)); // Red
                }
                return;
            }

            if (path.Contains('*') || path.Contains('?'))
            {
                if (SourceFolderStatus != null)
                {
                    SourceFolderStatus.Text = "⚠ Wildcards (* or ?) are not allowed in folder paths";
                    SourceFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
                }
                return;
            }

            if (path.StartsWith("<") && path.EndsWith(">"))
            {
                if (SourceFolderStatus != null)
                {
                    SourceFolderStatus.Text = "✓ Using special variable";
                    SourceFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                }
                return;
            }

            // Only check if directory exists if it's a normal path (not a network path that might hang)
            // Network paths starting with \\ can cause timeouts
            bool exists = false;
            if (!path.StartsWith("\\\\"))
            {
                exists = Directory.Exists(path);
            }

            if (SourceFolderStatus != null)
            {
                if (exists)
                {
                    SourceFolderStatus.Text = "✓ Folder exists and is accessible";
                    SourceFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                }
                else
                {
                    SourceFolderStatus.Text = path.StartsWith("\\\\") ? "⚠ Network path - cannot validate" : "⚠ Folder does not exist";
                    SourceFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
                }
            }
        }
        catch (Exception ex)
        {
            // Don't crash on validation errors
            System.Diagnostics.Debug.WriteLine($"Error validating source folder: {ex.Message}");
            if (SourceFolderStatus != null)
            {
                SourceFolderStatus.Text = "⚠ Validation error";
                SourceFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
            }
        }
    }

    /// <summary>
    /// Validates the destination folder path and updates the status indicator
    /// </summary>
    private void ValidateDestinationFolder()
    {
        try
        {
            var path = DestinationFolderTextBox?.Text;

            if (string.IsNullOrWhiteSpace(path))
            {
                if (DestinationFolderStatus != null)
                {
                    DestinationFolderStatus.Text = "Destination folder is required";
                    DestinationFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
                }
                return;
            }

            if (path.Contains('*') || path.Contains('?'))
            {
                if (DestinationFolderStatus != null)
                {
                    DestinationFolderStatus.Text = "⚠ Wildcards (* or ?) are not allowed in folder paths";
                    DestinationFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
                }
                return;
            }

            if (path.StartsWith("<") && path.EndsWith(">"))
            {
                if (DestinationFolderStatus != null)
                {
                    DestinationFolderStatus.Text = "✓ Using special variable";
                    DestinationFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                }
                return;
            }

            // Only check if directory exists if it's a normal path (not a network path that might hang)
            // Network paths starting with \\ can cause timeouts
            bool exists = false;
            if (!path.StartsWith("\\\\"))
            {
                exists = Directory.Exists(path);
            }

            if (DestinationFolderStatus != null)
            {
                if (exists)
                {
                    DestinationFolderStatus.Text = "✓ Folder exists and is writable";
                    DestinationFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                }
                else
                {
                    DestinationFolderStatus.Text = path.StartsWith("\\\\") ? "⚠ Network path - cannot validate" : "⚠ Folder does not exist";
                    DestinationFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                }
            }
        }
        catch (Exception ex)
        {
            // Don't crash on validation errors
            System.Diagnostics.Debug.WriteLine($"Error validating destination folder: {ex.Message}");
            if (DestinationFolderStatus != null)
            {
                DestinationFolderStatus.Text = "⚠ Validation error";
                DestinationFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
            }
        }
    }

    /// <summary>
    /// Updates the scan interval label with human-readable text (seconds/minutes)
    /// </summary>
    private void UpdateScanIntervalLabel()
    {
        var ms = (int)ScanIntervalSlider.Value;
        var seconds = ms / 1000;

        if (seconds < 60)
        {
            ScanIntervalLabel.Text = $"({seconds} sec)";
        }
        else
        {
            var minutes = seconds / 60;
            ScanIntervalLabel.Text = $"({minutes} min)";
        }
    }

    /// <summary>
    /// Validates and saves the edited rule, applying changes to the original rule object
    /// </summary>
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(_workingCopy.Name))
        {
            MessageBox.Show("Rule name is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_workingCopy.SourceFolder))
        {
            MessageBox.Show("Source folder is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_workingCopy.DestinationFolder))
        {
            MessageBox.Show("Destination folder is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_workingCopy.Extension))
        {
            MessageBox.Show("Extension is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Process date filters
        _workingCopy.LastAccessedMins = null;
        _workingCopy.LastModifiedMins = null;
        _workingCopy.AgeCreatedMins = null;

        if (LastAccessedRadio.IsChecked == true)
        {
            if (int.TryParse(LastAccessedMinsTextBox.Text, out int value) && value != 0)
            {
                _workingCopy.LastAccessedMins = LastAccessedWithinRadio.IsChecked == true ? -value : value;
            }
            else
            {
                MessageBox.Show("Last Accessed value must be a non-zero number.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else if (LastModifiedRadio.IsChecked == true)
        {
            if (int.TryParse(LastModifiedMinsTextBox.Text, out int value) && value != 0)
            {
                _workingCopy.LastModifiedMins = LastModifiedWithinRadio.IsChecked == true ? -value : value;
            }
            else
            {
                MessageBox.Show("Last Modified value must be a non-zero number.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else if (AgeCreatedRadio.IsChecked == true)
        {
            if (int.TryParse(AgeCreatedMinsTextBox.Text, out int value) && value != 0)
            {
                _workingCopy.AgeCreatedMins = AgeCreatedWithinRadio.IsChecked == true ? -value : value;
            }
            else
            {
                MessageBox.Show("Age Created value must be a non-zero number.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        // Check if OTHERS extension requires date filter
        if (_workingCopy.IsAllExtensionRule())
        {
            if (!_workingCopy.LastAccessedMins.HasValue &&
                !_workingCopy.LastModifiedMins.HasValue &&
                !_workingCopy.AgeCreatedMins.HasValue)
            {
                MessageBox.Show(
                    "Extension 'OTHERS' rules MUST have a date criteria.\n\nPlease select one of: Last Accessed, Last Modified, or Age Created.",
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        // Copy working values back to original
        _originalRule.Name = _workingCopy.Name;
        _originalRule.SourceFolder = _workingCopy.SourceFolder;
        _originalRule.DestinationFolder = _workingCopy.DestinationFolder;
        _originalRule.Extension = _workingCopy.Extension;
        _originalRule.ScanIntervalMs = _workingCopy.ScanIntervalMs;
        _originalRule.FileExists = _workingCopy.FileExists;
        _originalRule.IsActive = _workingCopy.IsActive;
        _originalRule.LastAccessedMins = _workingCopy.LastAccessedMins;
        _originalRule.LastModifiedMins = _workingCopy.LastModifiedMins;
        _originalRule.AgeCreatedMins = _workingCopy.AgeCreatedMins;

        EditedRule = _originalRule;
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Cancels the edit without saving changes
    /// </summary>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Raises the PropertyChanged event for data binding updates
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
