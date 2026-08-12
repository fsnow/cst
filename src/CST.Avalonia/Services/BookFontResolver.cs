using System.Linq;
using CST.Conversion;

namespace CST.Avalonia.Services;

/// <summary>
/// Decides which font face a book is rendered in. (#42)
///
/// <para>
/// One rule, in one place: the user's chosen face for that script if they have chosen one, otherwise the
/// shipped default. The result is injected into the single stylesheet as the <c>bookFontFamily</c>
/// parameter at transform time — the app no longer ships a stylesheet per script, and no longer writes any
/// CSS of its own for this.
/// </para>
///
/// <para>
/// An unset preference is stored as empty rather than as a copy of the default, so a user who never chose
/// a face keeps tracking the shipped stack rather than being frozen against whatever it was on the day
/// they first ran the app.
/// </para>
/// </summary>
public static class BookFontResolver
{
    /// <summary>
    /// The CSS font stack for <paramref name="script"/>. Never returns empty: an empty font-family would
    /// leave the browser to choose with no guidance, which for an Indic script usually means tofu.
    /// </summary>
    public static string Resolve(ISettingsService? settingsService, Script script)
    {
        var chosen = settingsService?.Settings.FontSettings.TryGetFont(script, out var setting) == true
            ? setting?.BookFontFamily
            : null;

        // The shipped stacks are ours and already correctly quoted (pinned by tests). A user choice is
        // arbitrary text and has to be made safe before it is interpolated into a stylesheet.
        return string.IsNullOrWhiteSpace(chosen)
            ? BookFontDefaults.For(script)
            : ToCssFontStack(chosen!, script);
    }

    /// <summary>
    /// Turns a user-chosen face into a CSS font stack that is valid and cannot escape the stylesheet.
    ///
    /// <para>
    /// <b>Quoting is not cosmetic.</b> An unquoted CSS family name must be a sequence of identifiers, and
    /// a great many real font names are not — macOS ships "Bodoni 72", "Bodoni 72 Oldstyle" and
    /// "Bodoni 72 Smallcaps", all of which the font picker offers. Emitted bare, <c>font-family: Bodoni 72;</c>
    /// is invalid, the whole declaration is dropped, and the book silently renders in the browser's default
    /// font with nothing to indicate why. (fable review)
    /// </para>
    ///
    /// <para>
    /// <b>Escaping is not optional either.</b> The stack is interpolated into a <c>&lt;style&gt;</c> element,
    /// and the HTML parser ends that element at a literal <c>&lt;/style</c> regardless of CSS string
    /// context — so quoting alone would not contain a face name containing markup. Reachable only by
    /// hand-editing one's own settings file, but the cost of closing it is three characters.
    /// </para>
    /// </summary>
    internal static string ToCssFontStack(string chosen, Script script = Script.Latin)
    {
        // Split on commas so a hand-written stack survives as a stack rather than being quoted whole into
        // one nonsense family name. The picker only ever produces a single name.
        var families = chosen
            .Split(',')
            .Select(f => f.Trim().Trim('"', '\''))
            .Where(f => f.Length > 0)
            .Select(QuoteFamily)
            // Filtered AFTER quoting as well as before it. QuoteFamily DROPS control characters and
            // unpaired surrogates, so a family that was non-empty going in can come back as `""` — and a
            // stored value of "" is not whitespace, survives the pre-quote check, and would otherwise
            // produce `font-family: "";`. That is valid CSS matching no family at all, so the book renders
            // in the browser default: the exact outcome the fallback below exists to prevent, arrived at by
            // a route that skipped it. (fable review, second pass)
            .Where(f => f.Length > 2);

        // A stored value of "," or "''" is not whitespace, so it reaches here and reduces to nothing.
        // Fall back to THIS script's stack rather than Latin's — the Latin stack in a Devanagari book is
        // tofu, which is a worse answer than the default nobody chose. (fable review)
        var stack = string.Join(", ", families);
        return stack.Length > 0 ? stack : BookFontDefaults.For(script);
    }

    private static string QuoteFamily(string family)
    {
        var sb = new System.Text.StringBuilder(family.Length + 8);
        sb.Append('"');
        foreach (var c in family)
        {
            switch (c)
            {
                // CSS string escapes.
                case '"':
                case '\\':
                    sb.Append('\\').Append(c);
                    break;
                // Hex-escaped rather than passed through: these are CSS-identical but cannot terminate the
                // style element or open a tag.
                case '<': sb.Append("\\3c "); break;
                case '>': sb.Append("\\3e "); break;
                case '&': sb.Append("\\26 "); break;
                // A newline would end the CSS string and invalidate the declaration.
                case '\n':
                case '\r': break;
                default:
                    // Control characters and unpaired surrogates are not merely bad CSS — the XmlWriter
                    // behind XslCompiledTransform REJECTS them, so one in a hand-edited settings value
                    // fails the whole transform and the book renders as the error page. Dropped rather
                    // than escaped: no font family legitimately contains them. (fable review)
                    if (c < 0x20 && c != '\t') break;
                    if (char.IsSurrogate(c)) break;
                    sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// True when the user has explicitly chosen a face for this script, as opposed to riding the default.
    /// Not consumed by the app yet — it is the check #573 wants when reporting that a configured face is
    /// no longer installed, since only an explicit choice can go missing.
    /// </summary>
    public static bool HasUserChoice(ISettingsService? settingsService, Script script) =>
        settingsService?.Settings.FontSettings.TryGetFont(script, out var setting) == true
        && !string.IsNullOrWhiteSpace(setting?.BookFontFamily);
}
