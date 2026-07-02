namespace CST.Conversion
{
    /// <summary>
    /// Classifies a single character to its <see cref="Script"/> for run-splitting during script
    /// auto-detection. Shared by <see cref="Any2Ipe"/> and <see cref="Any2Deva"/> so the two detection
    /// tables cannot drift (they previously had to be fixed in tandem — e.g. ZWJ/ZWNJ handling).
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
    }
}
