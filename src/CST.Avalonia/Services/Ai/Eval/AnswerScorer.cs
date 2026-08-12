using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CST.Avalonia.Services.Ai.Eval;

/// <summary>
/// What one answer scored. Every field is a <b>count or a list of evidence</b>, never a verdict: the harness
/// reports, a human decides. (#587)
/// </summary>
/// <param name="Quotes">Properly marked Pāli quotes.</param>
/// <param name="UnbalancedMarkers">Markers with no partner.</param>
/// <param name="UnmarkedPali">Words carrying Pāli diacritics that were left OUTSIDE the quote markers. The
/// measurement that decides whether v1.1 script conversion can be enabled for a model: a model that marks block
/// quotes but leaves inline terms bare would have its verse converted and its vocabulary left in Latin, which
/// is the worse half.</param>
/// <param name="QuotesNotInPassage">Marked quotes that do not appear in the supplied passage. The strongest
/// mechanical signal for answering from training data rather than from the text in front of it.</param>
/// <param name="UnsupportedReferences">Reference-shaped strings ("paragraph 33", "verse 12") whose number is
/// nowhere in the bundle's own citation. Evidence of an invented reference — the invented-citation hazard §6 is
/// written against.</param>
/// <param name="TerminologyLapses">Discouraged terms found, with counts. §10 is explicit that output is COUNTED,
/// never rewritten: string surgery on model prose produces worse artifacts than the term it removes.</param>
/// <param name="FailureSignatures">Per-case signatures that matched — the case set's own definition of getting
/// it wrong.</param>
public sealed record AnswerScore(
    int Quotes,
    int UnbalancedMarkers,
    IReadOnlyList<string> UnmarkedPali,
    IReadOnlyList<string> QuotesNotInPassage,
    IReadOnlyList<string> UnsupportedReferences,
    IReadOnlyList<string> TerminologyLapses,
    IReadOnlyList<string> FailureSignatures)
{
    /// <summary>True when nothing at all was flagged. Deliberately not called "passed" — see the class remarks.</summary>
    public bool Clean =>
        UnbalancedMarkers == 0
        && UnmarkedPali.Count == 0
        && QuotesNotInPassage.Count == 0
        && UnsupportedReferences.Count == 0
        && TerminologyLapses.Count == 0
        && FailureSignatures.Count == 0;
}

/// <summary>
/// Scores one model answer against the passage it was given. (#587, AI_SURFACE_B.md §13)
///
/// <para><b>It scores the RAW answer, before marker stripping.</b> The markers are the measurement; a scorer fed
/// the rendered text would be scoring text the app had already repaired.</para>
///
/// <para><b>Everything here is a heuristic, and each is chosen so its errors run in the safe direction.</b> The
/// harness's job is to surface candidates for a human to judge, not to declare a model good — a scorer trusted
/// as a verdict would quietly become the definition of fidelity, which is exactly the failure
/// <c>PALI_FIDELITY_CASES.md</c> is organized to avoid. Where a check can err, it errs toward flagging something
/// harmless rather than passing something wrong.</para>
/// </summary>
public static class AnswerScorer
{
    /// <summary>
    /// The characters romanized Pāli uses that English does not — the same set
    /// <c>LatinCapitalizer</c> keys on. A word carrying one of these in an English answer is almost
    /// certainly Pāli, which is what makes unmarked-Pāli detection mechanical at all.
    /// </summary>
    private const string PaliDiacritics = "ñṅṭḍṇḷāīūṃṁ";

    private static readonly Regex MarkedSpan = new(
        @"\[\[(?<text>.*?)\]\]", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex PaliWord = new(
        $@"\b[\p{{L}}]*[{PaliDiacritics}][\p{{L}}]*\b", RegexOptions.Compiled);

    /// <summary>
    /// Reference shapes a model invents. Deliberately narrow — it must not fire on ordinary prose containing a
    /// number, because a check that cries wolf gets switched off.
    /// </summary>
    private static readonly Regex ReferenceShape = new(
        @"\b(?:paragraph|para\.?|verse|vv?\.|page|p{1,2}\.)\s*(?<n>\d{1,4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <param name="rawAnswer">The model's answer exactly as streamed, markers intact.</param>
    /// <param name="passageText">Everything the model was given to quote from — the passage AND the print
    /// apparatus. A variant reading is legitimately quotable: the Translate preset explicitly asks the model to
    /// say which reading it followed, so scoring it as ungrounded would penalise doing what it was told. Found
    /// by a live run flagging <i>amataṃ padaṃ</i>, the sī/syā variant at Dhp 21.</param>
    /// <param name="citation">The app's own citation, whose numbers are the ones a reference may legitimately
    /// name.</param>
    /// <param name="discouragedTerms">House-terminology terms to count. Supplied as data rather than compiled
    /// in, so the list is Frank's to edit without a rebuild.</param>
    /// <param name="failureSignatures">Regexes defining this case's way of being wrong.</param>
    public static AnswerScore Score(
        string? rawAnswer,
        string passageText,
        CitationRef? citation = null,
        IEnumerable<string>? discouragedTerms = null,
        IEnumerable<string>? failureSignatures = null)
    {
        rawAnswer ??= string.Empty;

        var filter = new PaliQuoteFilter();
        filter.Feed(rawAnswer);
        filter.Flush();

        var marked = MarkedSpan.Matches(rawAnswer).Select(m => m.Groups["text"].Value.Trim()).ToList();

        return new AnswerScore(
            filter.Quotes,
            filter.UnbalancedMarkers,
            FindUnmarkedPali(rawAnswer),
            FindUngroundedQuotes(marked, passageText),
            FindUnsupportedReferences(rawAnswer, citation),
            CountTerms(rawAnswer, discouragedTerms),
            MatchSignatures(rawAnswer, failureSignatures));
    }

    /// <summary>
    /// Everything in a bundle the model may legitimately quote: the passage plus every apparatus note. Used in
    /// place of the passage text alone so a cited variant reading is not scored as invention.
    /// </summary>
    public static string QuotableText(AiContextBundle bundle) =>
        string.Join("\n", new[] { bundle.Passage.Text }
            .Concat(bundle.Passage.Notes.Select(n => n.Text))
            .Concat(bundle.Passage.Notes.Select(n => n.Reading ?? string.Empty))
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>
    /// Pāli words left outside the markers.
    ///
    /// <para>Marked spans are <b>removed entirely</b> first — not merely unwrapped — because after stripping,
    /// correctly marked Pāli is indistinguishable from unmarked Pāli, and a scorer that looked at the rendered
    /// text would report every well-behaved model as failing.</para>
    /// </summary>
    internal static IReadOnlyList<string> FindUnmarkedPali(string rawAnswer)
    {
        var outsideMarkers = MarkedSpan.Replace(rawAnswer, " ");

        return PaliWord.Matches(outsideMarkers)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(w => w, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Marked quotes absent from the supplied passage — the model quoting Pāli it was not given.
    ///
    /// <para>Single words are skipped. A model legitimately names a stem it is discussing (<i>appamāda</i> for
    /// the inflected <i>appamādo</i> in the text), and flagging that would bury the signal that matters: a
    /// multi-word quotation the passage does not contain, which the model can only have got from memory.</para>
    /// </summary>
    internal static IReadOnlyList<string> FindUngroundedQuotes(IEnumerable<string> markedQuotes, string passageText)
    {
        var window = Normalize(passageText);

        return markedQuotes
            .Select(Normalize)
            .Where(q => q.Length > 0 && q.Contains(' '))
            .Where(q => !window.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Reference-shaped strings the app's own citation does not support.
    ///
    /// <para>Compared by NUMBER rather than by exact phrasing: the model writes "verse 33" where the citation
    /// says "paragraph 33", and both name the same thing. What matters is whether a number appears that the
    /// bundle never supplied.</para>
    /// </summary>
    internal static IReadOnlyList<string> FindUnsupportedReferences(string rawAnswer, CitationRef? citation)
    {
        if (citation is null) return Array.Empty<string>();

        var supported = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in ReferenceShape.Matches(citation.NormalizedReference))
            supported.Add(m.Groups["n"].Value);
        foreach (var page in citation.Pages)
        {
            supported.Add(page.Number.ToString());
            if (page.Volume > 0) supported.Add(page.Volume.ToString());
        }

        // A range in the citation ("paragraphs 21-45") legitimises everything between its endpoints.
        foreach (Match m in Regex.Matches(citation.NormalizedReference, @"(\d{1,4})\s*[-–]\s*(\d{1,4})"))
        {
            if (int.TryParse(m.Groups[1].Value, out var from) && int.TryParse(m.Groups[2].Value, out var to))
                for (var n = from; n <= to && n - from < 5000; n++) supported.Add(n.ToString());
        }

        return ReferenceShape.Matches(rawAnswer)
            .Where(m => !supported.Contains(m.Groups["n"].Value))
            .Select(m => m.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Counts discouraged terms. Counts — never rewrites; §10 is explicit about why.</summary>
    internal static IReadOnlyList<string> CountTerms(string rawAnswer, IEnumerable<string>? discouragedTerms)
    {
        if (discouragedTerms is null) return Array.Empty<string>();

        var lapses = new List<string>();
        foreach (var term in discouragedTerms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;

            // Word-boundary matched so a term does not fire inside a longer word, and case-insensitive because
            // a sentence-initial occurrence is the same lapse.
            var count = Regex.Matches(rawAnswer, $@"\b{Regex.Escape(term.Trim())}\w*\b", RegexOptions.IgnoreCase)
                .Count;
            if (count > 0) lapses.Add($"{term.Trim()} ({count})");
        }
        return lapses;
    }

    /// <summary>A case's own definition of being wrong. An invalid pattern is reported, never swallowed.</summary>
    internal static IReadOnlyList<string> MatchSignatures(string rawAnswer, IEnumerable<string>? signatures)
    {
        if (signatures is null) return Array.Empty<string>();

        var matched = new List<string>();
        foreach (var pattern in signatures)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            try
            {
                if (Regex.IsMatch(rawAnswer, pattern, RegexOptions.IgnoreCase)) matched.Add(pattern);
            }
            catch (ArgumentException)
            {
                // A broken pattern must be visible: silently skipping it turns a case into one that can never
                // fail, which looks exactly like a case a model always passes.
                matched.Add($"[invalid pattern] {pattern}");
            }
        }
        return matched;
    }

    private static string Normalize(string text) =>
        Whitespace.Replace(text.Normalize(System.Text.NormalizationForm.FormC), " ").Trim();
}
