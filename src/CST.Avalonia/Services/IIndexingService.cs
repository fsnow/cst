using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CST.Lucene;

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
    }
}