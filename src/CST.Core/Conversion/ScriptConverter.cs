using System;
using System.Collections.Generic;
using System.Text;

namespace CST.Conversion
{
    public static class ScriptConverter
    {
        static ScriptConverter()
        {
        }

        public static string Convert(string str, Script inputScript, Script outputScript)
        {
            return Convert(str, inputScript, outputScript, false);
        }

        // for future: convert toTitleCase to an enumeration with FlagsAttribute if there are more options to handle
        public static string Convert(string str, Script inputScript, Script outputScript, bool toTitleCase)
        {
            string outStr = "";
            if (inputScript == outputScript)
            {
                outStr = str;
            }
            else if (inputScript == Script.Ipe)
            {
                if (outputScript == Script.Latin)
                {
                    outStr = Ipe2Latn.Convert(str);
                }
                else if (outputScript == Script.Devanagari)
                {
                    outStr = Convert(Convert(str, Script.Ipe, Script.Latin), Script.Latin, Script.Devanagari);
                }
                else
                {
                    outStr = Convert(Convert(str, Script.Ipe, Script.Devanagari), Script.Devanagari, outputScript);
                }
            }
            else if (inputScript == Script.Devanagari)
            {
                if (outputScript == Script.Ipe)
                    outStr = Deva2Ipe.Convert(str);
                else if (outputScript == Script.Bengali)
                    outStr = Deva2Beng.Convert(str);
                else if (outputScript == Script.Cyrillic)
                    outStr = Deva2Cyrl.Convert(str);
                else if (outputScript == Script.Gujarati)
                    outStr = Deva2Gujr.Convert(str);
                else if (outputScript == Script.Gurmukhi)
                    outStr = Deva2Guru.Convert(str);
                else if (outputScript == Script.Kannada)
                    outStr = Deva2Knda.Convert(str);
                else if (outputScript == Script.Khmer)
                    outStr = Deva2Khmr.Convert(str);
                else if (outputScript == Script.Latin)
                    outStr = Deva2Latn.Convert(str);
                else if (outputScript == Script.Malayalam)
                    outStr = Deva2Mlym.Convert(str);
                else if (outputScript == Script.Myanmar)
                    outStr = Deva2Mymr.Convert(str);
                else if (outputScript == Script.Sinhala)
                    outStr = Deva2Sinh.Convert(str);
                else if (outputScript == Script.Telugu)
                    outStr = Deva2Telu.Convert(str);
                else if (outputScript == Script.Thai)
                    outStr = Deva2Thai.Convert(str);
                else if (outputScript == Script.Tibetan)
                    outStr = Deva2Tibt.Convert(str);
            }
            else if (inputScript == Script.Latin)
            {
                if (outputScript == Script.Ipe)
                    outStr = Latn2Ipe.Convert(str);
                else if (outputScript == Script.Devanagari)
                    outStr = Latn2Deva.Convert(str);
                else
                {
                    outStr = Convert(Any2Deva.Convert(str), Script.Devanagari, outputScript);
                }
            }
            else if (inputScript == Script.Bengali)
            {
                if (outputScript == Script.Devanagari)
                    outStr = Beng2Deva.Convert(str);
                else
                    outStr = Convert(Beng2Deva.Convert(str), Script.Devanagari, outputScript);
            }
            else if (inputScript == Script.Gujarati)
            {
                if (outputScript == Script.Devanagari)
                    outStr = Gujr2Deva.Convert(str);
                else
                    outStr = Convert(Gujr2Deva.Convert(str), Script.Devanagari, outputScript);
            }
            else if (inputScript == Script.Gurmukhi)
            {
                if (outputScript == Script.Devanagari)
                    outStr = Guru2Deva.Convert(str);
                else
                    outStr = Convert(Guru2Deva.Convert(str), Script.Devanagari, outputScript);
            }
            else if (inputScript == Script.Kannada)
            {
                if (outputScript == Script.Devanagari)
                    outStr = Knda2Deva.Convert(str);
                else
                    outStr = Convert(Knda2Deva.Convert(str), Script.Devanagari, outputScript);
            }
            else if (inputScript == Script.Malayalam)
            {
                if (outputScript == Script.Devanagari)
                    outStr = Mlym2Deva.Convert(str);
                else
                    outStr = Convert(Mlym2Deva.Convert(str), Script.Devanagari, outputScript);
            }
            else if (inputScript == Script.Myanmar)
            {
                if (outputScript == Script.Devanagari)
                    outStr = Mymr2Deva.Convert(str);
                else
                    outStr = Convert(Mymr2Deva.Convert(str), Script.Devanagari, outputScript);
            }
            else if (inputScript == Script.Sinhala)
            {
                if (outputScript == Script.Devanagari)
                    outStr = Sinh2Deva.Convert(str);
                else
                    outStr = Convert(Sinh2Deva.Convert(str), Script.Devanagari, outputScript);
            }
            else if (inputScript == Script.Thai)
            {
                if (outputScript == Script.Devanagari)
                    outStr = Thai2Deva.Convert(str);
                else
                    outStr = Convert(Thai2Deva.Convert(str), Script.Devanagari, outputScript);
            }
            else if (inputScript == Script.Khmer)
            {
                if (outputScript == Script.Devanagari)
                    outStr = Khmr2Deva.Convert(str);
                else
                    outStr = Convert(Khmr2Deva.Convert(str), Script.Devanagari, outputScript);
            }
            else if (inputScript == Script.Tibetan)
            {
                if (outputScript == Script.Devanagari)
                    outStr = Tibt2Deva.Convert(str);
                else
                    outStr = Convert(Tibt2Deva.Convert(str), Script.Devanagari, outputScript);
            }
            else if (inputScript == Script.Telugu)
            {
                if (outputScript == Script.Devanagari)
                    outStr = Telu2Deva.Convert(str);
                else
                    outStr = Convert(Telu2Deva.Convert(str), Script.Devanagari, outputScript);
            }
            else if (inputScript == Script.Cyrillic)
            {
                if (outputScript == Script.Devanagari)
                    outStr = Cyrl2Deva.Convert(str);
                else
                    outStr = Convert(Cyrl2Deva.Convert(str), Script.Devanagari, outputScript);
            }
            else if (inputScript == Script.Unknown)
            {
                if (outputScript == Script.Ipe)
                    outStr = Any2Ipe.Convert(str);
                else
                    outStr = Convert(Any2Deva.Convert(str), Script.Devanagari, outputScript);
            }

            if (toTitleCase && outputScript == Script.Latin)
                return ToTitleCase(outStr);
            else
                return outStr;
        }


        public static string ConvertBook(string str, Script outputScript)
        {
            if (outputScript == Script.Bengali)
                return Deva2Beng.ConvertBook(str);
            else if (outputScript == Script.Cyrillic)
                return Deva2Cyrl.ConvertBook(str);
            else if (outputScript == Script.Devanagari)
                return str;
            else if (outputScript == Script.Gujarati)
                return Deva2Gujr.ConvertBook(str);
            else if (outputScript == Script.Gurmukhi)
                return Deva2Guru.ConvertBook(str);
            else if (outputScript == Script.Kannada)
                return Deva2Knda.ConvertBook(str);
            else if (outputScript == Script.Khmer)
                return Deva2Khmr.ConvertBook(str);
            else if (outputScript == Script.Latin)
                return Deva2Latn.ConvertBook(str);
            else if (outputScript == Script.Malayalam)
                return Deva2Mlym.ConvertBook(str);
            else if (outputScript == Script.Myanmar)
                return Deva2Mymr.ConvertBook(str);
            else if (outputScript == Script.Sinhala)
                return Deva2Sinh.ConvertBook(str);
            else if (outputScript == Script.Telugu)
                return Deva2Telu.ConvertBook(str);
            else if (outputScript == Script.Thai)
                return Deva2Thai.ConvertBook(str);
            else if (outputScript == Script.Tibetan)
                return Deva2Tibt.ConvertBook(str);

            return str;
        }

        public static string ToTitleCase(string str)
        {
            StringBuilder sb = new StringBuilder();
            bool lastWasLetter = false;
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (Char.IsLetter(c))
                {
                    if (lastWasLetter == false)
                        c = Char.ToUpper(c);

                    lastWasLetter = true;
                }
                else
                    lastWasLetter = false;

                sb.Append(c);
            }

            return sb.ToString();
        }


		public static string Iso15924Code(Script script)
		{
			switch (script)
			{
				case Script.Bengali:
					return "beng";
				case Script.Cyrillic:
					return "cyrl";
				case Script.Devanagari:
					return "deva";
				case Script.Gujarati:
					return "gujr";
				case Script.Gurmukhi:
					return "guru";
				case Script.Kannada:
					return "knda";
				case Script.Khmer:
					return "khmr";
				case Script.Latin:
					return "latn";
				case Script.Malayalam:
					return "mlym";
				case Script.Myanmar:
					return "mymr";
				case Script.Sinhala:
					return "sinh";
				case Script.Telugu:
					return "telu";
				case Script.Thai:
					return "thai";
				case Script.Tibetan:
					return "tibt";
				default:
					return "";
			}
		}
    }
}
