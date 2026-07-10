using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CST.Tools;
using ModelContextProtocol.Server;

namespace CST.Avalonia.Services.LocalApi.Mcp
{
    /// <summary>
    /// MCP dictionary tools over <see cref="IDictionaryTool"/> — <c>dictionary_lookup</c> (headword definition)
    /// and <c>dictionary_languages</c> (available dictionaries). Format-agnostic: the underlying service owns
    /// the dictionary format; headwords are returned in the requested output script. (#191)
    /// </summary>
    [McpServerToolType]
    internal sealed class DictionaryMcpTool
    {
        [McpServerTool(Name = "dictionary_lookup")]
        [Description("Look up a Pali headword in a dictionary: returns the exact match plus the surrounding "
            + "prefix run, or — on a near miss — the nearest headwords that share a leading prefix with the "
            + "query (each a headword + definition HTML). A query that shares no leading prefix with any "
            + "headword returns an empty list. Query may be in any script; headwords come back in the requested "
            + "output script.")]
        public static async Task<IReadOnlyList<DictionaryEntry>> LookupAsync(
            IDictionaryTool dictionary,
            [Description("The headword to look up, in any script.")]
            string word,
            [Description("Dictionary language code (see dictionary_languages), e.g. 'en'.")]
            string language = "en",
            [Description("Script for returned headwords.")]
            OutputScript outputScript = OutputScript.Latin,
            [Description("Maximum entries to return.")]
            int maxEntries = 25,
            CancellationToken ct = default)
        {
            var request = new DictionaryRequest(
                Language: language ?? string.Empty,
                Query: word ?? string.Empty,
                OutputScript: McpScript.ToScript(outputScript),
                MaxEntries: maxEntries);
            return await dictionary.LookupAsync(request, ct).ConfigureAwait(false);
        }

        [McpServerTool(Name = "dictionary_languages")]
        [Description("List the available dictionary language codes (e.g. 'en', 'hi') for dictionary_lookup.")]
        public static IReadOnlyList<string> Languages(IDictionaryTool dictionary) => dictionary.Languages;
    }
}
