using System;
using System.Text;

namespace CST.Conversion
{
    public static class Any2Ipe
    {
        public static string Convert(string str)
        {
            string deva = "";
            string run = "";
            Script lastScript = Script.Latin;
            
            foreach (char c in str.ToCharArray())
            {
                Script cScript = ScriptDetector.GetScript(c);
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

        private static string Convert(string str, Script script)
        {
            if (script == Script.Bengali)
                return Deva2Ipe.Convert(Beng2Deva.Convert(str));
            else if (script == Script.Devanagari)
                return Deva2Ipe.Convert(str);
            else if (script == Script.Gujarati)
                return Deva2Ipe.Convert(Gujr2Deva.Convert(str));
            else if (script == Script.Gurmukhi)
                return Deva2Ipe.Convert(Guru2Deva.Convert(str));
            else if (script == Script.Kannada)
                return Deva2Ipe.Convert(Knda2Deva.Convert(str));
            else if (script == Script.Latin)
                return Latn2Ipe.Convert(str);
            else if (script == Script.Malayalam)
                return Deva2Ipe.Convert(Mlym2Deva.Convert(str));
            else if (script == Script.Myanmar)
                return Deva2Ipe.Convert(Mymr2Deva.Convert(str));
            else if (script == Script.Sinhala)
                return Deva2Ipe.Convert(Sinh2Deva.Convert(str));
            else if (script == Script.Thai)
                return Deva2Ipe.Convert(Thai2Deva.Convert(str));
            else if (script == Script.Khmer)
                return Deva2Ipe.Convert(Khmr2Deva.Convert(str));
            else if (script == Script.Tibetan)
                return Deva2Ipe.Convert(Tibt2Deva.Convert(str));
            else if (script == Script.Telugu)
                return Deva2Ipe.Convert(Telu2Deva.Convert(str));
            else if (script == Script.Cyrillic)
                return Deva2Ipe.Convert(Cyrl2Deva.Convert(str));
            else
                return str;
        }

    }
}
