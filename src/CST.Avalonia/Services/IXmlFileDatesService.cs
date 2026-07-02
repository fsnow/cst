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

        /// <summary>
        /// Commit the timestamps detected by the most recent <see cref="GetChangedBooksAsync"/> for the
        /// given books into the persisted cache, marking them as successfully indexed. Called only after
        /// indexing succeeds so a failed index never marks a book up-to-date (SRCH-1).
        /// </summary>
        void MarkBooksIndexed(IEnumerable<int> bookIndexes);

        /// <summary>
        /// Clear the in-memory file-date cache so every book is reported changed on the next
        /// <see cref="GetChangedBooksAsync"/> — used to force a full re-index after the search index is
        /// found corrupt and deleted (SRCH-11). The persisted file is overwritten on the next save.
        /// </summary>
        void ResetFileDates();

        Task InitializeAsync();
        
        // Enhanced format methods for XmlUpdateService
        Task<FileDatesWithCommits?> GetFileDatesDataAsync();
        Task SaveFileDatesDataAsync(Dictionary<string, FileCommitInfo> files, string? repositoryCommitHash);
    }
}