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
    /// later user imports). Present-iff the lexicon file exists; the file is opened lazily on first use (a large
    /// lexicon is only loaded when actually queried, and a not-installed source costs nothing). Identity and
    /// attribution come from the lexicon's own <c>meta</c> — never hard-coded. (#466)
    /// </summary>
    public sealed class SqliteDictionarySource : IDictionarySource
    {
        private const int MaxDictEntries = 500;
        private readonly ILogger _logger = Log.ForContext<SqliteDictionarySource>();

        private readonly string _path;
        private readonly string _id;
        private readonly Lazy<LexiconReader?> _reader;

        /// <param name="path">The lexicon .db path.</param>
        /// <param name="sourceId">The source id, known ahead of opening the file so the registry can list it and
        /// route to it without loading the (possibly large) lexicon.</param>
        public SqliteDictionarySource(string path, string sourceId)
        {
            _path = path;
            _id = sourceId;
            _reader = new Lazy<LexiconReader?>(OpenReader, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public string Id => _id;
        public string DisplayName => _reader.Value?.Meta.DisplayName is { Length: > 0 } d ? d : _id;
        public string DefinitionLanguage => _reader.Value?.Meta.DefinitionLanguage ?? "en";
        public DictionarySourceKind Kind =>
            _reader.Value?.Meta.Kind == LexiconKind.ProperNames
                ? DictionarySourceKind.ProperNames : DictionarySourceKind.General;

        // Cheap: the file's presence is availability; the reader is only loaded on lookup. A file that exists but
        // is unreadable/corrupt is reported unavailable (the lazy open returned null).
        public bool IsAvailable => File.Exists(_path) && (!_reader.IsValueCreated || _reader.Value is not null);

        public DictionarySourceInfo? Attribution
        {
            get
            {
                var m = _reader.Value?.Meta;
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
            var reader = _reader.Value;
            if (reader is null || string.IsNullOrWhiteSpace(request.Query))
                return Array.Empty<DictionaryEntry>();

            string? source = Attribution?.Title;
            int cap = Math.Clamp(request.MaxEntries, 0, MaxDictEntries);
            // The lexicon reader derives the key from the query and returns exact + prefix / nearest hits.
            return reader.Lookup(request.Query, cap)
                .Select(e => new DictionaryEntry(
                    // The stored headword is the published form (Latin, e.g. "Nāgita 1"); project to the output
                    // script. The definition body is source HTML and passes through.
                    Headword: ToScript(e.Headword, request.OutputScript),
                    MeaningHtml: e.BodyHtml,
                    Source: source))
                .ToList();
        }

        private LexiconReader? OpenReader()
        {
            if (!File.Exists(_path)) return null;
            try { return LexiconReader.Open(_path); }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not open lexicon {Path}; source {Id} is unavailable.", _path, _id);
                return null;
            }
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
