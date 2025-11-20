using System.Data;
using Microsoft.Data.Sqlite;
using RJAutoMoverShared.Models;
using LogLevel = RJAutoMoverShared.Models.LogLevel;

namespace RJAutoMoverShared.Services;

/// <summary>
/// Manages persistent storage of file transfer activity history using SQLite.
/// Stores activity across service restarts and user sessions.
/// </summary>
public class ActivityHistoryService : IDisposable
{
    private readonly LoggingService _logger;
    private readonly string _databasePath;
    private readonly string _sessionId;
    private readonly int _maxRecords;
    private readonly int _retentionDays;
    private bool _enabled; // Not readonly - can be disabled if initialization fails
    private SqliteConnection? _connection;
    private bool _disposed = false;

    public string SessionId => _sessionId;

    public ActivityHistoryService(
        LoggingService logger,
        string databasePath,
        bool enabled = true,
        int maxRecords = 5000,
        int retentionDays = 90)
    {
        _logger = logger;
        _databasePath = databasePath;
        _sessionId = Guid.NewGuid().ToString("N")[..12]; // 12-char session ID
        _enabled = enabled;
        _maxRecords = maxRecords;
        _retentionDays = retentionDays;

        if (_enabled)
        {
            InitializeDatabase();
            PurgeOldRecords();
        }
    }

    private void InitializeDatabase()
    {
        const int maxRetries = 3;
        int retryCount = 0;
        bool initialized = false;

        while (retryCount < maxRetries && !initialized)
        {
            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Check if database file is corrupted before attempting to open
                if (File.Exists(_databasePath))
                {
                    try
                    {
                        // Quick integrity check - try to detect obvious corruption
                        var testConnectionString = new SqliteConnectionStringBuilder
                        {
                            DataSource = _databasePath,
                            Mode = SqliteOpenMode.ReadOnly
                        }.ToString();

                        using var testConnection = new SqliteConnection(testConnectionString);
                        testConnection.Open();
                        using var integrityCmd = testConnection.CreateCommand();
                        integrityCmd.CommandText = "PRAGMA quick_check;";
                        var result = integrityCmd.ExecuteScalar()?.ToString();

                        if (result != "ok")
                        {
                            _logger.Log(LogLevel.WARN, $"Database integrity check failed: {result}. Backing up and recreating...");
                            testConnection.Close();
                            BackupCorruptedDatabase();
                            File.Delete(_databasePath);
                        }
                    }
                    catch (SqliteException sqlEx)
                    {
                        _logger.Log(LogLevel.WARN, $"Database appears corrupted (SQLite error: {sqlEx.Message}). Backing up and recreating...");
                        BackupCorruptedDatabase();
                        File.Delete(_databasePath);
                    }
                }

                // Create connection string with WAL mode for better concurrency
                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = _databasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Shared
                }.ToString();

                _connection = new SqliteConnection(connectionString);
                _connection.Open();

                // Enable WAL mode for better concurrency and crash recovery
                using var walCmd = _connection.CreateCommand();
                walCmd.CommandText = "PRAGMA journal_mode=WAL;";
                walCmd.ExecuteNonQuery();

                // Set synchronous mode for crash safety (still fast with WAL)
                using var syncCmd = _connection.CreateCommand();
                syncCmd.CommandText = "PRAGMA synchronous=NORMAL;";
                syncCmd.ExecuteNonQuery();

                // Create Activities table if it doesn't exist
                using var createTableCmd = _connection.CreateCommand();
                createTableCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Activities (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SessionId TEXT NOT NULL,
                        Timestamp TEXT NOT NULL,
                        FileName TEXT NOT NULL,
                        SourceFolder TEXT NOT NULL,
                        DestinationFolder TEXT NOT NULL,
                        RuleName TEXT NOT NULL,
                        FileSizeBytes INTEGER NOT NULL,
                        Status TEXT NOT NULL,
                        ErrorMessage TEXT,
                        AttemptCount INTEGER DEFAULT 1
                    );";
                createTableCmd.ExecuteNonQuery();

                // Create indexes for better query performance
                using var createIndexCmd = _connection.CreateCommand();
                createIndexCmd.CommandText = @"
                    CREATE INDEX IF NOT EXISTS idx_activities_timestamp ON Activities(Timestamp DESC);
                    CREATE INDEX IF NOT EXISTS idx_activities_session ON Activities(SessionId);";
                createIndexCmd.ExecuteNonQuery();

                _logger.Log(LogLevel.INFO, $"Activity history database initialized: {_databasePath}");
                _logger.Log(LogLevel.INFO, $"Session ID: {_sessionId}");
                initialized = true;
            }
            catch (Exception ex)
            {
                retryCount++;
                _logger.Log(LogLevel.ERROR, $"Failed to initialize activity history database (attempt {retryCount}/{maxRetries}): {ex.Message}");

                // Clean up connection if it was created
                try
                {
                    _connection?.Close();
                    _connection?.Dispose();
                    _connection = null;
                }
                catch { /* Ignore cleanup errors */ }

                if (retryCount < maxRetries)
                {
                    _logger.Log(LogLevel.INFO, $"Retrying database initialization in 2 seconds...");
                    Thread.Sleep(2000);
                }
                else
                {
                    _logger.Log(LogLevel.ERROR, "Failed to initialize activity history after all retries. Activity history will be disabled for this session.");
                    _logger.Log(LogLevel.INFO, "The service will continue to operate normally, but file transfer history will not be persisted.");
                    _enabled = false; // Disable if all retries fail
                }
            }
        }
    }

    /// <summary>
    /// Backs up a corrupted database file for later analysis
    /// </summary>
    private void BackupCorruptedDatabase()
    {
        try
        {
            var backupPath = _databasePath + $".corrupted.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Copy(_databasePath, backupPath, overwrite: true);
            _logger.Log(LogLevel.WARN, $"Corrupted database backed up to: {backupPath}");
        }
        catch (Exception backupEx)
        {
            _logger.Log(LogLevel.WARN, $"Failed to backup corrupted database: {backupEx.Message}");
        }
    }

    /// <summary>
    /// Records a file transfer activity to the database with retry logic.
    /// Returns the database ID if successfully recorded, null otherwise.
    /// </summary>
    public long? RecordActivity(
        string fileName,
        string sourceFolder,
        string destinationFolder,
        string ruleName,
        long fileSizeBytes,
        string status,
        string? errorMessage = null,
        int attemptCount = 1)
    {
        if (!_enabled) return null;

        const int maxWriteRetries = 2;
        int writeAttempt = 0;
        bool recorded = false;
        long? recordId = null;

        while (writeAttempt < maxWriteRetries && !recorded)
        {
            writeAttempt++;

            // Check connection health before attempting to record
            if (!EnsureConnectionHealthy())
            {
                if (writeAttempt < maxWriteRetries)
                {
                    _logger.Log(LogLevel.WARN, $"Database connection unhealthy (attempt {writeAttempt}/{maxWriteRetries}) - retrying...");
                    Thread.Sleep(500);
                    continue;
                }
                else
                {
                    _logger.Log(LogLevel.ERROR, "============================================");
                    _logger.Log(LogLevel.FATAL, "ACTIVITY HISTORY WRITE FAILED");
                    _logger.Log(LogLevel.ERROR, "============================================");
                    _logger.Log(LogLevel.ERROR, "Reason: Database connection unhealthy after all retries");
                    _logger.Log(LogLevel.FATAL, "FILE TRANSFER WILL BE BLOCKED");
                    _logger.Log(LogLevel.ERROR, "Cannot proceed with file operations without accountability");
                    _logger.Log(LogLevel.ERROR, "============================================");
                    _enabled = false;
                    return null;
                }
            }

            try
            {
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Activities
                    (SessionId, Timestamp, FileName, SourceFolder, DestinationFolder, RuleName, FileSizeBytes, Status, ErrorMessage, AttemptCount)
                    VALUES
                    (@SessionId, @Timestamp, @FileName, @SourceFolder, @DestinationFolder, @RuleName, @FileSizeBytes, @Status, @ErrorMessage, @AttemptCount);
                    SELECT last_insert_rowid();";

                cmd.Parameters.AddWithValue("@SessionId", _sessionId);
                cmd.Parameters.AddWithValue("@Timestamp", DateTime.Now.ToString("o"));
                cmd.Parameters.AddWithValue("@FileName", fileName);
                cmd.Parameters.AddWithValue("@SourceFolder", sourceFolder);
                cmd.Parameters.AddWithValue("@DestinationFolder", destinationFolder);
                cmd.Parameters.AddWithValue("@RuleName", ruleName);
                cmd.Parameters.AddWithValue("@FileSizeBytes", fileSizeBytes);
                cmd.Parameters.AddWithValue("@Status", status);
                cmd.Parameters.AddWithValue("@ErrorMessage", errorMessage ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@AttemptCount", attemptCount);

                recordId = (long?)cmd.ExecuteScalar();
                _logger.Log(LogLevel.DEBUG, $"Activity recorded: {fileName} - {status} (ID: {recordId})");
                recorded = true;

                // Force WAL checkpoint to flush data to main DB file
                // This ensures data is visible in the main .db file, not just the .wal file
                CheckpointWAL();

                EnforceMaxRecords();
                return recordId; // Success
            }
            catch (SqliteException sqlEx)
            {
                HandleSqliteError(sqlEx, writeAttempt, maxWriteRetries, fileName);
                if (writeAttempt >= maxWriteRetries) break;

                // Recovery attempts for specific errors
                if (sqlEx.SqliteErrorCode == 11 || sqlEx.SqliteErrorCode == 26)
                {
                    if (!AttemptDatabaseRecovery()) break;
                }
                else if (sqlEx.SqliteErrorCode == 13)
                {
                    Thread.Sleep(1000); // Wait for potential disk space
                }
            }
            catch (UnauthorizedAccessException uaEx)
            {
                _logger.Log(LogLevel.ERROR, $"Permission denied writing to activity database (attempt {writeAttempt}/{maxWriteRetries}): {uaEx.Message}");
                if (writeAttempt >= maxWriteRetries)
                {
                    _logger.Log(LogLevel.ERROR, "============================================");
                    _logger.Log(LogLevel.FATAL, "ACTIVITY HISTORY WRITE FAILED");
                    _logger.Log(LogLevel.ERROR, "============================================");
                    _logger.Log(LogLevel.ERROR, "Reason: Insufficient permissions to write to database");
                    _logger.Log(LogLevel.FATAL, "FILE TRANSFER WILL BE BLOCKED");
                    _logger.Log(LogLevel.ERROR, "Cannot proceed without accountability - all file operations stopped");
                    _logger.Log(LogLevel.ERROR, $"Database location: {_databasePath}");
                    _logger.Log(LogLevel.ERROR, "Action: Check file system permissions for the service account, then restart service");
                    _logger.Log(LogLevel.ERROR, "============================================");
                    _enabled = false;
                }
                break;
            }
            catch (IOException ioEx)
            {
                _logger.Log(LogLevel.ERROR, $"I/O error writing to activity database (attempt {writeAttempt}/{maxWriteRetries}): {ioEx.Message}");
                if (writeAttempt >= maxWriteRetries)
                {
                    _logger.Log(LogLevel.ERROR, "============================================");
                    _logger.Log(LogLevel.FATAL, "ACTIVITY HISTORY WRITE FAILED");
                    _logger.Log(LogLevel.ERROR, "============================================");
                    _logger.Log(LogLevel.ERROR, "Reason: I/O error persists after retries");
                    _logger.Log(LogLevel.FATAL, "FILE TRANSFER WILL BE BLOCKED");
                    _logger.Log(LogLevel.ERROR, $"Database location: {_databasePath}");
                    _logger.Log(LogLevel.ERROR, "Possible causes: Disk full, network drive disconnected, antivirus blocking");
                    _logger.Log(LogLevel.ERROR, "Action: Fix the I/O issue, then restart service");
                    _logger.Log(LogLevel.ERROR, "============================================");
                    _enabled = false;
                }
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.WARN, $"Failed to record activity (attempt {writeAttempt}/{maxWriteRetries}): {ex.Message}");
                if (writeAttempt >= maxWriteRetries)
                {
                    _logger.Log(LogLevel.ERROR, "============================================");
                    _logger.Log(LogLevel.FATAL, "ACTIVITY HISTORY WRITE FAILED");
                    _logger.Log(LogLevel.ERROR, "============================================");
                    _logger.Log(LogLevel.ERROR, $"Reason: {ex.Message}");
                    _logger.Log(LogLevel.FATAL, "FILE TRANSFER WILL BE BLOCKED");
                    _logger.Log(LogLevel.ERROR, "Cannot proceed without accountability");
                    _logger.Log(LogLevel.ERROR, "============================================");
                }
                Thread.Sleep(500);
            }
        }

        return null; // Failed to record after all retries
    }

    /// <summary>
    /// Updates an existing activity record in the database (e.g., to change status from InProgress to Success/Failed)
    /// Returns true if successfully updated, false otherwise.
    /// </summary>
    public bool UpdateActivity(
        long recordId,
        string status,
        string? errorMessage = null,
        int attemptCount = 1)
    {
        if (!_enabled) return false;

        const int maxWriteRetries = 2;
        int writeAttempt = 0;
        bool updated = false;

        while (writeAttempt < maxWriteRetries && !updated)
        {
            writeAttempt++;

            // Check connection health before attempting to update
            if (!EnsureConnectionHealthy())
            {
                if (writeAttempt < maxWriteRetries)
                {
                    _logger.Log(LogLevel.WARN, $"Database connection unhealthy (attempt {writeAttempt}/{maxWriteRetries}) - retrying...");
                    Thread.Sleep(500);
                    continue;
                }
                else
                {
                    _logger.Log(LogLevel.ERROR, "Failed to update activity record - database connection unhealthy");
                    return false;
                }
            }

            try
            {
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    UPDATE Activities
                    SET Status = @Status,
                        ErrorMessage = @ErrorMessage,
                        AttemptCount = @AttemptCount,
                        Timestamp = @Timestamp
                    WHERE Id = @Id;";

                cmd.Parameters.AddWithValue("@Id", recordId);
                cmd.Parameters.AddWithValue("@Status", status);
                cmd.Parameters.AddWithValue("@ErrorMessage", errorMessage ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@AttemptCount", attemptCount);
                cmd.Parameters.AddWithValue("@Timestamp", DateTime.Now.ToString("o")); // Update timestamp on completion

                var rowsAffected = cmd.ExecuteNonQuery();
                if (rowsAffected > 0)
                {
                    _logger.Log(LogLevel.DEBUG, $"Activity updated: ID {recordId} - {status}");

                    // Force WAL checkpoint to flush data to main DB file
                    CheckpointWAL();

                    updated = true;
                    return true;
                }
                else
                {
                    _logger.Log(LogLevel.WARN, $"Activity update failed: No record found with ID {recordId}");
                    return false;
                }
            }
            catch (SqliteException sqlEx)
            {
                _logger.Log(LogLevel.ERROR, $"SQLite error updating activity (attempt {writeAttempt}/{maxWriteRetries}): {sqlEx.Message}");
                if (writeAttempt >= maxWriteRetries) break;
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.ERROR, $"Failed to update activity (attempt {writeAttempt}/{maxWriteRetries}): {ex.Message}");
                if (writeAttempt >= maxWriteRetries) break;
                Thread.Sleep(500);
            }
        }

        return false; // Failed to update after all retries
    }

    private void HandleSqliteError(SqliteException sqlEx, int attempt, int maxAttempts, string fileName)
    {
        var errorCode = sqlEx.SqliteErrorCode;
        var errorName = errorCode switch
        {
            11 => "SQLITE_CORRUPT (Database corruption)",
            13 => "SQLITE_FULL (Disk full or quota exceeded)",
            14 => "SQLITE_CANTOPEN (Unable to open database)",
            26 => "SQLITE_NOTADB (Not a valid database)",
            _ => $"SQLite error {errorCode}"
        };

        _logger.Log(LogLevel.ERROR, $"Database error (attempt {attempt}/{maxAttempts}): {errorName}");
        _logger.Log(LogLevel.DEBUG, $"SQLite message: {sqlEx.Message}");

        if (attempt >= maxAttempts)
        {
            _logger.Log(LogLevel.ERROR, "============================================");
            _logger.Log(LogLevel.FATAL, "ACTIVITY HISTORY WRITE FAILED");
            _logger.Log(LogLevel.ERROR, "============================================");

            switch (errorCode)
            {
                case 11 or 26:
                    _logger.Log(LogLevel.ERROR, "Reason: Database corruption could not be recovered");
                    _logger.Log(LogLevel.ERROR, "A corrupted database backup may exist for analysis");
                    break;
                case 13:
                    _logger.Log(LogLevel.ERROR, "Reason: Disk full - no space for activity records");
                    _logger.Log(LogLevel.ERROR, "Action: Free disk space, then restart service");
                    break;
                case 14:
                    _logger.Log(LogLevel.ERROR, "Reason: Cannot open database file");
                    _logger.Log(LogLevel.ERROR, "Possible: File locked, permission denied, or file system error");
                    break;
                default:
                    _logger.Log(LogLevel.ERROR, $"Reason: Database error {errorCode} persists");
                    break;
            }

            _logger.Log(LogLevel.FATAL, "FILE TRANSFER WILL BE BLOCKED");
            _logger.Log(LogLevel.ERROR, "Cannot proceed with file operations without accountability");
            _logger.Log(LogLevel.ERROR, $"Database location: {_databasePath}");
            _logger.Log(LogLevel.ERROR, "============================================");
            _enabled = false;
        }
        else
        {
            _logger.Log(LogLevel.WARN, $"Will retry database write for: {fileName}");
        }
    }

    /// <summary>
    /// Forces a WAL checkpoint to flush data from the .wal file to the main .db file
    /// </summary>
    private void CheckpointWAL()
    {
        try
        {
            if (_connection != null && _connection.State == System.Data.ConnectionState.Open)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                cmd.ExecuteNonQuery();
                _logger.Log(LogLevel.DEBUG, "WAL checkpoint executed - data flushed to main database file");
            }
        }
        catch (Exception ex)
        {
            // Don't fail the whole operation if checkpoint fails - it will happen automatically later
            _logger.Log(LogLevel.DEBUG, $"WAL checkpoint failed (non-critical): {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures the database connection is healthy, attempts recovery if needed
    /// </summary>
    private bool EnsureConnectionHealthy()
    {
        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
        {
            _logger.Log(LogLevel.WARN, "Database connection not open. Attempting to reconnect...");
            return AttemptDatabaseRecovery();
        }

        return true;
    }

    /// <summary>
    /// Attempts to recover from database errors
    /// </summary>
    private bool AttemptDatabaseRecovery()
    {
        try
        {
            _logger.Log(LogLevel.INFO, "Attempting database recovery...");

            // Close existing connection if any
            try
            {
                _connection?.Close();
                _connection?.Dispose();
            }
            catch { /* Ignore errors during cleanup */ }

            // Try to reinitialize
            InitializeDatabase();

            return _enabled && _connection != null;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Database recovery failed: {ex.Message}");
            _enabled = false;
            return false;
        }
    }

    /// <summary>
    /// Retrieves recent activities from the database
    /// </summary>
    public List<ActivityRecord> GetRecentActivities(int limit = 1000, string? sessionFilter = null)
    {
        var activities = new List<ActivityRecord>();

        if (!_enabled || _connection == null) return activities;

        try
        {
            using var cmd = _connection.CreateCommand();

            if (!string.IsNullOrEmpty(sessionFilter))
            {
                cmd.CommandText = @"
                    SELECT Id, SessionId, Timestamp, FileName, SourceFolder, DestinationFolder, RuleName, FileSizeBytes, Status, ErrorMessage, AttemptCount
                    FROM Activities
                    WHERE SessionId = @SessionId
                    ORDER BY Id DESC
                    LIMIT @Limit;";
                cmd.Parameters.AddWithValue("@SessionId", sessionFilter);
            }
            else
            {
                cmd.CommandText = @"
                    SELECT Id, SessionId, Timestamp, FileName, SourceFolder, DestinationFolder, RuleName, FileSizeBytes, Status, ErrorMessage, AttemptCount
                    FROM Activities
                    ORDER BY Id DESC
                    LIMIT @Limit;";
            }

            cmd.Parameters.AddWithValue("@Limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                activities.Add(new ActivityRecord
                {
                    Id = reader.GetInt64(0),
                    SessionId = reader.GetString(1),
                    Timestamp = DateTime.Parse(reader.GetString(2)),
                    FileName = reader.GetString(3),
                    SourceFolder = reader.GetString(4),
                    DestinationFolder = reader.GetString(5),
                    RuleName = reader.GetString(6),
                    FileSizeBytes = reader.GetInt64(7),
                    Status = reader.GetString(8),
                    ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9),
                    AttemptCount = reader.GetInt32(10)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.WARN, $"Failed to retrieve activities: {ex.Message}");
        }

        return activities;
    }

    /// <summary>
    /// Gets count of activities by session
    /// </summary>
    public Dictionary<string, int> GetSessionCounts()
    {
        var counts = new Dictionary<string, int>();

        if (!_enabled || _connection == null) return counts;

        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                SELECT SessionId, COUNT(*) as Count
                FROM Activities
                GROUP BY SessionId
                ORDER BY MAX(Id) DESC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                counts[reader.GetString(0)] = reader.GetInt32(1);
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.WARN, $"Failed to get session counts: {ex.Message}");
        }

        return counts;
    }

    /// <summary>
    /// Clears all activity records from the database
    /// </summary>
    public void ClearAllActivities()
    {
        if (!_enabled || _connection == null) return;

        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Activities;";
            var deleted = cmd.ExecuteNonQuery();

            // Vacuum to reclaim space
            using var vacuumCmd = _connection.CreateCommand();
            vacuumCmd.CommandText = "VACUUM;";
            vacuumCmd.ExecuteNonQuery();

            _logger.Log(LogLevel.INFO, $"Cleared {deleted} activity records from database");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Failed to clear activities: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets database statistics including path, status, total records, and last transfer timestamp
    /// </summary>
    public DatabaseStatistics GetDatabaseStatistics()
    {
        var stats = new DatabaseStatistics
        {
            DatabasePath = _databasePath,
            IsEnabled = _enabled,
            IsConnected = _connection != null && _connection.State == System.Data.ConnectionState.Open,
            TotalRecords = 0,
            LastTransferTimestamp = null
        };

        if (!_enabled || _connection == null)
        {
            return stats;
        }

        try
        {
            // Get total record count
            using var countCmd = _connection.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM Activities;";
            stats.TotalRecords = Convert.ToInt32(countCmd.ExecuteScalar());

            // Get last transfer timestamp
            using var lastCmd = _connection.CreateCommand();
            lastCmd.CommandText = "SELECT Timestamp FROM Activities ORDER BY Id DESC LIMIT 1;";
            var lastTimestamp = lastCmd.ExecuteScalar();
            if (lastTimestamp != null && lastTimestamp != DBNull.Value)
            {
                stats.LastTransferTimestamp = DateTime.Parse(lastTimestamp.ToString()!);
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.WARN, $"Failed to get database statistics: {ex.Message}");
            stats.ErrorMessage = ex.Message;
        }

        return stats;
    }

    /// <summary>
    /// Finds and cleans up orphaned transfers that are still marked as "InProgress" from previous sessions.
    /// These are transfers that were interrupted (e.g., service crash, power loss) and never completed.
    /// Returns the number of orphaned transfers found and cleaned up.
    /// </summary>
    public int CleanupOrphanedTransfers()
    {
        if (!_enabled || _connection == null) return 0;

        try
        {
            _logger.Log(LogLevel.INFO, "Checking for orphaned transfers from previous sessions...");

            // Find all InProgress records that are NOT from the current session
            using var findCmd = _connection.CreateCommand();
            findCmd.CommandText = @"
                SELECT Id, FileName, SourceFolder, DestinationFolder, RuleName, Timestamp
                FROM Activities
                WHERE Status = 'InProgress' AND SessionId != @CurrentSessionId;";
            findCmd.Parameters.AddWithValue("@CurrentSessionId", _sessionId);

            var orphanedTransfers = new List<(long Id, string FileName, string Timestamp)>();

            using (var reader = findCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    orphanedTransfers.Add((
                        reader.GetInt64(0),
                        reader.GetString(1),
                        reader.GetString(5)
                    ));
                }
            }

            if (orphanedTransfers.Count == 0)
            {
                _logger.Log(LogLevel.INFO, "No orphaned transfers found");
                return 0;
            }

            _logger.Log(LogLevel.WARN, $"Found {orphanedTransfers.Count} orphaned transfer(s) from previous session(s)");

            // Update all orphaned transfers to Failed status
            using var updateCmd = _connection.CreateCommand();
            updateCmd.CommandText = @"
                UPDATE Activities
                SET Status = 'Failed',
                    ErrorMessage = 'Transfer interrupted - service was stopped or crashed'
                WHERE Status = 'InProgress' AND SessionId != @CurrentSessionId;";
            updateCmd.Parameters.AddWithValue("@CurrentSessionId", _sessionId);

            var updated = updateCmd.ExecuteNonQuery();

            // Log each orphaned transfer
            foreach (var (id, fileName, timestamp) in orphanedTransfers)
            {
                _logger.Log(LogLevel.INFO, $"Marked orphaned transfer as Failed: {fileName} (ID: {id}, started: {timestamp})");
            }

            _logger.Log(LogLevel.INFO, $"Cleaned up {updated} orphaned transfer(s)");

            return updated;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Failed to cleanup orphaned transfers: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Purges old records based on retention policy
    /// </summary>
    private void PurgeOldRecords()
    {
        if (!_enabled || _connection == null || _retentionDays <= 0) return;

        try
        {
            var cutoffDate = DateTime.Now.AddDays(-_retentionDays).ToString("o");

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Activities WHERE Timestamp < @CutoffDate;";
            cmd.Parameters.AddWithValue("@CutoffDate", cutoffDate);
            var deleted = cmd.ExecuteNonQuery();

            if (deleted > 0)
            {
                _logger.Log(LogLevel.INFO, $"Purged {deleted} old activity records (older than {_retentionDays} days)");
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.WARN, $"Failed to purge old records: {ex.Message}");
        }
    }

    /// <summary>
    /// Enforces maximum record limit
    /// </summary>
    private void EnforceMaxRecords()
    {
        if (!_enabled || _connection == null || _maxRecords <= 0) return;

        try
        {
            using var countCmd = _connection.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM Activities;";
            var count = Convert.ToInt32(countCmd.ExecuteScalar());

            if (count > _maxRecords)
            {
                var toDelete = count - _maxRecords;
                using var deleteCmd = _connection.CreateCommand();
                deleteCmd.CommandText = @"
                    DELETE FROM Activities
                    WHERE Id IN (
                        SELECT Id FROM Activities
                        ORDER BY Id ASC
                        LIMIT @Limit
                    );";
                deleteCmd.Parameters.AddWithValue("@Limit", toDelete);
                deleteCmd.ExecuteNonQuery();

                _logger.Log(LogLevel.DEBUG, $"Enforced max records limit: deleted {toDelete} oldest records");
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.WARN, $"Failed to enforce max records: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears all transfer history from the database.
    /// </summary>
    /// <returns>The number of records deleted.</returns>
    public int ClearAllHistory()
    {
        if (!_enabled || _connection == null) return 0;

        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Activities;";
            int deleted = cmd.ExecuteNonQuery();

            _logger.Log(LogLevel.INFO, $"Cleared all transfer history: {deleted} record(s) deleted");
            return deleted;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Failed to clear all history: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Clears transfer history for all sessions except the current one.
    /// </summary>
    /// <param name="currentSessionId">The session ID to keep (typically the current service session).</param>
    /// <returns>The number of records deleted.</returns>
    public int ClearPreviousSessions(string currentSessionId)
    {
        if (!_enabled || _connection == null) return 0;

        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Activities WHERE SessionId != @SessionId;";
            cmd.Parameters.AddWithValue("@SessionId", currentSessionId);
            int deleted = cmd.ExecuteNonQuery();

            _logger.Log(LogLevel.INFO, $"Cleared previous session history: {deleted} record(s) deleted (kept session: {currentSessionId})");
            return deleted;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.ERROR, $"Failed to clear previous sessions: {ex.Message}");
            throw;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                // Force final WAL checkpoint to ensure all data is written to main DB file before closing
                if (_connection != null && _connection.State == System.Data.ConnectionState.Open)
                {
                    _logger.Log(LogLevel.INFO, "Performing final WAL checkpoint before closing database...");
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "PRAGMA wal_checkpoint(RESTART);"; // RESTART mode forces a full checkpoint
                    cmd.ExecuteNonQuery();
                    _logger.Log(LogLevel.INFO, "Final WAL checkpoint completed - all data written to main database file");
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.WARN, $"Final WAL checkpoint failed: {ex.Message}");
            }

            _connection?.Close();
            _connection?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Represents a single activity record from the database
/// </summary>
public class ActivityRecord
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string SourceFolder { get; set; } = string.Empty;
    public string DestinationFolder { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public int AttemptCount { get; set; }

    /// <summary>
    /// Formats the activity record for display matching AboutWindow parser format
    /// Format: "YYYY-MM-DD HH:mm:ss - filename size indicator [ruleName]"
    /// </summary>
    public string ToDisplayString(bool includeSession = true)
    {
        var timeStr = Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        var sizeStr = FormatFileSize(FileSizeBytes);

        // Determine status indicator based on status
        string indicator;
        if (Status == "Success")
        {
            indicator = "✓";
        }
        else if (Status == "InProgress")
        {
            indicator = "⠋"; // Use first braille spinner frame
        }
        else if (Status == "Blacklisted")
        {
            indicator = "⚠";
        }
        else // Failed or other
        {
            indicator = "✗";
        }

        // Match format expected by AboutWindow parser:
        // "YYYY-MM-DD HH:mm:ss - filename size indicator [ruleName]"
        return $"{timeStr} - {FileName} {sizeStr} {indicator} [{RuleName}] {{{DestinationFolder}}}|{FileSizeBytes}^{SourceFolder}";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes}B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024}KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024)}MB";
        return $"{bytes / (1024 * 1024 * 1024)}GB";
    }
}

/// <summary>
/// Database statistics information
/// </summary>
public class DatabaseStatistics
{
    public string DatabasePath { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsConnected { get; set; }
    public int TotalRecords { get; set; }
    public DateTime? LastTransferTimestamp { get; set; }
    public string? ErrorMessage { get; set; }
}
