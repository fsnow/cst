using CST.Avalonia.Input;
using Xunit;

namespace CST.Avalonia.Tests.Input;

/// <summary>
/// #846: plain Cmd/Ctrl+F must reach the active book's find bar from a WebView-hosted view.
///
/// <para><b>Why these assert against generated script text.</b> The relay's keyboard handling is JavaScript
/// injected into a CEF view; there is no JS engine in this suite and no way to raise a real keydown, so the
/// script's text is the only thing reachable from a test. That is weak evidence about behaviour and strong
/// evidence about the one thing that actually went wrong here.</para>
///
/// <para>What went wrong: <c>includeFind</c> reads naturally as "forward find", but it used to mean "swallow
/// find" — and I misread it in exactly that direction while diagnosing the bug, concluding the dead key was
/// deliberate. These tests pin the flag's meaning so the next reader cannot make that inference, and so an
/// inversion of it fails rather than silently killing Cmd+F in the dictionary again.</para>
/// </summary>
public class WebViewShortcutRelayTests
{
    private const string ViewId = "dict_abc123";

    /// <summary>
    /// The dictionary meaning pane (which takes the default) forwards find rather than eating it.
    ///
    /// <para><b>Asserted on the gate, not on the command name.</b> The first version of this test looked for
    /// the absence of <c>'FIND_IN_PAGE'</c> in the opted-out script and failed, correctly: the flag does not
    /// omit the branch, it emits a JavaScript boolean literal into the branch's condition. So the command
    /// name is present in the text either way, and only the literal says which behaviour was built.</para>
    /// </summary>
    [Fact]
    public void With_find_included_the_find_branch_is_live()
    {
        var script = WebViewShortcutRelay.BuildScript(ViewId);

        Assert.Contains("!event.shiftKey && true) { name = 'FIND_IN_PAGE'", script);
    }

    /// <summary>
    /// The PDF viewer opts out: its pages are scans with no text layer, so there is nothing to find.
    ///
    /// <para>This is the assertion that catches the flag being wired backwards — the mistake that produced
    /// #846's original misdiagnosis — because an inversion swaps the literal in both directions.</para>
    /// </summary>
    [Fact]
    public void With_find_excluded_the_find_branch_is_dead()
    {
        var script = WebViewShortcutRelay.BuildScript(ViewId, includeFind: false);

        Assert.Contains("!event.shiftKey && false) { name = 'FIND_IN_PAGE'", script);
    }

    /// <summary>
    /// The command the script emits has to be the one the handler switches on. A mismatch here is invisible
    /// at runtime — <see cref="WebViewShortcutRelay.TryHandle"/> would fall to its default arm and log a
    /// warning, so the key would go on doing nothing and look like the original bug.
    /// </summary>
    [Fact]
    public void The_forwarded_command_is_addressed_to_this_view_and_sequenced()
    {
        var script = WebViewShortcutRelay.BuildScript(ViewId);

        // send() builds "<prefix><name>|VIEW:<id>|SEQ:<n>", and TryHandle drops any message whose view id
        // is not its own — the guard that stops a background WebView acting on the visible one's keystroke.
        Assert.Contains("|VIEW:" + ViewId, script);
        Assert.Contains("|SEQ:", script);
    }

    /// <summary>
    /// Excluding find must not disturb the shortcuts that are not conditional on it. The find branches sit
    /// in the middle of the same if/else chain, so a bad edit there can strand the ones after it.
    /// </summary>
    [Theory]
    [InlineData("'SELECT_BOOK'")]
    [InlineData("'DICTIONARY'")]
    [InlineData("'SETTINGS'")]
    public void The_unconditional_shortcuts_survive_either_setting(string command)
    {
        Assert.Contains(command, WebViewShortcutRelay.BuildScript(ViewId));
        Assert.Contains(command, WebViewShortcutRelay.BuildScript(ViewId, includeFind: false));
    }

    /// <summary>
    /// Shift+F (Search) is still gated on the same flag it always was, and is still distinct from plain F.
    /// </summary>
    [Fact]
    public void Shift_F_remains_the_search_shortcut_and_is_gated_the_same_way()
    {
        Assert.Contains("event.shiftKey && true) { name = 'SEARCH'",
            WebViewShortcutRelay.BuildScript(ViewId));
        Assert.Contains("event.shiftKey && false) { name = 'SEARCH'",
            WebViewShortcutRelay.BuildScript(ViewId, includeFind: false));
    }
}
