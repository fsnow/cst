using System.Collections.Generic;
using CST.Avalonia.Models;
using CST.Avalonia.Services.Presentation;
using Xunit;

namespace CST.Avalonia.Tests.Services
{
    /// <summary>
    /// #187: the pure mapping from a presentation request to the dock open-path parameters. Guards the target
    /// precedence (explicit hit/anchor outranks a restored reading position) and the choice between the
    /// search-tab path (multiple instances + highlight plumbing) and the general path (carries a landing target).
    /// </summary>
    public class PresentationPlannerTests
    {
        private static readonly global::CST.Book AnyBook = global::CST.Books.Inst[0];

        private static PresentationRequest Req(
            PresentationTarget? target = null,
            bool withHighlights = false) => new()
        {
            Book = AnyBook,
            Target = target,
            SearchTerms = withHighlights ? new List<string> { "dhamma" } : null,
            Positions = withHighlights ? new List<TermPosition> { new() { Position = 1, StartOffset = 10, EndOffset = 16 } } : null,
        };

        [Fact]
        public void No_target_no_highlights_uses_general_path_with_no_landing()
        {
            var p = PresentationPlanner.Plan(Req());
            Assert.False(p.UseSearchTab);
            Assert.Null(p.Anchor);
            Assert.Null(p.HitIndex);
            Assert.Null(p.PositionToken);
        }

        [Fact]
        public void Highlights_without_a_target_use_the_search_tab_path()
        {
            var p = PresentationPlanner.Plan(Req(withHighlights: true));
            Assert.True(p.UseSearchTab);
        }

        [Fact]
        public void Explicit_hit_wins_and_forces_the_general_path_even_with_highlights()
        {
            // An explicit landing target must be carried by the general path — the search tab lands on its own.
            var p = PresentationPlanner.Plan(Req(new PresentationTarget.Hit(3), withHighlights: true));
            Assert.False(p.UseSearchTab);
            Assert.Equal(3, p.HitIndex);
            Assert.Null(p.Anchor);
            Assert.Null(p.PositionToken);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void Hit_index_below_one_is_clamped(int given)
        {
            var p = PresentationPlanner.Plan(Req(new PresentationTarget.Hit(given)));
            Assert.Equal(1, p.HitIndex);
        }

        [Fact]
        public void Anchor_target_maps_to_the_anchor_parameter()
        {
            var p = PresentationPlanner.Plan(Req(new PresentationTarget.Anchor("V1.0023")));
            Assert.Equal("V1.0023", p.Anchor);
            Assert.Null(p.HitIndex);
            Assert.Null(p.PositionToken);
            Assert.False(p.UseSearchTab);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Blank_anchor_is_ignored(string name)
        {
            var p = PresentationPlanner.Plan(Req(new PresentationTarget.Anchor(name)));
            Assert.Null(p.Anchor);
        }

        [Fact]
        public void Position_token_target_maps_to_the_token_parameter()
        {
            var token = new ReadingPositionToken { Above = "para5", Below = "para6", Fraction = 0.4 };
            var p = PresentationPlanner.Plan(Req(new PresentationTarget.Position(token)));
            Assert.Same(token, p.PositionToken);
            Assert.Null(p.Anchor);
            Assert.Null(p.HitIndex);
            Assert.False(p.UseSearchTab);
        }

        [Fact]
        public void Position_token_with_highlights_still_uses_the_general_path()
        {
            // A restored reading position is a landing target too — it must not be swallowed by the search tab.
            var token = new ReadingPositionToken { Above = "para5", Below = "para6", Fraction = 0.4 };
            var p = PresentationPlanner.Plan(Req(new PresentationTarget.Position(token), withHighlights: true));
            Assert.False(p.UseSearchTab);
            Assert.Same(token, p.PositionToken);
        }

        [Fact]
        public void Terms_without_positions_do_not_trigger_the_search_tab()
        {
            // The search tab exists for real hit highlighting; terms alone have nothing to land on.
            var r = new PresentationRequest { Book = AnyBook, SearchTerms = new List<string> { "dhamma" } };
            Assert.False(PresentationPlanner.Plan(r).UseSearchTab);
        }
    }
}
