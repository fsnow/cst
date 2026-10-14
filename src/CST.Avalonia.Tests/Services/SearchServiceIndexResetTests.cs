using System;
using System.IO;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Conversion;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// The search service lets go of the index when a rebuild from scratch is about to delete it. (#1054)
///
/// <para>[observed] Merlin, 2026-10-14: the IPE v1 → v2 rebuild found the search service's reader (opened by a
/// search restored at startup) still holding the old segment files. Windows would not delete them, and the rebuild
/// failed with "Cannot overwrite: _48.fdt", because the new index reuses the old segment names.</para>
/// </summary>
public class SearchServiceIndexResetTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "cst-reset-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IIndexingService> _indexing = new();
    private readonly SearchService _service;

    public SearchServiceIndexResetTests()
    {
        Build("old");
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(new Settings { IndexDirectory = _path });
        var script = new Mock<IScriptService>();
        script.SetupGet(s => s.CurrentScript).Returns(Script.Latin);
        _service = new SearchService(NullLogger<SearchService>.Instance, _indexing.Object, script.Object, settings.Object);
    }

    public void Dispose()
    {
        _service.Dispose();
        try { System.IO.Directory.Delete(_path, recursive: true); } catch { }
    }

    // As the app rebuilds: the directory stays, its files go, a writer in CREATE_OR_APPEND commits once.
    //
    // Non-compound on purpose. [observed] With a compound (.cfs) segment the old files deleted on Windows even
    // with the reader open, so the tests passed with the fix removed. The app's index is non-compound - BookIndexer
    // asks for compound files, but Lucene writes its large merged segments as separate files - and those are what
    // Windows refused to delete. With UseCompoundFile = false all four tests fail when the fix is unhooked.
    private void Build(string word, int docs = 3)
    {
        System.IO.Directory.CreateDirectory(_path);
        foreach (var file in System.IO.Directory.GetFiles(_path)) File.Delete(file);
        using var dir = FSDirectory.Open(_path);
        using var writer = new IndexWriter(dir, new IndexWriterConfig(LuceneVersion.LUCENE_48,
            new WhitespaceAnalyzer(LuceneVersion.LUCENE_48)) { OpenMode = OpenMode.CREATE_OR_APPEND, UseCompoundFile = false });
        for (var i = 0; i < docs; i++)
            writer.AddDocument(new Document { new StringField("text", word + i, Field.Store.YES) });
        writer.Commit();
    }

    // What a search does: acquire a counted reference, use it, release it. The service keeps its owner ref.
    private DirectoryReader SearchOnce()
    {
        var reader = _service.AcquireReader();
        reader.DecRef();
        return reader;
    }

    private void RaiseReset() => _indexing.Raise(i => i.IndexResetting += null, _indexing.Object, EventArgs.Empty);

    [Fact]
    public void A_reset_closes_the_reader_the_service_was_holding()
    {
        var reader = SearchOnce();
        Assert.Equal(1, reader.RefCount);   // the service's owner reference, which held the files open

        RaiseReset();

        Assert.Equal(0, reader.RefCount);
    }

    [Fact]
    public void A_search_in_flight_during_a_reset_keeps_its_reader_until_it_finishes()
    {
        var inFlight = _service.AcquireReader();

        RaiseReset();

        Assert.Equal(1, inFlight.RefCount);
        Assert.Equal(3, inFlight.NumDocs);   // still readable, not closed out from under the search
        inFlight.DecRef();
        Assert.Equal(0, inFlight.RefCount);
    }

    [Fact]
    public void After_a_reset_and_rebuild_the_next_search_reads_the_new_index()
    {
        SearchOnce();
        RaiseReset();
        Build("new", docs: 5);

        var reader = _service.AcquireReader();
        try { Assert.Equal(5, reader.NumDocs); }
        finally { reader.DecRef(); }
    }

    // The defect itself. Only Windows refuses to delete a file a reader holds open; on macOS this would pass
    // with or without the fix, so it proves nothing there.
    [WindowsFact]
    public void After_a_reset_the_rebuild_can_delete_the_old_index_files()
    {
        SearchOnce();

        RaiseReset();

        foreach (var file in System.IO.Directory.GetFiles(_path))
            File.Delete(file);   // threw IOException "used by another process" before #1054
        Assert.Empty(System.IO.Directory.GetFiles(_path));
    }
}
