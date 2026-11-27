using System.Threading.Tasks;

namespace CST.Avalonia.Services
{
    /// <summary>A source PDF on disk, and its version (null if it could not be hashed).</summary>
    public sealed record PdfFile(string LocalPath, string? QuickXorHash);

    /// <summary>
    /// Service for downloading PDF files from SharePoint using Microsoft Graph API.
    /// </summary>
    public interface ISharePointService
    {
        /// <summary>
        /// Downloads a PDF file from SharePoint to the local CSTReader directory.
        /// </summary>
        /// <param name="sharePointPath">The path to the file within SharePoint (e.g., "_Source/01 - Burmese-CST/1957 edition/file.pdf")</param>
        /// <returns>The local file path where the PDF was saved, or null if download failed</returns>
        Task<string?> DownloadPdfAsync(string sharePointPath);

        /// <summary>
        /// The local copy of a PDF, refreshed when SharePoint's copy differs from it, with the version it is:
        /// its SharePoint <c>quickXorHash</c>, by which <c>Sources.Source.ForVersion</c> picks the page mapping
        /// that fits it. Null if there is no local copy and none can be downloaded. (#1136)
        /// </summary>
        Task<PdfFile?> GetPdfAsync(string sharePointPath);

        /// <summary>
        /// Gets the local path where a PDF would be stored, without downloading.
        /// </summary>
        /// <param name="sharePointPath">The path to the file within SharePoint</param>
        /// <returns>The expected local file path</returns>
        string GetLocalPdfPath(string sharePointPath);

        /// <summary>
        /// Checks if a PDF already exists locally.
        /// </summary>
        /// <param name="sharePointPath">The path to the file within SharePoint</param>
        /// <returns>True if the file exists locally</returns>
        bool PdfExistsLocally(string sharePointPath);

        /// <summary>
        /// Tests the connection to SharePoint using the configured credentials.
        /// </summary>
        /// <returns>True if authentication succeeds, false otherwise</returns>
        Task<(bool Success, string Message)> TestConnectionAsync();

        /// <summary>
        /// Lists files in a SharePoint folder.
        /// </summary>
        /// <param name="folderPath">The folder path within SharePoint</param>
        /// <returns>List of file names in the folder</returns>
        Task<string[]> ListFilesAsync(string folderPath);
    }
}
