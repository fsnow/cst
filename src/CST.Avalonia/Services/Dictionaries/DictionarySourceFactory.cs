using System;
using System.Collections.Generic;
using System.Linq;
using CST.Lemma;

namespace CST.Avalonia.Services.Dictionaries
{
    /// <summary>
    /// Builds the app's <see cref="DictionarySourceRegistry"/> from the runtime services. Extracted from the DI
    /// wiring so the composition — especially reserved-id ownership — is directly testable (the DI-order bug this
    /// guards against is invisible to a test that constructs the registry by hand). (#466, fable HIGH-1)
    /// </summary>
    public static class DictionarySourceFactory
    {
        /// <summary>Ids owned by a dedicated source, NOT by a flat-file dictionary of the same name. A flat dir
        /// literally named "dpd"/"dppn" must never shadow the real DPD / lexicon source.</summary>
        private static readonly HashSet<string> ReservedIds =
            new(StringComparer.OrdinalIgnoreCase) { DpdDictionarySource.SourceId, "dppn" };

        public static DictionarySourceRegistry Build(
            IDictionaryService dictionary, ILemmaProvider lemma, string dppnLexiconPath)
        {
            var sources = new List<IDictionarySource>();

            // Flat-file languages first (so en/hi lead the list — the natural default), EXCLUDING any that
            // collide with a reserved id, which a dedicated source owns.
            foreach (var lang in dictionary.AvailableLanguages)
                if (!ReservedIds.Contains(lang))
                    sources.Add(new FlatFileDictionarySource(dictionary, lang));

            // The reserved, present-iff-installed sources.
            sources.Add(new DpdDictionarySource(lemma));
            sources.Add(new SqliteDictionarySource(dppnLexiconPath, "dppn"));

            return new DictionarySourceRegistry(sources);
        }
    }
}
