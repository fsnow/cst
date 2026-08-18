using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CST.Avalonia.Models.Ai
{
    /// <summary>
    /// Substitutes a connection's inputs into a base URL or header value. (#689)
    ///
    /// <para>Several providers do not have a base URL so much as a shape:
    /// <c>https://{resourceName}.openai.azure.com/openai/v1</c> (Azure),
    /// <c>https://gateway.ai.cloudflare.com/v1/{accountId}/{gatewayId}/compat</c> (Cloudflare AI Gateway),
    /// <c>https://bedrock-runtime.{region}.amazonaws.com</c> (Bedrock). Without substitution there is nowhere
    /// to put the reader's own resource name, and each of those providers becomes a special case in code.</para>
    ///
    /// <para><b>One mechanism, deliberately unlike upstream.</b> opencode carries the same templates in its
    /// catalogue but expands them in hand-written per-provider plugin code — e.g. <c>expandAccountId</c> in
    /// <c>packages/core/src/plugin/provider/cloudflare-workers-ai.ts:74</c> does a literal
    /// <c>replaceAll("${CLOUDFLARE_ACCOUNT_ID}", …)</c>, and every provider that needs it writes its own. A
    /// single generic substitution is less code and cannot drift between providers, so this is one of the
    /// places we do NOT mirror them. Placeholders are <c>{key}</c>, keyed to the prompt that collects them,
    /// rather than upstream's <c>${ENV_VAR}</c>, which conflates "where the value came from" with "what it is
    /// called".</para>
    /// </summary>
    public static class AiTemplate
    {
        /// <summary>
        /// Replaces every <c>{key}</c> with the matching input.
        ///
        /// <para><b>An unresolved placeholder is left verbatim</b> rather than replaced with an empty string.
        /// Emptying it would silently produce a plausible-looking but wrong URL —
        /// <c>https://.openai.azure.com/…</c> — that fails at request time with a DNS error naming nothing.
        /// Left intact, the value is visibly unfinished and <see cref="HasUnresolvedPlaceholders"/> can refuse
        /// it before anything is sent.</para>
        /// </summary>
        public static string Expand(string template, IReadOnlyDictionary<string, string> inputs)
        {
            if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0) return template;

            var sb = new StringBuilder(template.Length);
            int i = 0;
            while (i < template.Length)
            {
                int open = template.IndexOf('{', i);
                if (open < 0) { sb.Append(template, i, template.Length - i); break; }

                int close = template.IndexOf('}', open + 1);
                if (close < 0) { sb.Append(template, i, template.Length - i); break; }

                sb.Append(template, i, open - i);
                var key = template.Substring(open + 1, close - open - 1);

                if (inputs.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                    sb.Append(value);
                else
                    sb.Append('{').Append(key).Append('}');   // leave it visible

                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>True when any <c>{key}</c> survived expansion, i.e. the connection is not yet usable.</summary>
        public static bool HasUnresolvedPlaceholders(string expanded) =>
            expanded.IndexOf('{') >= 0 && expanded.IndexOf('}') > expanded.IndexOf('{');

        /// <summary>The placeholder names a template needs, in order of first appearance.</summary>
        public static IReadOnlyList<string> PlaceholdersIn(string template)
        {
            var keys = new List<string>();
            if (string.IsNullOrEmpty(template)) return keys;

            int i = 0;
            while (i < template.Length)
            {
                int open = template.IndexOf('{', i);
                if (open < 0) break;
                int close = template.IndexOf('}', open + 1);
                if (close < 0) break;

                var key = template.Substring(open + 1, close - open - 1);
                if (key.Length > 0 && !keys.Contains(key, StringComparer.Ordinal)) keys.Add(key);
                i = close + 1;
            }
            return keys;
        }
    }
}
