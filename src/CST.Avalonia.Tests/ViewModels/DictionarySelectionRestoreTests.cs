using System;
using System.Collections.Generic;
using CST.Avalonia.ViewModels;
using CST.Tools;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// Which headword a completed lookup selects. (#935)
///
/// <para>The Dictionary pane restored its search text and its source but not WHICH of several matching
/// headwords was chosen, so a reader who had selected the third of four came back to the first. This covers
/// the decision; the one-shot bookkeeping that limits it to a restore lives in the view model, which needs a
/// dock layout and a live lookup and so is not reachable from a unit test.</para>
/// </summary>
public class DictionarySelectionRestoreTests
{
    private static IReadOnlyList<DictionaryEntryViewModel> Words(params string[] headwords)
    {
        var list = new List<DictionaryEntryViewModel>();
        foreach (var h in headwords)
            list.Add(new DictionaryEntryViewModel(new DictionaryEntry(h, "<p>def</p>", "dppn")));
        return list;
    }

    private static string Word(DictionaryEntryViewModel? vm) => vm?.DisplayWord ?? "(none)";

    /// <summary>The reported case: nālanda matches four headwords in DPPN and the reader had picked the
    /// third.</summary>
    [Fact]
    public void A_remembered_headword_is_selected_rather_than_the_first_match()
    {
        var words = Words("N\u0101land\u0101 1", "N\u0101land\u0101 2",
                          "N\u0101land\u0101sutta 1", "N\u0101land\u0101sutta 2");

        Assert.Equal("N\u0101land\u0101sutta 1",
            Word(DictionaryViewModel.ChooseSelection("N\u0101land\u0101sutta 1", words)));
    }

    /// <summary>No remembered selection — a fresh lookup — keeps the auto-select that shows a definition
    /// immediately, which is CST4's behaviour and the reason the auto-select exists.</summary>
    [Fact]
    public void An_ordinary_lookup_still_auto_selects_the_first_match()
    {
        var words = Words("N\u0101land\u0101 1", "N\u0101land\u0101 2");

        Assert.Equal("N\u0101land\u0101 1", Word(DictionaryViewModel.ChooseSelection(null, words)));
        Assert.Equal("N\u0101land\u0101 1", Word(DictionaryViewModel.ChooseSelection("", words)));
    }

    /// <summary>A remembered headword the dictionary no longer offers falls back to the first match. Sources
    /// are updated between sessions, and #933 changed what a lookup returns — so this is a normal outcome,
    /// not an error, and the fallback is what the pane did before any of this existed.</summary>
    [Fact]
    public void A_headword_that_is_no_longer_in_the_results_falls_back_to_the_first()
    {
        var words = Words("N\u0101land\u0101 1", "N\u0101land\u0101 2");

        Assert.Equal("N\u0101land\u0101 1",
            Word(DictionaryViewModel.ChooseSelection("Nobody\u0101 3", words)));
    }

    /// <summary>A headword is rendered in whichever script was current when it was saved. Comparing through
    /// IPE is what lets a reader who switched script between sessions still match their own selection —
    /// here the same word saved in Devanāgarī and looked up in Latin.</summary>
    [Fact]
    public void A_remembered_headword_still_matches_after_the_reading_script_changed()
    {
        // "n\u0101land\u0101" in Devanāgarī: na + aa + la + nna + virama + da + aa
        const string deva = "\u0928\u093E\u0932\u0928\u094D\u0926\u093E";

        // The wanted entry is deliberately NOT first: if it were, the auto-select would produce the same
        // answer and the test would pass without any matching happening at all.
        var words = Words("n\u0101land\u0101sutta", "n\u0101land\u0101");

        Assert.Equal("n\u0101land\u0101", Word(DictionaryViewModel.ChooseSelection(deva, words)));
    }

    /// <summary>An empty result list selects nothing rather than throwing — a lookup that matched nothing is
    /// ordinary.</summary>
    [Fact]
    public void No_results_selects_nothing()
    {
        Assert.Null(DictionaryViewModel.ChooseSelection("anything", Words()));
        Assert.Null(DictionaryViewModel.ChooseSelection(null, Words()));
    }
}
