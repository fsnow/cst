using System.Collections.Generic;
using System.Linq;

namespace CST.Avalonia.Services.Ai;

/// <summary>
/// The token estimate the assistant reports. (#672)
///
/// <para><b>Still an estimate, and still never a limit.</b> There is no local tokenizer and the endpoints do
/// not agree on one, so this is a ratio applied to a character count. What changed in #672 is that the ratio
/// is measured and the string is the right string; nothing here should ever gate a request.</para>
/// </summary>
public static class AiTokens
{
    /// <summary>
    /// Characters per token for romanized Pāli, <b>measured</b> rather than assumed.
    ///
    /// <para>The figure it replaces was 4 — the familiar rule of thumb for English prose, applied to a corpus
    /// that tokenizes nothing like English. Across 292 real passage windows drawn from every third book and
    /// converted to Latin exactly as the bundler sends them (161,266 characters):</para>
    ///
    /// <list type="bullet">
    ///   <item><description><c>o200k_base</c> — 2.30 characters per token</description></item>
    ///   <item><description><c>cl100k_base</c> — 1.73</description></item>
    /// </list>
    ///
    /// <para>So the old figure under-counted by between 1.7x and 2.3x, on a number shown to the reader. 2.0 is
    /// taken deliberately below the modern tokenizer's measurement and above the older one's: for a figure
    /// nobody can make exact, over-reporting is the safer error — a reader surprised by a bigger number is not
    /// harmed, one surprised by a bill is. Anthropic's tokenizer is not public, so it is not in the sample; the
    /// providers' own reported input-token counts are the way to check this against reality in use.</para>
    ///
    /// <para>Re-derive with <c>docs/testing/token-ratio/</c>.</para>
    /// </summary>
    public const double PaliCharsPerToken = 2.0;

    /// <summary>An estimate for the given strings, taken together. Nulls and blanks contribute nothing.</summary>
    public static int Estimate(params string?[] parts) =>
        Estimate(parts.AsEnumerable());

    public static int Estimate(IEnumerable<string?> parts)
    {
        var characters = parts.Sum(p => p?.Length ?? 0);
        return (int)(characters / PaliCharsPerToken);
    }
}
