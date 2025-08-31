using System;
using System.Threading.Tasks;

namespace CST.Avalonia.Services
{
    public interface IXmlUpdateService : IDisposable
    {
        /// <summary>
        /// Checks for updates to the XML data files from the configured GitHub repository.
        /// If this is the first run and no data exists, prompts user to download or locate existing data.
        /// </summary>
        Task CheckForUpdatesAsync();
        
        /// <summary>
        /// Event raised when the update status changes, providing status messages to subscribers.
        /// </summary>
        event Action<string> UpdateStatusChanged;
        
        /// <summary>
        /// Event raised to report progress during download operations.
        /// </summary>
        event Action<int, int> DownloadProgressChanged; // (current, total)
        
        /// <summary>
        /// Gets whether an update check is currently in progress.
        /// </summary>
        bool IsCheckingForUpdates { get; }
        
        /// <summary>
        /// Gets whether a download operation is currently in progress.
        /// </summary>
        bool IsDownloading { get; }
    }
}