using System.Collections.Generic;
using System.Text;

namespace CST.Conversion
{
    // Direct IPE -> Devanagari converter, the inverse of Deva2Ipe.
    //
    // IPE ("Ideal Pali Encoding") is a linear, fully-decomposed encoding:
    //   - each consonant is a single code point (no inherent "a" baked in)
    //   - every vowel is explicit and has a single code point (IPE makes no
    //     distinction between independent and dependent/matra vowel forms)
    //   - there is no virama; a conjunct is simply two consonant code points
    //     with no vowel between them
    //   - niggahita is a single code point following a vowel
    //
    // Reconstructing Devanagari therefore requires context: a vowel renders as
    // an independent letter at the start of a syllable but as a dependent sign
    // (or the inherent "a", which is written as nothing) immediately after a
    // consonant; and a consonant followed by another consonant or by a word
    // boundary needs an explicit virama.
    //
    // Because IPE preserves the exact akshara structure of the Devanagari it was
    // derived from, this is a direct, lossless inverse of Deva2Ipe:
    // Deva2Ipe(Ipe2Deva(ipe)) == ipe for all well-formed IPE. It replaces an
    // earlier development shortcut that routed IPE -> Latin -> Devanagari.
    //
    // Following the convention of the other converters, all non-ASCII code
    // points are written as \uXXXX escapes rather than literal glyphs.
    public static class Ipe2Deva
    {
        // IPE consonant code point -> Devanagari consonant
        private static readonly Dictionary<char, char> consonants;
        // IPE vowel code point -> Devanagari independent vowel
        private static readonly Dictionary<char, char> independentVowels;
        // IPE vowel code point -> Devanagari dependent vowel sign
        // (the inherent "a", \u00C1, has no dependent sign and is omitted)
        private static readonly Dictionary<char, char> dependentVowels;

        private const char IpeNiggahita = '\u00C0';
        private const char IpeInherentA = '\u00C1';
        private const char IpeVowelFirst = '\u00C1'; // a
        private const char IpeVowelLast = '\u00C8';  // o
        private const char DevaVirama = '\u094D';
        private const char DevaNiggahita = '\u0902'; // anusvara

        static Ipe2Deva()
        {
            consonants = new Dictionary<char, char>
            {
                ['\u00C9'] = '\u0915', // ka
                ['\u00CA'] = '\u0916', // kha
                ['\u00CB'] = '\u0917', // ga
                ['\u00CC'] = '\u0918', // gha
                ['\u00CD'] = '\u0919', // n overdot
                ['\u00CE'] = '\u091A', // ca
                ['\u00CF'] = '\u091B', // cha
                ['\u00D0'] = '\u091C', // ja
                ['\u00D1'] = '\u091D', // jha
                ['\u00D2'] = '\u091E', // n tilde
                ['\u00D3'] = '\u091F', // t underdot
                ['\u00D4'] = '\u0920', // t underdot ha
                ['\u00D5'] = '\u0921', // d underdot
                ['\u00D6'] = '\u0922', // d underdot ha
                ['\u00D8'] = '\u0923', // n underdot
                ['\u00D9'] = '\u0924', // ta
                ['\u00DA'] = '\u0925', // tha
                ['\u00DB'] = '\u0926', // da
                ['\u00DC'] = '\u0927', // dha
                ['\u00DD'] = '\u0928', // na
                ['\u00DE'] = '\u092A', // pa
                ['\u00DF'] = '\u092B', // pha
                ['\u00E0'] = '\u092C', // ba
                ['\u00E1'] = '\u092D', // bha
                ['\u00E2'] = '\u092E', // ma
                ['\u00E3'] = '\u092F', // ya
                ['\u00E4'] = '\u0930', // ra
                ['\u00E5'] = '\u0932', // la
                ['\u00E6'] = '\u0935', // va
                ['\u00E7'] = '\u0938', // sa
                ['\u00E8'] = '\u0939', // ha
                ['\u00E9'] = '\u0933', // l underdot
            };

            independentVowels = new Dictionary<char, char>
            {
                ['\u00C1'] = '\u0905', // a
                ['\u00C2'] = '\u0906', // aa
                ['\u00C3'] = '\u0907', // i
                ['\u00C4'] = '\u0908', // ii
                ['\u00C5'] = '\u0909', // u
                ['\u00C6'] = '\u090A', // uu
                ['\u00C7'] = '\u090F', // e
                ['\u00C8'] = '\u0913', // o
            };

            dependentVowels = new Dictionary<char, char>
            {
                // a (\u00C1) is inherent in the consonant and written as nothing
                ['\u00C2'] = '\u093E', // aa
                ['\u00C3'] = '\u093F', // i
                ['\u00C4'] = '\u0940', // ii
                ['\u00C5'] = '\u0941', // u
                ['\u00C6'] = '\u0942', // uu
                ['\u00C7'] = '\u0947', // e
                ['\u00C8'] = '\u094B', // o
            };
        }

        public static string Convert(string ipe)
        {
            var sb = new StringBuilder(ipe.Length);

            // True when the previously emitted Devanagari character was a
            // consonant that has not yet been "closed" by a vowel. Such a
            // consonant is pending and will receive either a virama (if the
            // next code point is another consonant, a boundary, or any
            // non-vowel) or a dependent vowel sign / inherent "a".
            bool pendingConsonant = false;

            foreach (char c in ipe)
            {
                if (c >= IpeVowelFirst && c <= IpeVowelLast) // vowel
                {
                    if (pendingConsonant)
                    {
                        // a dependent sign, or nothing for the inherent "a"
                        if (c != IpeInherentA)
                            sb.Append(dependentVowels[c]);
                    }
                    else
                    {
                        sb.Append(independentVowels[c]);
                    }
                    pendingConsonant = false;
                    continue;
                }

                // Anything that is not a vowel closes a pending consonant with
                // an explicit virama (conjunct or word-final halanta).
                if (pendingConsonant)
                {
                    sb.Append(DevaVirama);
                    pendingConsonant = false;
                }

                if (consonants.TryGetValue(c, out char deva))
                {
                    sb.Append(deva);
                    pendingConsonant = true;
                }
                else if (c == IpeNiggahita)
                {
                    sb.Append(DevaNiggahita);
                }
                else
                {
                    // spaces, punctuation, digits, and any unmapped code point
                    sb.Append(c);
                }
            }

            // A consonant pending at end of input is a word-final halanta.
            if (pendingConsonant)
                sb.Append(DevaVirama);

            return InsertConjunctZwj(sb.ToString());
        }

        // Insert ZWJ (\u200D) after the virama (\u094D) in the Devanagari
        // conjuncts that must render in the "open" (half-form) shape rather than
        // a stacked ligature. This is the single source of truth shared by every
        // IPE/Latin -> Devanagari path (Latn2Deva delegates here), so that search
        // results (IPE -> Deva) and book text (Latin -> Deva) render identically.
        // Deva2Ipe strips these ZWJ again, so they never affect search matching
        // or round-trip conversion.
        public static string InsertConjunctZwj(string deva)
        {
            deva = deva.Replace("\u0915\u094D\u0915", "\u0915\u094D\u200D\u0915"); // ka + ka
            deva = deva.Replace("\u0915\u094D\u0932", "\u0915\u094D\u200D\u0932"); // ka + la
            deva = deva.Replace("\u0915\u094D\u0935", "\u0915\u094D\u200D\u0935"); // ka + va
            deva = deva.Replace("\u091A\u094D\u091A", "\u091A\u094D\u200D\u091A"); // ca + ca
            deva = deva.Replace("\u091C\u094D\u091C", "\u091C\u094D\u200D\u091C"); // ja + ja
            deva = deva.Replace("\u091E\u094D\u091A", "\u091E\u094D\u200D\u091A"); // n(tilde)a + ca
            deva = deva.Replace("\u091E\u094D\u091C", "\u091E\u094D\u200D\u091C"); // n(tilde)a + ja
            deva = deva.Replace("\u091E\u094D\u091E", "\u091E\u094D\u200D\u091E"); // n(tilde)a + n(tilde)a
            deva = deva.Replace("\u0928\u094D\u0928", "\u0928\u094D\u200D\u0928"); // na + na
            deva = deva.Replace("\u092A\u094D\u0932", "\u092A\u094D\u200D\u0932"); // pa + la
            deva = deva.Replace("\u0932\u094D\u0932", "\u0932\u094D\u200D\u0932"); // la + la

            return deva;
        }
    }
}
