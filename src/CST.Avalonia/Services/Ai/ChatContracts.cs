using System;
using System.Collections.Generic;

namespace CST.Avalonia.Services.Ai;

/// <summary>Ceilings that are facts about the provider APIs rather than choices of ours.</summary>
public static class AiLimits
{
    /// <summary>
    /// The largest <c>max_tokens</c> valid across every current Claude model, used where the Anthropic API
    /// requires a number and the caller did not choose one.
    ///
    /// <para><b>Why 64K and not 128K.</b> Opus 5, Sonnet 5, Fable 5 and the Opus 4.x family all cap output at
    /// 128K, but <b>Haiku 4.5 caps at 64K</b> — and the model id here is whatever the user typed into Settings,
    /// so the adapter cannot know which it is. 64K is the largest value that cannot 400 on any of them. A user
    /// who wants the full 128K on a large model can say so once #584 carries per-model limits.</para>
    ///
    /// <para>This is a runaway guard, not a size estimate. Streaming is what keeps a cap this high safe: a
    /// non-streaming request anywhere near it would hit the HTTP timeout first, and both adapters stream.</para>
    /// </summary>
    public const int UniversalMaxTokens = 64_000;
}

/// <summary>Who authored a turn. There is no System role — the system prompt is its own request field.</summary>
public enum ChatRole
{
    User,
    Assistant,
}

/// <summary>One turn of the conversation sent to the model.</summary>
public sealed record ChatMessage(ChatRole Role, string Content);

/// <summary>
/// A request to a model, in the shape surface B needs and nothing more. Deliberately narrow: the two adapters
/// translate this into their own wire format, so nothing provider-specific leaks into the caller.
/// </summary>
/// <param name="Model">Provider-specific model id, verbatim as the user configured it.</param>
/// <param name="MaxTokens">
/// Output cap, or null for "do not specify one". <b>Null is the ordinary case</b>: an answer-shaped cap has to
/// predict output length, and on a reasoning model it cannot — reasoning tokens count against the same budget,
/// so the cap silently truncates or produces an empty answer (#601). Cost is better controlled by showing the
/// user what each call spent (AI_SURFACE_B.md §10) than by a limit they never see.
///
/// <para>The two adapters differ because the wire formats do: the OpenAI-compatible shape omits the field
/// entirely, which is the honest expression of "no limit"; the Anthropic Messages API <b>requires</b> a number,
/// so that adapter substitutes <see cref="AiLimits.UniversalMaxTokens"/>.</para>
/// </param>
/// <param name="System">System prompt, or null. Anthropic carries it in a top-level field; the
/// OpenAI-compatible shape carries it as a leading message, which the adapter handles.</param>
/// <param name="ReasoningEffort">
/// How hard the model should think before answering, in the provider's own vocabulary, or null to say nothing
/// and let the provider apply its default. (#671)
///
/// <para><b>Null is the ordinary case, and sending nothing is not the same as sending a default.</b> Support is
/// per-model rather than per-provider, and an unknown field can be a 400 rather than an ignored key — the same
/// failure mode as the sampling parameters that are the reason there is no temperature control. So this is
/// written only when the reader has explicitly chosen a value.</para>
///
/// <para><b>A string, not an enum.</b> The vocabulary is the model's: <c>low/medium/high</c> at most providers,
/// <c>minimal/low/medium/high</c> at OpenAI, <c>none/default</c> on Groq's Qwen3, <c>low/high/max</c> at
/// DeepSeek — 130+ distinct published sets. An enum here would be this app deciding what the levels are, which
/// is the shape #670 forbids, and it would be wrong within a month besides. What reaches this field came from
/// a list the provider published for that model.</para>
///
/// <para><b>Honoured by the OpenAI-compatible adapter only.</b> The Anthropic Messages API expresses this as
/// a <c>thinking</c> object with a token budget rather than an effort string, and mapping one onto the other
/// means choosing numbers — deferred to #779 rather than guessed. Nothing arms this field for an Anthropic
/// connection today, because the levels come from a listing field that adapter's models do not publish; if one
/// ever arrives non-null there it is logged rather than silently dropped.</para>
///
/// <para><b>This is the control that replaced the one #601 removed.</b> Output caps could not govern a
/// reasoning model, because reasoning and answer share the budget; effort governs the reasoning itself, which
/// is the quantity that actually varies by an order of magnitude between models.</para>
/// </param>
public sealed record ChatRequest(
    string Model,
    int? MaxTokens,
    string? System,
    IReadOnlyList<ChatMessage> Messages,
    string? ReasoningEffort = null);

/// <summary>What a <see cref="ChatDelta"/> carries.</summary>
public enum ChatDeltaKind
{
    /// <summary>Answer text. The only kind a caller must render.</summary>
    Text,

    /// <summary>
    /// Model reasoning, segregated rather than dropped so a caller may show it deliberately. Never mix it into
    /// the answer: DeepSeek's reasoning models stream this alongside the answer over the same OpenAI-compatible
    /// surface, and a naive concatenation renders chain-of-thought at the user.
    /// </summary>
    Reasoning,

    /// <summary>
    /// Token accounting. May arrive more than once, and <b>must be merged per field, not wholesale</b>: the
    /// Anthropic stream reports the two halves separately (input at the start, output at the end), so a consumer
    /// that lets a later value supersede an earlier one erases the input count. A null field means "not reported
    /// in this delta", never zero.
    /// </summary>
    Usage,

    /// <summary>
    /// The stream ended early. Emitted INSTEAD of throwing, because by this point the caller is already showing
    /// partial text and losing it would be worse than showing it with an error appended. A pre-stream failure
    /// throws <see cref="AiException"/> instead — see <see cref="IChatProvider.StreamAsync"/>.
    /// </summary>
    Error,
}

/// <summary>One increment of a streamed response.</summary>
public sealed record ChatDelta(
    ChatDeltaKind Kind,
    string? Text = null,
    ChatUsage? Usage = null,
    AiError? Error = null)
{
    public static ChatDelta ForText(string text) => new(ChatDeltaKind.Text, Text: text);
    public static ChatDelta ForReasoning(string text) => new(ChatDeltaKind.Reasoning, Text: text);
    public static ChatDelta ForUsage(ChatUsage usage) => new(ChatDeltaKind.Usage, Usage: usage);
    public static ChatDelta ForError(AiError error) => new(ChatDeltaKind.Error, Error: error);
}

/// <summary>Token counts as the provider reported them. Null where a provider does not report that half.</summary>
public sealed record ChatUsage(int? InputTokens, int? OutputTokens);

/// <summary>
/// The normalized failure set. Callers switch on this rather than on HTTP status codes or provider-specific
/// error bodies, so the UI has one vocabulary regardless of which provider is configured.
/// </summary>
public enum AiErrorKind
{
    /// <summary>No model, endpoint, or (where required) key. Detected before any request is made.</summary>
    NotConfigured,

    /// <summary>Could not reach the provider, or the stream died mid-flight. Distinct from cancellation.</summary>
    Network,

    /// <summary>401/403 — the key is missing, wrong, or lacks access to the model.</summary>
    Unauthorized,

    /// <summary>429. <see cref="AiError.RetryAfter"/> is set when the provider said how long to wait.</summary>
    RateLimited,

    /// <summary>
    /// 402 — the key is valid but has nothing left to spend. (#673)
    ///
    /// <para>Separate from <see cref="Unauthorized"/> because the fix is different and the reader cannot
    /// guess it from a rejected-key message: nothing is wrong with the key, the account behind it is out of
    /// credit. Separate from <see cref="RateLimited"/> because no amount of waiting clears it.</para>
    /// </summary>
    PaymentRequired,

    /// <summary>The request exceeded the model's context window. Actionable: the caller can trim and retry.</summary>
    ContextTooLong,

    /// <summary>
    /// The model rejected a parameter the reader chose — today, reasoning effort. (#671)
    ///
    /// <para><b>Separate because the fix is a setting the reader can see, and no other kind points at it.</b>
    /// Support for effort is per-model rather than per-provider, and an unsupported field can be a 400 rather
    /// than an ignored key. Left as a bare <see cref="Provider"/> error the reader is told "the provider
    /// rejected the request (HTTP 400)" about a request that worked yesterday on a different model, with
    /// nothing connecting it to the control they changed.</para>
    ///
    /// <para>This is #671's stated alternative to predicting support: <i>report</i> a rejection rather than
    /// maintain a list of which models will reject, which would be the curated capability table #670
    /// forbids.</para>
    /// </summary>
    UnsupportedParameter,

    /// <summary>
    /// The app could not assemble what the model needs — no passage at the reference, a book whose XML never
    /// downloaded. Distinct from every kind above because <b>nothing left the machine</b>: it is not the
    /// provider's fault, no tokens were spent, and the fix is in the reader rather than in Settings. (#583)
    /// </summary>
    ContextUnavailable,

    /// <summary>
    /// The turn completed successfully and produced no answer. Usually the whole output budget went to
    /// reasoning (#601). Named rather than folded into <see cref="Provider"/> because it is the one failure a
    /// correct provider layer makes INVISIBLE — segregating reasoning from answer, exactly as it should, leaves
    /// the caller a well-formed blank turn. (#583)
    /// </summary>
    EmptyAnswer,

    /// <summary>
    /// The model hit its output limit and stopped before finishing — <c>finish_reason: "length"</c> on the
    /// OpenAI-compatible shape, <c>stop_reason: "max_tokens"</c> on Anthropic's. (#601)
    ///
    /// <para><b>Distinct from <see cref="EmptyAnswer"/> because it is measured rather than inferred, and because
    /// it catches the case that would otherwise be SILENT.</b> A turn cut off after writing half a translation
    /// ends its stream in exactly the same way a complete one does: the app would render a partial answer under
    /// a citation, indistinguishable from a finished one. An answer that stops mid-verse and says so is a much
    /// smaller problem than one that stops mid-verse and does not.</para>
    ///
    /// <para>Both are kept because not every OpenAI-compatible gateway reports a finish reason. Where one is
    /// reported this is what fires; where none is, <see cref="EmptyAnswer"/> still catches the total case.</para>
    /// </summary>
    Truncated,

    /// <summary>Anything else the provider rejected or failed on.</summary>
    Provider,
}

/// <summary>
/// A provider failure, normalized.
///
/// <para><b>Nothing here echoes request material.</b> <see cref="Message"/> is composed by us, never lifted from
/// the provider's error body: those bodies routinely quote the offending request back, which for surface B means
/// corpus text and the user's own question. <see cref="ProviderCode"/> carries only the provider's short
/// machine-readable token (<c>rate_limit_error</c>, <c>context_length_exceeded</c>) — genuinely useful when
/// diagnosing, and safe to log because it is clamped by <c>AiHttp.SanitizeProviderCode</c> to a token-shaped
/// value. That clamp is what makes the claim true rather than merely intended: the field is provider-controlled,
/// and "OpenAI-compatible" means an arbitrary user-pasted endpoint that could put anything there.</para>
/// </summary>
public sealed record AiError(
    AiErrorKind Kind,
    string Message,
    int? StatusCode = null,
    string? ProviderCode = null,
    TimeSpan? RetryAfter = null);

/// <summary>
/// Thrown for a failure that occurs BEFORE any content has been streamed, where there is no partial answer to
/// preserve and throwing is the more ergonomic contract. Once text is flowing the provider yields
/// <see cref="ChatDeltaKind.Error"/> instead.
/// </summary>
public sealed class AiException : Exception
{
    public AiException(AiError error) : base(error.Message) => Error = error;

    public AiException(AiError error, Exception inner) : base(error.Message, inner) => Error = error;

    public AiError Error { get; }
}
