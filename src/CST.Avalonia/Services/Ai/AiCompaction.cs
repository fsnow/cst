using System.Collections.Generic;
using System.Text;

namespace CST.Avalonia.Services.Ai;

/// <summary>
/// Compaction: a run of older turns replaced, in what the model is SENT, by a summary of them. (#998,
/// ASSISTANT_SESSIONS.md §3.4)
///
/// <para><b>[fsnow]</b>: <i>"Manual and auto at a fraction of context length"</i>; the fraction is <i>"95%, but
/// make this a setting"</i>; <i>"Last 4 turns"</i> stay verbatim. The reference is Claude Code's
/// <c>/compact [instructions]</c> and its auto-compact — <b>[fsnow]</b>: <i>"Claude Code itself is my model and we
/// should aim for feature parity with CC in context management…"</i></para>
///
/// <para><b>What changes and what does not.</b> The transcript on screen keeps every turn; a summarised turn is
/// still there to read, copy and retry. What changes is the conversation the next request replays: the summary
/// first, as one exchange, then the answered turns after it word for word, then the current turn.</para>
///
/// <para><b>The summary's message shape</b> [suggestion, decided in #998]: the summary goes back as an ordinary
/// <c>user</c>/<c>assistant</c> pair, the same shape as every other replayed turn — the user side is the app's own
/// line (<see cref="SummaryAskedLine"/>), the assistant side is the summary itself. The assistant side is where
/// it belongs: the model wrote the summary, so it is shown back as its own words, exactly as its answers are. It
/// keeps the roles alternating (the Anthropic Messages API requires it) and neither half can be empty (the same
/// API refuses an empty text block). The alternatives were worse: a summary in a <c>user</c> message would need a
/// fabricated assistant acknowledgement after it to keep the roles alternating, and a summary folded into the
/// system prompt would make that prompt differ on every compaction and would hide the summary from the Sent block
/// the reader uses to see what was sent.</para>
///
/// <para><b>A second compaction summarises the first.</b> [suggestion] The summariser is handed the standing
/// summary and the turns since it (all but the last four), and the new summary replaces both. So there is only
/// ever one summary in a request, and each compaction record lists every turn it stands in for — the earlier
/// record's turns included — so the latest record alone says what the model was not shown word for word.</para>
/// </summary>
public static class AiCompaction
{
    /// <summary>
    /// How many answered turns stay verbatim after a compaction. <b>[fsnow]</b>: <i>"Last 4 turns"</i>.
    ///
    /// <para>Also the reason compaction needs at least one more than this: with four or fewer turns after the
    /// summary there is nothing older to summarise, and re-summarising the summary alone would only lose detail.</para>
    /// </summary>
    public const int KeepVerbatim = 4;

    /// <summary>
    /// The default for <c>ChatSettings.AutoCompactPercent</c>. <b>[fsnow]</b>: <i>"95%, but make this a setting"</i>.
    /// </summary>
    public const int DefaultAutoCompactPercent = 95;

    /// <summary>
    /// The user side of the replayed summary: the app's own words, never the model's. Stored on each compaction
    /// record as it was sent (<c>AiCompactionRecord.AskedLine</c>) for the reason a turn stores its asked line — a
    /// record of what a model saw must not re-render itself from today's wording.
    ///
    /// <para>Written as a request because it is one: the summary that follows it is the model's answer to being
    /// asked for it. Nothing in it moves between turns — no count, no date — so the replayed prefix stays
    /// byte-stable until the next compaction.</para>
    /// </summary>
    public const string SummaryAskedLine =
        "\u00abEarlier in this conversation\u00bb \u2014 summarise what we discussed before the turns that follow.";

    /// <summary>
    /// Whether a request estimated at <paramref name="estimatedTokens"/> reaches <paramref name="percent"/> of a
    /// context window of <paramref name="contextLength"/> tokens. "Reaches" — at the threshold, not only past it.
    /// Computed in whole numbers, widened, so a large window cannot overflow and 95% of it is not a float.
    /// </summary>
    public static bool Reaches(int estimatedTokens, int contextLength, int percent) =>
        percent > 0 && contextLength > 0 && (long)estimatedTokens * 100 >= (long)contextLength * percent;

    /// <summary>
    /// The turns being compacted, as the summariser reads them: the standing summary first where there is one,
    /// then each turn's question line and the answer as the model wrote it, <c>[[…]]</c> markers and all.
    ///
    /// <para><b>Marked, not stripped</b>, for the reason the replayed answers are marked (#991): the summary is
    /// replayed to the model, and a summary written from marker-free answers would teach it to drop them.</para>
    ///
    /// <para>Rendered here rather than in the template because the template has no loops — deliberately, see
    /// <see cref="PromptPlaceholders"/>. Every heading below is unconditional and true of what follows it.</para>
    /// </summary>
    public static string RenderConversation(AiExchange? previousSummary, IReadOnlyList<AiExchange> turns)
    {
        var text = new StringBuilder();

        if (previousSummary is { } summary && !string.IsNullOrWhiteSpace(summary.Answer))
        {
            text.AppendLine("### The earlier summary");
            text.AppendLine();
            text.AppendLine(summary.Answer.Trim());
            text.AppendLine();
        }

        var number = 0;
        foreach (var turn in turns)
        {
            if (string.IsNullOrWhiteSpace(turn.Question) || string.IsNullOrWhiteSpace(turn.Answer)) continue;

            number++;
            text.AppendLine($"### Turn {number}");
            text.AppendLine();
            text.AppendLine($"**Asked:** {turn.Question.Trim()}");
            text.AppendLine();
            text.AppendLine("**Answered:**");
            text.AppendLine();
            text.AppendLine(turn.Answer.Trim());
            text.AppendLine();
        }

        return text.ToString().TrimEnd();
    }
}

/// <summary>
/// What to summarise. (#998)
/// </summary>
/// <param name="PreviousSummary">The summary already standing in for older turns, or null. Folded into the new
/// one, so a second compaction loses nothing the first kept.</param>
/// <param name="Turns">
/// <b>Only the turns being compacted</b> — never the whole session and never the passage. At the automatic
/// threshold the request that triggered this already fills most of the window, so the summary call has to fit in
/// what is left; and the turns being kept verbatim are about to be sent in full anyway. Each is the replayed form:
/// the asked line and the marked answer.
/// </param>
/// <param name="Instructions">What the reader asked the summary to attend to — Claude Code's
/// <c>/compact [instructions]</c>. Null or blank for none.</param>
public sealed record AiCompactionRequest(
    AiExchange? PreviousSummary,
    IReadOnlyList<AiExchange> Turns,
    string? Instructions = null);

/// <summary>
/// A summary, or why there is none. <b>Never an exception</b> for an expected failure — the caller shows
/// <see cref="Error"/> as a sentence, as a turn's error is shown.
/// </summary>
/// <param name="Summary">The model's summary, <c>[[…]]</c> markers intact. Null on failure.</param>
/// <param name="AskedLine">The user side it is replayed after — <see cref="AiCompaction.SummaryAskedLine"/>.</param>
/// <param name="Notices">Degradations worth telling the reader, e.g. an edited compaction template that was
/// rejected and replaced by the built-in.</param>
public sealed record AiCompactionResult(
    string? Summary,
    string AskedLine,
    AiError? Error,
    string? ProviderId = null,
    string? ModelId = null,
    IReadOnlyList<string>? Notices = null)
{
    public bool Succeeded => Error is null && !string.IsNullOrWhiteSpace(Summary);

    internal static AiCompactionResult Failed(AiError error) =>
        new(null, AiCompaction.SummaryAskedLine, error);
}

/// <summary>
/// A compaction the orchestrator made on its own, in the middle of a turn — what the
/// <see cref="AiTurnEventKind.Compacted"/> event carries, so the caller can record it. (#998)
/// </summary>
/// <param name="SummarisedExchanges">
/// How many entries from the FRONT of <see cref="AiTurnRequest.History"/>, as the caller passed it, the summary
/// now stands in for. The previous <see cref="AiTurnRequest.Summary"/>, where there was one, is always folded in
/// as well — so the caller's new record covers the previous record's turns plus these.
/// </param>
/// <remarks>Always automatic — the threshold, or the provider rejecting the request as too long. Manual
/// compaction goes through <see cref="IAiChatOrchestrator.CompactAsync"/> and never arrives as an event.</remarks>
public sealed record AiCompacted(
    string AskedLine,
    string Summary,
    int SummarisedExchanges,
    string? ProviderId,
    string? ModelId);
