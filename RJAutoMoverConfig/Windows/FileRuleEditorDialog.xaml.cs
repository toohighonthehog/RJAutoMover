using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using RJAutoMoverShared.Models;
using RJAutoMoverShared.Helpers;

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
            DateFilter = ruleToEdit.DateFilter
        };

        DataContext = _workingCopy;

        // Defer initialization until window is loaded to ensure all XAML elements are ready
        this.Loaded += (s, e) =>
        {
            // Initialize FileExists radio buttons - check the value from the config
            var fileExistsValue = _workingCopy.FileExists?.Trim().ToLower() ?? "skip";

            if (fileExistsValue == "overwrite")
            {
                FileExistsOverwriteRadio.IsChecked = true;
                FileExistsSkipRadio.IsChecked = false;
            }
            else
            {
                FileExistsSkipRadio.IsChecked = true;
                FileExistsOverwriteRadio.IsChecked = false;
            }

            System.Diagnostics.Debug.WriteLine($"FileRuleEditorDialog: FileExists value = '{_workingCopy.FileExists}', set Skip={FileExistsSkipRadio.IsChecked}, Overwrite={FileExistsOverwriteRadio.IsChecked}");

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

    /// <summary>
    /// Initializes the date filter controls based on the rule's current DateFilter string
    /// </summary>
    private void InitializeDateFilters()
    {
        try
        {
            // Check if there's a DateFilter to parse
            if (string.IsNullOrWhiteSpace(_workingCopy.DateFilter))
            {
                NoDateFilterRadio.IsChecked = true;
                return;
            }

            // Parse the DateFilter string using DateFilterHelper
            var parsed = DateFilterHelper.Parse(_workingCopy.DateFilter);
            if (!parsed.IsValid)
            {
                System.Diagnostics.Debug.WriteLine($"Invalid DateFilter: {parsed.ErrorMessage}");
                NoDateFilterRadio.IsChecked = true;
                return;
            }

            // Set the appropriate UI controls based on the filter type
            switch (parsed.Type)
            {
                case DateFilterHelper.FilterType.LastAccessed:
                    LastAccessedRadio.IsChecked = true;
                    LastAccessedDirection.SelectedIndex = parsed.Direction == DateFilterHelper.FilterDirection.OlderThan ? 0 : 1;
                    LastAccessedMinsTextBox.Text = parsed.Minutes.ToString();
                    break;

                case DateFilterHelper.FilterType.LastModified:
                    LastModifiedRadio.IsChecked = true;
                    LastModifiedDirection.SelectedIndex = parsed.Direction == DateFilterHelper.FilterDirection.OlderThan ? 0 : 1;
                    LastModifiedMinsTextBox.Text = parsed.Minutes.ToString();
                    break;

                case DateFilterHelper.FilterType.FileCreated:
                    AgeCreatedRadio.IsChecked = true;
                    AgeCreatedDirection.SelectedIndex = parsed.Direction == DateFilterHelper.FilterDirection.OlderThan ? 0 : 1;
                    AgeCreatedMinsTextBox.Text = parsed.Minutes.ToString();
                    break;

                default:
                    NoDateFilterRadio.IsChecked = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            // Log the error but don't crash - just use defaults
            System.Diagnostics.Debug.WriteLine($"Error initializing date filters: {ex.Message}");
            NoDateFilterRadio.IsChecked = true;
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
        // Guard against being called during XAML initialization before all controls are created
        if (LastAccessedMinsTextBox == null || LastModifiedMinsTextBox == null || AgeCreatedMinsTextBox == null)
            return;

        // Clear previous values when switching
        if (sender == NoDateFilterRadio && NoDateFilterRadio.IsChecked == true)
        {
            LastAccessedMinsTextBox.Text = "";
            LastModifiedMinsTextBox.Text = "";
            AgeCreatedMinsTextBox.Text = "";
        }

        // Revalidate when filter type changes
        ValidateDateFilter();
        UpdateValidationSummary();
    }

    /// <summary>
    /// Real-time validation for rule name
    /// </summary>
    private void RuleNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateRuleName();
        UpdateValidationSummary();
    }

    /// <summary>
    /// Real-time validation for extension
    /// </summary>
    private void ExtensionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateExtension();
        UpdateValidationSummary();
    }

    /// <summary>
    /// Real-time validation for scan interval
    /// </summary>
    private void ScanIntervalTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateScanInterval();
        UpdateValidationSummary();
    }

    /// <summary>
    /// Real-time validation for date filters (TextBox changed)
    /// </summary>
    private void DateFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateDateFilter();
        UpdateValidationSummary();
    }

    /// <summary>
    /// Event handler for FileExists radio button changes
    /// </summary>
    private void FileExistsRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_workingCopy == null) return;

        if (sender == FileExistsSkipRadio && FileExistsSkipRadio.IsChecked == true)
        {
            _workingCopy.FileExists = "skip";
            System.Diagnostics.Debug.WriteLine("FileExistsRadio_Checked: Set FileExists = 'skip'");
        }
        else if (sender == FileExistsOverwriteRadio && FileExistsOverwriteRadio.IsChecked == true)
        {
            _workingCopy.FileExists = "overwrite";
            System.Diagnostics.Debug.WriteLine("FileExistsRadio_Checked: Set FileExists = 'overwrite'");
        }
    }

    /// <summary>
    /// Real-time validation for date filters (ComboBox selection changed)
    /// </summary>
    private void DateFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ValidateDateFilter();
        UpdateValidationSummary();
    }

    /// <summary>
    /// Validates rule name and shows inline error
    /// </summary>
    private void ValidateRuleName()
    {
        if (RuleNameTextBox == null || RuleNameError == null) return;

        var name = RuleNameTextBox.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowFieldError(RuleNameTextBox, RuleNameError, "Rule name is required");
        }
        else if (name.Length > 32)
        {
            ShowFieldError(RuleNameTextBox, RuleNameError, "Rule name must be 32 characters or less");
        }
        else if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9\s]+$"))
        {
            ShowFieldError(RuleNameTextBox, RuleNameError, "Rule name must contain only letters, numbers, and spaces");
        }
        else
        {
            ClearFieldError(RuleNameTextBox, RuleNameError);
        }
    }

    /// <summary>
    /// Validates extension and shows inline error
    /// </summary>
    private void ValidateExtension()
    {
        if (ExtensionTextBox == null || ExtensionError == null) return;

        var extension = ExtensionTextBox.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(extension))
        {
            ShowFieldError(ExtensionTextBox, ExtensionError, "Extension is required");
        }
        else if (extension.Equals("OTHERS", StringComparison.OrdinalIgnoreCase))
        {
            // OTHERS is valid, clear error
            ClearFieldError(ExtensionTextBox, ExtensionError);
        }
        else
        {
            // Validate extension format (must start with period or be pipe-separated)
            var extensions = extension.Split('|');
            bool allValid = true;
            foreach (var ext in extensions)
            {
                var trimmedExt = ext.Trim();
                if (!trimmedExt.StartsWith(".") || trimmedExt.Length < 2)
                {
                    allValid = false;
                    break;
                }
            }

            if (!allValid)
            {
                ShowFieldError(ExtensionTextBox, ExtensionError, "Extensions must start with a period (e.g., .pdf or .doc|.txt)");
            }
            else
            {
                ClearFieldError(ExtensionTextBox, ExtensionError);
            }
        }
    }

    /// <summary>
    /// Validates scan interval and shows inline error
    /// </summary>
    private void ValidateScanInterval()
    {
        if (ScanIntervalTextBox == null || ScanIntervalError == null) return;

        var text = ScanIntervalTextBox.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(text))
        {
            ShowFieldError(ScanIntervalTextBox, ScanIntervalError, "Scan interval is required");
        }
        else if (!int.TryParse(text, out int value))
        {
            ShowFieldError(ScanIntervalTextBox, ScanIntervalError, "Scan interval must be a number");
        }
        else if (value < 5000 || value > 900000)
        {
            ShowFieldError(ScanIntervalTextBox, ScanIntervalError, "Scan interval must be between 5000 and 900000 ms");
        }
        else
        {
            ClearFieldError(ScanIntervalTextBox, ScanIntervalError);
        }
    }

    /// <summary>
    /// Validates date filter values and shows inline error
    /// </summary>
    private void ValidateDateFilter()
    {
        if (DateFilterError == null) return;

        // Check which filter is active
        if (LastAccessedRadio?.IsChecked == true)
        {
            ValidateDateFilterValue(LastAccessedMinsTextBox?.Text, "Last Accessed");
        }
        else if (LastModifiedRadio?.IsChecked == true)
        {
            ValidateDateFilterValue(LastModifiedMinsTextBox?.Text, "Last Modified");
        }
        else if (AgeCreatedRadio?.IsChecked == true)
        {
            ValidateDateFilterValue(AgeCreatedMinsTextBox?.Text, "Age Created");
        }
        else
        {
            DateFilterError.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Validates a specific date filter value
    /// </summary>
    private void ValidateDateFilterValue(string? text, string filterName)
    {
        if (DateFilterError == null) return;

        if (string.IsNullOrWhiteSpace(text))
        {
            DateFilterError.Text = $"{filterName}: value is required";
            DateFilterError.Visibility = Visibility.Visible;
        }
        else if (!int.TryParse(text, out int value))
        {
            DateFilterError.Text = $"{filterName}: must be a number";
            DateFilterError.Visibility = Visibility.Visible;
        }
        else if (value == 0)
        {
            DateFilterError.Text = $"{filterName}: cannot be zero";
            DateFilterError.Visibility = Visibility.Visible;
        }
        else if (value < 1 || value > 5256000)
        {
            DateFilterError.Text = $"{filterName}: must be between 1 and 5256000 minutes";
            DateFilterError.Visibility = Visibility.Visible;
        }
        else
        {
            DateFilterError.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Shows inline error for a field
    /// </summary>
    private void ShowFieldError(TextBox textBox, TextBlock errorBlock, string message)
    {
        textBox.BorderBrush = new SolidColorBrush(Colors.Red);
        textBox.BorderThickness = new Thickness(2);
        errorBlock.Text = message;
        errorBlock.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Clears inline error for a field
    /// </summary>
    private void ClearFieldError(TextBox textBox, TextBlock errorBlock)
    {
        textBox.ClearValue(BorderBrushProperty);
        textBox.ClearValue(BorderThicknessProperty);
        errorBlock.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Updates the validation summary at the top of the dialog
    /// </summary>
    private void UpdateValidationSummary()
    {
        if (ValidationSummary == null || ValidationSummaryText == null) return;

        var errors = new List<string>();

        // Collect all visible errors
        if (RuleNameError?.Visibility == Visibility.Visible)
            errors.Add(RuleNameError.Text);
        if (ExtensionError?.Visibility == Visibility.Visible)
            errors.Add(ExtensionError.Text);
        if (ScanIntervalError?.Visibility == Visibility.Visible)
            errors.Add(ScanIntervalError.Text);
        if (DateFilterError?.Visibility == Visibility.Visible)
            errors.Add(DateFilterError.Text);

        // Check OTHERS extension requiring date filter
        var extension = ExtensionTextBox?.Text?.Trim() ?? "";
        if (extension.Equals("OTHERS", StringComparison.OrdinalIgnoreCase))
        {
            if (NoDateFilterRadio?.IsChecked == true)
            {
                errors.Add("Extension 'OTHERS' requires a date filter");
            }
        }

        if (errors.Count > 0)
        {
            ValidationSummaryText.Text = string.Join("\n", errors.Select((e, i) => $"{i + 1}. {e}"));
            ValidationSummary.Visibility = Visibility.Visible;
        }
        else
        {
            ValidationSummary.Visibility = Visibility.Collapsed;
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

            // Only validate syntax - service will validate actual permissions under its own user context
            // Check if path looks syntactically valid
            if (SourceFolderStatus != null)
            {
                try
                {
                    // Try to parse the path to check if it's valid
                    var fullPath = Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(path);

                    // Check for invalid characters
                    var invalidChars = Path.GetInvalidPathChars();
                    if (path.Any(c => invalidChars.Contains(c)))
                    {
                        SourceFolderStatus.Text = "⚠ Path contains invalid characters";
                        SourceFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
                    }
                    else
                    {
                        SourceFolderStatus.Text = "✓ Path syntax is valid (service will verify access)";
                        SourceFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                    }
                }
                catch
                {
                    SourceFolderStatus.Text = "⚠ Invalid path syntax";
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

            // Only validate syntax - service will validate actual permissions under its own user context
            // Check if path looks syntactically valid
            if (DestinationFolderStatus != null)
            {
                try
                {
                    // Try to parse the path to check if it's valid
                    var fullPath = Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(path);

                    // Check for invalid characters
                    var invalidChars = Path.GetInvalidPathChars();
                    if (path.Any(c => invalidChars.Contains(c)))
                    {
                        DestinationFolderStatus.Text = "⚠ Path contains invalid characters";
                        DestinationFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
                    }
                    else
                    {
                        DestinationFolderStatus.Text = "✓ Path syntax is valid (service will verify access)";
                        DestinationFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                    }
                }
                catch
                {
                    DestinationFolderStatus.Text = "⚠ Invalid path syntax";
                    DestinationFolderStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
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

        // Process date filters using new DateFilter format
        _workingCopy.DateFilter = string.Empty;

        if (LastAccessedRadio.IsChecked == true)
        {
            if (int.TryParse(LastAccessedMinsTextBox.Text, out int value) && value != 0 && value >= 1 && value <= 5256000)
            {
                var direction = LastAccessedDirection.SelectedIndex == 1
                    ? DateFilterHelper.FilterDirection.WithinLast
                    : DateFilterHelper.FilterDirection.OlderThan;
                _workingCopy.DateFilter = DateFilterHelper.Format(DateFilterHelper.FilterType.LastAccessed, direction, value);
            }
            else
            {
                MessageBox.Show("Last Accessed value must be a number between 1 and 5256000.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else if (LastModifiedRadio.IsChecked == true)
        {
            if (int.TryParse(LastModifiedMinsTextBox.Text, out int value) && value != 0 && value >= 1 && value <= 5256000)
            {
                var direction = LastModifiedDirection.SelectedIndex == 1
                    ? DateFilterHelper.FilterDirection.WithinLast
                    : DateFilterHelper.FilterDirection.OlderThan;
                _workingCopy.DateFilter = DateFilterHelper.Format(DateFilterHelper.FilterType.LastModified, direction, value);
            }
            else
            {
                MessageBox.Show("Last Modified value must be a number between 1 and 5256000.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else if (AgeCreatedRadio.IsChecked == true)
        {
            if (int.TryParse(AgeCreatedMinsTextBox.Text, out int value) && value != 0 && value >= 1 && value <= 5256000)
            {
                var direction = AgeCreatedDirection.SelectedIndex == 1
                    ? DateFilterHelper.FilterDirection.WithinLast
                    : DateFilterHelper.FilterDirection.OlderThan;
                _workingCopy.DateFilter = DateFilterHelper.Format(DateFilterHelper.FilterType.FileCreated, direction, value);
            }
            else
            {
                MessageBox.Show("File Created value must be a number between 1 and 5256000.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        // Check if OTHERS extension requires date filter
        if (_workingCopy.IsAllExtensionRule())
        {
            if (string.IsNullOrWhiteSpace(_workingCopy.DateFilter))
            {
                MessageBox.Show(
                    "Extension 'OTHERS' rules MUST have a date filter.\n\nPlease select one of: Last Accessed, Last Modified, or File Created.",
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
        _originalRule.DateFilter = _workingCopy.DateFilter;

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
