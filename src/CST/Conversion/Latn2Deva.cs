using System;
using System.Collections;
using System.Text;
using CST.Collections;

namespace CST.Conversion
{
    public static class Latn2Deva
    {
        static Latn2Deva()
        {
            paliChars = new Set();
            paliChars.Add('\x1E43');  // m underdot
            paliChars.Add('a');
            paliChars.Add('\x0101');  // a macron
            paliChars.Add('i');
            paliChars.Add('\x012B');  // i macron
            paliChars.Add('u');
            paliChars.Add('\x016B');  // u macron
            paliChars.Add('e');
            paliChars.Add('o');
            paliChars.Add('k');
            paliChars.Add('g');
            paliChars.Add('\x1E45');  // n overdot
            paliChars.Add('c');
            paliChars.Add('j');
            paliChars.Add('\x00F1');  // n tilde
            paliChars.Add('\x1E6D');  // t underdot
            paliChars.Add('\x1E0D');  // d underdot
            paliChars.Add('\x1E47');  // n underdot
            paliChars.Add('t');
            paliChars.Add('d');
            paliChars.Add('n');
            paliChars.Add('p');
            paliChars.Add('b');
            paliChars.Add('m');
            paliChars.Add('y');
            paliChars.Add('r');
            paliChars.Add('l');
            paliChars.Add('\x1E37');  // l underdot
            paliChars.Add('v');
            paliChars.Add('s');
            paliChars.Add('h');

            paliVowels = new Set();
            paliVowels.Add('a');
            paliVowels.Add('\x0101');  // a macron
            paliVowels.Add('i');
            paliVowels.Add('\x012B');  // i macron
            paliVowels.Add('u');
            paliVowels.Add('\x016B');  // u macron
            paliVowels.Add('e');
            paliVowels.Add('o');

            devInitialVowels = new Hashtable();
            devInitialVowels['a'] = '\x0905';
            devInitialVowels['\x0101'] = '\x0906';  // a macron
            devInitialVowels['i'] = '\x0907';
            devInitialVowels['\x012B'] = '\x0908';  // i macron
            devInitialVowels['u'] = '\x0909';
            devInitialVowels['\x016B'] = '\x090A';  // u macron
            devInitialVowels['e'] = '\x090F';
            devInitialVowels['o'] = '\x0913';

            devVowels = new Hashtable();
            devVowels['a'] = "";
            devVowels['\x0101'] = '\x093E';  // a macron
            devVowels['i'] = '\x093F';
            devVowels['\x012B'] = '\x0940';  // i macron
            devVowels['u'] = '\x0941';
            devVowels['\x016B'] = '\x0942';  // u macron
            devVowels['e'] = '\x0947';
            devVowels['o'] = '\x094B';

            devConsonants = new Hashtable();
            devConsonants["k"] = String.Concat('\x0915');
            devConsonants["kh"] = String.Concat('\x0916');
            devConsonants["g"] = String.Concat('\x0917');
            devConsonants["gh"] = String.Concat('\x0918');
            devConsonants[String.Concat('\x1E45')] = String.Concat('\x0919');
            devConsonants["c"] = String.Concat('\x091A');
            devConsonants["ch"] = String.Concat('\x091B');
            devConsonants["j"] = String.Concat('\x091C');
            devConsonants["jh"] = String.Concat('\x091D');
            devConsonants[String.Concat('\x00F1')] = String.Concat('\x091E');
            devConsonants[String.Concat('\x1E6D')] = String.Concat('\x091F');
            devConsonants[String.Concat('\x1E6D', 'h')] = String.Concat('\x0920');
            devConsonants[String.Concat('\x1E0D')] = String.Concat('\x0921');
            devConsonants[String.Concat('\x1E0D', 'h')] = String.Concat('\x0922');
            devConsonants[String.Concat('\x1E47')] = String.Concat('\x0923');
            devConsonants["t"] = String.Concat('\x0924');
            devConsonants["th"] = String.Concat('\x0925');
            devConsonants["d"] = String.Concat('\x0926');
            devConsonants["dh"] = String.Concat('\x0927');
            devConsonants["n"] = String.Concat('\x0928');
            devConsonants["p"] = String.Concat('\x092A');
            devConsonants["ph"] = String.Concat('\x092B');
            devConsonants["b"] = String.Concat('\x092C');
            devConsonants["bh"] = String.Concat('\x092D');
            devConsonants["m"] = String.Concat('\x092E');
            devConsonants["y"] = String.Concat('\x092F');
            devConsonants["r"] = String.Concat('\x0930');
            devConsonants["l"] = String.Concat('\x0932');
            devConsonants[String.Concat('\x1E37')] = String.Concat('\x0933'); // l underdot
            devConsonants["v"] = String.Concat('\x0935');
            devConsonants["s"] = String.Concat('\x0938');
            devConsonants["h"] = String.Concat('\x0939');

            paliAspiratables = new Set();
            paliAspiratables.Add('k');
            paliAspiratables.Add('g');
            paliAspiratables.Add('c');
            paliAspiratables.Add('j');
            paliAspiratables.Add('\x1E6D');
            paliAspiratables.Add('\x1E0D');
            paliAspiratables.Add('t');
            paliAspiratables.Add('d');
            paliAspiratables.Add('p');
            paliAspiratables.Add('b');
        }

        private static Set paliChars;
        private static Set paliVowels;
        private static Set paliAspiratables;

        private static Hashtable devInitialVowels;
        private static Hashtable devVowels;
        private static Hashtable devConsonants;

        public static string Convert(string latin)
		{
			StringBuilder book = new StringBuilder();
			StringBuilder word = new StringBuilder();

            char scriptZero = '\x0966';

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

			// insert ZWJ in some Devanagari conjuncts
			book = book.Replace("\x0915\x094D\x0915", "\x0915\x094D\x200D\x0915"); // ka + ka
			book = book.Replace("\x0915\x094D\x0932", "\x0915\x094D\x200D\x0932"); // ka + la
			book = book.Replace("\x0915\x094D\x0935", "\x0915\x094D\x200D\x0935"); // ka + va
			book = book.Replace("\x091A\x094D\x091A", "\x091A\x094D\x200D\x091A"); // ca + ca
			book = book.Replace("\x091C\x094D\x091C", "\x091C\x094D\x200D\x091C"); // ja + ja
			book = book.Replace("\x091E\x094D\x091A", "\x091E\x094D\x200D\x091A"); // ña + ca
			book = book.Replace("\x091E\x094D\x091C", "\x091E\x094D\x200D\x091C"); // ña + ja
			book = book.Replace("\x091E\x094D\x091E", "\x091E\x094D\x200D\x091E"); // ña + ña
			book = book.Replace("\x0928\x094D\x0928", "\x0928\x094D\x200D\x0928"); // na + na
			book = book.Replace("\x092A\x094D\x0932", "\x092A\x094D\x200D\x0932"); // pa + la
			book = book.Replace("\x0932\x094D\x0932", "\x0932\x094D\x200D\x0932"); // la + la

			return book.ToString();
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
				char c2 = '\x0';
				if (i < latin.Length - 1)
					c2 = latin[i + 1];

				if (IsPaliVowel(c))
				{
					if (last == LetterType.Vowel)
					{
						dev = String.Concat(dev, devInitialVowels[c]);
					}
					else
					{
						dev = String.Concat(dev, devVowels[c]);
					}

					last = LetterType.Vowel;
				}
				else if (c.Equals('\x1E43'))  // m underdot
				{
					last = LetterType.Nasal;
					dev = String.Concat(dev, '\x0902');  // anusvara
				}
				else
				{
					if (last == LetterType.Consonant)
						dev = String.Concat(dev, '\x094D'); // halant after last consonant

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
                dev += '\x094D';

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
