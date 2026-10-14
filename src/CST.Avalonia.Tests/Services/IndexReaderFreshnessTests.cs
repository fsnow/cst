using System;
using System.IO;
using CST.Avalonia.Services;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// A reader held across a rebuild from scratch is seen as stale. (#1052)
///
/// <para>[fsnow], testing the IPE v1 → v2 migration: the index was rebuilt, yet searches gave "no results" until the
/// app restarted - the search service's reader still held the deleted index.</para>
/// </summary>
public class IndexReaderFreshnessTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "cst-freshness-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_path, recursive: true); } catch { }
    }

    // The same build procedure each time, as the app's rebuild repeats its first build: IndexingService's
    // DeleteCorruptIndex deletes the index FILES (the directory stays), then BookIndexer opens a writer in
    // CREATE_OR_APPEND mode and commits once.
    private void BuildFromScratch(string word)
    {
        if (System.IO.Directory.Exists(_path))
            foreach (var file in System.IO.Directory.GetFiles(_path)) File.Delete(file);
        using var dir = FSDirectory.Open(_path);
        var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, new WhitespaceAnalyzer(LuceneVersion.LUCENE_48))
        {
            OpenMode = OpenMode.CREATE_OR_APPEND
        };
        using var writer = new IndexWriter(dir, config);
        for (var i = 0; i < 3; i++)
            writer.AddDocument(new Document { new TextField("text", word + i, Field.Store.NO) });
        writer.Commit();
    }

    [Fact]
    public void A_reader_on_an_index_that_was_deleted_and_rebuilt_is_stale()
    {
        BuildFromScratch("old");
        using var dir = FSDirectory.Open(_path);
        using var reader = DirectoryReader.Open(dir);
        var stamp = IndexReaderFreshness.CommitStamp(reader, dir);
        Assert.False(IndexReaderFreshness.IsStale(reader, dir, stamp));

        BuildFromScratch("new");

        // The premise, measured: Lucene's own numbers cannot see this rebuild.
        Assert.True(reader.IsCurrent());
        Assert.Equal(SegmentInfos.GetLastCommitGeneration(dir), reader.IndexCommit.Generation);

        Assert.True(IndexReaderFreshness.IsStale(reader, dir, stamp),
            "A reader on the deleted index was not seen as stale, so searches would keep reading it.");
    }

    [Fact]
    public void A_reader_is_stale_after_an_ordinary_commit_too()
    {
        BuildFromScratch("a");
        using var dir = FSDirectory.Open(_path);
        using var reader = DirectoryReader.Open(dir);
        var stamp = IndexReaderFreshness.CommitStamp(reader, dir);

        using (var writer = new IndexWriter(dir, new IndexWriterConfig(LuceneVersion.LUCENE_48,
                   new WhitespaceAnalyzer(LuceneVersion.LUCENE_48)) { OpenMode = OpenMode.APPEND }))
        {
            writer.AddDocument(new Document { new TextField("text", "more", Field.Store.NO) });
            writer.Commit();
        }

        Assert.True(IndexReaderFreshness.IsStale(reader, dir, stamp));
    }
}
