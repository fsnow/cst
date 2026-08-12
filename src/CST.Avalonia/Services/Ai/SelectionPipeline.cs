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
/// <para><b>Two phases, because they run in different places.</b> <see cref="Normalize"/> needs the display
/// script, which only the reader knows; <see cref="IsWithin"/> needs the passage window, which only the bundler
/// has. Keeping them one class keeps the normalization rules in one place — the two sides of a comparison
/// drifting apart is exactly how a locator starts reporting false misses.</para>
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
    /// symptom would be a bundle reporting "selection not found in the passage window": a false diagnostic
    /// from the one component whose job is faithful diagnostics.</para>
    /// </summary>
    public static string? Normalize(string? raw, Script sourceScript)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var latin = sourceScript == Script.Latin ? raw : ScriptConverter.Convert(raw, sourceScript, Script.Latin);

        // Compose BEFORE collapsing whitespace: a combining mark is not whitespace, but normalizing after a
        // regex pass means the regex ran over a different string than the one that ships.
        latin = latin.Normalize(NormalizationForm.FormC);
        latin = Whitespace.Replace(latin, " ").Trim();

        return latin.Length == 0 ? null : latin;
    }

    /// <summary>
    /// Whether a normalized selection appears in a passage window.
    ///
    /// <para>The window is normalized the same way here rather than at its source, because the window is the
    /// tool layer's output and must not be silently rewritten on its way to the model — only the copy used for
    /// this comparison is.</para>
    ///
    /// <para><b>Ordinal, case-insensitively.</b> Case-insensitive because the corpus capitalises
    /// programmatically at sentence starts, so a selection lifted from mid-sentence and the same words at a
    /// sentence head differ only in case. Ordinal rather than culture-aware because a culture-aware comparison
    /// would fold characters Pāli distinguishes.</para>
    /// </summary>
    public static bool IsWithin(string normalizedSelection, string windowText)
    {
        if (string.IsNullOrEmpty(normalizedSelection) || string.IsNullOrEmpty(windowText)) return false;

        var window = Whitespace.Replace(windowText.Normalize(NormalizationForm.FormC), " ");
        return window.Contains(normalizedSelection, StringComparison.OrdinalIgnoreCase);
    }
}
