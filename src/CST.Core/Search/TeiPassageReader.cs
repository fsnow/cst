using System;
using System.Collections.Generic;
using System.Text;
using CST.Conversion;

namespace CST.Search
{
    /// <summary>
    /// Reads a bounded, paged "reading window" from a book — the level-2 zoom above a snippet. From a start
    /// position it returns up to <c>maxChars</c> of rendered text (tags transparent; footnotes/paranum
    /// stripped by default), snapped at BOTH ends to sentence boundaries so it never cuts mid-sentence,
    /// romanized to the requested script — plus prev/next cursors (character positions) to page through the
    /// surrounding text, and the citation refs at the start. A wall-of-text paragraph just becomes page 1 of N.
    /// When <paramref name="snapStartToSentence"/> is set (a cursor pointing AT a hit, which lands mid-sentence),
    /// the window START is pulled back to the enclosing sentence's start so the hit is read with its governing
    /// clause, not from the hit itself.
    /// </summary>
    public static class TeiPassageReader
    {
        public static PassageWindow ReadWindow(
            string xml, int startPos, int maxChars, bool includeVariants, Script outputScript, BookMarkers markers,
            bool snapStartToSentence = false, bool structuredNotes = false)
        {
            startPos = Math.Clamp(startPos, 0, xml.Length);
            if (maxChars < 1) maxChars = 1;

            // A cursor from `occurrences` points at the hit (mid-sentence); pull the start back to the enclosing
            // sentence start so the reader gets the governing clause, not a headless predicate. Bounded by the
            // enclosing paragraph so it can't bleed into the previous paragraph, and only applied if the
            // sentence-aligned window still reaches past the cursor — otherwise a hard-capped over-long-sentence
            // paging cursor would snap back onto itself and loop. (Desktop MCP friction report, P1)
            int readStart = startPos;
            if (snapStartToSentence)
            {
                int floor = EnclosingParagraphStart(startPos, markers);
                // Note-aware like the snippet extractor's sentence scans: a danda INSIDE a <note> is apparatus
                // punctuation, not a base-text sentence boundary, so snapping to it would land the window start
                // mid-note. (#310 A4-2)
                var snapNotes = TeiText.NoteRegions(xml, floor, startPos);
                int candidate = SnapBackToSentenceStart(xml, startPos, floor, snapNotes);
                if (candidate < startPos
                    && WalkForward(xml, candidate, maxChars, includeVariants, xml.Length) > startPos)
                    readStart = candidate;
            }

            // When returning structured notes, size the window with notes SKIPPED (never entered), so the end
            // (and thus nextCursor) can't land mid-note and leave an unmatched brace / apparatus in the clean
            // base text — even if includeFootnotes is also set. (#267 review, Defect 2)
            int end = WalkForward(xml, readStart, maxChars, includeNotes: includeVariants && !structuredNotes, xml.Length);

            return Materialize(xml, readStart, end, maxChars, includeVariants, outputScript, markers, structuredNotes);
        }

        /// <summary>
        /// Render a raw span into a <see cref="PassageWindow"/>: the text in the requested script, the paging
        /// cursors, the citation refs and the apparatus. Shared by both entry points so a window built from a
        /// paragraph and one built around a selection cannot describe themselves differently.
        /// </summary>
        private static PassageWindow Materialize(
            string xml, int readStart, int end, int maxChars, bool includeVariants, Script outputScript,
            BookMarkers markers, bool structuredNotes)
        {
            IReadOnlyList<ApparatusNote> notes = System.Array.Empty<ApparatusNote>();
            string text;
            if (structuredNotes)
            {
                // Render WITH brace delimiters through the SAME convert/collapse/trim pipeline, then split the
                // {reading (sigla)} spans out into structured notes and return brace-free text. The note text is
                // therefore already in the output script and offsets index the returned brace-free text. (#267)
                string braced = TeiText.Collapse(
                    TeiText.Convert(TeiText.Clean(xml, readStart, end, includeNotes: true), outputScript)).Trim();
                (text, notes) = SplitBracedNotes(braced);
            }
            else
            {
                text = TeiText.Collapse(
                    TeiText.Convert(TeiText.Clean(xml, readStart, end, includeVariants), outputScript)).Trim();
            }

            int? next = end < xml.Length ? end : (int?)null;
            int prevStart = WalkBackward(xml, readStart, maxChars, 0);
            int? prev = prevStart < readStart ? prevStart : (int?)null;

            // Apparatus notes ({…}) in this window — counted from the raw XML regardless of includeVariants, so a
            // caller knows whether apparatus exists here without a second call. (#293) Count notes INTERSECTING the
            // window (including one opened before readStart), not just those starting in it. (#310 A4-15)
            int paraStart = EnclosingParagraphStart(readStart, markers);
            int noteCount = TeiText.CountNotesIntersecting(xml, paraStart, readStart, end);
            var (num, code, _) = markers.RefsAt(readStart);   // pages come from PagesAcross below

            // EVERY page this window covers, not just the one it opens on. A window that crosses a page break
            // sits on two printed pages, and reporting only the first made /v1/passage disagree with
            // /v1/occurrences about the same text: occurrences reports the page at the HIT, which can be the
            // second one. Same text, two citations, and nothing in either response admitting it. (#561)
            //
            // The first entry is unchanged, so a window that does not cross a break reports exactly what it
            // did before. This is the same reasoning as the end-paragraph fields below: a window describes its
            // EXTENT, and a caller citing from its start alone understates it.
            var pages = markers.PagesAcross(readStart, end);

            // The reference in effect at the window's END. Without this a caller can only say where the window
            // STARTED, so a character budget that runs on through many paragraphs is indistinguishable from one
            // that covered exactly the paragraph asked for — and any citation built from the start alone
            // understates the window's real extent. (#602)
            //
            // `end` is exclusive, so probe the last character actually included: at a paragraph boundary, `end`
            // itself already sits in the NEXT paragraph and would overstate the span by one.
            var (endNum, endCode, _) = markers.RefsAt(Math.Max(readStart, end - 1));

            return new PassageWindow(text, prev, next, num, code, pages, noteCount, notes, endNum, endCode);
        }

        /// <summary>
        /// A reading window built AROUND a selection, so the selection is always inside it. (#649)
        ///
        /// <para><b>Why this exists at all.</b> The window used to be fetched from the reader's paragraph,
        /// which is derived from SCROLL position — so a selection near the bottom of the viewport routinely
        /// fell outside the window built to explain it, and the app went on to report that as a caveat. A
        /// context that can fail to contain the thing it is context FOR is not a context; it is a coincidence
        /// that usually holds. There is deliberately no longer any way to express "the selection was not
        /// found in the window", because with the window built from the selection there is no such state.</para>
        ///
        /// <para><b>The rule.</b> If the selection is already at or over budget, it IS the window — never
        /// trimmed, because the subject of the request must not be cut to make room for its own context.
        /// Otherwise the shortfall is split: roughly half is spent expanding backwards, the rest forwards.
        /// Whatever the backward side cannot spend — because it hit the start of the section — is handed to
        /// the forward side rather than lost, so a selection at the top of a sutta still gets a full budget's
        /// worth of context, all of it below.</para>
        ///
        /// <para><b>Neither direction crosses a <c>&lt;div&gt;</c>.</b> A div boundary separates one section
        /// from the next, and text from the next section is not this passage's context however close it sits
        /// in the file. Where a book carries no div markup the whole document is the bound — see
        /// <see cref="BookMarkers.EnclosingDivRange"/>.</para>
        /// </summary>
        /// <param name="selectionStart">Start of the selection, as a character position in the raw XML.</param>
        /// <param name="selectionEnd">End of the selection, exclusive.</param>
        public static PassageWindow ReadWindowAroundSelection(
            string xml, int selectionStart, int selectionEnd, int maxChars, bool includeVariants,
            Script outputScript, BookMarkers markers, bool structuredNotes = false)
        {
            selectionStart = Math.Clamp(selectionStart, 0, xml.Length);
            selectionEnd = Math.Clamp(selectionEnd, selectionStart, xml.Length);
            if (maxChars < 1) maxChars = 1;

            // Each direction is bounded by the section at ITS OWN end of the selection, not by one section
            // chosen for the whole window.
            //
            // A selection may legitimately cross a div boundary — the reader dragged across it, so both sides
            // are what they are asking about, and the window must carry all of it. What must not cross is the
            // EXPANSION: text beyond the selection on the far side of a boundary is a different section and
            // is not this passage's context. So the backward floor comes from the div holding the selection's
            // start, and the forward ceiling from the div holding its end. For a selection inside one section
            // these are the same div and this is the ordinary case; for one that spans two, each side expands
            // within the section it actually reaches into.
            int sectionStart = markers.EnclosingDivRange(selectionStart).Start;
            int sectionEnd = markers.EnclosingDivRange(Math.Max(selectionStart, selectionEnd - 1)).End;

            // The selection itself is never bounded away, whatever it spans.
            sectionStart = Math.Min(sectionStart, selectionStart);
            sectionEnd = Math.Max(sectionEnd, selectionEnd);

            bool includeNotes = includeVariants && !structuredNotes;

            int start = selectionStart;
            int end = selectionEnd;

            int selectionLength = RenderedLength(xml, selectionStart, selectionEnd, includeNotes);
            if (selectionLength < maxChars)
            {
                int shortfall = maxChars - selectionLength;
                int half = shortfall / 2;

                // Backwards first, so what it cannot spend is known before the forward budget is set.
                //
                // Snapped OUTWARDS, to the start of the sentence the budget landed inside — not forwards to
                // the next one. WalkBackward snaps forward, which is right for a paging cursor and wrong
                // here: it discards the partial sentence, so a half-budget reliably bought one sentence less
                // than it paid for and the window came out lopsided towards what follows the selection.
                //
                // The two directions are NOT symmetric and it is worth not pretending otherwise: forward
                // extends past its budget only once the budget is spent, backward always snaps — so a
                // selection one character under budget gets a preceding sentence and one exactly at budget
                // gets none. A discontinuity at the edge, accepted because the alternative is cutting the
                // sentence the selection sits in.
                // The snap gets the SAME hard cap the forward walk has (1.5x its budget), and for the same
                // reason. Unbounded, it walks to the previous danda however far away that is: a selection in
                // the danda-free front matter of a book with no div markup snapped to position 0, and a
                // 40-character budget produced a 2,000-character window. Bounding the floor rather than
                // rejecting the snap keeps the behaviour it was added for — finishing the sentence the budget
                // landed inside — while making the total window actually bounded.
                int floor = RawBackward(xml, selectionStart, half + half / 2, sectionStart);
                int rough = RawBackward(xml, selectionStart, half, sectionStart);
                var boundaryNotes = TeiText.NoteRegions(xml, floor, selectionStart);
                start = SnapBackToSentenceStart(xml, rough, floor, boundaryNotes);

                int spentBack = RenderedLength(xml, start, selectionStart, includeNotes);

                // Clamped at zero: snapping outwards can spend more than half.
                int forwardBudget = Math.Max(0, shortfall - spentBack);
                end = WalkForward(xml, selectionEnd, forwardBudget, includeNotes, sectionEnd);
            }

            // Finish the sentence the window ends inside, whatever the budget had left.
            //
            // WalkForward's hard cap is 1.5x ITS budget, so a budget of zero caps at zero and it returns after
            // a single character — cutting mid-sentence, which is the one thing this class promises never to
            // do. That is reachable whenever the backward snap spends the whole shortfall, and it is exactly
            // the case where the reader most needs the sentence intact, because the selection is the subject.
            end = ExtendToSentenceEnd(xml, end, sectionEnd, includeNotes);

            {
            }

            return Materialize(xml, start, end, maxChars, includeVariants, outputScript, markers, structuredNotes);
        }

        /// <summary>
        /// Find a selection's raw span inside a bounded region of the XML, or null. (#649)
        ///
        /// <para><b>Matched against the RAW XML, not against rendered text.</b> The obvious approach — render
        /// the region, find the selection's offset in it, map the offset back — cannot work: the renderer
        /// converts script (one Devanagari character becomes several Latin ones), inserts a space for each
        /// stripped tag, deletes a space before close punctuation, then collapses and trims. Rendered offsets
        /// are not any function of raw offsets that counting can invert. Walking the raw text and skipping
        /// what the renderer skips yields the raw span directly, with no arithmetic to get wrong.</para>
        ///
        /// <para><b>The needle must already be in the XML's own script.</b> Callers hold the selection in
        /// Latin; converting it here would need a script this class does not know.</para>
        ///
        /// <para>Whitespace runs on either side compare equal to a single space, because the selection came
        /// through the DOM and the XML is line-wrapped. Bounded to one region by the caller so a formulaic
        /// phrase — this corpus repeats them verbatim across books — cannot match somewhere the reader has
        /// never been.</para>
        /// </summary>
        public static (int Start, int End)? LocateSelection(string xml, int from, int to, string needle)
        {
            from = Math.Clamp(from, 0, xml.Length);
            to = Math.Clamp(to, from, xml.Length);
            if (string.IsNullOrWhiteSpace(needle)) return null;

            // Collapse the needle the same way the comparison collapses the haystack.
            var wanted = new StringBuilder(needle.Length);
            foreach (var c in needle)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (wanted.Length > 0 && wanted[^1] != ' ') wanted.Append(' ');
                }
                else wanted.Append(c);
            }
            var target = wanted.ToString().Trim();
            if (target.Length == 0) return null;

            for (int anchor = from; anchor < to; anchor++)
            {
                if (xml[anchor] == '<') continue;
                if (char.IsWhiteSpace(xml[anchor])) continue;

                int matched = 0, i = anchor, lastConsumed = anchor;
                bool pendingSpace = false;

                while (i < to && matched < target.Length)
                {
                    char c = xml[i];

                    if (c == '<')
                    {
                        int gt = xml.IndexOf('>', i);
                        if (gt < 0 || gt >= to) break;
                        string tag = xml.Substring(i, gt - i + 1);
                        string name = TeiText.TagName(tag);
                        // Skip exactly what the renderer drops, so a note or a paragraph number sitting in the
                        // middle of the reader's selection does not break the match.
                        if ((name == "note" || (name == "hi" && TeiText.IsStructuralHi(tag)))
                            && !tag.EndsWith("/>", StringComparison.Ordinal)
                            && !tag.StartsWith("</", StringComparison.Ordinal))
                            i = TeiText.SkipSubtree(xml, gt + 1, name, to);
                        else
                            i = gt + 1;
                        continue;
                    }

                    if (char.IsWhiteSpace(c)) { pendingSpace = true; i++; continue; }

                    if (pendingSpace)
                    {
                        pendingSpace = false;
                        if (target[matched] == ' ') matched++;
                        else break;
                        if (matched == target.Length) { lastConsumed = i - 1; break; }
                    }

                    if (target[matched] != c) break;
                    matched++;
                    lastConsumed = i;
                    i++;
                }

                if (matched == target.Length) return (anchor, lastConsumed + 1);
            }

            return null;
        }

        /// <summary>
        /// Walk on to the end of the sentence in progress, ignoring any budget. Bounded by the section and by
        /// a generous cap, so a text with no sentence punctuation ahead cannot run to the end of the book.
        /// </summary>
        private static int ExtendToSentenceEnd(string xml, int from, int limit, bool includeNotes)
        {
            const int Cap = 4000;

            var notes = TeiText.NoteRegions(xml, from, Math.Min(limit, from + Cap));

            int i = from, seen = 0;
            while (i < limit && seen < Cap)
            {
                char c = xml[i];
                if (c == '<')
                {
                    int gt = xml.IndexOf('>', i);
                    if (gt < 0) break;
                    string tag = xml.Substring(i, gt - i + 1);
                    string name = TeiText.TagName(tag);
                    if (name == "note" && !includeNotes && !tag.EndsWith("/>", StringComparison.Ordinal))
                        i = tag.StartsWith("</", StringComparison.Ordinal)
                            ? gt + 1
                            : TeiText.SkipSubtree(xml, gt + 1, "note", limit);
                    else i = gt + 1;
                    continue;
                }

                // A danda inside a note is apparatus punctuation, not a base-text sentence end. (#310 A4-2)
                if (TeiText.IsBoundary(c) && !TeiText.InNote(i, notes)) return i + 1;
                i++;
                seen++;
            }

            return Math.Min(i, limit);
        }

        /// <summary>
        /// Rendered characters between two raw positions — tags free, stripped subtrees free. The same
        /// accounting <see cref="WalkForward"/> spends its budget in, so "half the shortfall" means the same
        /// thing to the measurement and to the walk.
        /// </summary>
        private static int RenderedLength(string xml, int from, int to, bool includeNotes)
        {
            if (to <= from) return 0;

            int i = from, rendered = 0;
            while (i < to)
            {
                char c = xml[i];
                if (c == '<')
                {
                    int gt = xml.IndexOf('>', i);
                    if (gt < 0 || gt >= to) break;
                    string tag = xml.Substring(i, gt - i + 1);
                    string name = TeiText.TagName(tag);
                    if (name == "note" && !includeNotes && !tag.EndsWith("/>", StringComparison.Ordinal))
                        i = tag.StartsWith("</", StringComparison.Ordinal)
                            ? gt + 1
                            : TeiText.SkipSubtree(xml, gt + 1, "note", to);
                    else if (name == "hi" && TeiText.IsStructuralHi(tag) && !tag.EndsWith("/>", StringComparison.Ordinal))
                        i = TeiText.SkipSubtree(xml, gt + 1, "hi", to);
                    else i = gt + 1;
                }
                else
                {
                    rendered++;
                    i++;
                }
            }

            return rendered;
        }

        // Strip the {reading (sigla)} apparatus spans out of already-rendered text into structured notes
        // (shared with the snippet path). (#267)
        private static (string Text, IReadOnlyList<ApparatusNote> Notes) SplitBracedNotes(string braced)
        {
            var (text, notes, _) = TeiText.SplitApparatus(braced);
            return (text, notes);
        }

        // The nearest sentence start at or after <paramref name="minStart"/> and at/before <paramref name="startPos"/>
        // — i.e. just past the closest preceding sentence boundary, without crossing minStart. Tags are skipped.
        private static int SnapBackToSentenceStart(string xml, int startPos, int minStart, List<(int s, int e)> notes)
        {
            if (minStart < 0) minStart = 0;
            int i = startPos - 1;
            while (i >= minStart)
            {
                char c = xml[i];
                if (c == '>')
                {
                    int lt = xml.LastIndexOf('<', i);
                    i = lt >= minStart ? lt - 1 : minStart - 1;
                    continue;
                }
                // begin just past the sentence-ending danda — but a danda inside a note is apparatus, not a
                // base-text boundary. (#310 A4-2)
                if (TeiText.IsBoundary(c) && !TeiText.InNote(i, notes)) return i + 1;
                i--;
            }
            return minStart;
        }

        // Start position of the paragraph enclosing <paramref name="startPos"/> (the backward-snap floor), or 0.
        private static int EnclosingParagraphStart(int startPos, BookMarkers markers)
        {
            var (num, code, _) = markers.RefsAt(startPos);
            if (num is int n)
            {
                int p = markers.PositionOfParagraph(n, code);
                if (p >= 0 && p <= startPos) return p;
            }
            return 0;
        }

        // Raw end position after accumulating ~maxChars rendered chars, then extending to the next sentence
        // boundary (capped) so we never cut mid-sentence. Tags and stripped subtrees cost zero budget.
        private static int WalkForward(string xml, int start, int maxChars, bool includeNotes, int limit)
        {
            // long: an unclamped client maxChars (e.g. int.MaxValue) would overflow `maxChars + maxChars/2`
            // negative, tripping `rendered >= hardCap` on the first char. (#313 A4-13; endpoint also clamps, #305)
            int i = start, rendered = 0;
            long hardCap = (long)maxChars + maxChars / 2;
            bool budgetReached = false;
            while (i < limit)
            {
                char c = xml[i];
                if (c == '<')
                {
                    int gt = xml.IndexOf('>', i);
                    if (gt < 0) break;
                    string tag = xml.Substring(i, gt - i + 1);
                    string name = TeiText.TagName(tag);
                    if (name == "note" && !includeNotes && !tag.EndsWith("/>", StringComparison.Ordinal))
                        // Open <note> strips its subtree; a lone </note> (walk began inside a note) is zero-width,
                        // never a subtree — else SkipSubtree jumps to the next </note>, silently skipping text. (#310 A4-2)
                        i = tag.StartsWith("</", StringComparison.Ordinal)
                            ? gt + 1
                            : TeiText.SkipSubtree(xml, gt + 1, "note", limit);
                    else if (name == "hi" && TeiText.IsStructuralHi(tag) && !tag.EndsWith("/>", StringComparison.Ordinal))
                        i = TeiText.SkipSubtree(xml, gt + 1, "hi", limit);
                    else i = gt + 1;
                }
                else
                {
                    if (budgetReached && TeiText.IsBoundary(c)) return i + 1;   // stop just past a sentence end
                    rendered++;
                    i++;
                    if (rendered >= maxChars) budgetReached = true;
                    if (rendered >= hardCap) return i;                          // no boundary found: hard cap
                }
            }
            return i;
        }

        /// <summary>
        /// Raw position ~<paramref name="maxChars"/> rendered characters before <paramref name="start"/>,
        /// with no sentence snapping — the caller decides which way to snap. Tags are treated as zero-width
        /// backwards, which is approximate and good enough for choosing where to begin looking.
        /// </summary>
        private static int RawBackward(string xml, int start, int maxChars, int limit)
        {
            int i = start - 1, rendered = 0;
            while (i >= limit && rendered < maxChars)
            {
                if (xml[i] == '>')
                {
                    int lt = xml.LastIndexOf('<', i);
                    i = lt >= limit ? lt - 1 : limit - 1;
                }
                else { rendered++; i--; }
            }
            return Math.Max(i + 1, limit);
        }

        // Start position ~maxChars rendered chars before <paramref name="start"/>, snapped forward to a
        // sentence start. Approximate (tags treated as zero-width backward); good enough for a page cursor.
        private static int WalkBackward(string xml, int start, int maxChars, int limit)
        {
            int i = start - 1, rendered = 0;
            while (i >= limit && rendered < maxChars)
            {
                if (xml[i] == '>')
                {
                    int lt = xml.LastIndexOf('<', i);
                    i = lt >= limit ? lt - 1 : limit - 1;
                }
                else { rendered++; i--; }
            }
            int from = Math.Max(i, limit);
            var notes = TeiText.NoteRegions(xml, from, start);
            for (int j = from; j < start; j++)
                if (TeiText.IsBoundary(xml[j]) && !TeiText.InNote(j, notes)) return j + 1;   // sentence start (note-aware, #310)
            // No boundary found: the raw fallback can land mid-note, so the previous page would render that note's
            // tail as base text (and Clean from mid-note emits an unbalanced brace). Snap out to the enclosing note's
            // start so the cursor sits on a clean (base-text) boundary. (#355)
            int fallback = Math.Max(i + 1, limit);
            foreach (var (s, e) in notes)
                if (fallback > s && fallback < e) { fallback = Math.Max(s, limit); break; }
            return fallback;
        }
    }

    /// <summary>A reading window: the text plus page cursors and the citation refs at its start.</summary>
    public sealed record PassageWindow(
        string Text,
        int? PrevCursor,
        int? NextCursor,
        int? ParagraphNumber,
        string? ParagraphBookCode,
        IReadOnlyList<SnippetPageRef> Pages,
        int NoteCount,
        IReadOnlyList<ApparatusNote> Notes,
        int? EndParagraphNumber = null,
        string? EndParagraphBookCode = null);

    /// <summary>One apparatus note (a digitized print footnote — usually a variant reading) as structured data:
    /// its character <paramref name="Offset"/> into the returned brace-free <c>Text</c>, its full converted
    /// <paramref name="Text"/> (e.g. "anupāyinī (ka.)"), and — when it matches the simple <c>reading (sigla)</c>
    /// shape — the split <paramref name="Reading"/> and witness <paramref name="Sigla"/> (else both null). (#267)</summary>
    public sealed record ApparatusNote(int Offset, string Text, string? Reading, string? Sigla);
}
