using System;
using System.Text;
using System.Text.RegularExpressions;
using CST.Conversion;

namespace CST.Avalonia.Services.Ai;

/// <summary>
/// Turns what the WebView reports into something the rest of surface B can use. (#581, AI_SURFACE_B.md §3.1)
///
/// <para><b>The selection is the one bundler input genuinely scraped from the DOM.</b> Everything else comes
/// from the tool layer, which owns its formats; this arrives in the user's display script, through a
/// <c>document.title</c> round trip, with a timeout. So it gets the handling that deserves.</para>
///
/// <para><b>Normalization only, since #649.</b> This class used to carry a second phase — asking whether the
/// normalized selection appeared in the passage window — because the window was built from scroll position and
/// might not contain it. The window is now built around the selection, so the question has no answer to give
/// and both it and the punctuation folding it needed are gone. Locating a selection in the corpus is
/// <see cref="CST.Search.TeiPassageReader.LocateSelection"/>'s job, against the raw XML.</para>
/// </summary>
public static class SelectionPipeline
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Convert to Latin, compose, and collapse whitespace — the form every downstream comparison and lookup
    /// assumes. Returns null when there is nothing usable.
    ///
    /// <para><b>Conversion is not a refinement.</b> A Burmese- or Devanagari-script reader selects text in that
    /// script; unconverted, every dictionary and lemma lookup misses and the two presets that differentiate
    /// this app work only for Latin-script readers — the opposite of the user who prompted the feature.</para>
    ///
    /// <para><b>Composition (NFC) is cheap insurance against a real failure.</b> `ScriptConverter` emits
    /// composed Latin (ā is U+0101), and the passage text comes through the same converter, so in the ordinary
    /// path both sides already agree. But a decomposed selection — a different input path, an OS that
    /// normalizes on copy — is <i>ordinally unequal</i> to the composed form (a + U+0304 ≠ U+0101), and the
    /// symptom would be a dictionary or lemma lookup silently missing on text the reader is looking straight
    /// at — a false negative from the one component whose job is faithful handling of what they selected.</para>
    /// </summary>
    public static string? Normalize(string? raw, Script sourceScript)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var latin = sourceScript == Script.Latin ? raw : ScriptConverter.Convert(raw, sourceScript, Script.Latin);

        // Latin letters with Devanāgarī punctuation is not a script. (#942)
        //
        // ScriptConverter.Convert converts LETTERS; the daṇḍa rules are written against <p rend> and need
        // markup a selection does not have. So a reader in Devanāgarī sent "।" and "॥" to the model inside
        // otherwise-Latin text. This is the same fallback TeiText.Convert applies to passage text that has
        // lost its tags, now shared rather than copied.
        //
        // ONLY reachable for a non-Latin reading script. A Latin reader's selection is copied from what the
        // reader itself rendered, through ConvertBook, which does run the markup rules - so their gāthā
        // already reads ";" and passes through here untouched.
        //
        // Both marks flatten to a period, which loses the gāthā distinction the window keeps. [fsnow] made
        // that call: "I am fine with both single and double danda being converted to period in this case. I
        // don't think it will materially affect the LLMs ability to understand the text. It's better than
        // non-Latin punctuation coming through, which could potentially confuse."
        latin = ScriptConverter.LatinizeDandas(latin);

        // Compose BEFORE collapsing whitespace: a combining mark is not whitespace, but normalizing after a
        // regex pass means the regex ran over a different string than the one that ships.
        latin = latin.Normalize(NormalizationForm.FormC);
        latin = Whitespace.Replace(latin, " ").Trim();

        return latin.Length == 0 ? null : latin;
    }
}
