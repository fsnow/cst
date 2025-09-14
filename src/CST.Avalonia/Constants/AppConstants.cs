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