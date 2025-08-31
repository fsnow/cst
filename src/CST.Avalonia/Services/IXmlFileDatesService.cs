using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CST.Avalonia.Services
{
    public interface IXmlFileDatesService
    {
        Task<List<int>> GetChangedBooksAsync();
        Task SaveFileDatesAsync();
        void UpdateFileDate(int bookIndex, DateTime lastWriteTime);
        Task InitializeAsync();
        
        // Enhanced format methods for XmlUpdateService
        Task<FileDatesWithCommits?> GetFileDatesDataAsync();
        Task SaveFileDatesDataAsync(Dictionary<string, FileCommitInfo> files, string? repositoryCommitHash);
    }
}