using System;
using System.Collections.Generic;
using System.Text;

namespace CST.Conversion
{
    public static class Latn2Deva
    {
        static Latn2Deva()
        {
            paliChars = new HashSet<char>();
            paliChars.Add('\u1E43');  // m underdot (m with dot below)
            paliChars.Add('\u1E41');  // m overdot (m with dot above)
            paliChars.Add('a');
            paliChars.Add('\u0101');  // a macron
            paliChars.Add('i');
            paliChars.Add('\u012B');  // i macron
            paliChars.Add('u');
            paliChars.Add('\u016B');  // u macron
            paliChars.Add('e');
            paliChars.Add('o');
            paliChars.Add('k');
            paliChars.Add('g');
            paliChars.Add('\u1E45');  // n overdot
            paliChars.Add('c');
            paliChars.Add('j');
            paliChars.Add('\u00F1');  // n tilde
            paliChars.Add('\u1E6D');  // t underdot
            paliChars.Add('\u1E0D');  // d underdot
            paliChars.Add('\u1E47');  // n underdot
            paliChars.Add('t');
            paliChars.Add('d');
            paliChars.Add('n');
            paliChars.Add('p');
            paliChars.Add('b');
            paliChars.Add('m');
            paliChars.Add('y');
            paliChars.Add('r');
            paliChars.Add('l');
            paliChars.Add('\u1E37');  // l underdot
            paliChars.Add('v');
            paliChars.Add('s');
            paliChars.Add('h');

            paliVowels = new HashSet<char>();
            paliVowels.Add('a');
            paliVowels.Add('\u0101');  // a macron
            paliVowels.Add('i');
            paliVowels.Add('\u012B');  // i macron
            paliVowels.Add('u');
            paliVowels.Add('\u016B');  // u macron
            paliVowels.Add('e');
            paliVowels.Add('o');

            devInitialVowels = new Dictionary<char, char>();
            devInitialVowels['a'] = '\u0905';
            devInitialVowels['\u0101'] = '\u0906';  // a macron
            devInitialVowels['i'] = '\u0907';
            devInitialVowels['\u012B'] = '\u0908';  // i macron
            devInitialVowels['u'] = '\u0909';
            devInitialVowels['\u016B'] = '\u090A';  // u macron
            devInitialVowels['e'] = '\u090F';
            devInitialVowels['o'] = '\u0913';

            devVowels = new Dictionary<char, object>();
            devVowels['a'] = "";
            devVowels['\u0101'] = '\u093E';  // a macron
            devVowels['i'] = '\u093F';
            devVowels['\u012B'] = '\u0940';  // i macron
            devVowels['u'] = '\u0941';
            devVowels['\u016B'] = '\u0942';  // u macron
            devVowels['e'] = '\u0947';
            devVowels['o'] = '\u094B';

            devConsonants = new Dictionary<string, string>();
            devConsonants["k"] = "\u0915";
            devConsonants["kh"] = "\u0916";
            devConsonants["g"] = "\u0917";
            devConsonants["gh"] = "\u0918";
            devConsonants["\u1E45"] = "\u0919"; // n overdot
            devConsonants["c"] = "\u091A";
            devConsonants["ch"] = "\u091B";
            devConsonants["j"] = "\u091C";
            devConsonants["jh"] = "\u091D";
            devConsonants["\u00F1"] = "\u091E"; // n tilde
            devConsonants["\u1E6D"] = "\u091F"; // t underdot 
            devConsonants["\u1E6Dh"] = "\u0920"; // t underdot h
            devConsonants["\u1E0D"] = "\u0921"; // d underdot
            devConsonants["\u1E0Dh"] = "\u0922"; // d underdot h
            devConsonants["\u1E47"] = "\u0923"; // n underdot
            devConsonants["t"] = "\u0924";
            devConsonants["th"] = "\u0925";
            devConsonants["d"] = "\u0926";
            devConsonants["dh"] = "\u0927";
            devConsonants["n"] = "\u0928";
            devConsonants["p"] = "\u092A";
            devConsonants["ph"] = "\u092B";
            devConsonants["b"] = "\u092C";
            devConsonants["bh"] = "\u092D";
            devConsonants["m"] = "\u092E";
            devConsonants["y"] = "\u092F";
            devConsonants["r"] = "\u0930";
            devConsonants["l"] = "\u0932";
            devConsonants["\u1E37"] = "\u0933"; // l underdot
            devConsonants["v"] = "\u0935";
            devConsonants["s"] = "\u0938";
            devConsonants["h"] = "\u0939";

            paliAspiratables = new HashSet<char>();
            paliAspiratables.Add('k');
            paliAspiratables.Add('g');
            paliAspiratables.Add('c');
            paliAspiratables.Add('j');
            paliAspiratables.Add('\u1E6D'); // t underdot
            paliAspiratables.Add('\u1E0D'); // d underdot
            paliAspiratables.Add('t');
            paliAspiratables.Add('d');
            paliAspiratables.Add('p');
            paliAspiratables.Add('b');

            // Mirror the sets/dictionaries into char-indexed tables for the optimized path. (#86)
            foreach (char c in paliChars) if (c < MapLen) isPaliCharArr[c] = true;
            foreach (char c in paliVowels) if (c < MapLen) isPaliVowelArr[c] = true;
            foreach (var kvp in devInitialVowels) if (kvp.Key < MapLen) initVowelArr[kvp.Key] = kvp.Value;
            foreach (var kvp in devVowels) if (kvp.Key < MapLen && kvp.Value is char vch) depVowelArr[kvp.Key] = vch; // 'a' -> "" stays '\0'
            foreach (var kvp in devConsonants)
            {
                if (kvp.Key.Length == 1) { if (kvp.Key[0] < MapLen) consCharArr[kvp.Key[0]] = kvp.Value[0]; }
                else if (kvp.Key.Length == 2 && kvp.Key[1] == 'h' && kvp.Key[0] < MapLen) consAspArr[kvp.Key[0]] = kvp.Value[0];
            }
        }

        private static ISet<char> paliChars;
        private static ISet<char> paliVowels;
        private static ISet<char> paliAspiratables;

        private static IDictionary<char, char> devInitialVowels;
        private static IDictionary<char, object> devVowels;
        private static IDictionary<string, string> devConsonants;

        // Fast char-indexed tables for the optimized Convert()/ToDevanagariFast, mirroring the data above.
        // '\0' = none; depVowelArr['a'] is '\0' because the inherent "a" is written as nothing. (#86)
        private const int MapLen = 0x1E70; // covers the Latin Pali letters incl. U+1E6D (t underdot)
        private static readonly bool[] isPaliCharArr = new bool[MapLen];
        private static readonly bool[] isPaliVowelArr = new bool[MapLen];
        private static readonly char[] initVowelArr = new char[MapLen];
        private static readonly char[] depVowelArr = new char[MapLen];
        private static readonly char[] consCharArr = new char[MapLen];  // single-letter consonant -> Deva
        private static readonly char[] consAspArr = new char[MapLen];   // aspiratable letter (+'h') -> Deva

        /// <summary>
        /// FROZEN reference implementation - the correctness oracle for the optimized Convert(). Do NOT change;
        /// tests assert Convert == ConvertReference over the corpus. (#86)
        /// </summary>
        public static string ConvertReference(string latin)
		{
			StringBuilder book = new StringBuilder();
			StringBuilder word = new StringBuilder();

            char scriptZero = '\u0966';

			foreach (char c in latin.ToCharArray())
			{
				if (word.Length > 0 && IsPaliChar(c) == false)
				{
					book.Append(ToDevanagari(word.ToString()));
                    book.Append(c); // punctuation
					word.Length = 0;
				}
				else if (IsDigit(c))
				{
					char scriptNumber = (char)(c - '0' + scriptZero);
					book.Append(scriptNumber);
				}
				else if (IsPaliChar(c))
					word.Append(c);
				else
					book.Append(c);
			}

			if (word.Length > 0)
			{
				book.Append(ToDevanagari(word.ToString()));
				word.Length = 0;
			}

			// ZWJ insertion for open-form conjuncts is shared with the
			// IPE -> Devanagari path; see Ipe2Deva.InsertConjunctZwj.
			return Ipe2Deva.InsertConjunctZwj(book.ToString());
		}

        // Optimized (#86): byte-identical to ConvertReference (verified by tests). Same word-by-word structure,
        // but ToDevanagariFast builds each word in O(n) (the reference's String.Concat loop was O(n^2)) and the
        // shared 11-pass Ipe2Deva.InsertConjunctZwj is folded into one ZwjFold scan.
        public static string Convert(string latin)
        {
            if (string.IsNullOrEmpty(latin))
                return latin;

            StringBuilder book = new StringBuilder(latin.Length);
            StringBuilder word = new StringBuilder();

            char scriptZero = '०';

            foreach (char c in latin)
            {
                if (word.Length > 0 && IsPaliChar(c) == false)
                {
                    book.Append(ToDevanagariFast(word.ToString()));
                    book.Append(c); // punctuation
                    word.Length = 0;
                }
                else if (IsDigit(c))
                {
                    char scriptNumber = (char)(c - '0' + scriptZero);
                    book.Append(scriptNumber);
                }
                else if (IsPaliChar(c))
                    word.Append(c);
                else
                    book.Append(c);
            }

            if (word.Length > 0)
            {
                book.Append(ToDevanagariFast(word.ToString()));
                word.Length = 0;
            }

            return ZwjFold(book.ToString());
        }

        // O(n) StringBuilder version of ToDevanagari - same outputs, no per-char whole-string reallocation. (#86)
        private static string ToDevanagariFast(string latin)
        {
            var dev = new StringBuilder(latin.Length + 4);
            LetterType last = LetterType.Vowel;

            for (int i = 0; i < latin.Length; i++)
            {
                char c = latin[i];
                char c2 = ' ';
                if (i < latin.Length - 1)
                    c2 = latin[i + 1];

                if (c < MapLen && isPaliVowelArr[c])
                {
                    if (last == LetterType.Vowel || last == LetterType.Nasal)
                        dev.Append(initVowelArr[c]);
                    else if (depVowelArr[c] != '\0') // the inherent "a" is written as nothing
                        dev.Append(depVowelArr[c]);
                    last = LetterType.Vowel;
                }
                else if (c.Equals('ṃ') || c.Equals('ṁ')) // m underdot / m overdot (niggahita)
                {
                    last = LetterType.Nasal;
                    dev.Append('ं'); // anusvara
                }
                else
                {
                    if (last == LetterType.Consonant)
                        dev.Append('्'); // halant after the previous consonant

                    if (c < MapLen && consAspArr[c] != '\0' && c2 == 'h')
                    {
                        dev.Append(consAspArr[c]); // aspirate: consonant + 'h'
                        i++;
                    }
                    else
                        dev.Append(consCharArr[c]);

                    last = LetterType.Consonant;
                }
            }

            char lastCh = latin[latin.Length - 1];
            if (lastCh < MapLen && consCharArr[lastCh] != '\0') // word-final consonant -> halanta
                dev.Append('्');

            return dev.ToString();
        }

        // Single-pass equivalent of Ipe2Deva.InsertConjunctZwj: insert ZWJ into the registered open-form
        // conjuncts. Conjuncts never span word boundaries, so one scan over the whole book is exact. (#86)
        private static string ZwjFold(string s)
        {
            int n = s.Length;
            var buf = new char[n * 2];
            int k = 0;
            int prevC2Index = -1; char prevC1 = '\0', prevC2 = '\0';
            for (int i = 0; i < n; i++)
                k = EmitDeva(buf, k, s[i], ref prevC2Index, ref prevC1, ref prevC2);
            return new string(buf, 0, k);
        }

        // C1 + virama + C2 -> C1 + virama + ZWJ + C2 for the registered conjuncts. Each conjunct is one ordered,
        // non-overlapping Replace in InsertConjunctZwj, so skip only when this match overlaps the previously
        // inserted one AND is the SAME pair. (#86)
        private static int EmitDeva(char[] buf, int k, char x, ref int prevC2Index, ref char prevC1, ref char prevC2)
        {
            buf[k++] = x;
            if (k >= 3 && buf[k - 2] == 0x094D)
            {
                char c1 = buf[k - 3];
                if (IsZwjConjunct(c1, x))
                {
                    bool samePairOverlap = (k - 3 == prevC2Index) && c1 == prevC1 && x == prevC2;
                    if (!samePairOverlap)
                    {
                        buf[k - 1] = (char)0x200D;
                        buf[k++] = x;
                        prevC2Index = k - 1; prevC1 = c1; prevC2 = x;
                    }
                }
            }
            return k;
        }

        // The 11 Devanagari conjuncts handled by Ipe2Deva.InsertConjunctZwj. (#86)
        private static bool IsZwjConjunct(char c1, char c2)
        {
            switch (c1)
            {
                case (char)0x0915: return c2 == 0x0915 || c2 == 0x0932 || c2 == 0x0935; // ka + ka/la/va
                case (char)0x091A: return c2 == 0x091A;                                 // ca + ca
                case (char)0x091C: return c2 == 0x091C;                                 // ja + ja
                case (char)0x091E: return c2 == 0x091A || c2 == 0x091C || c2 == 0x091E; // nya + ca/ja/nya
                case (char)0x0928: return c2 == 0x0928;                                 // na + na
                case (char)0x092A: return c2 == 0x0932;                                 // pa + la
                case (char)0x0932: return c2 == 0x0932;                                 // la + la
                default: return false;
            }
        }

        public static bool IsDigit(char c)
		{
			if (c >= '0' && c <= '9')
				return true;
			else
				return false;
		}

        public static bool IsPaliChar(char c)
		{
			return paliChars.Contains(c);
		}

        public static bool IsPaliVowel(char c)
		{
			return paliVowels.Contains(c);
		}

		// Is a Latin-script letter that when followed by 'h' is a single Pali
		// aspirated stop, e.g. t -> th
        public static bool IsPaliAspiratable(char c)
		{
			return paliAspiratables.Contains(c);
		}

        public static string GetDevConsonants(string consonants)
		{
			if (devConsonants[consonants] == null)
				return "";
			else
				return (string)devConsonants[consonants];
		}

        public static string ToDevanagari(string latin)
		{
			string dev = "";
			LetterType last = LetterType.Vowel;
			
			for (int i = 0; i < latin.Length; i++)
			{
				char c = latin[i];
				char c2 = '\u0000';
				if (i < latin.Length - 1)
					c2 = latin[i + 1];

				if (IsPaliVowel(c))
				{
					// Vowels after another vowel OR after niggahita should be independent vowels
					if (last == LetterType.Vowel || last == LetterType.Nasal)
					{
						dev = String.Concat(dev, devInitialVowels[c]);
					}
					else
					{
						dev = String.Concat(dev, devVowels[c]);
					}

					last = LetterType.Vowel;
				}
				else if (c.Equals('\u1E43') || c.Equals('\u1E41'))  // m underdot or m overdot (niggahita)
				{
					last = LetterType.Nasal;
					dev = String.Concat(dev, '\u0902');  // anusvara
				}
				else
				{
					if (last == LetterType.Consonant)
						dev = String.Concat(dev, '\u094D'); // halant after last consonant

					if (IsPaliAspiratable(c) && c2.Equals('h'))
					{
						dev = String.Concat(dev, devConsonants[String.Concat(c, c2)]);
						i++;
					}
					else
						dev = String.Concat(dev, devConsonants[String.Concat(c)]);

					last = LetterType.Consonant;
				}
			}

            if (devConsonants.ContainsKey(latin.Substring(latin.Length - 1)))
                dev += '\u094D';

			return dev;
		}
	}

	public enum LetterType
	{
		Vowel,
		Consonant,
		Nasal
	}


}
