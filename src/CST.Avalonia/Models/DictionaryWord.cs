namespace CST.Avalonia.Models;

/// <summary>
/// A single dictionary entry: an IPE-normalized headword and its HTML definition.
///
/// <para><see cref="Word"/> is always stored in IPE (see <c>CST.Conversion.Any2Ipe</c>). IPE is a
/// script-independent Pāli encoding whose codepoints sort in Pāli alphabetical order, so ordinal
/// comparison of <see cref="Word"/> yields the correct collation — the invariant the lookup's binary
/// search relies on. Do not store headwords in any display script.</para>
///
/// Port of CST4's <c>DictionaryWord</c>.
/// </summary>
public sealed class DictionaryWord
{
    public DictionaryWord(string word, string meaning)
    {
        Word = word;
        Meaning = meaning;
    }

    /// <summary>IPE-normalized headword; the sort/search key.</summary>
    public string Word { get; }

    /// <summary>HTML definition fragment (may be several source definitions joined together).</summary>
    public string Meaning { get; }
}
