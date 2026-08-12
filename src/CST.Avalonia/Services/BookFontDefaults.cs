using System.Collections.Generic;
using CST.Conversion;

namespace CST.Avalonia.Services;

/// <summary>
/// The shipped book font face for each script. (#42)
///
/// <para>
/// These strings used to be the ONLY difference between the fourteen <c>tipitaka-*.xsl</c> files — #574
/// made every other byte identical and verified it. With the face now injected at transform time there is
/// one stylesheet, and these live here as data.
/// </para>
///
/// <para>
/// They are CSS font stacks, not single names: the first installed face wins, so each carries its own
/// fallbacks. They are also old — chosen against the fonts Windows shipped roughly twenty years ago — so
/// treat them as a starting point rather than a recommendation. That is precisely why #42 makes the face
/// user-settable, and why #573 wants to report when a chosen face is not installed.
/// </para>
/// </summary>
public static class BookFontDefaults
{
    private static readonly Dictionary<Script, string> Faces = new()
    {
        [Script.Latin]      = "\"Times Ext Roman\", \"Indic Times\", \"Doulos SIL\", Tahoma, \"Arial Unicode MS\", Gentium",
        [Script.Devanagari] = "CDAC-GISTSurekh, CDAC-GISTYogesh, Mangal",
        [Script.Bengali]    = "\"BN BIDISHA Opentype\", Vrinda",
        // Cyrillic has its own entry and its own picker, from tipitaka-cyrl.xsl. Note this is a live
        // behaviour change: the old script-to-stylesheet switch had no Cyrillic case, so Cyrillic books
        // fell through to the Latin stylesheet and cyrl's was never used. That fallthrough was reasonable
        // - a font carrying the extended Latin characters Pali needs usually carries full Cyrillic too -
        // but it also meant Cyrillic could not be chosen independently, which #42 now allows.
        [Script.Cyrillic]   = "\"Doulos SIL\", \"Times New Roman\", Tahoma",
        [Script.Gujarati]   = "Shruti, \"Arial Unicode MS\"",
        [Script.Gurmukhi]   = "Raavi",
        [Script.Kannada]    = "Tunga, \"Arial Unicode MS\"",
        [Script.Khmer]      = "\"Khmer Mondulkiri U OT ls\", KhmerOS",
        [Script.Malayalam]  = "Kartika",
        [Script.Myanmar]    = "Padauk,Tharlon,Myanmar1",
        [Script.Sinhala]    = "UN-Abhaya,KaputaUnicode",
        [Script.Telugu]     = "Gautami",
        [Script.Thai]       = "CordaliaUPC",
        [Script.Tibetan]    = "\"Tibetan Machine Uni\"",
    };

    /// <summary>
    /// The shipped face stack for a script. Falls back to the Latin stack for a script with no entry,
    /// which is a reasonable last resort rather than a correct answer — but better than an empty
    /// font-family, which would leave the browser to pick with no guidance at all.
    /// </summary>
    public static string For(Script script) =>
        Faces.TryGetValue(script, out var face) ? face : Faces[Script.Latin];

    /// <summary>True when a shipped default exists for this script.</summary>
    public static bool Has(Script script) => Faces.ContainsKey(script);

    /// <summary>Every script that has a shipped default, for tests and for the Settings list.</summary>
    public static IReadOnlyDictionary<Script, string> All => Faces;
}
