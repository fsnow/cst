using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Conversion;
using CST.Tools;

namespace CST.Avalonia.Services.Dictionaries
{
    /// <summary>
    /// A dictionary source over one flat-file language of the app's <see cref="IDictionaryService"/> (the two
    /// bundled VRI dictionaries, en/hi). Definition text is the source HTML; the IPE headword is projected to
    /// the requested output script. Wraps the existing loader unchanged. (#466)
    /// </summary>
    public sealed class FlatFileDictionarySource : IDictionarySource
    {
        private const int MaxDictEntries = 500;   // mirror the tool contract's clamp (#305)

        private readonly IDictionaryService _dictionary;

        public FlatFileDictionarySource(IDictionaryService dictionary, string language)
        {
            _dictionary = dictionary;
            Id = language;
        }

        public string Id { get; }
        // The picker label: the source.json displayName if set, else the citation Title, else the language code.
        public string DisplayName =>
            !string.IsNullOrWhiteSpace(Attribution?.DisplayName) ? Attribution!.DisplayName!
            : !string.IsNullOrWhiteSpace(Attribution?.Title) ? Attribution!.Title!
            : Id;
        public string DefinitionLanguage => Id;
        public DictionarySourceKind Kind => DictionarySourceKind.General;
        public bool IsAvailable => _dictionary.AvailableLanguages.Contains(Id, StringComparer.OrdinalIgnoreCase);
        public DictionarySourceInfo? Attribution => _dictionary.SourceFor(Id);

        public async Task<IReadOnlyList<DictionaryEntry>> LookupAsync(DictionaryRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var words = await _dictionary.LookupAsync(Id, request.Query ?? string.Empty, ct).ConfigureAwait(false);
            // Inline attribution is the citation Title, not the picker DisplayName; blank (e.g. hi) → none.
            var source = string.IsNullOrWhiteSpace(Attribution?.Title) ? null : Attribution!.Title;
            return words
                .Take(Math.Clamp(request.MaxEntries, 0, MaxDictEntries))
                .Select(w => new DictionaryEntry(
                    Headword: ScriptConverter.Convert(w.Word, Script.Ipe, request.OutputScript),
                    MeaningHtml: w.Meaning,
                    Source: source))
                .ToList();
        }
    }
}
