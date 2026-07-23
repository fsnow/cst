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
    /// Render <paramref name="meaningHtml"/> (one entry's definition, possibly multi-definition) into a full
    /// host document. <paramref name="linkDisplay"/> maps a <c>&lt;see&gt;</c> target (stored in Latin) to
    /// its current-script display form; <paramref name="separator"/> is the merge sentinel between a merged
    /// headword's definitions.
    /// </summary>
    public static string Render(
        string? meaningHtml,
        Func<string, string> linkDisplay,
        string separator,
        string fontFamily,
        double fontSizePt)
    {
        var body = BuildBody(meaningHtml, linkDisplay, separator);
        var family = WebUtility.HtmlEncode(fontFamily);
        // POINTS, not px, to match the book pages (tipitaka-*.xsl size body text in pt). The WebView renders
        // px smaller than the app's font setting implies; pt keeps the meaning in scale with the reader,
        // without a zoom hack (the book pipeline uses none either). (#466)
        var size = fontSizePt.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
              hr.def-sep { border: none; border-top: 1px solid #d0d0d0; margin: 10px 0; }
              @media (prefers-color-scheme: dark) {
                body { color: #e0e0e0; }
                a.see { color: #6db3f2; }
                hr.def-sep { border-top-color: #444; }
              }
            </style>
            </head>
            <body>{{body}}</body>
            </html>
            """;
    }

    private static string BuildBody(string? meaningHtml, Func<string, string> linkDisplay, string separator)
    {
        if (string.IsNullOrEmpty(meaningHtml))
            return string.Empty;

        // A merged headword's definitions are joined by the separator sentinel; render each with a rule
        // between, so the boundary reads as a divider rather than the raw marker. (DICT-1, now in HTML)
        var definitions = meaningHtml.Split(new[] { separator }, StringSplitOptions.None);
        var sb = new StringBuilder();
        for (int i = 0; i < definitions.Length; i++)
        {
            if (i > 0)
                sb.Append("<hr class=\"def-sep\">");
            sb.Append("<div class=\"def\">")
              .Append(TransformSeeTags(definitions[i].Trim(), linkDisplay))
              .Append("</div>");
        }
        return sb.ToString();
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
