using System;
using System.IO;
using System.Threading;

namespace CST.Avalonia.Services;

/// <summary>
/// Simple logging service that writes to both console (stdout) and file simultaneously
/// Provides structured logging with timestamps and log levels
/// Thread-safe for concurrent access from multiple tabs/components
/// </summary>
public class LoggingService
{
    private static readonly Lazy<LoggingService> _instance = new(() => new LoggingService());
    public static LoggingService Instance => _instance.Value;
    
    private readonly string _logFilePath;
    private readonly object _writeLock = new object();
    
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
    
    private LoggingService()
    {
        // Always use project root logs directory, not build output directory
        // Find the project root by looking for CST.Avalonia.csproj
        var currentDir = Directory.GetCurrentDirectory();
        var projectRoot = FindProjectRoot(currentDir);
        var logsDir = Path.Combine(projectRoot, "logs");
        Directory.CreateDirectory(logsDir);
        
        // Use date-based log file naming
        var logFileName = $"cst-avalonia-{DateTime.Now:yyyyMMdd}.log";
        _logFilePath = Path.Combine(logsDir, logFileName);
        
        LogInfo("CST.Avalonia", "Logging service initialized", $"LogFile={_logFilePath}");
    }
    
    private static string FindProjectRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CST.Avalonia.csproj")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        
        // Fallback to current directory if project file not found
        return startPath;
    }
    
    public void LogDebug(string component, string message, string? details = null)
    {
        Log(LogLevel.Debug, component, message, details);
    }
    
    public void LogInfo(string component, string message, string? details = null)
    {
        Log(LogLevel.Info, component, message, details);
    }
    
    public void LogWarning(string component, string message, string? details = null)
    {
        Log(LogLevel.Warning, component, message, details);
    }
    
    public void LogError(string component, string message, string? details = null)
    {
        Log(LogLevel.Error, component, message, details);
    }
    
    private void Log(LogLevel level, string component, string message, string? details = null)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var levelStr = level.ToString().ToUpper().PadRight(7);
        var componentStr = component.PadRight(20);
        
        var logEntry = details != null 
            ? $"{timestamp} [{levelStr}] {componentStr} {message} | {details}"
            : $"{timestamp} [{levelStr}] {componentStr} {message}";
        
        lock (_writeLock)
        {
            try
            {
                // Write to console (stdout) - visible to both user and Claude
                Console.WriteLine(logEntry);
                
                // Write to file for persistence
                File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // Fallback to console only if file writing fails
                Console.WriteLine($"{timestamp} [ERROR  ] LoggingService      Failed to write to log file: {ex.Message}");
                Console.WriteLine(logEntry);
            }
        }
    }
    
    // Convenience methods for tab-specific logging
    public void LogTab(string tabId, LogLevel level, string message, string? details = null)
    {
        Log(level, $"Tab[{tabId[^8..]}]", message, details); // Use last 8 chars of tab ID
    }
    
    public void LogTabDebug(string tabId, string message, string? details = null)
    {
        LogTab(tabId, LogLevel.Debug, message, details);
    }
    
    public void LogTabInfo(string tabId, string message, string? details = null)
    {
        LogTab(tabId, LogLevel.Info, message, details);
    }
    
    public void LogTabWarning(string tabId, string message, string? details = null)
    {
        LogTab(tabId, LogLevel.Warning, message, details);
    }
    
    public void LogTabError(string tabId, string message, string? details = null)
    {
        LogTab(tabId, LogLevel.Error, message, details);
    }
}