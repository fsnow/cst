using CST.Avalonia.Services;
using CST.Avalonia.Views;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Which Avalonia focus landings count as interactions (#621).
///
/// <para>
/// This rule was written from a measurement, not from reasoning, and the measurement is worth keeping
/// beside it. In a three-way split in a floating window, a single click into a book targeted the right book
/// only about half the time, while a second click always worked. The log showed why: activating the window
/// makes Avalonia focus the document VIEW, which emits a focus event naming whichever document held focus
/// last — the same stale book every time, regardless of which one was clicked — landing within ~15ms of the
/// correct CEF report. Whichever arrived second won.
/// </para>
/// </summary>
public class DocumentFocusReporterTests
{
    private sealed class FakeBrowserView : IBrowserDocumentView
    {
    }

    private sealed class SomethingElse
    {
    }

    [Fact]
    public void Focus_landing_on_a_browser_view_is_not_an_interaction()
    {
        // The echo. Ignoring it loses nothing: CEF reports this document's real focus directly.
        Assert.False(DocumentFocusReporter.ShouldReport(new FakeBrowserView()));
    }

    [Fact]
    public void Focus_landing_anywhere_else_is()
    {
        // The reason this feed exists at all — a tab-strip click raises no activation event when the tab is
        // already the active one in its own split, and no CEF focus when it lands on the header.
        Assert.True(DocumentFocusReporter.ShouldReport(new SomethingElse()));
        Assert.True(DocumentFocusReporter.ShouldReport(null));
    }

    [Fact]
    public void Every_browser_hosting_document_view_carries_the_marker()
    {
        // The rule is only as complete as the marker's application. A browser-hosting view that forgets it
        // reintroduces the stale echo for its own document, and the symptom — a command acting on the wrong
        // tab about half the time — reads as a race rather than as a missing interface.
        Assert.True(typeof(IBrowserDocumentView).IsAssignableFrom(typeof(BookDisplayView)));
        Assert.True(typeof(IBrowserDocumentView).IsAssignableFrom(typeof(PdfDisplayView)));
        Assert.True(typeof(IBrowserDocumentView).IsAssignableFrom(typeof(WelcomeView)));
    }
}
