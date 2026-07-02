using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Search;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Search
{
    /// <summary>
    /// Unit tests for the pure multi-word / phrase search logic (parsing + position matching).
    /// These exercise the algorithm directly with synthetic positions, independent of Lucene and
    /// the Books singleton. Covers the cases CST4 could not express: 3+ word proximity, 3+ word
    /// phrases, a phrase mixed with loose words, and multiple distinct phrases.
    /// </summary>
    public class MultiWordSearchTests
    {
        // ---- helpers --------------------------------------------------------

        /// <summary>Positions for one word; StartOffset/EndOffset derived from position.</summary>
        private static List<TermPosition> Pos(string word, params int[] positions) =>
            positions.Select(p => new TermPosition
            {
                Position = p,
                StartOffset = p * 10,
                EndOffset = p * 10 + 1,
                Word = word
            }).ToList();

        private static List<UnitOccurrence> WordUnit(string word, params int[] positions) =>
            MultiWordSearch.FindUnitOccurrences(new[] { Pos(word, positions) });

        private static List<UnitOccurrence> PhraseUnit(params List<TermPosition>[] slots) =>
            MultiWordSearch.FindUnitOccurrences(slots);

        // ---- StripJoiners (SRCH-3) ------------------------------------------

        [Fact]
        public void StripJoiners_RemovesZeroWidthJoinerAndNonJoiner()
        {
            // "dhamma" with a ZWNJ (U+200C) and a ZWJ (U+200D) embedded
            Assert.Equal("dhamma", MultiWordSearch.StripJoiners("dha\u200Cm\u200Dma"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void StripJoiners_NullOrEmpty_ReturnsEmpty(string? input)
        {
            Assert.Equal(string.Empty, MultiWordSearch.StripJoiners(input));
        }

        [Fact]
        public void StripJoiners_PastedRomanQuery_ConvertsToSameIpeAsClean()
        {
            // The SRCH-3 bug: a pasted joiner survives into the IPE term (index terms are joiner-free),
            // so the query matches nothing. Stripping first makes it convert identically to the clean form.
            var pasted = "dhamma".Insert(3, "\u200C");   // "dham<ZWNJ>ma"
            var cleanIpe = Any2Ipe.Convert("dhamma");

            Assert.Equal(cleanIpe, Any2Ipe.Convert(MultiWordSearch.StripJoiners(pasted)));
            // And confirm the bug is real without the strip (raw pasted query yields a different IPE term).
            Assert.NotEqual(cleanIpe, Any2Ipe.Convert(pasted));
        }

        // ---- ParseUnits -----------------------------------------------------

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseUnits_Empty_ReturnsNoUnits(string q)
        {
            Assert.Empty(MultiWordSearch.ParseUnits(q));
        }

        [Fact]
        public void ParseUnits_SingleWord_OneWordUnit()
        {
            var units = MultiWordSearch.ParseUnits("dhamma");
            var unit = Assert.Single(units);
            Assert.False(unit.IsPhrase);
            Assert.Equal(new[] { "dhamma" }, unit.Words);
        }

        [Fact]
        public void ParseUnits_ThreeWords_ThreeWordUnits()
        {
            var units = MultiWordSearch.ParseUnits("dhamma vinaya sangha");
            Assert.Equal(3, units.Count);
            Assert.All(units, u => Assert.False(u.IsPhrase));
            Assert.Equal(new[] { "dhamma", "vinaya", "sangha" }, units.Select(u => u.Words[0]));
        }

        [Fact]
        public void ParseUnits_QuotedPhrase_OnePhraseUnit()
        {
            var units = MultiWordSearch.ParseUnits("\"evam me sutam\"");
            var unit = Assert.Single(units);
            Assert.True(unit.IsPhrase);
            Assert.Equal(new[] { "evam", "me", "sutam" }, unit.Words);
        }

        [Fact]
        public void ParseUnits_PhrasePlusWord_TwoUnits()
        {
            var units = MultiWordSearch.ParseUnits("\"evam me\" sutam");
            Assert.Equal(2, units.Count);
            Assert.True(units[0].IsPhrase);
            Assert.Equal(new[] { "evam", "me" }, units[0].Words);
            Assert.False(units[1].IsPhrase);
            Assert.Equal("sutam", units[1].Words[0]);
        }

        [Fact]
        public void ParseUnits_TwoPhrases_TwoPhraseUnits()
        {
            var units = MultiWordSearch.ParseUnits("\"evam me\" \"sutam bhagava\"");
            Assert.Equal(2, units.Count);
            Assert.True(units[0].IsPhrase);
            Assert.True(units[1].IsPhrase);
            Assert.Equal(new[] { "evam", "me" }, units[0].Words);
            Assert.Equal(new[] { "sutam", "bhagava" }, units[1].Words);
        }

        [Fact]
        public void ParseUnits_WordPhraseWord_PreservesOrder()
        {
            var units = MultiWordSearch.ParseUnits("x \"a b\" y");
            Assert.Equal(3, units.Count);
            Assert.False(units[0].IsPhrase);
            Assert.True(units[1].IsPhrase);
            Assert.False(units[2].IsPhrase);
            Assert.Equal("x", units[0].Words[0]);
            Assert.Equal(new[] { "a", "b" }, units[1].Words);
            Assert.Equal("y", units[2].Words[0]);
        }

        [Fact]
        public void ParseUnits_UnbalancedTrailingQuote_ClosesAtEnd()
        {
            var units = MultiWordSearch.ParseUnits("\"evam me");
            var unit = Assert.Single(units);
            Assert.True(unit.IsPhrase);
            Assert.Equal(new[] { "evam", "me" }, unit.Words);
        }

        [Fact]
        public void ParseUnits_SingleQuotedWord_IsNotPhrase()
        {
            var units = MultiWordSearch.ParseUnits("\"dhamma\"");
            var unit = Assert.Single(units);
            Assert.False(unit.IsPhrase); // one word, even though quoted
            Assert.Equal("dhamma", unit.Words[0]);
        }

        [Fact]
        public void ParseUnits_CollapsesExtraSpaces()
        {
            var units = MultiWordSearch.ParseUnits("a    b");
            Assert.Equal(2, units.Count);
        }

        // ---- FindUnitOccurrences -------------------------------------------

        [Fact]
        public void FindUnitOccurrences_SingleWord_EveryPositionIsOccurrence()
        {
            var occs = WordUnit("a", 1, 5, 9);
            Assert.Equal(3, occs.Count);
            Assert.Equal(new[] { 1, 5, 9 }, occs.Select(o => o.AnchorPosition));
            Assert.All(occs, o => Assert.Single(o.Members));
        }

        [Fact]
        public void FindUnitOccurrences_Phrase_MatchesOnlyAdjacentRuns()
        {
            // a at 1 and 10; b at 2 and 20 -> only (a@1,b@2) is adjacent
            var occs = PhraseUnit(Pos("a", 1, 10), Pos("b", 2, 20));
            var occ = Assert.Single(occs);
            Assert.Equal(1, occ.AnchorPosition);
            Assert.Equal(new[] { 1, 2 }, occ.Members.Select(m => m.Position));
        }

        [Fact]
        public void FindUnitOccurrences_ThreeWordPhrase_RequiresFullAdjacency()
        {
            // a@1 b@2 c@3 (adjacent) ; a@10 b@11 c@13 (gap at c) -> only first matches
            var occs = PhraseUnit(
                Pos("a", 1, 10),
                Pos("b", 2, 11),
                Pos("c", 3, 13));
            var occ = Assert.Single(occs);
            Assert.Equal(new[] { 1, 2, 3 }, occ.Members.Select(m => m.Position));
        }

        [Fact]
        public void FindUnitOccurrences_PhraseMissingSlot_NoOccurrence()
        {
            var occs = PhraseUnit(Pos("a", 1), Pos("b", 5)); // b not adjacent to a
            Assert.Empty(occs);
        }

        [Fact]
        public void FindUnitOccurrences_PhraseSlotWithMultipleExpansions()
        {
            // slot 1 has two expansion words both contributing positions; either may complete the phrase
            var slot1 = Pos("b", 2).Concat(Pos("bb", 12)).ToList();
            var occs = PhraseUnit(Pos("a", 1, 11), slot1);
            Assert.Equal(2, occs.Count); // a@1+b@2, and a@11+bb@12
        }

        [Fact]
        public void FindUnitOccurrences_PhraseWithWildcardSlot_OnlyAdjacentCandidateMatches()
        {
            // Mirrors the live `"sammā *"` case: a fixed first word plus a slot expanded to many
            // candidate words. Only the candidate that actually sits immediately after the fixed
            // word forms a phrase occurrence; the rest are irrelevant. (This is why a bare `*`,
            // truncated to an arbitrary set of words, can match very few phrases.)
            var fixedWord = Pos("samma", 5, 100);                 // two occurrences of the fixed word
            var wildcardSlot = Pos("akatatta", 6)                 // only this candidate is adjacent (5->6)
                .Concat(Pos("buddho", 40))                        // present in book but not adjacent
                .Concat(Pos("dhammo", 77)).ToList();              // present in book but not adjacent
            var occs = PhraseUnit(fixedWord, wildcardSlot);
            var occ = Assert.Single(occs);
            Assert.Equal(new[] { 5, 6 }, occ.Members.Select(m => m.Position));
            Assert.Equal("akatatta", occ.Members[1].Word);
        }

        // ---- FindHits: single unit -----------------------------------------

        [Fact]
        public void FindHits_SinglePhrase_OneNavigableAnchor()
        {
            var phrase = PhraseUnit(Pos("a", 1), Pos("b", 2), Pos("c", 3));
            var hits = MultiWordSearch.FindHits(new List<List<UnitOccurrence>> { phrase }, window: 10);
            var hit = Assert.Single(hits);
            Assert.Equal(new[] { 1, 2, 3 }, hit.Select(m => m.Position));
            // Exactly one navigable anchor (the first word); the rest are context (green).
            Assert.Equal(1, hit.Count(m => m.IsFirstTerm));
            Assert.True(hit.Single(m => m.Position == 1).IsFirstTerm);
        }

        // ---- FindHits: proximity (window) ----------------------------------

        [Fact]
        public void FindHits_TwoWords_WithinWindow_Hit()
        {
            var hits = MultiWordSearch.FindHits(
                new List<List<UnitOccurrence>> { WordUnit("a", 5), WordUnit("b", 8) }, window: 10);
            var hit = Assert.Single(hits);
            Assert.Contains(hit, m => m.Position == 5 && m.IsFirstTerm);   // first unit blue
            Assert.Contains(hit, m => m.Position == 8 && !m.IsFirstTerm);  // second unit green
        }

        [Fact]
        public void FindHits_TwoWords_OutsideWindow_NoHit()
        {
            var hits = MultiWordSearch.FindHits(
                new List<List<UnitOccurrence>> { WordUnit("a", 5), WordUnit("b", 8) }, window: 2);
            Assert.Empty(hits);
        }

        [Fact]
        public void FindHits_WindowBoundary_Inclusive()
        {
            // span exactly == window is a hit; window-1 is not
            var units = new List<List<UnitOccurrence>> { WordUnit("a", 0), WordUnit("b", 7) };
            Assert.Single(MultiWordSearch.FindHits(units, window: 7));
            Assert.Empty(MultiWordSearch.FindHits(units, window: 6));
        }

        [Fact]
        public void FindHits_ThreeWords_AllWithinWindow_Hit()
        {
            var hits = MultiWordSearch.FindHits(new List<List<UnitOccurrence>>
            {
                WordUnit("a", 5), WordUnit("b", 8), WordUnit("c", 12)
            }, window: 10); // span 12-5 = 7
            Assert.Single(hits);
        }

        [Fact]
        public void FindHits_ThreeWords_OneFarOutside_NoHit()
        {
            var hits = MultiWordSearch.FindHits(new List<List<UnitOccurrence>>
            {
                WordUnit("a", 5), WordUnit("b", 8), WordUnit("c", 50)
            }, window: 10);
            Assert.Empty(hits);
        }

        [Fact]
        public void FindHits_ProximityIsOrderIndependent()
        {
            // second unit occurs BEFORE the first in the text; still a hit (window measures span)
            var hits = MultiWordSearch.FindHits(
                new List<List<UnitOccurrence>> { WordUnit("a", 20), WordUnit("b", 8) }, window: 15);
            var hit = Assert.Single(hits);
            Assert.Contains(hit, m => m.Position == 20 && m.IsFirstTerm);
            Assert.Contains(hit, m => m.Position == 8 && !m.IsFirstTerm);
        }

        [Fact]
        public void FindHits_MultipleDistinctWindows_ReportedSeparately()
        {
            var hits = MultiWordSearch.FindHits(new List<List<UnitOccurrence>>
            {
                WordUnit("a", 5, 50), WordUnit("b", 8, 52)
            }, window: 10);
            Assert.Equal(2, hits.Count); // (a@5,b@8) and (a@50,b@52); the cross pairs are too far
        }

        // ---- FindHits: phrases + words combined ----------------------------

        [Fact]
        public void FindHits_PhrasePlusWord_WithinWindow()
        {
            var phrase = PhraseUnit(Pos("a", 5), Pos("b", 6)); // "a b" anchored at 5
            var word = WordUnit("c", 10);
            var hits = MultiWordSearch.FindHits(new List<List<UnitOccurrence>> { phrase, word }, window: 10);
            var hit = Assert.Single(hits);
            Assert.Equal(new[] { 5, 6, 10 }, hit.OrderBy(m => m.Position).Select(m => m.Position));
            // One navigable anchor: the first word of the first unit (the phrase). The rest are green.
            Assert.Equal(1, hit.Count(m => m.IsFirstTerm));
            Assert.True(hit.Single(m => m.Position == 5).IsFirstTerm);
            Assert.False(hit.Single(m => m.Position == 6).IsFirstTerm);
            Assert.False(hit.Single(m => m.Position == 10).IsFirstTerm);
        }

        [Fact]
        public void FindHits_TwoPhrases_WithinWindow()
        {
            var p1 = PhraseUnit(Pos("a", 1), Pos("b", 2));  // anchor 1
            var p2 = PhraseUnit(Pos("c", 5), Pos("d", 6));  // anchor 5
            var hits = MultiWordSearch.FindHits(new List<List<UnitOccurrence>> { p1, p2 }, window: 10);
            var hit = Assert.Single(hits);
            Assert.Equal(new[] { 1, 2, 5, 6 }, hit.OrderBy(m => m.Position).Select(m => m.Position));
            // One navigable anchor: the first word of the first phrase.
            Assert.Equal(1, hit.Count(m => m.IsFirstTerm));
            Assert.True(hit.Single(m => m.Position == 1).IsFirstTerm);
        }

        [Fact]
        public void FindHits_TwoPhrases_OutsideWindow_NoHit()
        {
            var p1 = PhraseUnit(Pos("a", 1), Pos("b", 2));  // anchor 1
            var p2 = PhraseUnit(Pos("c", 5), Pos("d", 6));  // anchor 5, span 4
            Assert.Empty(MultiWordSearch.FindHits(new List<List<UnitOccurrence>> { p1, p2 }, window: 3));
        }

        [Fact]
        public void FindHits_AnyUnitMissing_NoHit()
        {
            var hits = MultiWordSearch.FindHits(new List<List<UnitOccurrence>>
            {
                WordUnit("a", 5), new List<UnitOccurrence>() // second unit absent in this book
            }, window: 10);
            Assert.Empty(hits);
        }
    }
}
