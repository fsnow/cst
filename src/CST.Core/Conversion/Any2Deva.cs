using System;
using System.Text;

namespace CST.Conversion
{
    public static class Any2Deva
    {
        public static string Convert(string str)
        {
            string deva = "";
            string run = "";
            Script lastScript = Script.Latin;
            
            foreach (char c in str.ToCharArray())
            {
                Script cScript = GetScript(c);
                // Zero-width joiners (Unknown) belong to the surrounding run; they must not split it,
                // or a script's contextual conjunct handling breaks at the boundary. Mirrors Any2Ipe.
                if (cScript == lastScript || cScript == Script.Unknown)
                    run += c;
                else
                {
                    if (run.Length > 0)
                        deva += Convert(run, lastScript);

                    run = "" + c;
                    lastScript = cScript;
                }
            }

            deva += Convert(run, lastScript);

            return deva;
        }

        public static string Convert(string str, Script script)
        {
            if (script == Script.Bengali)
                return Beng2Deva.Convert(str);
            else if (script == Script.Gujarati)
                return Gujr2Deva.Convert(str);
            else if (script == Script.Gurmukhi)
                return Guru2Deva.Convert(str);
            else if (script == Script.Kannada)
                return Knda2Deva.Convert(str);
            else if (script == Script.Latin)
                return Latn2Deva.Convert(str);
            else if (script == Script.Malayalam)
                return Mlym2Deva.Convert(str);
            else if (script == Script.Myanmar)
                return Mymr2Deva.Convert(str);
            else if (script == Script.Sinhala)
                return Sinh2Deva.Convert(str);
            else if (script == Script.Thai)
                return Thai2Deva.Convert(str);
            else if (script == Script.Khmer)
                return Khmr2Deva.Convert(str);
            else if (script == Script.Tibetan)
                return Tibt2Deva.Convert(str);
            else if (script == Script.Telugu)
                return Telu2Deva.Convert(str);
            else if (script == Script.Cyrillic)
                return Cyrl2Deva.Convert(str);
            else
                return str;
        }

        // Delegates to the shared detector (kept as a public method for existing callers/tests). (CORE-3)
        public static Script GetScript(char c) => ScriptDetector.GetScript(c);
    }
}
