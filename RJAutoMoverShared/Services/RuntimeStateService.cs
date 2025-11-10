using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RJAutoMoverShared.Models;

namespace RJAutoMoverShared.Services;

/// <summary>
/// Manages persistent runtime state that survives service restarts.
/// Separate from configuration to avoid triggering config change detection.
/// State is stored in C:\ProgramData\RJAutoMover\runtime-state.json
/// </summary>
public class RuntimeStateService
{
    private readonly string _stateFilePath;
    private readonly LoggingService _logger;
    private RuntimeState _currentState;
    private readonly object _lock = new();

    public RuntimeStateService(LoggingService logger, string? dataDirectory = null)
    {
        _logger = logger;

        // Default to ProgramData folder
        var baseDir = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RJAutoMover");

        _stateFilePath = Path.Combine(baseDir, "runtime-state.json");

        // Ensure directory exists
        Directory.CreateDirectory(baseDir);

        // Load or create initial state
        _currentState = LoadState();

        _logger.Log(LogLevel.INFO, $"Runtime state initialized: {_stateFilePath}");
    }

    /// <summary>
    /// Gets or sets whether processing is paused.
    /// Setting this value persists it to disk automatically.
    /// </summary>
    public bool IsProcessingPaused
    {
        get
        {
            lock (_lock)
            {
                return _currentState.ProcessingPaused;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_currentState.ProcessingPaused != value)
                {
                    _currentState.ProcessingPaused = value;
                    _currentState.LastModified = DateTime.UtcNow;
                    _currentState.LastModifiedBy = GetCurrentUsername();

                    SaveState();

                    _logger.Log(LogLevel.INFO, $"Processing paused state changed to: {value} by {_currentState.LastModifiedBy}");
                }
            }
        }
    }

    /// <summary>
    /// Gets the current session ID
    /// </summary>
    public string SessionId
    {
        get
        {
            lock (_lock)
            {
                return _currentState.SessionId;
            }
        }
    }

    /// <summary>
    /// Gets when the session started
    /// </summary>
    public DateTime SessionStartTime
    {
        get
        {
            lock (_lock)
            {
                return _currentState.SessionStartTime;
            }
        }
    }

    /// <summary>
    /// Gets who last modified the paused state
    /// </summary>
    public string? LastModifiedBy
    {
        get
        {
            lock (_lock)
            {
                return _currentState.LastModifiedBy;
            }
        }
    }

    /// <summary>
    /// Gets when the state was last modified
    /// </summary>
    public DateTime? LastModified
    {
        get
        {
            lock (_lock)
            {
                return _currentState.LastModified;
            }
        }
    }

    /// <summary>
    /// Checks if runtime state file exists (used to determine if we should use config default)
    /// </summary>
    public bool StateFileExists()
    {
        return File.Exists(_stateFilePath);
    }

    /// <summary>
    /// Starts a new session (called on service start).
    /// Generates a new session ID and records the start time.
    /// </summary>
    public void StartNewSession()
    {
        lock (_lock)
        {
            _currentState.SessionId = Guid.NewGuid().ToString("N")[..12];
            _currentState.SessionStartTime = DateTime.UtcNow;
            _currentState.LastSessionEndTime = null;

            SaveState();

            _logger.Log(LogLevel.INFO, $"New session started: {_currentState.SessionId}");
        }
    }

    /// <summary>
    /// Marks the session as ended (called on graceful shutdown)
    /// </summary>
    public void EndSession()
    {
        lock (_lock)
        {
            _currentState.LastSessionEndTime = DateTime.UtcNow;
            SaveState();

            _logger.Log(LogLevel.INFO, $"Session ended: {_currentState.SessionId}");
        }
    }

    /// <summary>
    /// Gets the full runtime state (for diagnostics)
    /// </summary>
    public RuntimeState GetState()
    {
        lock (_lock)
        {
            // Return a copy
            return new RuntimeState
            {
                ProcessingPaused = _currentState.ProcessingPaused,
                SessionId = _currentState.SessionId,
                SessionStartTime = _currentState.SessionStartTime,
                LastSessionEndTime = _currentState.LastSessionEndTime,
                LastModifiedBy = _currentState.LastModifiedBy,
                LastModified = _currentState.LastModified
            };
        }
    }

    private RuntimeState LoadState()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                var json = File.ReadAllText(_stateFilePath);
                var state = JsonSerializer.Deserialize<RuntimeState>(json);

                if (state != null)
                {
                    _logger.Log(LogLevel.INFO, $"Runtime state loaded from file. Paused: {state.ProcessingPaused}");
                    return state;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.WARN, $"Failed to load runtime state: {ex.Message}. Using defaults.");
        }

        // Return default state (will be saved on first modification)
        _logger.Log(LogLevel.INFO, "Runtime state file not found - will use config default on first run");
        return new RuntimeState
        {
            ProcessingPaused = false,
            SessionId = Guid.NewGuid().ToString("N")[..12],
            SessionStartTime = DateTime.UtcNow
        };
    }

    private void SaveState()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(_currentState, options);
            File.WriteAllText(_stateFilePath, json);

            _logger.Log(LogLevel.DEBUG, "Runtime state saved to file");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Failed to save runtime state: {ex.Message}");
        }
    }

    private string GetCurrentUsername()
    {
        try
        {
            return Environment.UserName;
        }
        catch
        {
            return "Unknown";
        }
    }
}

/// <summary>
/// Runtime state that persists across service restarts.
/// Stored as JSON in C:\ProgramData\RJAutoMover\runtime-state.json
/// </summary>
public class RuntimeState
{
    [JsonPropertyName("processingPaused")]
    public bool ProcessingPaused { get; set; }

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("sessionStartTime")]
    public DateTime SessionStartTime { get; set; }

    [JsonPropertyName("lastSessionEndTime")]
    public DateTime? LastSessionEndTime { get; set; }

    [JsonPropertyName("lastModifiedBy")]
    public string? LastModifiedBy { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTime? LastModified { get; set; }
}
