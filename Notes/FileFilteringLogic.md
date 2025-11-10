# File Filtering Logic

**Implementation Date:** September 21, 2025
**Updated:** October 2025
**Feature:** Zero-byte and locked file filtering

## 🎯 Overview

The RJAutoMover FileProcessorService intelligently filters files during scanning to avoid processing files that shouldn't be moved:

- **Zero-byte files**: Completely ignored during scans
- **Locked files**: Skipped during scans (files in use by other processes)
- **Dynamic behavior**: Files become eligible when they gain content or become unlocked

## 🔧 Implementation Details

### **File Discovery Enhancement**

**Previous Logic**:
```csharp
var files = Directory.GetFiles(rule.SourceFolder)
    .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
    .ToList();
```

**Enhanced Logic**:
```csharp
var files = Directory.GetFiles(rule.SourceFolder)
    .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
    .Where(f => IsFileProcessable(f))  // NEW: Filter zero-byte and locked files
    .ToList();
```

### **File Filtering Method**

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

### **Inaccessible Files**
- **Condition**: Any other exception during file access
- **Action**: Skip with error logging
- **Use Cases**: Permission denied, corrupted files, network issues

## 🔄 Dynamic Processing

### **Scenario 1: Zero-Byte File Gains Content**
```
1. Scan 1: file.txt (0 bytes) → SKIPPED
2. External process writes to file.txt (1024 bytes)
3. Scan 2: file.txt (1024 bytes) → PROCESSED
```

### **Scenario 2: Locked File Becomes Available**
```
1. Scan 1: report.pdf (locked by Excel) → SKIPPED
2. User closes Excel
3. Scan 2: report.pdf (unlocked) → PROCESSED
```

### **Scenario 3: Mixed File States**
```
Input Folder Contents:
- empty.txt (0 bytes) → SKIPPED
- document.pdf (locked) → SKIPPED
- readme.txt (available) → PROCESSED
- data.log (available) → PROCESSED

Result: Only readme.txt and data.log are processed
```

## 📊 Performance Benefits

1. **Reduced Processing**: No time wasted on files that can't be moved
2. **Cleaner Logs**: Fewer error messages from failed file operations
3. **Better Error Handling**: Distinguishes between temporary and permanent issues
4. **Resource Efficiency**: No unnecessary file operations attempted

## 🚨 Error Prevention

### **Problems Avoided**
- **IOException**: Attempting to move locked files
- **Empty File Operations**: Processing zero-byte files unnecessarily
- **Retry Loops**: Failing repeatedly on permanently inaccessible files
- **Log Spam**: Excessive error messages from problematic files

### **Logged at DEBUG Level**
```
2025-09-21 22:05:15.123 | [DEBUG] | Skipping zero-byte file: empty.txt
2025-09-21 22:05:15.124 | [DEBUG] | Skipping locked file: document.pdf
2025-09-21 22:05:15.125 | [DEBUG] | Skipping file due to access error: corrupted.txt - Access denied
```

## 🎯 Use Cases

### **Common Scenarios**
1. **Download Folders**: Files being downloaded (0 bytes initially, then growing)
2. **Office Documents**: Excel/Word files open for editing
3. **Log Files**: Currently being written by applications
4. **Media Files**: Videos/audio files being encoded or streamed
5. **Database Files**: Database files locked by running applications

### **Benefits for Large Deployments**
- **Reduces support tickets** for "failed to process" errors
- **Improves processing efficiency** by focusing on ready files
- **Provides clear diagnostic information** for troubleshooting
- **Prevents unnecessary retry attempts** on permanently locked files

This intelligent filtering ensures the service only processes files that are actually ready for transfer, making the entire system more reliable and efficient.