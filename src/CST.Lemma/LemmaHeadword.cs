namespace CST.Lemma;

/// <summary>DPD headword helpers shared by the provider and its callers (lemma report, dictionary source, the
/// dictionary panel's link handling), so the homonym rule lives in one place. (#1002)</summary>
public static class LemmaHeadword
{
    /// <summary>"paññāya 1" → "paññāya"; DPD's dotted sub-numbering "dhamma 1.01" → "dhamma";
    /// "pajānāti" → "pajānāti". A trailing space-separated token of only digits and dots is a homonym marker.
    /// <see cref="char.IsDigit(char)"/> accepts any script's decimal digits, so a headword rendered in
    /// Devanāgarī strips the same way.</summary>
    public static string StripHomonym(string lemma)
    {
        int sp = lemma.LastIndexOf(' ');
        if (sp <= 0 || sp + 1 >= lemma.Length) return lemma;
        for (int i = sp + 1; i < lemma.Length; i++)
            if (!char.IsDigit(lemma[i]) && lemma[i] != '.') return lemma;
        return lemma[..sp];
    }

    /// <summary>True when <paramref name="lemma"/> ends in a homonym marker (see <see cref="StripHomonym"/>).</summary>
    public static bool HasHomonym(string lemma) => StripHomonym(lemma).Length != lemma.Length;
}
