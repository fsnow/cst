namespace CST.Conversion
{
    /// <summary>
    /// The version of the IPE encoding. Anything that stores IPE must record the version it was written with
    /// and rebuild when it differs. The search index does (#1049). application-state.json also holds IPE, in
    /// the restored search pane's selected terms and the book windows' search terms; after a version change
    /// a restored term selection simply fails to match once, and highlighting still works because it runs
    /// off stored offsets, so that state is not stamped.
    ///
    /// <para>IPE packs the alphabet into consecutive code points, so adding a letter in its alphabetical place
    /// renumbers every letter after it. An index built with an older IPE still opens, but its terms no longer
    /// match queries, and searches miss silently.</para>
    ///
    /// <para>History. 1: the Pali alphabet, U+00C0-U+00E9 (2007). 2: visarga, vocalic r, ai, au and the
    /// palatal and retroflex sibilants inserted in alphabetical order, U+00C0-U+00EF (#1049, 2026-10).</para>
    /// </summary>
    public static class IpeVersion
    {
        public const int Current = 2;

        /// <summary>
        /// Rewrite IPE written by version 1 as version 2. Every version-1 letter has exactly one version-2
        /// code, so this is exact: the letters #1049 added did not exist in version 1, and only the shift
        /// past them changes. Characters outside the version-1 range pass through.
        /// </summary>
        public static string UpgradeFromV1(string ipe)
        {
            if (string.IsNullOrEmpty(ipe)) return ipe;
            var chars = ipe.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c >= '\u00C0' && c <= '\u00E9' && V1ToV2[c - 0xC0] != '\0')
                    chars[i] = V1ToV2[c - 0xC0];
            }
            return new string(chars);
        }

        // Indexed by (version-1 code - U+00C0). U+00D7 was never used in either version.
        private static readonly char[] V1ToV2 =
        {
            '\u00C0',                                                              // niggahita
            '\u00C2', '\u00C3', '\u00C4', '\u00C5', '\u00C6', '\u00C7',       // a aa i ii u uu
            '\u00C9', '\u00CB',                                                   // e o
            '\u00CD', '\u00CE', '\u00CF', '\u00D0', '\u00D1',                  // k kh g gh n overdot
            '\u00D2', '\u00D3', '\u00D4', '\u00D5', '\u00D6',                  // c ch j jh n tilde
            '\u00D8', '\u00D9', '\u00DA', '\u00DB',                             // t th d dh (underdot)
            '\0',                                                                  // U+00D7, unused
            '\u00DC',                                                              // n underdot
            '\u00DD', '\u00DE', '\u00DF', '\u00E0', '\u00E1',                  // t th d dh n
            '\u00E2', '\u00E3', '\u00E4', '\u00E5', '\u00E6',                  // p ph b bh m
            '\u00E7', '\u00E8', '\u00E9', '\u00EA', '\u00ED', '\u00EE', '\u00EF', // y r l v s h l underdot
        };
    }
}
