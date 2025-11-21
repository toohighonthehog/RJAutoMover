# File Filtering Logic

**Updated:** November 21, 2025
**Features:** Zero-byte and locked file filtering, Date-based filtering

## 🎯 Overview

The RJAutoMover FileProcessorService intelligently filters files during scanning based on multiple criteria:

- **Zero-byte files**: Completely ignored during scans
- **Locked files**: Skipped during scans (files in use by other processes)
- **Date-based filtering**: Optional filtering by file creation, modification, or access time
- **Dynamic behavior**: Files become eligible when they gain content, become unlocked, or meet date criteria

## 🔧 Implementation Details

### **File Discovery Enhancement**

**Basic Logic**:
```csharp
var files = Directory.GetFiles(rule.SourceFolder)
    .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
    .ToList();
```

**Enhanced Logic with All Filters**:
```csharp
var files = Directory.GetFiles(rule.SourceFolder)
    .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
    .Where(f => IsFileProcessable(f))  // Filter zero-byte and locked files
    .Where(f => MatchesDateFilter(f, rule))  // NEW: Date-based filtering
    .ToList();
```

### **File Processability Check**

```csharp
private bool IsFileProcessable(string filePath)
{
    try
    {
        var fileInfo = new FileInfo(filePath);

        // Skip zero-byte files
        if (fileInfo.Length == 0)
        {
            _logger.Log(LogLevel.DEBUG, $"Skipping zero-byte file: {Path.GetFileName(filePath)}");
            return false;
        }

        // Test if file is locked by trying to open it exclusively
        try
        {
            using var fileStream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.None);
            return true;  // File is not locked
        }
        catch (IOException)
        {
            _logger.Log(LogLevel.DEBUG, $"Skipping locked file: {Path.GetFileName(filePath)}");
            return false;  // File is locked
        }
    }
    catch (Exception ex)
    {
        _logger.Log(LogLevel.DEBUG, $"Skipping file due to access error: {Path.GetFileName(filePath)} - {ex.Message}");
        return false;  // Skip files we can't access
    }
}
```

### **Date Filter Matching (NEW)**

```csharp
private bool MatchesDateFilter(string filePath, FileRule rule)
{
    // No date filter = all files match
    if (string.IsNullOrWhiteSpace(rule.DateFilter))
        return true;

    // Parse DateFilter format: "TYPE:SIGN:MINUTES"
    var parsed = DateFilterHelper.Parse(rule.DateFilter);
    if (!parsed.IsValid)
    {
        _logger.Log(LogLevel.ERROR, $"Invalid DateFilter: {parsed.ErrorMessage}");
        return false;
    }

    var fileInfo = new FileInfo(filePath);
    var now = DateTime.Now;

    // Get appropriate timestamp based on filter type
    DateTime fileTimestamp = parsed.Type switch
    {
        DateFilterHelper.FilterType.LastAccessed => fileInfo.LastAccessTime,
        DateFilterHelper.FilterType.LastModified => fileInfo.LastWriteTime,
        DateFilterHelper.FilterType.FileCreated => fileInfo.CreationTime,
        _ => fileInfo.CreationTime
    };

    // Calculate file age in minutes
    var ageMinutes = (now - fileTimestamp).TotalMinutes;

    // Apply filter based on direction
    bool matches = parsed.Direction == DateFilterHelper.FilterDirection.OlderThan
        ? ageMinutes >= parsed.Minutes  // Positive: older than
        : ageMinutes <= parsed.Minutes; // Negative: within last

    // Log result
    var logLevel = LogLevel.TRACE;
    var directionText = parsed.Direction == DateFilterHelper.FilterDirection.OlderThan
        ? "older than" : "within last";

    if (matches)
    {
        _logger.Log(logLevel, $"File matches DateFilter: {Path.GetFileName(filePath)} " +
            $"({parsed.Type} is {ageMinutes:F1} min ago, requires {directionText} {parsed.Minutes} min)");
    }
    else
    {
        _logger.Log(logLevel, $"File does NOT match DateFilter: {Path.GetFileName(filePath)} " +
            $"({parsed.Type} is {ageMinutes:F1} min ago, requires {directionText} {parsed.Minutes} min)");
    }

    return matches;
}
```

## 📋 Filtering Behavior

### **Zero-Byte Files**
- **Condition**: `fileInfo.Length == 0`
- **Action**: Skip completely during scan
- **Logging**: DEBUG level message
- **Dynamic**: If file gains content later, it will be processed in next scan

### **Locked Files**
- **Detection**: Attempt exclusive file access (`FileShare.None`)
- **Action**: Skip if `IOException` thrown (file in use)
- **Logging**: DEBUG level message
- **Dynamic**: If file becomes unlocked, it will be processed in next scan

### **Date-Based Filtering (NEW)**

#### DateFilter Format
```
"TYPE:SIGN:MINUTES"
```

**Components:**
- **TYPE**: `LA` (Last Accessed), `LM` (Last Modified), `FC` (File Created)
- **SIGN**: `+` (older than), `-` (within last)
- **MINUTES**: Integer value (1-5256000, up to 10 years)

#### Examples
| DateFilter | Meaning |
|------------|---------|
| `LA:+43200` | Files NOT accessed in last 30 days (older files) |
| `LA:-1440` | Files accessed within last 1 day (recent files) |
| `LM:+10080` | Files NOT modified in last 7 days |
| `FC:+4320` | Files created more than 3 days ago |
| `FC:-60` | Files created within last 60 minutes |
| Empty/null | No date filtering (process all files) |

#### Behavior
- **Positive (+)**: File age MUST be >= threshold (older files)
- **Negative (-)**: File age MUST be <= threshold (recent files)
- **Logging**: TRACE level for each file checked
- **Dynamic**: Files automatically become eligible/ineligible as time passes

### **Inaccessible Files**
- **Condition**: Any other exception during file access
- **Action**: Skip with error logging
- **Use Cases**: Permission denied, corrupted files, network issues

## 🔄 Dynamic Processing Scenarios

### **Scenario 1: Zero-Byte File Gains Content**
```
1. Scan 1: file.txt (0 bytes) → SKIPPED (zero-byte)
2. External process writes to file.txt (1024 bytes)
3. Scan 2: file.txt (1024 bytes) → PROCESSED
```

### **Scenario 2: Locked File Becomes Available**
```
1. Scan 1: report.pdf (locked by Excel) → SKIPPED (locked)
2. User closes Excel
3. Scan 2: report.pdf (unlocked) → PROCESSED
```

### **Scenario 3: File Becomes Old Enough (Date Filter)**
```
Configuration: DateFilter: "FC:+1440" (older than 1 day)

1. Scan 1 (10:00 AM): document.pdf (created yesterday 11:00 AM, 23 hours old) → SKIPPED
2. Scan 2 (11:00 AM): document.pdf (created yesterday 11:00 AM, 24 hours old) → PROCESSED
```

### **Scenario 4: File Ages Out of Date Range**
```
Configuration: DateFilter: "LM:-60" (within last 60 minutes)

1. Scan 1 (10:00 AM): report.txt (modified 10 min ago) → PROCESSED
2. Scan 2 (11:00 AM): report.txt (modified 70 min ago) → SKIPPED
```

### **Scenario 5: Combined Filters**
```
Configuration: DateFilter: "FC:+10080" (older than 7 days)
Input Folder Contents:
- empty.txt (0 bytes) → SKIPPED (zero-byte)
- locked.pdf (locked, 8 days old) → SKIPPED (locked)
- recent.doc (unlocked, 3 days old) → SKIPPED (too recent)
- old.log (unlocked, 10 days old) → PROCESSED ✓

Result: Only old.log is processed (passes all filters)
```

## 📊 Performance Benefits

1. **Reduced Processing**: No time wasted on files that can't or shouldn't be moved
2. **Cleaner Logs**: Fewer error messages from failed file operations
3. **Better Error Handling**: Distinguishes between temporary and permanent issues
4. **Resource Efficiency**: No unnecessary file operations attempted
5. **Intelligent Scheduling**: Date filters prevent premature processing

## 🚨 Error Prevention

### **Problems Avoided**
- **IOException**: Attempting to move locked files
- **Empty File Operations**: Processing zero-byte files unnecessarily
- **Premature Processing**: Moving files before they meet date criteria
- **Retry Loops**: Failing repeatedly on permanently inaccessible files
- **Log Spam**: Excessive error messages from problematic files

### **Logged at Different Levels**

**DEBUG Level (Zero-byte and Locked):**
```
2025-01-21 10:05:15.123 | [DEBUG] | Skipping zero-byte file: empty.txt
2025-01-21 10:05:15.124 | [DEBUG] | Skipping locked file: document.pdf
2025-01-21 10:05:15.125 | [DEBUG] | Skipping file due to access error: corrupted.txt - Access denied
```

**TRACE Level (Date Filtering):**
```
2025-01-21 10:05:15.126 | [TRACE] | File matches DateFilter: old_doc.pdf (FC is 180.5 min ago, requires older than 120 min)
2025-01-21 10:05:15.127 | [TRACE] | File does NOT match DateFilter: recent.txt (FC is 45.2 min ago, requires older than 120 min)
```

## 🎯 Use Cases

### **Common Scenarios**

1. **Download Folders**:
   - Zero-byte files being downloaded (skipped until complete)
   - DateFilter: `FC:-60` to process only recent downloads

2. **Office Documents**:
   - Excel/Word files open for editing (skipped while locked)
   - DateFilter: `LM:+1440` to archive documents not modified in 24 hours

3. **Log Files**:
   - Currently being written by applications (skipped while locked)
   - DateFilter: `FC:+10080` to archive logs older than 7 days

4. **Media Files**:
   - Videos/audio files being encoded (skipped while locked)
   - DateFilter: `LA:+43200` to archive media not accessed in 30 days

5. **Temporary Files**:
   - DateFilter: `FC:+60` to clean up temp files older than 1 hour

6. **Archive Workflow**:
   - DateFilter: `FC:+129600` to archive files older than 90 days
   - Prevents archiving recently created files

### **OTHERS Extension with Date Filter (Required)**

```yaml
FileRules:
  - Name: "Archive Very Old Files"
    SourceFolder: C:\Data\Misc
    Extension: OTHERS  # Catches all file types
    DestinationFolder: C:\Archive\Old
    ScanIntervalMs: 60000
    IsActive: true
    FileExists: skip
    DateFilter: "FC:+259200"  # REQUIRED: Older than 180 days
```

**Behavior:**
- OTHERS rules MUST have a DateFilter (validation enforced)
- Catches files not matched by specific extension rules
- Only processes files meeting date criteria
- Prevents accidental mass-processing of all files

### **Benefits for Large Deployments**
- **Reduces support tickets** for "failed to process" errors
- **Improves processing efficiency** by focusing on ready files
- **Provides clear diagnostic information** for troubleshooting
- **Prevents unnecessary retry attempts** on permanently locked files
- **Intelligent time-based processing** with date filters
- **Prevents premature archiving** of recently created files

## 📖 Related Documentation

- **DateFilter Helper**: [DateFilterHelper.cs](../RJAutoMoverShared/Helpers/DateFilterHelper.cs)
- **Test Plan**: [TEST_PLAN.md](../TestPlans/TEST_PLAN.md)
- **Testing Scripts**: [TestPlans/](../TestPlans/)
- **Processing Logic**: [FileProcessingLogic.md](FileProcessingLogic.md)

---

This intelligent multi-layered filtering ensures the service only processes files that are actually ready and appropriate for transfer, making the entire system more reliable, efficient, and intelligent.
