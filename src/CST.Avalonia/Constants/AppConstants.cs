using System;
using System.IO;

namespace CST.Avalonia.Constants;

/// <summary>
/// Central location for application-wide constants
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// The application name used for user data directories, registry keys, etc.
    /// Change this in one place to update throughout the application.
    /// </summary>
    public const string AppDataDirectoryName = "CSTReader";

    /// <summary>
    /// The per-user data directory (<c>&lt;ApplicationData&gt;/CSTReader</c>). Single source of truth so the
    /// single-instance guard, the <c>--mcp-bridge</c> handshake, and the local-API server can never target
    /// diverging directories (a future configurable data dir changes only this). (#317 A6-9)
    /// </summary>
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDataDirectoryName);

    /// <summary>
    /// The application name for display purposes
    /// </summary>
    public const string AppDisplayName = "CST Reader";

    /// <summary>
    /// User agent string for HTTP requests
    /// </summary>
    public const string UserAgent = "CSTReader";

    /// <summary>
    /// Registry base path for Windows
    /// </summary>
    public const string RegistryBasePath = @"SOFTWARE\CSTReader";
}