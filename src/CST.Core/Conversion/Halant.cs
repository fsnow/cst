using System.Text.RegularExpressions;

namespace CST.Conversion
{
    /// <summary>
    /// The visible halant: a Devanagari virama (U+094D) with no consonant after it, so it is shown as a mark
    /// and does not join two consonants into a conjunct. (#1048)
    ///
    /// <para>Pāli has no word-final consonants, but the corpus has visible halants all the same: 101 of them,
    /// all in the grammar texts, where they mark technical forms such as vaṅ or ñ. Sanskrit quotations
    /// restored under #1046 add more (āsīd, yad). Scripts whose virama also forms conjuncts, such as Myanmar
    /// and Khmer, have a separate visible sign for this case, so a converter has to tell the two apart.</para>
    ///
    /// <para>A virama followed by ZWJ or ZWNJ and then a consonant still forms a conjunct: the joiners only
    /// choose its shape. So joiners are skipped when looking for the next consonant.</para>
    /// </summary>
    public static class Halant
    {
        /// <summary>A virama not followed (after any joiners) by a Devanagari consonant.</summary>
        public static readonly Regex VisibleVirama =
            new Regex("\u094D(?![\u200C\u200D]*[\u0915-\u0939])", RegexOptions.Compiled);

        /// <summary>
        /// Whether the virama at <paramref name="i"/> in <paramref name="deva"/> is visible. The caller
        /// guarantees <c>deva[i]</c> is U+094D.
        /// </summary>
        public static bool IsVisibleAt(string deva, int i)
        {
            int j = i + 1;
            while (j < deva.Length && (deva[j] == '\u200C' || deva[j] == '\u200D'))
                j++;
            return j >= deva.Length || deva[j] < '\u0915' || deva[j] > '\u0939';
        }
    }
}
