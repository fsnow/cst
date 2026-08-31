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

            // A cursor can point INTO a note — note text is indexed, so such cursors are real — and a window
            // that opens inside one renders the note's tail as base text: Clean meets only the closing tag,
            // which is zero-width (#310), so the variant reading and its sigla arrive undelimited. With
            // structuredNotes the same start leaves an unmatched brace in the text that is documented as
            // clean and quotable, because SplitBracedNotes never saw the opening one.
            //
            // The snippet extractor has always nudged its bounds out of note regions for exactly this
            // reason; the passage start never did, and the sentence snap above cannot stand in for it — it
            // is optional, and it declines whenever the base text before the note outruns the budget. (#913)
            int noteFloor = EnclosingParagraphStart(readStart, markers);
            foreach (var (noteStart, noteEnd) in TeiText.NoteRegions(xml, noteFloor, readStart + 1))
            {
                if (readStart < noteStart || readStart >= noteEnd) continue;

                // The note's START, so what the cursor pointed at is still in the window and is rendered AS
                // apparatus. Its END only when opening at the start cannot reach past the cursor: a paging
                // cursor that moved backwards would re-read what it just returned and never advance, the
                // same loop the sentence snap guards against. The end always advances, since the cursor is
                // inside the note.
                // The note's end also when the note does not FIT: WalkForward's boundary check is the one in
                // this file that is not note-aware, so with the apparatus rendered inline it can stop just
                // past a danda inside the note. The window would then open at `<note>` and close before
                // `</note>`, and Clean would emit an opening brace with nothing to close it — the same class
                // of malformation as the tail this nudge exists to prevent, mirrored. Requiring the whole
                // note to fit is narrower than making that check note-aware, which would move where every
                // window ends. (ultrareview; the check itself is #917)
                int fromNoteStart = WalkForward(xml, noteStart, maxChars, includeVariants && !structuredNotes, xml.Length);
                readStart = fromNoteStart > startPos && fromNoteStart >= noteEnd ? noteStart : noteEnd;
                break;
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
            BookMarkers markers, bool structuredNotes,
            bool selectionTruncated = false)
        {
            IReadOnlyList<ApparatusNote> notes = System.Array.Empty<ApparatusNote>();
            string text;
            if (structuredNotes)
            {
                // Render WITH brace delimiters through the SAME convert/collapse/trim pipeline, then split the
                // {reading (sigla)} spans out into structured notes and return brace-free text. The note text is
                // therefore already in the output script and offsets index the returned brace-free text. (#267)
                string braced = TeiText.Collapse(
                    TeiText.Convert(TeiText.Clean(xml, readStart, end, includeNotes: true, outputScript), outputScript)).Trim();
                (text, notes) = SplitBracedNotes(braced);
            }
            else
            {
                text = TeiText.Collapse(
                    TeiText.Convert(TeiText.Clean(xml, readStart, end, includeVariants, outputScript), outputScript)).Trim();
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

            // Whether naming the two ends as a range describes what is actually between them. Asked of the
            // markers positionally, over the same span the window renders, because the numbers alone cannot
            // tell a straight run from one that crosses a restart. (#914)
            bool contiguous = markers.ParagraphsRunContiguously(readStart, Math.Max(readStart, end - 1));

            return new PassageWindow(text, prev, next, num, code, pages, noteCount, notes, endNum, endCode,
                selectionTruncated, contiguous);
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
        /// <param name="contextSentences">How many sentences to expand by on each side. (#672)</param>
        /// <param name="selectionCap">Absolute bound on the selection itself, so a select-all cannot send a
        /// whole book. Deliberately NOT <paramref name="maxChars"/>, which is the caller's requested window
        /// size: a client asking for a small window is asking for less context, not for its own selection to
        /// be cut. (#672)</param>
        public static PassageWindow ReadWindowAroundSelection(
            string xml, int selectionStart, int selectionEnd, int maxChars, bool includeVariants,
            Script outputScript, BookMarkers markers, bool structuredNotes = false,
            int contextSentences = DefaultContextSentences, int selectionCap = MaxSelectionChars)
        {
            selectionStart = Math.Clamp(selectionStart, 0, xml.Length);
            selectionEnd = Math.Clamp(selectionEnd, selectionStart, xml.Length);
            if (maxChars < 1) maxChars = 1;
            if (contextSentences < 0) contextSentences = 0;

            // The SELECTION is capped too, which it never used to be. (#672)
            //
            // The rule that the subject of a request is never trimmed to make room for its own context is
            // still right, and it is not what this changes: expansion is what yields. But the selection was
            // unbounded in absolute terms as well, so a select-all sent an entire book verbatim — which is
            // neither a passage nor a question anyone meant to ask. Capping it at the same limit that already
            // bounds a requested window keeps one number rather than inventing a second, and a reader who
            // selected more than that is far past any reading intent the window is built to serve.
            //
            // Clamped from the START of the selection, so what survives is what the reader selected first
            // rather than an arbitrary middle. Reported through the ordinary trimmed path, never silently.
            bool selectionTruncated = false;
            if (selectionCap > 0 && RenderedLength(xml, selectionStart, selectionEnd, includeNotes: true) > selectionCap)
            {
                selectionEnd = RawForwardCap(xml, selectionStart, selectionCap, selectionEnd);
                selectionTruncated = true;
            }

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

            // Expansion is a SENTENCE COUNT, not a character budget. (#672)
            //
            // The character budgets this replaces were never derived from anything, and characters are the
            // wrong unit for this corpus twice over: the same figure buys a whole gāthā or a fraction of one
            // commentarial sentence, and it is measured on source text whose length varies by script. A
            // sentence is the unit these walkers already snap to, and it is the unit in which "the context
            // around what I selected" is actually meaningful.
            //
            // The section still bounds it, and not merely as a backstop for an over-long expansion: text
            // beyond a <div> belongs to the previous sutta and is not this passage's context however close it
            // sits in the file.
            start = WalkBackSentences(xml, selectionStart, contextSentences, sectionStart, includeNotes);
            end = WalkForwardSentences(xml, selectionEnd, contextSentences, sectionEnd, includeNotes);

            return Materialize(xml, start, end, maxChars, includeVariants, outputScript, markers, structuredNotes,
                selectionTruncated);
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

            // Collapse the needle the same way the comparison collapses the haystack: whitespace runs to one
            // space, punctuation dropped entirely.
            //
            // Punctuation is dropped because the two sides reach this comparison by different routes and do
            // not agree about it. The reader renders through a path that turns the danda into a period; the
            // XML has the danda. Even with both paths fixed, a selection dragged across a note bracket or a
            // typographic apostrophe would differ from the source by a character nobody typed. Matching on
            // letters alone cannot be broken by punctuation at all, which is the only version of this that
            // stays fixed. (#668)
            var wanted = new StringBuilder(needle.Length);
            foreach (var c in needle)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (wanted.Length > 0 && wanted[^1] != ' ') wanted.Append(' ');
                }
                else if (IsSignificant(c)) wanted.Append(c);
            }
            var target = wanted.ToString().Trim();
            if (target.Length == 0) return null;

            for (int anchor = from; anchor < to; anchor++)
            {
                // Tags are jumped WHOLE when looking for somewhere to start. Testing only the character
                // meant an anchor could land inside a tag and match its attribute text: a selection opening
                // "331." matched the n="331" of the paragraph's own <p> element, reporting a span that began
                // inside markup. Attribute values are not text the reader can select and must never be
                // matchable.
                if (xml[anchor] == '<')
                {
                    int close = xml.IndexOf('>', anchor);
                    if (close < 0 || close >= to) break;
                    anchor = close;
                    continue;
                }

                // A match must BEGIN on a letter, digit or mark. Starting anywhere else lets the skip rules
                // below consume punctuation before the first real character, so the span comes back opening
                // on something the reader did not select.
                if (!IsSignificant(xml[anchor])) continue;

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

                    // Punctuation on the haystack side is skipped for the same reason it is dropped from the
                    // needle, and skipped rather than treated as a separator so "so'haṃ" and "sohaṃ" match.
                    // Not lastConsumed: trailing punctuation is not part of what the reader selected.
                    if (!IsSignificant(c)) { i++; continue; }

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
        /// Whether a character carries meaning for matching: letters, digits, and combining marks. Marks are
        /// kept explicitly — they are not letters, and dropping them would let a form match a different word
        /// that differs only by a diacritic.
        /// </summary>
        private static bool IsSignificant(char c) =>
            char.IsLetterOrDigit(c)
            || System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
               == System.Globalization.UnicodeCategory.NonSpacingMark;

        /// <summary>
        /// Sentences of context on each side of a selection. Two, decided rather than derived: every task the
        /// assistant offers benefits from the sentences immediately around the selection, none of them
        /// benefits from a fixed character count, and one rule is one thing to revise when evidence arrives.
        /// Bounded by the enclosing &lt;div&gt; in both directions. (#672)
        /// </summary>
        public const int DefaultContextSentences = 2;

        /// <summary>
        /// Absolute bound on a selection, so a select-all on the longest book is not sent verbatim. Matches
        /// the hardening clamp the API already applies to a requested window (#305); reusing that figure keeps
        /// one number rather than inventing a second, and a reader who selected more than this is far past any
        /// reading intent a passage window is built to serve. (#672)
        /// </summary>
        public const int MaxSelectionChars = 20_000;

        /// <summary>
        /// How far one hop may scan for a sentence boundary before concluding it is not looking at a sentence.
        ///
        /// <para><b>Measured, not chosen.</b> Across the 217-book corpus — 1,038,209 danda-delimited sentences,
        /// notes stripped — the median is 56 characters, p99 is 363, p99.9 is 713, and just 30 sentences
        /// (0.003%) run past 2,000. So a scan that has read 2,000 characters without meeting a boundary is
        /// almost certainly not in running text at all: front matter, a heading, a table or a verse index,
        /// where "two sentences back" has no answer and an unbounded walk returns the whole run. (#672)</para>
        /// </summary>
        private const int SentenceScanCap = 2_000;

        /// <summary>
        /// Back to the start of the <paramref name="count"/>-th preceding sentence, or to <paramref name="limit"/>
        /// (the section start) — whichever comes first. Note-aware: a danda inside a &lt;note&gt; is apparatus
        /// punctuation, not a base-text sentence end (#310). (#672)
        /// </summary>
        private static int WalkBackSentences(string xml, int start, int count, int limit, bool includeNotes)
        {
            // The first hop finds the boundary ENDING the sentence before the selection's own — so it snaps the
            // window to the start of the sentence the selection sits in — and each further hop buys one whole
            // sentence of context. Hence count + 1.
            int at = start, boundary = -1;
            for (int n = 0; n < count + 1; n++)
            {
                int floor = Math.Max(limit, at - SentenceScanCap);
                var notes = TeiText.NoteRegions(xml, floor, at);
                int found = -1;
                for (int i = at - 1; i >= floor; i--)
                    if (TeiText.IsBoundary(xml[i]) && !TeiText.InNote(i, notes)) { found = i; break; }

                // No boundary within reach. Take what the scan could see and stop — bounded by the section,
                // and by the cap above.
                //
                // Stopping dead instead would be wrong in an ordinary case: the FIRST sentence of a section
                // has no danda in front of it, so a selection in the second sentence would get no context at
                // all despite a whole sentence sitting right there. Falling back to the scan floor keeps that
                // sentence and still bounds the front-matter case the cap exists for. (#672)
                // The scan floor is a raw character count like the forward caps, so it can open a window
                // mid-akṣara — the same orphaned vowel sign #871 fixes at the other three cuts. Reachable:
                // 14 books carry danda-free runs past this cap, the longest 19,010 characters. Aligned
                // against the SECTION start, not the floor, so the retreat cannot leave the section.
                // (#871, fable review)
                if (found < 0)
                    return Math.Min(start, ClusterStart(xml, AdvanceToParagraph(xml, floor, start), limit));
                boundary = found;
                at = found;
            }

            // Just past the danda: the first character of the sentence. No boundary anywhere behind means no
            // expansion at all — the selection keeps its own start.
            return boundary < 0 ? start : Math.Min(start, boundary + 1);
        }

        /// <summary>
        /// Forward past <paramref name="count"/> sentence ends, then on to the end of the sentence in progress
        /// — so the window never stops mid-sentence — bounded by <paramref name="limit"/> (the section end).
        /// (#672)
        /// </summary>
        private static int WalkForwardSentences(string xml, int end, int count, int limit, bool includeNotes)
        {
            int at = ExtendToSentenceEnd(xml, end, limit, includeNotes);   // finish the selection's own sentence
            for (int n = 0; n < count; n++)
            {
                int next = ExtendToSentenceEnd(xml, at, limit, includeNotes);
                if (next <= at) break;      // nothing further within the section
                at = next;
            }
            return Math.Min(at, limit);
        }

        /// <summary>
        /// Move a fallback window start forward to the first paragraph at or after <paramref name="from"/>,
        /// stopping at <paramref name="limit"/>.
        ///
        /// <para>The scan-floor fallback lands where the section begins, which is before the tags that open it
        /// — and in the real corpus before the section's <c>&lt;head&gt;</c> as well, whose TEXT precedes the
        /// first <c>&lt;p n=…&gt;</c> marker. A window starting there is cited from the last paragraph marker
        /// BEHIND it, which belongs to the previous sutta: the passage comes back citing a range that opens in
        /// a section it contains none of. Skipping tags alone cannot fix that, because a head is text, not
        /// markup — so this walks to the paragraph instead.</para>
        ///
        /// <para>The section heading is lost from the window, which is the right trade: a heading is a title,
        /// not the sentence context the expansion was asked for, and a wrong citation is worse than a missing
        /// one — "confidently the previous sutta" is a harder error to notice than "no paragraph". (#672, fable)</para>
        /// </summary>
        private static int AdvanceToParagraph(string xml, int from, int limit)
        {
            int i = Math.Max(0, from);
            if (i >= limit) return Math.Min(i, limit);

            // The next paragraph marker at or after the fallback point, if one opens before the window would.
            int p = xml.IndexOf("<p ", i, StringComparison.Ordinal);
            if (p >= 0 && p < limit)
            {
                int gt = xml.IndexOf('>', p);
                if (gt >= 0 && gt < limit) return gt + 1;
            }

            // No paragraph opens between here and the selection — the window is already inside one. Fall back
            // to skipping the tags at this position so it at least begins on text.
            while (i < limit && xml[i] == '<')
            {
                int gt = xml.IndexOf('>', i);
                if (gt < 0 || gt >= limit) break;
                i = gt + 1;
            }
            return Math.Min(i, limit);
        }

        /// <summary>Raw forward walk of at most <paramref name="maxChars"/> RENDERED characters — the selection
        /// clamp, which is a hard bound and so deliberately does not snap to a sentence. (#672)</summary>
        private static int RawForwardCap(string xml, int start, int maxChars, int limit)
        {
            int i = start, rendered = 0;
            while (i < limit && rendered < maxChars)
            {
                if (xml[i] == '<')
                {
                    int gt = xml.IndexOf('>', i);
                    i = gt < 0 || gt >= limit ? limit : gt + 1;
                }
                else { rendered++; i++; }
            }
            i = Math.Min(i, limit);

            // Never stop INSIDE a note. A cap is a character count and lands wherever it lands, so without
            // this it can cut a <note> in half: the rendered text then carries an unbalanced brace, the note's
            // tail reads as base text, and the apparatus list loses the note entirely — and the dandas in that
            // tail are read as base-text sentence ends by everything downstream, because NoteRegions only sees
            // notes that START inside the range it is given. Retreat to the note's opening. (#310/#355, #672)
            foreach (var (s0, e0) in TeiText.NoteRegions(xml, start, i))
                if (i > s0 && i < e0)
                    return Math.Max(start, s0);
            return i;
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

            // Falling out of the loop means the 4,000-char cap or the section ran out before any danda did,
            // so this return is a raw count like the hard cap — and on the selection path it is the cut that
            // actually ends the window, since every selection end is passed through here. Back off a half-cut
            // akṣara, but never below where we started: returning less than `from` would shorten the
            // selection the caller asked to keep whole. (#871)
            if (i > from) i = Math.Max(ClusterStart(xml, i, from), from);

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

        /// <summary>
        /// Retreat a raw cut position to the start of the akṣara it falls inside. (#871)
        ///
        /// <para><b>A character count is not a letter count.</b> The corpus is Devanagari at this layer, and
        /// three of this reader's cuts are pure counts that stop wherever the budget runs out — so a cut can
        /// land between a consonant and its dependent vowel sign, or just after a virama. What comes back is
        /// not visible damage: <c>Deva2Latn</c> gives a bare consonant its inherent 'a', so a window cut
        /// inside <c>\u092C\u0941\u0926\u094D\u0927\u0940</c> ("buddhī") ends "…buddha" — a different,
        /// entirely plausible Pāli word, with nothing to tell a reader or an agent that it was cut. This is
        /// the citation path, so subtly wrong Pāli is the worst output the reader can produce.</para>
        ///
        /// <para><b>Retreat rather than advance, because the cut is also the next page's cursor.</b> Moving
        /// it back to the cluster start puts the whole akṣara at the head of the next window, so paging
        /// through stitches back to the original text; advancing past the cluster would drop it from both.
        /// </para>
        ///
        /// <para>Two rules, applied until neither fires: a combining mark AT the cut belongs to the character
        /// before it, and a virama immediately BEFORE the cut binds to the consonant that follows. Marks are
        /// recognised by Unicode category rather than by a Devanagari range, so a conjunct of any depth
        /// unwinds without a table to keep current — and the virama is itself a mark, so a cut landing ON one
        /// is already covered by the first rule.</para>
        ///
        /// <para><b>Never across markup.</b> The retreat crosses text characters only; it stops at a tag's
        /// <c>&gt;</c> rather than stepping onto it. That is what keeps it from moving a cut across a note
        /// boundary, and the alternative is worse than the bug: 23 places in the corpus carry a mark
        /// immediately after a close tag (<c>&lt;hi rend="bold"&gt;…&lt;/hi&gt;</c> + niggahita), and one
        /// opens a paragraph with a vowel sign, so stepping back would return a position INSIDE the tag and
        /// the window would render the markup's own text. At those points the mark is left stranded, which is
        /// what this reader did before. (fable review)</para>
        /// </summary>
        /// <param name="floor">Never retreat below this. The result may equal it; callers cutting a window
        /// must reject that themselves, since an empty window makes nextCursor point at its own start.</param>
        private static int ClusterStart(string xml, int cut, int floor)
        {
            while (cut > floor)
            {
                if (cut < xml.Length && IsCombining(xml[cut]) && xml[cut - 1] != '>') { cut--; continue; }
                if (xml[cut - 1] == Virama) { cut--; continue; }

                // The same bond written in the corpus's open form. A ZWJ between the virama and the consonant
                // asks for the half-form rather than the stacked ligature, and it is not a rarity to reason
                // about hypothetically: it sits in 15% of the corpus's viramas, concentrated in exactly the
                // danda-free runs where these count-based cuts fire. (fable review)
                if (xml[cut - 1] == Zwj && cut - 2 >= floor && xml[cut - 2] == Virama) { cut -= 2; continue; }

                break;
            }

            return cut;
        }

        private const char Virama = '\u094D';
        private const char Zwj = '\u200D';

        private static bool IsCombining(char c) =>
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) is
                System.Globalization.UnicodeCategory.NonSpacingMark or
                System.Globalization.UnicodeCategory.SpacingCombiningMark;

        // Raw end position after accumulating ~maxChars rendered chars, then extending to the next sentence
        // boundary (capped) so we never cut mid-sentence. Tags and stripped subtrees cost zero budget.
        private static int WalkForward(string xml, int start, int maxChars, bool includeNotes, int limit)
        {
            // long: an unclamped client maxChars (e.g. int.MaxValue) would overflow `maxChars + maxChars/2`
            // negative, tripping `rendered >= hardCap` on the first char. (#313 A4-13; endpoint also clamps, #305)
            int i = start, rendered = 0;
            long hardCap = (long)maxChars + maxChars / 2;
            bool budgetReached = false;

            // How deep in apparatus the walk currently is, and where the outermost open note began. Only ever
            // non-zero when includeNotes is set: otherwise the branch below strips each note's subtree and the
            // walk is never inside one. Counted from the tags as they pass rather than scanned up front, which
            // costs nothing on a path that runs for every window — and is exact, because every caller starts
            // outside a note (the #913 nudge guarantees it for the one that could not otherwise). (#917)
            int noteDepth = 0, noteOpenedAt = -1;

            while (i < limit)
            {
                char c = xml[i];
                if (c == '<')
                {
                    int gt = xml.IndexOf('>', i);
                    if (gt < 0) break;
                    string tag = xml.Substring(i, gt - i + 1);
                    string name = TeiText.TagName(tag);
                    bool selfClosing = tag.EndsWith("/>", StringComparison.Ordinal);
                    if (name == "note" && !includeNotes && !selfClosing)
                        // Open <note> strips its subtree; a lone </note> (walk began inside a note) is zero-width,
                        // never a subtree — else SkipSubtree jumps to the next </note>, silently skipping text. (#310 A4-2)
                        i = tag.StartsWith("</", StringComparison.Ordinal)
                            ? gt + 1
                            : TeiText.SkipSubtree(xml, gt + 1, "note", limit);
                    else if (name == "hi" && TeiText.IsStructuralHi(tag) && !selfClosing)
                        i = TeiText.SkipSubtree(xml, gt + 1, "hi", limit);
                    else
                    {
                        if (name == "note" && !selfClosing)
                        {
                            if (tag.StartsWith("</", StringComparison.Ordinal))
                            {
                                if (noteDepth > 0) noteDepth--;
                            }
                            else if (noteDepth++ == 0) noteOpenedAt = i;
                        }
                        i = gt + 1;
                    }
                }
                else
                {
                    // A danda inside a <note> is apparatus punctuation, not a base-text sentence end. Stopping
                    // there would close the window between <note> and </note>, and Clean would emit an opening
                    // brace with nothing to match it. The other four boundary checks in this file have carried
                    // this guard since #310; this one did not. (#917)
                    if (budgetReached && noteDepth == 0 && TeiText.IsBoundary(c)) return i + 1;
                    rendered++;
                    i++;
                    if (rendered >= maxChars) budgetReached = true;
                    if (rendered >= hardCap)
                    {
                        // The cap is unconditional, so it can fall inside a note where the boundary check now
                        // cannot. End before the note opened rather than inside it — same reason — but only
                        // when that still advances, or the window's end becomes its own nextCursor.
                        if (noteDepth > 0 && noteOpenedAt > start) return noteOpenedAt;

                        // No boundary found: hard cap. Back off any half-cut akṣara — but not to nothing, or
                        // this window's end becomes its own nextCursor and the caller pages forever. (#871)
                        int cut = ClusterStart(xml, i, start);
                        return cut > start ? cut : i;
                    }
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
            // A start position, so there is no empty window to guard against: retreating merely begins the
            // previous page a letter earlier. Left where the count landed, that page opened on an orphaned
            // vowel sign. (#871)
            int fallback = ClusterStart(xml, Math.Max(i + 1, limit), limit);
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
        string? EndParagraphBookCode = null,
        /// <summary>The SELECTION was longer than the cap and was cut. Reported so the caller can say so —
        /// an answer about part of a selection captioned as being about all of it is the failure this
        /// window is written to avoid everywhere else. (#672)</summary>
        bool SelectionTruncated = false,
        /// <summary>The paragraph numbering runs straight through the window, so naming its two ends as a
        /// range describes what is actually in it. False where the window crosses a numbering restart or a
        /// sub-book boundary, and a range would name paragraphs the window does not contain — the citation
        /// says so in words, and this says so to a caller reading the structured fields. (#914)</summary>
        bool ParagraphsContiguous = true);

    /// <summary>One apparatus note (a digitized print footnote — usually a variant reading) as structured data:
    /// its character <paramref name="Offset"/> into the returned brace-free <c>Text</c>, its full converted
    /// <paramref name="Text"/> (e.g. "anupāyinī (ka.)"), and — when it matches the simple <c>reading (sigla)</c>
    /// shape — the split <paramref name="Reading"/> and witness <paramref name="Sigla"/> (else both null). (#267)</summary>
    public sealed record ApparatusNote(int Offset, string Text, string? Reading, string? Sigla);
}
