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
        };

        public static bool IsTopic(string topic) => Topics.Any(t => t.Topic == topic);

        private static readonly Regex MarkerLine =
            new(@"^[ \t]*<!--/?doc:\w+-->[ \t]*\r?\n?", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>Remove every region marker line (for the full-document output).</summary>
        public static string StripMarkers(string raw) => MarkerLine.Replace(raw, "");

        /// <summary>The pointer block injected into <c>/llms.txt</c> so agents learn the slices exist.</summary>
        public static string Pointer()
        {
            var sb = new StringBuilder();
            sb.Append("## Progressive discovery\n\n");
            sb.Append("This is the full reference. To spend fewer tokens, fetch only the slice you need — each is ")
              .Append("a subset of THIS document (nothing here is lost), served from the same source so it can't drift:\n");
            foreach (var (topic, title, covers) in Topics)
                sb.Append($"- `GET /docs/{topic}.md` — {title}: {covers}\n");
            sb.Append("- `GET /llms-full.txt` — this entire document in one fetch.\n\n");
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
                "A focused slice of `/llms.txt`. Shared conventions (output is romanized by default, the scripts, " +
                "opaque cursors, error status codes) and the auth handshake live in `/llms.txt` (or the whole doc " +
                "at `/llms-full.txt`).\n\n";
            return header + body.ToString().TrimEnd() + "\n";
        }
    }
}
