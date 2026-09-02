using System;
using System.Collections.Generic;
using System.Text;
using CST.Navigation;

namespace CST.Avalonia.ViewModels
{
    /// <summary>
    /// What page-numbering systems a book actually carries, and how to present that.
    ///
    /// <para>#457 and #541 were the same missing fact seen from two sides: Go To <b>hid</b> systems a book
    /// does have, because it read the page numbers in effect at the current scroll position; the status bar
    /// <b>showed</b> systems the book does not have, because it composed all five unconditionally. Both are
    /// answered by <c>BookMarkers.Editions()</c> — the distinct <c>&lt;pb&gt;</c> editions parsed out of the
    /// book's XML, which depends on neither scroll position nor cache readiness.</para>
    ///
    /// <para><b>Absence is not a data gap.</b> Most of the corpus legitimately carries no PTS or Thai
    /// numbering — the VRI print set is 140 volumes and the extra-canonical texts were never published
    /// outside Myanmar. A blank <c>PTS:</c> field reads to a scholar as "the PTS page here is unknown", which
    /// is a different and wrong claim. Omitting the field says "not applicable", which is the true one.</para>
    /// </summary>
    public static class PageNumbering
    {
        /// <summary>
        /// Labels for the status bar, in display order.
        ///
        /// <para>Kept in one place deliberately. The format string this replaced was flagged in-code as
        /// temporary pending localization (#26); this is the seam that work replaces, so a localized build
        /// swaps this table rather than hunting an interpolated string.</para>
        /// </summary>
        private static readonly (PageEdition Edition, string Label)[] Order =
        {
            (PageEdition.Vri, "VRI"),
            (PageEdition.Myanmar, "Myanmar"),
            (PageEdition.Pts, "PTS"),
            (PageEdition.Thai, "Thai"),
            (PageEdition.Other, "Other"),
        };

        private const string Separator = "   ";

        /// <summary>
        /// Whether <paramref name="edition"/> is one the book carries.
        ///
        /// <para><b><paramref name="editions"/> is nullable on purpose</b>, and null does NOT mean "none".
        /// It means the book's markers have not been built yet — the state #457's first symptom lived in,
        /// where Go To opened before the anchor cache resolved and greyed out every system on every book.
        /// Unknown answers <c>true</c>: offering a system that turns out to be empty costs the reader one
        /// failed lookup, while withholding one the book has leaves them no route in at all. When the two
        /// error directions are unequal, take the cheap one.</para>
        /// </summary>
        public static bool Has(IReadOnlyList<PageEdition>? editions, PageEdition edition)
        {
            if (editions is null) return true;
            for (int i = 0; i < editions.Count; i++)
                if (editions[i] == edition) return true;
            return false;
        }

        /// <summary>
        /// The navigation type Go To should open on.
        ///
        /// <para>Paragraph was the hardcoded default, and it is the <i>weakest</i> address in the corpus:
        /// paragraph numbers restart per sub-book in the 7 Multi volumes, so a bare number is ambiguous in
        /// 102 of 217 books (#447, #596). Where the book carries a page edition, prefer it — page numbers
        /// are unambiguous within a volume. Editions are offered in <see cref="Order"/>, so VRI wins when
        /// present, which is the numbering the reader is most likely holding.</para>
        /// </summary>
        public static NavigationType DefaultType(IReadOnlyList<PageEdition>? editions)
        {
            if (editions is null || editions.Count == 0) return NavigationType.Paragraph;

            foreach (var (edition, _) in Order)
                if (Has(editions, edition))
                    return ToNavigationType(edition);

            return NavigationType.Paragraph;
        }

        /// <summary>
        /// Whether Go To can offer <paramref name="type"/> for a book carrying <paramref name="editions"/>.
        ///
        /// <para>Paragraph is always offered: every book has paragraphs, which is why it is the fallback
        /// even though it is the weakest address. For the page systems this defers to <see cref="Has"/>,
        /// including its deliberate "unknown answers true" for a book whose markers are not built yet.</para>
        ///
        /// <para>Walks <see cref="Order"/> rather than carrying an inverse of
        /// <see cref="ToNavigationType"/>: a second switch would be a second thing to keep in step, and this
        /// one cannot drift from the table the rest of the class already uses.</para>
        /// </summary>
        public static bool Offers(IReadOnlyList<PageEdition>? editions, NavigationType type)
        {
            if (type == NavigationType.Paragraph) return true;

            foreach (var (edition, _) in Order)
                if (ToNavigationType(edition) == type)
                    return Has(editions, edition);

            return false;
        }

        /// <summary>
        /// The type Go To should open on, given what the reader last chose. (#844)
        ///
        /// <para><b>The remembered choice and the choice in effect are different things, and only the first
        /// persists.</b> A reader who works in PTS opens a book with no PTS pagination; this returns the
        /// book's own default so the dialog is usable, and the caller must NOT write that back as the
        /// preference — one visit to a Myanmar-only text would otherwise silently convert a PTS reader to
        /// Myanmar for good. That is the whole reason this is a pure function of the two inputs: it cannot
        /// mutate the preference, because it cannot see it.</para>
        ///
        /// <para><paramref name="preferred"/> null means the reader has never chosen — a first run, or a
        /// state file from before this existed — and falls through to <see cref="DefaultType"/>, which is
        /// exactly the behaviour that shipped before.</para>
        /// </summary>
        public static NavigationType Resolve(NavigationType? preferred, IReadOnlyList<PageEdition>? editions)
            => preferred is { } want && Offers(editions, want) ? want : DefaultType(editions);

        /// <summary>Maps an edition to the Go To navigation type that addresses it.</summary>
        public static NavigationType ToNavigationType(PageEdition edition) => edition switch
        {
            PageEdition.Vri => NavigationType.VriPage,
            PageEdition.Myanmar => NavigationType.MyanmarPage,
            PageEdition.Pts => NavigationType.PtsPage,
            PageEdition.Thai => NavigationType.ThaiPage,
            PageEdition.Other => NavigationType.OtherPage,
            _ => NavigationType.Paragraph,
        };

        /// <summary>
        /// The status-bar text: only the systems this book carries, each with its page at the current
        /// position.
        ///
        /// <para>The paragraph number is deliberately absent. It was in the shipped status bar behind a
        /// comment describing it as "for debugging" (#541).</para>
        ///
        /// <para>A book whose markers are not built yet (<paramref name="editions"/> null) shows every
        /// system, matching <see cref="Has"/> — the alternative is a status bar that flickers from empty to
        /// populated on load, which reads as a fault.</para>
        /// </summary>
        /// <param name="pageFor">The page currently in effect for an edition, as the reader reports it.
        /// <c>"*"</c> means no page of that edition is in effect at this scroll position — a real state
        /// (above the first page break, say), distinct from the book not having the edition at all.</param>
        public static string ComposeStatus(
            IReadOnlyList<PageEdition>? editions,
            Func<PageEdition, string> pageFor)
        {
            var sb = new StringBuilder();
            foreach (var (edition, label) in Order)
            {
                if (!Has(editions, edition)) continue;
                if (sb.Length > 0) sb.Append(Separator);
                sb.Append(label).Append(": ").Append(pageFor(edition));
            }
            return sb.ToString();
        }
    }
}
