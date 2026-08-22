using System.Collections.Generic;

namespace CST.Avalonia.Models.Ai
{
    /// <summary>
    /// A way of obtaining the credential for a provider. (#689)
    ///
    /// <para><b>Why a union rather than a flag.</b> The first version of this carried
    /// <c>RequiresKey: bool</c> plus <c>EnvironmentVariables: string[]</c> — which is exactly
    /// <see cref="Key"/> and <see cref="Env"/> flattened into two fields that cannot express a third case.
    /// Vertex authenticates by Application Default Credentials and Bedrock by SigV4 or the AWS credential
    /// chain; neither is a key, and both are on the roadmap. Modelling this as a union means those need
    /// credential machinery rather than a schema change.</para>
    ///
    /// <para>Shape taken from opencode's <c>Integration.Method</c>
    /// (<c>packages/schema/src/integration.ts:60-74</c>, MIT) — see
    /// <c>docs/reference/OpenCode/</c>. Aligning deliberately: the plan is to track their provider work and
    /// fold in changes, and a matching representation makes each sync a mapping rather than a
    /// re-interpretation.</para>
    ///
    /// <para><b>A method is not a connection.</b> <see cref="Env"/> says where a credential might already be
    /// found; it does not mean one has been adopted. Discovery makes a provider <i>available</i>, never
    /// <i>connected</i> — the reader still has to act. That distinction is structural here rather than a rule
    /// someone has to remember.</para>
    /// </summary>
    public abstract record AiCredentialMethod
    {
        private AiCredentialMethod() { }

        /// <summary>The reader pastes an API key, which we store in the OS credential store.</summary>
        public sealed record Key(string? Label = null) : AiCredentialMethod;

        /// <summary>
        /// A key may already be present in one of these environment variables, in precedence order.
        /// Several providers accept more than one name — Google alone answers to three.
        /// </summary>
        public sealed record Env(IReadOnlyList<string> Names) : AiCredentialMethod;

        /// <summary>
        /// An interactive or ambient credential flow rather than a stored secret — Vertex's ADC, Bedrock's
        /// credential chain, a subscription login. Declared here so the schema is ready; no provider in the
        /// current catalogue uses it yet.
        /// </summary>
        public sealed record OAuth(string Id, string Label) : AiCredentialMethod;
    }

    /// <summary>How a prompt's answer is presented. Free text unless <see cref="Options"/> is non-empty.</summary>
    /// <param name="Key">Names the value in <c>AiConnection.Inputs</c>, and is what a URL or header template
    /// substitutes.</param>
    /// <param name="When">Ask only when another input has (or has not) a given value. Azure needs this: it
    /// wants a resource name <i>or</i> an explicit base URL, and asking for both is wrong.</param>
    /// <param name="Secret">
    /// Whether the answer is a credential, and so must never reach <c>settings.json</c>. (#777)
    ///
    /// <para><b>Nothing in the catalogue sets this today</b> — the two prompts that exist are Azure's resource
    /// name and Cloudflare's account id, both identifiers rather than secrets, and both correctly stored in
    /// the clear. The flag exists because the hazard is structural: the moment a provider needs a second
    /// secret, the prompt mechanism is the path of least resistance and it writes to a plaintext file. An AWS
    /// secret access key reaching <c>settings.json</c> that way would be a real leak taken by the easy route,
    /// and without this flag nothing in the types would object.</para>
    ///
    /// <para>A secret answer is filed in the OS credential store under
    /// <c>AiCredentialNames.Input(key)</c> and its key recorded in <c>AiConnectionRecord.SecretInputs</c> —
    /// the same name-here/value-there routing a secret header uses (#771), reused rather than reinvented.</para>
    ///
    /// <para><b>A secret may not be substituted into a base URL.</b> A URL reaches server logs, the Providers
    /// list, and every error message that names the endpoint. Refused at the point of save rather than
    /// discouraged in a comment — see <c>AiConnectionService.SecretInUrl</c>. A header template is the
    /// legitimate destination.</para>
    /// </param>
    public sealed record AiInputPrompt(
        string Key,
        string Message,
        string? Placeholder = null,
        IReadOnlyList<AiPromptOption>? Options = null,
        AiPromptCondition? When = null,
        bool Secret = false);

    /// <summary>One choice in a select-style prompt.</summary>
    public sealed record AiPromptOption(string Label, string Value, string? Hint = null);

    /// <summary>Whether a prompt applies, given the inputs answered so far.</summary>
    public sealed record AiPromptCondition(string Key, AiConditionOperator Op, string Value)
    {
        /// <summary>Evaluates against the inputs gathered so far. An absent key reads as an empty value, so a
        /// <c>NotEquals</c> condition is true before anything has been typed — which is what makes a
        /// conditional field appear rather than hide by default.</summary>
        public bool IsSatisfiedBy(IReadOnlyDictionary<string, string> inputs)
        {
            inputs.TryGetValue(Key, out var actual);
            actual ??= string.Empty;
            return Op == AiConditionOperator.Equals
                ? string.Equals(actual, Value, System.StringComparison.Ordinal)
                : !string.Equals(actual, Value, System.StringComparison.Ordinal);
        }
    }

    public enum AiConditionOperator { Equals, NotEquals }
}
