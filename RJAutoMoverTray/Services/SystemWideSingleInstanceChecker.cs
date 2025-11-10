using System.Diagnostics;

namespace RJAutoMoverTray.Services;

public static class SystemWideSingleInstanceChecker
{
    private const string GlobalMutexName = @"Global\RJAutoMoverTray_SystemWide_SingleInstance_Mutex";
    private const string ProcessName = "RJAutoMoverTray";

    public static (bool IsAlreadyRunning, string? RunningUserInfo, Mutex? CreatedMutex) CheckForExistingInstance()
    {
        try
        {
            // Method 1: Check for running processes first and kill them
            var processResult = CheckAndKillRunningProcesses();
            if (processResult.KilledProcesses)
            {
                // Wait a moment for processes to fully terminate
                System.Threading.Thread.Sleep(1000);
            }

            // Method 2: Try to create a global mutex after killing processes
            var (mutexResult, mutexInfo, createdMutex) = CheckAndCreateGlobalMutex();
            if (mutexResult)
            {
                // Another instance detected via mutex (shouldn't happen after killing processes)
                return (true, mutexInfo, null);
            }

            // No existing instance found, return the created mutex
            return (false, processResult.KillInfo, createdMutex);
        }
        catch (Exception ex)
        {
            // Log error but don't prevent startup
            return (false, $"Error during check: {ex.Message}", null);
        }
    }

    private static (bool IsExistingInstance, string MutexInfo, Mutex? CreatedMutex) CheckAndCreateGlobalMutex()
    {
        try
        {
            // Try to create a global mutex
            var mutex = new Mutex(true, GlobalMutexName, out bool createdNew);

            if (!createdNew)
            {
                // Mutex already exists - check if it's abandoned or legitimately held
                try
                {
                    // Try to acquire the mutex with a longer timeout to avoid false positives
                    // If it's abandoned (process crashed/killed), we'll get it
                    // If it's held by a running process, we'll timeout
                    // Increased timeout from 100ms to 500ms to reduce race condition false positives
                    bool acquired = mutex.WaitOne(500); // 500ms timeout

                    if (acquired)
                    {
                        // We got the mutex! This means it was abandoned (orphaned)
                        // The previous process was killed/crashed without releasing it
                        return (false, "Acquired abandoned mutex from killed process", mutex);
                    }
                    else
                    {
                        // Timeout - mutex is actively held by another instance
                        // Before we give up, let's verify with a second attempt after a brief pause
                        // This handles the edge case where the mutex was released between our checks
                        System.Threading.Thread.Sleep(100);
                        acquired = mutex.WaitOne(500); // Try one more time

                        if (acquired)
                        {
                            return (false, "Acquired mutex on second attempt (first holder released)", mutex);
                        }
                        else
                        {
                            // Definitely held by another active instance
                            mutex.Dispose();
                            return (true, "Detected via global mutex (actively held after retry)", null);
                        }
                    }
                }
                catch (AbandonedMutexException)
                {
                    // Mutex was abandoned - we now own it
                    return (false, "Acquired explicitly abandoned mutex", mutex);
                }
            }

            // Successfully created new mutex - return it to keep alive
            return (false, "", mutex);
        }
        catch (UnauthorizedAccessException)
        {
            // Can't access the mutex - likely a permission issue with an orphaned mutex
            // Try to open and acquire it instead
            try
            {
                var mutex = Mutex.OpenExisting(GlobalMutexName);
                bool acquired = mutex.WaitOne(500); // Try to acquire with longer timeout

                if (acquired)
                {
                    // Got it! It was orphaned
                    return (false, "Recovered orphaned mutex after permission error", mutex);
                }
                else
                {
                    // Actively held
                    mutex.Dispose();
                    return (true, "Detected via mutex (different user, actively held)", null);
                }
            }
            catch (AbandonedMutexException ex)
            {
                // Create new reference to the now-acquired mutex
                var mutex = new Mutex(false, GlobalMutexName);
                return (false, "Recovered abandoned mutex after permission error", mutex);
            }
            catch
            {
                // Can't recover - assume it's held by another user
                return (true, "Detected via mutex access denial (likely running under different user)", null);
            }
        }
        catch (Exception ex)
        {
            // If we can't create mutex, don't prevent startup but log error
            return (false, $"Mutex check failed: {ex.Message}", null);
        }
    }

    private static (bool KilledProcesses, string KillInfo) CheckAndKillRunningProcesses()
    {
        try
        {
            var currentProcess = Process.GetCurrentProcess();
            var allRJTrayProcesses = Process.GetProcessesByName(ProcessName);

            var runningProcesses = allRJTrayProcesses
                .Where(p => p.Id != currentProcess.Id) // Exclude current process
                .ToList();

            if (runningProcesses.Count == 0)
            {
                return (false, "");
            }

            // Filter out processes that are too young (started very recently)
            // This prevents race conditions where multiple instances start simultaneously
            var now = DateTime.Now;
            var matureProcesses = runningProcesses
                .Where(p =>
                {
                    try
                    {
                        var age = now - p.StartTime;
                        // Only kill processes that have been running for at least 2 seconds
                        // This gives them time to fully initialize and acquire the mutex
                        return age.TotalSeconds >= 2;
                    }
                    catch
                    {
                        // If we can't get start time, assume it's mature enough to kill
                        return true;
                    }
                })
                .ToList();

            // Dispose of the young processes we're not killing
            foreach (var youngProcess in runningProcesses.Except(matureProcesses))
            {
                youngProcess.Dispose();
            }

            if (matureProcesses.Count == 0)
            {
                return (false, "Found instance(s) but all were too young (< 2 seconds) - allowing concurrent startup to resolve via mutex");
            }

            // Kill all mature existing instances and collect information
            var killedInfos = new List<string>();
            var failedKills = new List<string>();

            foreach (var process in matureProcesses)
            {
                try
                {
                    // Validate the process is still running before attempting to kill
                    // This prevents issues with stale Process objects
                    if (process.HasExited)
                    {
                        process.Dispose();
                        continue;
                    }

                    string userInfo = GetProcessUserInfo(process);
                    string startTime = process.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
                    int processId = process.Id;
                    var age = now - process.StartTime;

                    // Attempt to kill the process
                    process.Kill();
                    process.WaitForExit(5000); // Wait up to 5 seconds for graceful exit

                    killedInfos.Add($"Killed PID {processId} ({userInfo}) started at {startTime} (age: {age.TotalSeconds:F1}s)");
                }
                catch (InvalidOperationException)
                {
                    // Process already exited - not an error, just skip it
                    continue;
                }
                catch (Exception ex)
                {
                    failedKills.Add($"Failed to kill PID {process.Id}: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            // Build combined status message
            var statusParts = new List<string>();
            if (killedInfos.Count > 0)
            {
                statusParts.Add($"Successfully killed {killedInfos.Count} instance(s): {string.Join("; ", killedInfos)}");
            }
            if (failedKills.Count > 0)
            {
                statusParts.Add($"Failed to kill {failedKills.Count} instance(s): {string.Join("; ", failedKills)}");
            }

            string combinedInfo = string.Join(" | ", statusParts);
            return (killedInfos.Count > 0, combinedInfo);
        }
        catch (Exception ex)
        {
            return (false, $"Process kill operation error: {ex.Message}");
        }
    }

    private static string GetProcessUserInfo(Process process)
    {
        try
        {
            // Try to get the owner of the process
            var processHandle = process.Handle;

            // This approach works but requires additional permissions
            var startInfo = new ProcessStartInfo
            {
                FileName = "wmic",
                Arguments = $"process where processid={process.Id} get owner /format:csv",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var wmicProcess = Process.Start(startInfo);
            if (wmicProcess != null)
            {
                string output = wmicProcess.StandardOutput.ReadToEnd();
                wmicProcess.WaitForExit();

                // Parse WMIC output to extract username
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 1)
                {
                    var parts = lines[1].Split(',');
                    if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
                    {
                        return parts[2].Trim();
                    }
                }
            }

            // Fallback: try to get session info
            return $"Session {process.SessionId}";
        }
        catch (Exception)
        {
            // Fallback to session ID only
            try
            {
                return $"Session {process.SessionId}";
            }
            catch
            {
                return "Unknown User";
            }
        }
    }

    public static void ShowAlreadyRunningMessage(string? existingInstanceInfo)
    {
        string message = "RJAutoMoverTray is already running on this system.";

        if (!string.IsNullOrEmpty(existingInstanceInfo))
        {
            message += $"\n\nExisting instance details:\n{existingInstanceInfo}";
        }

        message += "\n\nOnly one instance of RJAutoMoverTray is allowed to run system-wide.\nPlease close the existing instance before starting a new one.";

        System.Windows.MessageBox.Show(
            message,
            "RJAutoMoverTray Already Running",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning
        );
    }

}
