using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CST.Lucene;
using Lucene.Net.Index;

namespace CST.Avalonia.Services
{
    public interface IIndexingService
    {
        Task<bool> IsIndexValidAsync();
        Task BuildIndexAsync(IProgress<IndexingProgress> progress);
        Task UpdateIndexAsync(List<int> changedBooks, IProgress<IndexingProgress> progress);
        Task OptimizeIndexAsync();
        string IndexDirectory { get; }
        Task InitializeAsync();

        /// <summary>
        /// Returns a reference-counted, up-to-date <see cref="DirectoryReader"/> for highlighting, or null
        /// if none is available. The caller MUST release it with <c>DecRef()</c> in a finally. The reader is
        /// refreshed when the index changes, so it never serves stale term vectors after a re-index (SRCH-6).
        /// </summary>
        DirectoryReader? GetIndexReader();
    }
}