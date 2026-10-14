using System;
using System.IO;
using Lucene.Net.Index;
using Lucene.Net.Store;

namespace CST.Avalonia.Services;

/// <summary>
/// Whether an open index reader still reflects the index on disk. (#1052)
///
/// <para>[observed] <c>DirectoryReader.IsCurrent()</c> alone missed a rebuild from scratch: the IPE-version rebuild
/// (#1049) deleted the index and built a new one while the search service held a reader on the old one, and every
/// search kept reading the deleted index - on macOS its files stay readable through the open handles - until the
/// app restarted. Rebuilding the same books by the same procedure reproduces both numbers Lucene could compare - the
/// commit version and the commit generation (segments_N) - so neither can tell the new index from the old
/// (measured, IndexReaderFreshnessTests). What does change is the commit FILE: the old segments_N was deleted and a
/// new one written. So the reader is paired with that file's write time when it is opened.</para>
/// </summary>
public static class IndexReaderFreshness
{
    /// <summary>The identity of the commit a reader was opened on: its segments file's last write time.</summary>
    public static DateTime? CommitStamp(DirectoryReader reader, global::Lucene.Net.Store.Directory directory)
    {
        if (directory is not FSDirectory fs) return null;
        var file = new FileInfo(Path.Combine(fs.Directory.FullName, reader.IndexCommit.SegmentsFileName));
        return file.Exists ? file.LastWriteTimeUtc : null;
    }

    public static bool IsStale(DirectoryReader reader, global::Lucene.Net.Store.Directory directory, DateTime? openedStamp)
    {
        if (!reader.IsCurrent()) return true;
        if (reader.IndexCommit.Generation != SegmentInfos.GetLastCommitGeneration(directory)) return true;
        return CommitStamp(reader, directory) != openedStamp;
    }
}
