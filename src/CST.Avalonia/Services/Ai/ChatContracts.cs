using System;
using System.Collections.Generic;

namespace CST.Avalonia.Services.Ai;

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
/// <param name="MaxTokens">Output cap. Required — the Anthropic API rejects a request without one.</param>
/// <param name="System">System prompt, or null. Anthropic carries it in a top-level field; the
/// OpenAI-compatible shape carries it as a leading message, which the adapter handles.</param>
public sealed record ChatRequest(
    string Model,
    int MaxTokens,
    string? System,
    IReadOnlyList<ChatMessage> Messages);

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

    /// <summary>The request exceeded the model's context window. Actionable: the caller can trim and retry.</summary>
    ContextTooLong,

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
