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
    }
}
