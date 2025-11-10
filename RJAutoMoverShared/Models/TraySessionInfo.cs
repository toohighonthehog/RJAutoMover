namespace RJAutoMoverShared.Models;

/// <summary>
/// Represents an active tray session managed by the service.
/// Used to enforce single-instance tray across multiple users.
/// </summary>
public class TraySessionInfo
{
    /// <summary>
    /// Unique identifier for this tray instance
    /// </summary>
    public string TrayId { get; set; } = string.Empty;

    /// <summary>
    /// Windows username of the user running this tray
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Windows session ID (for RDS/Citrix environments)
    /// </summary>
    public int WindowsSessionId { get; set; }

    /// <summary>
    /// Process ID of the tray application (for validation)
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// When this tray was registered with the service
    /// </summary>
    public DateTime RegistrationTime { get; set; }

    /// <summary>
    /// Last successful heartbeat from this tray
    /// </summary>
    public DateTime LastHeartbeat { get; set; }

    /// <summary>
    /// Checks if this session is considered alive based on heartbeat timeout AND process existence
    /// </summary>
    public bool IsAlive(int timeoutSeconds = 60, Action<string>? logger = null)
    {
        // First check heartbeat timeout
        var secondsSinceHeartbeat = (DateTime.Now - LastHeartbeat).TotalSeconds;
        var heartbeatAlive = secondsSinceHeartbeat < timeoutSeconds;

        logger?.Invoke($"IsAlive check for {GetDescription()}: heartbeat {secondsSinceHeartbeat:F1}s ago (timeout: {timeoutSeconds}s, alive: {heartbeatAlive})");

        if (!heartbeatAlive)
        {
            logger?.Invoke($"IsAlive returning FALSE: heartbeat timeout exceeded ({secondsSinceHeartbeat:F1}s > {timeoutSeconds}s)");
            return false;
        }

        // Then verify the process actually exists
        try
        {
            if (ProcessId > 0)
            {
                logger?.Invoke($"IsAlive: Checking if process PID {ProcessId} exists...");
                var process = System.Diagnostics.Process.GetProcessById(ProcessId);
                var hasExited = process.HasExited;
                logger?.Invoke($"IsAlive: Process found, hasExited={hasExited}");
                // Process exists and hasn't exited
                return !hasExited;
            }
            else
            {
                logger?.Invoke($"IsAlive: No ProcessId (0), relying on heartbeat only -> returning {heartbeatAlive}");
            }
        }
        catch (ArgumentException ex)
        {
            // Process doesn't exist - the PID is invalid or process has terminated
            // This is a definitive signal that the process is dead
            logger?.Invoke($"IsAlive returning FALSE: Process PID {ProcessId} doesn't exist (ArgumentException: {ex.Message})");
            return false;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // IMPORTANT: Win32Exception can occur when trying to access a process running under a different user
            // or when the service doesn't have permission to query the process.
            // Since the heartbeat is still alive (checked above), assume the process is alive.
            // The heartbeat mechanism is the definitive source of truth for cross-user scenarios.
            logger?.Invoke($"IsAlive: Win32Exception accessing process PID {ProcessId} (access denied/cross-user): {ex.Message} - relying on heartbeat -> returning {heartbeatAlive}");
            return heartbeatAlive;
        }
        catch (UnauthorizedAccessException ex)
        {
            // Access denied when trying to query the process (common in cross-user scenarios)
            // Since the heartbeat is alive, trust it over the process check
            logger?.Invoke($"IsAlive: UnauthorizedAccessException accessing process PID {ProcessId}: {ex.Message} - relying on heartbeat -> returning {heartbeatAlive}");
            return heartbeatAlive;
        }
        catch (Exception ex)
        {
            // If we can't check process for any other reason, fall back to heartbeat only
            // (backward compatibility for sessions without ProcessId)
            logger?.Invoke($"IsAlive: Unexpected exception accessing process PID {ProcessId}: {ex.GetType().Name}: {ex.Message} - relying on heartbeat -> returning {heartbeatAlive}");
            return heartbeatAlive;
        }

        // If ProcessId is 0 or we couldn't check, rely on heartbeat only
        logger?.Invoke($"IsAlive: Fallback - relying on heartbeat only -> returning {heartbeatAlive}");
        return heartbeatAlive;
    }

    /// <summary>
    /// Gets a display-friendly description of this session
    /// </summary>
    public string GetDescription()
    {
        // Defensive: Handle empty username
        var displayName = string.IsNullOrWhiteSpace(Username) ? "Unknown User" : Username;
        var pidInfo = ProcessId > 0 ? $", PID: {ProcessId}" : "";
        return $"{displayName} (Session {WindowsSessionId}, TrayId: {TrayId.Substring(0, Math.Min(8, TrayId.Length))}{pidInfo})";
    }
}
