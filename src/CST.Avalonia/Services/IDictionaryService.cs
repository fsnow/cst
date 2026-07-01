using System.Collections.Generic;
using System.Threading.Tasks;
using CST.Avalonia.Models;

namespace CST.Avalonia.Services;

/// <summary>
/// Pāli dictionary lookup. Loads the flat word/definition files under the app-support
/// <c>dictionaries/&lt;lang&gt;/</c> tree, normalizes headwords to IPE, and answers lookups with
/// CST4's exact / prefix / nearest-neighbor matching. UI-free; a view model drives it.
/// </summary>
public interface IDictionaryService
{
    /// <summary>
    /// Two-letter language codes that currently have dictionary data on disk (e.g. <c>"en"</c>,
    /// <c>"hi"</c>). Empty if the dictionaries directory is missing.
    /// </summary>
    IReadOnlyList<string> AvailableLanguages { get; }

    /// <summary>
    /// Look up <paramref name="query"/> (typed in any supported script) in the given language. The
    /// query is IPE-normalized internally, so it matches regardless of input script. The language's
    /// data is loaded and cached on first use. Returns matching entries in display order (see
    /// <see cref="DictionaryIndex.Lookup"/>); empty for an unknown language, empty query, or no match.
    /// </summary>
    Task<IReadOnlyList<DictionaryWord>> LookupAsync(string language, string query);
}
