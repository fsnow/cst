using System;
using System.Text;

namespace CST.Conversion
{
    public static class Any2Ipe
    {
        public static string Convert(string str)
        {
            // Runs in the Indic and other non-Latin scripts are converted to Devanagari and joined BEFORE the
            // Devanagari -> IPE step, not converted to IPE one run at a time. A script that has no letter for a
            // Sanskrit vowel keeps the Devanagari sign (#1049), so a word can be, e.g., a Myanmar consonant
            // followed by a Devanagari au sign. Converted separately, the consonant would get an inherent a and
            // the sign would stand alone; joined, they make one syllable. Latin runs go straight to IPE.
            var ipe = new StringBuilder();
            var deva = new StringBuilder();
            var run = new StringBuilder();
            Script lastScript = Script.Latin;

            void Flush()
            {
                if (run.Length == 0) return;
                if (lastScript == Script.Latin)
                {
                    if (deva.Length > 0) { ipe.Append(Deva2Ipe.Convert(deva.ToString())); deva.Clear(); }
                    ipe.Append(Latn2Ipe.Convert(run.ToString()));
                }
                else
                {
                    deva.Append(ToDeva(run.ToString(), lastScript));
                }
                run.Clear();
            }

            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                Script cScript = ScriptDetector.GetScript(str, i, lastScript); // context-aware for U+0AB5 (#1025)
                if (cScript == lastScript || cScript == Script.Unknown)
                    run.Append(c);
                else
                {
                    Flush();
                    run.Append(c);
                    lastScript = cScript;
                }
            }

            Flush();
            if (deva.Length > 0)
                ipe.Append(Deva2Ipe.Convert(deva.ToString()));

            return ipe.ToString();
        }

        // A non-Latin run to Devanagari; an unknown script passes through.
        private static string ToDeva(string str, Script script) => script switch
        {
            Script.Bengali => Beng2Deva.Convert(str),
            Script.Devanagari => str,
            Script.Gujarati => Gujr2Deva.Convert(str),
            Script.Gurmukhi => Guru2Deva.Convert(str),
            Script.Kannada => Knda2Deva.Convert(str),
            Script.Malayalam => Mlym2Deva.Convert(str),
            Script.Myanmar => Mymr2Deva.Convert(str),
            Script.Sinhala => Sinh2Deva.Convert(str),
            Script.Thai => Thai2Deva.Convert(str),
            Script.Khmer => Khmr2Deva.Convert(str),
            Script.Tibetan => Tibt2Deva.Convert(str),
            Script.Telugu => Telu2Deva.Convert(str),
            Script.Cyrillic => Cyrl2Deva.Convert(str),
            _ => str,
        };

    }
}
