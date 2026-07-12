using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Conversion;
using CST.Tools;

namespace CST.Avalonia.Services.Tools
{
    /// <summary>
    /// <see cref="IDictionaryTool"/> over the app's <see cref="IDictionaryService"/> (AI_INTEGRATION.md
    /// surface C / §9). The service owns the on-disk format; this maps entries to the tool DTO and projects
    /// the IPE headword to the requested output script. The meaning is passed through as its source HTML.
    /// </summary>
    public sealed class DictionaryTool : IDictionaryTool
    {
        private readonly IDictionaryService _dictionary;

        public DictionaryTool(IDictionaryService dictionary) => _dictionary = dictionary;

        // Upper bound on dictionary hits returned in one call — negative asks 0, huge asks are capped. (#305)
        private const int MaxDictEntries = 500;

        public IReadOnlyList<string> Languages => _dictionary.AvailableLanguages;

        public async Task<IReadOnlyList<DictionaryEntry>> LookupAsync(
            DictionaryRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var words = await _dictionary.LookupAsync(request.Language, request.Query ?? string.Empty, ct)
                .ConfigureAwait(false);

            return words
                .Take(Math.Clamp(request.MaxEntries, 0, MaxDictEntries))
                .Select(w => new DictionaryEntry(
                    Headword: ScriptConverter.Convert(w.Word, Script.Ipe, request.OutputScript),
                    MeaningHtml: w.Meaning))
                .ToList();
        }
    }
}
