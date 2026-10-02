namespace CST.Conversion
{
    /// <summary>
    /// Classifies characters to their <see cref="Script"/> for run-splitting during script
    /// auto-detection. Shared by <see cref="Any2Ipe"/> and <see cref="Any2Deva"/> so the two detection
    /// tables cannot drift (they previously had to be fixed in tandem — e.g. ZWJ/ZWNJ handling).
    ///
    /// <para><see cref="GetScript(char)"/> classifies one character by its Unicode block.
    /// <see cref="GetScript(string, int, Script)"/>, which the run-splitters call, does the same for every
    /// character but one: U+0AB5, the Gujarati va, is classed by the letters around it, because builds before
    /// #1025 wrote it for Gurmukhi va and Gurmukhi text from them still carries it.</para>
    /// </summary>
    public static class ScriptDetector
    {
        public static Script GetScript(char c)
        {
            int ccode = System.Convert.ToInt32(c);
            Script script;
            if (ccode == 0x200C || ccode == 0x200D) // ZWJ and ZWNJ
                script = Script.Unknown;
            else if (ccode >= 0x0300 && ccode <= 0x036F)
                // Combining diacritical marks (used by the Cyrillic Pāli scheme, e.g. dot-above / tilde).
                // Treat as Unknown so they stay with the surrounding run instead of splitting it. (CORE-3)
                script = Script.Unknown;
            else if (ccode >= 0x0400 && ccode <= 0x04FF)
                script = Script.Cyrillic; // (CORE-3)
            else if (ccode >= 0x0900 && ccode <= 0x097F)
                script = Script.Devanagari;
            else if (ccode >= 0x0980 && ccode <= 0x09FF)
                script = Script.Bengali;
            else if (ccode >= 0x0A00 && ccode <= 0x0A7F)
                script = Script.Gurmukhi;
            else if (ccode >= 0x0A80 && ccode <= 0x0AFF)
                script = Script.Gujarati;
            else if (ccode >= 0x0C00 && ccode <= 0x0C7F)
                script = Script.Telugu;
            else if (ccode >= 0x0C80 && ccode <= 0x0CFF)
                script = Script.Kannada;
            else if (ccode >= 0x0D00 && ccode <= 0x0D7F)
                script = Script.Malayalam;
            else if (ccode >= 0x0D80 && ccode <= 0x0DFF)
                script = Script.Sinhala;
            else if (ccode >= 0x0E00 && ccode <= 0x0E7F)
                script = Script.Thai;
            else if (ccode >= 0x0F00 && ccode <= 0x0FFF)
                script = Script.Tibetan;
            else if (ccode >= 0x1000 && ccode <= 0x107F)
                script = Script.Myanmar;
            else if (ccode >= 0x1780 && ccode <= 0x17FF)
                script = Script.Khmer;
            else
                script = Script.Latin;

            return script;
        }

        // U+0AB5 is the Gujarati letter va. Builds before #1025 wrote it for Gurmukhi va too, so it turns up
        // inside Gurmukhi words in text copied or stored from them.
        private const char GujaratiVa = '\u0AB5';

        /// <summary>
        /// Classifies <c>s[i]</c> for run-splitting, as <see cref="GetScript(char)"/> does, except that a
        /// Gujarati va (U+0AB5) standing in a Gurmukhi word is classed as Gurmukhi. Builds before #1025
        /// wrote U+0AB5 for Gurmukhi va; classed by its block, it split the Gurmukhi run and left the vowel
        /// sign or virama after it in a run with no consonant (<c>vAreti</c> read back as <c>vaAreti</c>).
        ///
        /// <para>The character after it decides: a Gurmukhi character means Gurmukhi, a Gujarati one means
        /// Gujarati, so genuine Gujarati text is unaffected. With neither (end of text, a space, Latin) it
        /// joins the run before it. Zero-width joiners are skipped when looking ahead. Both converters read
        /// U+0AB5 as va, so the choice only decides which run it is converted with.</para>
        /// </summary>
        public static Script GetScript(string s, int i, Script lastScript)
        {
            char c = s[i];
            Script script = GetScript(c);
            if (c != GujaratiVa)
                return script;

            for (int j = i + 1; j < s.Length; j++)
            {
                Script next = GetScript(s[j]);
                if (next == Script.Unknown)
                    continue;
                if (next == Script.Gurmukhi)
                    return Script.Gurmukhi;
                if (next == Script.Gujarati)
                    return Script.Gujarati;
                break;
            }

            return lastScript == Script.Gurmukhi ? Script.Gurmukhi : script;
        }
    }
}
