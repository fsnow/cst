using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Conversion;
using CST.Lexicon;
using CST.Tools;
using Serilog;

namespace CST.Avalonia.Services.Dictionaries
{
    /// <summary>
    /// A dictionary source backed by a downloaded/imported <see cref="CST.Lexicon"/> asset (DPPN first, and
    /// later user imports).
    ///
    /// <para>Two-level laziness, keyed on the file's (length, mtime): the cheap <c>meta</c> table is loaded to
    /// answer identity/attribution and availability; the full (possibly large) entry set is loaded only on the
    /// first real lookup. Neither a failed nor a successful load is memoized forever — if the file changes on
    /// disk (the delivery layer replacing it, a mid-write completing), the next access reloads. So a
    /// just-installed asset flips available and a transient partial file self-heals, as the contract promises.</para>
    /// </summary>
    public sealed class SqliteDictionarySource : IDictionarySource
    {
        private const int MaxDictEntries = 500;
        private readonly ILogger _logger = Log.ForContext<SqliteDictionarySource>();

        private readonly string _path;
        private readonly string _id;

        private readonly object _gate = new();
        private (long Length, long MtimeTicks)? _stamp;   // the file state the caches below reflect
        private LexiconMeta? _meta;
        private LexiconReader? _reader;
        private bool _metaTried, _readerTried;

        /// <param name="path">The lexicon .db path.</param>
        /// <param name="sourceId">The source id, known ahead of opening the file so the registry can list it and
        /// route to it without loading the lexicon.</param>
        public SqliteDictionarySource(string path, string sourceId)
        {
            _path = path;
            _id = sourceId;
        }

        public string Id => _id;
        public string DisplayName => Meta()?.DisplayName is { Length: > 0 } d ? d : _id;
        public string DefinitionLanguage => Meta()?.DefinitionLanguage ?? "en";
        public DictionarySourceKind Kind =>
            Meta()?.Kind == LexiconKind.ProperNames ? DictionarySourceKind.ProperNames : DictionarySourceKind.General;

        // Available iff a valid lexicon is present (its meta reads) — a cheap meta-only open, retried on change.
        public bool IsAvailable => Meta() is not null;

        public DictionarySourceInfo? Attribution
        {
            get
            {
                var m = Meta();
                if (m is null) return null;
                var info = new DictionarySourceInfo(
                    Title: NullIfBlank(m.Title),
                    Compiler: NullIfBlank(m.Author),               // the source's author (e.g. Malalasekera)
                    Edition: NullIfBlank(m.Reviser),               // reviser, where one applies (Ānandajoti)
                    Year: NullIfBlank(m.Year),
                    Publisher: NullIfBlank(m.Publisher),
                    License: NullIfBlank(m.License),               // may be null — not every source asserts one
                    Url: NullIfBlank(m.Url));
                return IsUnattributed(info) ? null : info;
            }
        }

        public Task<IReadOnlyList<DictionaryEntry>> LookupAsync(DictionaryRequest request, CancellationToken ct = default)
            // Loading + lookup is synchronous SQLite/CPU work; offload off the request thread. (#279)
            => Task.Run(() => Lookup(request, ct), ct);

        private IReadOnlyList<DictionaryEntry> Lookup(DictionaryRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            int cap = Math.Clamp(request.MaxEntries, 0, MaxDictEntries);
            // Contract (#305): a non-positive request asks for zero. Guard BEFORE the lexicon reader, whose
            // own max=0 means "unbounded" — the opposite. (fable MED-1)
            if (cap == 0 || string.IsNullOrWhiteSpace(request.Query)) return Array.Empty<DictionaryEntry>();

            var reader = Reader();
            if (reader is null) return Array.Empty<DictionaryEntry>();

            string? source = Attribution?.Title;
            return reader.Lookup(request.Query, cap)
                .Select(e => new DictionaryEntry(
                    // The stored headword is the published form (Latin, e.g. "Nāgita 1"); project to the output
                    // script. The definition body is source HTML and passes through.
                    Headword: ToScript(e.Headword, request.OutputScript),
                    MeaningHtml: e.BodyHtml,
                    Source: source))
                .ToList();
        }

        // Current file stamp, or null if the file is absent/unreadable.
        private (long, long)? Stamp()
        {
            try
            {
                var fi = new FileInfo(_path);
                return fi.Exists ? (fi.Length, fi.LastWriteTimeUtc.Ticks) : null;
            }
            catch { return null; }
        }

        private LexiconMeta? Meta()
        {
            lock (_gate)
            {
                if (!SyncStamp()) return null;               // file gone → nothing available
                if (!_metaTried)
                {
                    _metaTried = true;
                    try { _meta = LexiconReader.OpenMeta(_path); }
                    catch (Exception ex) { _logger.Warning(ex, "Could not read lexicon meta {Path}", _path); _meta = null; }
                }
                return _meta;
            }
        }

        private LexiconReader? Reader()
        {
            lock (_gate)
            {
                if (!SyncStamp()) return null;
                if (!_readerTried)
                {
                    _readerTried = true;
                    try { _reader = LexiconReader.Open(_path); }
                    catch (Exception ex) { _logger.Warning(ex, "Could not open lexicon {Path}", _path); _reader = null; }
                }
                return _reader;
            }
        }

        // Reset the caches when the file changes since they were populated, so a replaced/completed file is
        // re-read rather than a stale (or failed) result being served forever. Returns false when the file is
        // absent. Caller holds _gate.
        private bool SyncStamp()
        {
            var s = Stamp();
            if (s is null) { _stamp = null; _meta = null; _reader = null; _metaTried = _readerTried = false; return false; }
            if (_stamp != s)
            {
                _stamp = s;
                _meta = null; _reader = null; _metaTried = _readerTried = false;
            }
            return true;
        }

        // A headword is stored in Latin; only its letters convert. Latin/IPE/Unknown output return it as-is.
        private static string ToScript(string latin, Script output)
            => output is Script.Latin or Script.Ipe or Script.Unknown
                ? latin
                : ScriptConverter.Convert(latin, Script.Latin, output);

        private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private static bool IsUnattributed(DictionarySourceInfo s) =>
            s.Title is null && s.Compiler is null && s.Edition is null && s.Year is null &&
            s.Publisher is null && s.License is null && s.Url is null;
    }
}
