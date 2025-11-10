using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace RJAutoMoverShared.Services;

/// <summary>
/// Service to check for new versions of RJAutoMover from GitHub.
/// Fetches the latest version from the version.txt file in the GitHub repository.
/// </summary>
public class VersionCheckerService
{
    private const string VersionUrl = "https://raw.githubusercontent.com/toohighonthehog/RJAutoMover/main/installer/version.txt";
    private const string InstallerUrl = "https://github.com/toohighonthehog/RJAutoMover/raw/main/installer/RJAutoMoverSetup.exe";
    private static readonly HttpClient _httpClient = new HttpClient();

    static VersionCheckerService()
    {
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
            MaxAge = TimeSpan.Zero
        };
    }

    /// <summary>
    /// Fetches the latest version from GitHub.
    /// Adds a cache-busting query parameter to bypass GitHub's CDN caching.
    /// </summary>
    /// <returns>The latest version string, or null if unable to fetch.</returns>
    public static async Task<string?> GetLatestVersionAsync()
    {
        try
        {
            // Add cache-busting query parameter to bypass GitHub CDN cache
            var cacheBustingUrl = $"{VersionUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var response = await _httpClient.GetStringAsync(cacheBustingUrl);
            return response?.Trim();
        }
        catch
        {
            // Silently fail - version checking is not critical
            return null;
        }
    }

    /// <summary>
    /// Compares two version strings to determine if the remote version is newer.
    /// The remote version represents the NEXT version to be released, so we subtract 1
    /// from the final octet to get the actual latest released version.
    /// </summary>
    /// <param name="currentVersion">The current version (e.g., "0.9.5.4").</param>
    /// <param name="remoteVersion">The remote version from GitHub (next version to be released).</param>
    /// <returns>True if remote version is newer, false otherwise.</returns>
    public static bool IsNewerVersion(string currentVersion, string remoteVersion)
    {
        try
        {
            // Parse versions
            var current = new Version(currentVersion);
            var remote = new Version(remoteVersion);

            // The version.txt contains the NEXT version to be compiled
            // So subtract 1 from the final octet to get the actual latest released version
            int actualLatestRevision = Math.Max(0, remote.Revision - 1);
            var actualLatest = new Version(remote.Major, remote.Minor, remote.Build, actualLatestRevision);

            return actualLatest > current;
        }
        catch
        {
            // If parsing fails, assume not newer
            return false;
        }
    }

    /// <summary>
    /// Gets the actual latest released version by subtracting 1 from the final octet
    /// of the remote version (since version.txt contains the next version to be compiled).
    /// </summary>
    /// <param name="remoteVersion">The remote version from GitHub (next version).</param>
    /// <returns>The actual latest released version string, or null if parsing fails.</returns>
    public static string? GetActualLatestVersion(string remoteVersion)
    {
        try
        {
            var remote = new Version(remoteVersion);
            int actualLatestRevision = Math.Max(0, remote.Revision - 1);
            var actualLatest = new Version(remote.Major, remote.Minor, remote.Build, actualLatestRevision);
            return actualLatest.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the URL for downloading the installer.
    /// </summary>
    /// <returns>The GitHub URL for the installer.</returns>
    public static string GetInstallerUrl()
    {
        return InstallerUrl;
    }

    /// <summary>
    /// Compares version to determine version status.
    /// </summary>
    /// <param name="currentVersion">The current installed version.</param>
    /// <param name="latestVersion">The actual latest released version.</param>
    /// <returns>1 if update available, 0 if current, -1 if pre-release.</returns>
    public static int CompareVersions(string currentVersion, string latestVersion)
    {
        try
        {
            var current = new Version(currentVersion);
            var latest = new Version(latestVersion);

            if (latest > current)
            {
                return 1;  // Update available
            }
            else if (latest < current)
            {
                return -1; // Pre-release (current is newer)
            }
            else
            {
                return 0;  // Latest version
            }
        }
        catch
        {
            return 0; // Default to current if parsing fails
        }
    }

    /// <summary>
    /// Checks if there's a new version available and returns update information.
    /// </summary>
    /// <param name="currentVersion">The current version to compare against.</param>
    /// <returns>A tuple with (versionStatus, actualLatestVersion, installerUrl) where versionStatus: 1=update available, 0=latest, -1=pre-release.</returns>
    public static async Task<(int versionStatus, string? latestVersion, string installerUrl)> CheckForUpdatesAsync(string currentVersion)
    {
        var remoteVersion = await GetLatestVersionAsync();

        if (string.IsNullOrEmpty(remoteVersion))
        {
            return (0, null, InstallerUrl);
        }

        // Get the actual latest released version (subtract 1 from final octet)
        var actualLatestVersion = GetActualLatestVersion(remoteVersion);

        if (string.IsNullOrEmpty(actualLatestVersion))
        {
            return (0, null, InstallerUrl);
        }

        int status = CompareVersions(currentVersion, actualLatestVersion);
        return (status, actualLatestVersion, InstallerUrl);
    }
}
