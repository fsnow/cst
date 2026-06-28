using System;
using System.Collections.Generic;
using System.Text;

namespace CST.Conversion
{
    public static class ScriptConverter
    {
        // Direct converters for specific (from, to) pairs. Any pair not listed here (and not an identity
        // conversion) is routed through Devanagari - the pivot hub - via _toDevanagari. (#75)
        private static readonly Dictionary<(Script From, Script To), Func<string, string>> _direct = new()
        {
            [(Script.Ipe, Script.Latin)] = Ipe2Latn.Convert,
            [(Script.Ipe, Script.Devanagari)] = Ipe2Deva.Convert,

            [(Script.Devanagari, Script.Ipe)] = Deva2Ipe.Convert,
            [(Script.Devanagari, Script.Bengali)] = Deva2Beng.Convert,
            [(Script.Devanagari, Script.Cyrillic)] = Deva2Cyrl.Convert,
            [(Script.Devanagari, Script.Gujarati)] = Deva2Gujr.Convert,
            [(Script.Devanagari, Script.Gurmukhi)] = Deva2Guru.Convert,
            [(Script.Devanagari, Script.Kannada)] = Deva2Knda.Convert,
            [(Script.Devanagari, Script.Khmer)] = Deva2Khmr.Convert,
            [(Script.Devanagari, Script.Latin)] = Deva2Latn.Convert,
            [(Script.Devanagari, Script.Malayalam)] = Deva2Mlym.Convert,
            [(Script.Devanagari, Script.Myanmar)] = Deva2Mymr.Convert,
            [(Script.Devanagari, Script.Sinhala)] = Deva2Sinh.Convert,
            [(Script.Devanagari, Script.Telugu)] = Deva2Telu.Convert,
            [(Script.Devanagari, Script.Thai)] = Deva2Thai.Convert,
            [(Script.Devanagari, Script.Tibetan)] = Deva2Tibt.Convert,

            [(Script.Latin, Script.Ipe)] = Latn2Ipe.Convert,
            [(Script.Latin, Script.Devanagari)] = Latn2Deva.Convert,

            [(Script.Bengali, Script.Devanagari)] = Beng2Deva.Convert,
            [(Script.Gujarati, Script.Devanagari)] = Gujr2Deva.Convert,
            [(Script.Gurmukhi, Script.Devanagari)] = Guru2Deva.Convert,
            [(Script.Kannada, Script.Devanagari)] = Knda2Deva.Convert,
            [(Script.Malayalam, Script.Devanagari)] = Mlym2Deva.Convert,
            [(Script.Myanmar, Script.Devanagari)] = Mymr2Deva.Convert,
            [(Script.Sinhala, Script.Devanagari)] = Sinh2Deva.Convert,
            [(Script.Thai, Script.Devanagari)] = Thai2Deva.Convert,
            [(Script.Khmer, Script.Devanagari)] = Khmr2Deva.Convert,
            [(Script.Tibetan, Script.Devanagari)] = Tibt2Deva.Convert,
            [(Script.Telugu, Script.Devanagari)] = Telu2Deva.Convert,
            [(Script.Cyrillic, Script.Devanagari)] = Cyrl2Deva.Convert,

            [(Script.Unknown, Script.Ipe)] = Any2Ipe.Convert,
        };

        // How to reach Devanagari from a given input script, for conversions that pivot through it
        // (e.g. Bengali -> Thai = Bengali -> Devanagari -> Thai). Latin and Unknown deliberately use the
        // script-auto-detecting Any2Deva here, matching the original dispatch's non-Devanagari-output
        // paths. Devanagari itself is absent: it is the hub and never pivots. (#75)
        private static readonly Dictionary<Script, Func<string, string>> _toDevanagari = new()
        {
            [Script.Ipe] = Ipe2Deva.Convert,
            [Script.Latin] = Any2Deva.Convert,
            [Script.Bengali] = Beng2Deva.Convert,
            [Script.Gujarati] = Gujr2Deva.Convert,
            [Script.Gurmukhi] = Guru2Deva.Convert,
            [Script.Kannada] = Knda2Deva.Convert,
            [Script.Malayalam] = Mlym2Deva.Convert,
            [Script.Myanmar] = Mymr2Deva.Convert,
            [Script.Sinhala] = Sinh2Deva.Convert,
            [Script.Thai] = Thai2Deva.Convert,
            [Script.Khmer] = Khmr2Deva.Convert,
            [Script.Tibetan] = Tibt2Deva.Convert,
            [Script.Telugu] = Telu2Deva.Convert,
            [Script.Cyrillic] = Cyrl2Deva.Convert,
            [Script.Unknown] = Any2Deva.Convert,
        };

        public static string Convert(string str, Script inputScript, Script outputScript)
        {
            return Convert(str, inputScript, outputScript, false);
        }

        // for future: convert toTitleCase to an enumeration with FlagsAttribute if there are more options to handle
        public static string Convert(string str, Script inputScript, Script outputScript, bool toTitleCase)
        {
            // Null/empty needs no conversion - and the per-script converters would NRE on null
            // (e.g. a search result whose ShortNavPath and FileName are both null). (#82)
            if (string.IsNullOrEmpty(str))
                return str;

            string outStr;
            if (inputScript == outputScript)
                outStr = str;
            else if (_direct.TryGetValue((inputScript, outputScript), out var convert))
                outStr = convert(str);
            else if (_toDevanagari.TryGetValue(inputScript, out var toDeva))
                // Pivot through Devanagari, then convert on to the target script.
                outStr = Convert(toDeva(str), Script.Devanagari, outputScript);
            else
                // No conversion path (e.g. Devanagari -> an unsupported output script): the original
                // dispatch left outStr as "" in this case, so preserve that.
                outStr = "";

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
