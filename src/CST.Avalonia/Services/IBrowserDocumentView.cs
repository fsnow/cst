namespace CST.Avalonia.Services;

/// <summary>
/// A document view whose content is a CEF browser: the book reader, the source-PDF viewer, the Welcome
/// page. (#621)
///
/// <para>
/// Marks the views for which <b>CEF's own focus callback is authoritative and Avalonia's is not</b>. Focus
/// inside these lands on a native surface, so Avalonia never sees the click that matters; what it does see
/// is the view control itself being focused when the window is activated or the layout rebuilt — which
/// names whichever document held focus last, not the one the user just clicked.
/// </para>
///
/// <para>
/// Measured, in a three-way split in a floating window: every window-activation echo arrived as
/// <c>source=BookDisplayView</c> and named the same stale book regardless of which book was clicked,
/// landing within ~15ms of the correct CEF report. Whichever arrived last won, so a single click targeted
/// the right book only about half the time, while a second click — which produces no echo — always worked.
/// </para>
///
/// <para>
/// The marker exists rather than a type list in <see cref="DocumentFocusReporter"/> so that a fourth
/// browser-hosting document, whenever one appears, states this property about itself instead of relying on
/// someone finding a list in another namespace.
/// </para>
/// </summary>
internal interface IBrowserDocumentView
{
}
