using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CST.Avalonia.Services;

/// <summary>
/// Builds the self-contained HTML document the dictionary meaning pane renders in its WebView (#466).
///
/// Every source now returns its definition as an HTML fragment (<c>MeaningHtml</c>): the flat-file
/// dictionaries emit plain text + <c>&lt;see&gt;word&lt;/see&gt;</c> cross-references (joined by a merge
/// separator when a headword has several definitions); DPD/DPPN emit richer structured HTML, already
/// reduced to a safe tag allowlist when the asset was built. This wraps that fragment in a host page that:
///   - carries a strict CSP (no network, no script) — so the pane can never fetch or execute anything;
///   - transforms <c>&lt;see&gt;</c> tags into <c>&lt;a href="cst-see:…"&gt;</c> links (display text in the
///     current script) that the view intercepts via BeforeNavigate — no JavaScript needed;
///   - styles Pāli text in the reader's current script font.
/// Pure/string-only so it is unit-testable without a WebView.
/// </summary>
public static class DictionaryHtmlRenderer
{
    private static readonly Regex SeeTag =
        new(@"<see>(.*?)</see>", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>The custom scheme a <c>&lt;see&gt;</c> cross-reference navigates to; the view cancels the
    /// navigation and looks the word up instead.</summary>
    public const string SeeScheme = "cst-see:";

    /// <summary>
    /// Render <paramref name="meaningHtml"/> (one entry's definition) into a full host document.
    /// <paramref name="linkDisplay"/> maps a <c>&lt;see&gt;</c> target (stored in Latin) to its current-script
    /// display form.
    /// </summary>
    public static string Render(
        string? meaningHtml,
        Func<string, string> linkDisplay,
        string fontFamily,
        double fontSizePt)
        => Render(meaningHtml, linkDisplay, fontFamily, fontSizePt, paintBackground: OperatingSystem.IsWindows());

    /// <summary>
    /// <paramref name="paintBackground"/>: give the page an opaque background instead of leaving it
    /// transparent. Windows only (#1001): there CEF paints white behind a transparent page rather than letting
    /// the Avalonia border show through, so in dark mode the #e0e0e0 text landed on white. macOS keeps the
    /// transparent page it has always had.
    /// </summary>
    public static string Render(
        string? meaningHtml,
        Func<string, string> linkDisplay,
        string fontFamily,
        double fontSizePt,
        bool paintBackground)
    {
        // The fragment is rendered as-is (with <see> turned into links). We do NOT split on the flat-file
        // merge sentinel <hr/>: it also occurs as real content in some HTML sources (e.g. DPPN entries), and
        // a plain <hr/> already renders as the divider a merged flat-file headword wants — so passing it
        // through is both correct for HTML sources and visually right for flat-file. (#466, Fable)
        var body = string.IsNullOrEmpty(meaningHtml) ? "" : TransformSeeTags(meaningHtml, linkDisplay);
        var family = WebUtility.HtmlEncode(fontFamily);
        // POINTS, not px, to match the book pages (tipitaka.xsl sizes body text in pt). The WebView renders
        // px smaller than the app's font setting implies; pt keeps the meaning in scale with the reader,
        // without a zoom hack (the book pipeline uses none either). (#466)
        var size = fontSizePt.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Appended after the base rules so it overrides their 'background: transparent'. Dark is #2b2b2b,
        // the Words list directly above the pane (sampled on Windows 10 dark mode).
        var background = paintBackground
            ? """
              html, body { background: #ffffff; }
              @media (prefers-color-scheme: dark) { html, body { background: #2b2b2b; } }
              """
            : "";

        // CSP: nothing loads (default-src 'none'); only inline styles are allowed, no scripts, no network.
        return $$"""
            <!doctype html>
            <html>
            <head>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy"
                  content="default-src 'none'; style-src 'unsafe-inline'; img-src data:;">
            <style>
              html, body { margin: 0; padding: 0; background: transparent; }
              body {
                font-family: '{{family}}', 'Helvetica Neue', Helvetica, Arial, sans-serif;
                font-size: {{size}}pt;
                line-height: 1.5;
                padding: 8px;
                color: #202020;
                word-wrap: break-word;
              }
              a.see { color: #0b5cad; text-decoration: underline; cursor: pointer; }
              hr { border: none; border-top: 1px solid #d0d0d0; margin: 10px 0; }
              @media (prefers-color-scheme: dark) {
                body { color: #e0e0e0; }
                a.see { color: #6db3f2; }
                hr { border-top-color: #444; }
              }
            {{background}}
            </style>
            </head>
            <body>{{body}}</body>
            </html>
            """;
    }

    // Replace <see>word</see> with an anchor to the cst-see scheme; the link text is the word in the
    // current display script, the href carries the original (Latin) target for lookup.
    private static string TransformSeeTags(string html, Func<string, string> linkDisplay) =>
        SeeTag.Replace(html, m =>
        {
            var target = m.Groups[1].Value.Trim();
            var display = WebUtility.HtmlEncode(linkDisplay(target));
            var href = WebUtility.HtmlEncode(SeeScheme + target);
            return $"<a class=\"see\" href=\"{href}\">{display}</a>";
        });
}
