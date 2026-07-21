using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CST.Avalonia.Services.LocalApi
{
    /// <summary>
    /// Progressive-discovery layering over the single llms.txt source (#259). The monolith carries
    /// <c>&lt;!--doc:TOPIC--&gt;…&lt;!--/doc:TOPIC--&gt;</c> region markers; from that ONE source we serve the
    /// full document (markers stripped) at <c>/llms.txt</c> and <c>/llms-full.txt</c>, and per-topic slices at
    /// <c>/docs/{topic}.md</c> — so a slice can never drift from the monolith. The default <c>/llms.txt</c> is
    /// unchanged except for a short pointer to the slices, so no agent experience regresses.
    /// </summary>
    internal static class LayeredDocs
    {
        // Topic id -> (short title, the endpoints it covers). Order is the pointer's display order.
        public static readonly IReadOnlyList<(string Topic, string Title, string Covers)> Topics = new[]
        {
            ("search", "finding words", "/v1/search, /v1/occurrences"),
            ("reading", "locating & reading", "/v1/books, /v1/passage, the {…} apparatus"),
            ("dictionary", "glosses & lemmas", "/v1/dictionary, /v1/lemma, /v1/forms"),
            ("scripts", "scripts", "/v1/scripts, /v1/convert"),
            // Documented unconditionally even when the user has not granted remote control: an agent that
            // knows navigate exists can tell them how to enable it, which a hidden endpoint cannot. (#187)
            ("navigate", "showing the user a passage", "/v1/navigate"),
        };

        public static bool IsTopic(string topic) => Topics.Any(t => t.Topic == topic);

        private static readonly Regex MarkerLine =
            new(@"^[ \t]*<!--/?doc:\w+-->[ \t]*\r?\n?", RegexOptions.Multiline | RegexOptions.Compiled);

        // A whole topic region (open marker … its nearest close). Regions are sequential, never nested, so a
        // non-greedy match pairs each open with its own close.
        private static readonly Regex TopicRegion =
            new(@"[ \t]*<!--doc:\w+-->.*?<!--/doc:\w+-->[ \t]*\r?\n?", RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex MultiBlank = new(@"\n{3,}", RegexOptions.Compiled);

        // Asset-dependent blocks: <!--dpd-->…<!--/dpd--> wraps content that only works when the dpd-cst-subset
        // asset is installed (lemma / forms / deconstruct endpoints + the DPD dictionary source). These nest
        // INSIDE the doc:TOPIC regions; GateDpd runs FIRST so the doc-marker processing then sees a doc that
        // already reflects asset presence.
        private static readonly Regex DpdMarkerLine =
            new(@"^[ \t]*<!--/?dpd-->[ \t]*\r?\n?", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex DpdRegion =
            new(@"[ \t]*<!--dpd-->.*?<!--/dpd-->[ \t]*\r?\n?", RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>Gate the DPD/lemma blocks on asset presence: when the dpd-cst-subset asset is ABSENT, drop the
        /// whole <c>&lt;!--dpd--&gt;…&lt;!--/dpd--&gt;</c> regions so agents don't discover functionality that only
        /// 503s (matching the MCP tools, which are already unregistered when the asset is absent); when PRESENT,
        /// just remove the marker lines and keep the content. Apply BEFORE the doc-marker processing.</summary>
        public static string GateDpd(string raw, bool assetAvailable) =>
            assetAvailable ? DpdMarkerLine.Replace(raw, "") : DpdRegion.Replace(raw, "");

        /// <summary>Remove every region marker line (for the full-document output).</summary>
        public static string StripMarkers(string raw) => MarkerLine.Replace(raw, "");

        /// <summary>The THIN index: the monolith with every topic REGION (the detail) removed — leaving only the
        /// cross-cutting essentials (intro, connecting/auth, output/error/terminology conventions) — plus the
        /// progressive-discovery pointer. This is what <c>/llms.txt</c> serves, so an agent reads a small
        /// orientation and fetches only the slice(s) it needs; the full doc stays at <c>/llms-full.txt</c>. (#259)</summary>
        public static string ThinIndex(string raw)
        {
            var index = TopicRegion.Replace(raw, "");           // drop the detail
            index = MultiBlank.Replace(StripMarkers(index), "\n\n");  // tidy the gaps left behind
            return WithPointer(index.TrimEnd() + "\n");
        }

        /// <summary>The progressive-discovery pointer injected into the <c>/llms.txt</c> thin index.</summary>
        public static string Pointer()
        {
            var sb = new StringBuilder();
            sb.Append("## Progressive discovery — where the details are\n\n");
            sb.Append("This is a THIN index. Each endpoint's full reference lives in a topic doc; FETCH ONLY the ")
              .Append("one(s) your task needs (each is far smaller than the whole reference):\n");
            foreach (var (topic, title, covers) in Topics)
                sb.Append($"- `GET /docs/{topic}.md` — {title}: {covers}\n");
            sb.Append("- `GET /llms-full.txt` — the ENTIRE reference in one fetch, if you'd rather load it all.\n\n");
            return sb.ToString();
        }

        /// <summary>Inject the pointer just before the first section (<c>## Connecting</c>), else prepend it.</summary>
        public static string WithPointer(string full)
        {
            const string anchor = "## Connecting";
            int at = full.IndexOf(anchor, System.StringComparison.Ordinal);
            return at < 0 ? Pointer() + full : full[..at] + Pointer() + full[at..];
        }

        /// <summary>The topic slice: the concatenation of the topic's marked regions under a short header, or
        /// null for an unknown topic. Markers are stripped from the emitted content.</summary>
        public static string? Slice(string raw, string topic)
        {
            if (!IsTopic(topic)) return null;
            string open = $"<!--doc:{topic}-->", close = $"<!--/doc:{topic}-->";
            var body = new StringBuilder();
            int i = 0;
            while (true)
            {
                int s = raw.IndexOf(open, i, System.StringComparison.Ordinal);
                if (s < 0) break;
                int contentStart = s + open.Length;
                int e = raw.IndexOf(close, contentStart, System.StringComparison.Ordinal);
                if (e < 0) break;
                body.Append(StripMarkers(raw[contentStart..e]).Trim('\n')).Append("\n\n");
                i = e + close.Length;
            }
            if (body.Length == 0) return null;
            var (_, title, _) = Topics.First(t => t.Topic == topic);
            string header =
                $"# CST Reader local API — {topic} ({title})\n\n" +
                "A topic slice of the CST Reader local API. Cross-cutting essentials (romanized output by default, " +
                "error codes) and the auth handshake are in the index at `/llms.txt`; the whole reference is at " +
                "`/llms-full.txt`.\n\n";
            return header + body.ToString().TrimEnd() + "\n";
        }
    }
}
