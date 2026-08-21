using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The token estimate. (#672)
///
/// <para>What is worth pinning here is not arithmetic — it is the two things that were wrong. The ratio was
/// English's, on a Pāli corpus; and the figure reported as "estimated context" measured the bundle rather
/// than the prompt that was sent. The first is a constant with a measurement behind it
/// (<c>docs/testing/token-ratio/</c>); the second is a question of which string is passed in, which is why
/// <see cref="AiTokens.Estimate(string?[])"/> is variadic and takes whatever the caller actually sends.</para>
/// </summary>
public sealed class AiTokensTests
{
    [Fact]
    public void The_ratio_is_the_measured_one_not_the_English_rule_of_thumb()
    {
        // 4 chars/token is the English figure this replaced. The measurement across 292 real Latin-script
        // windows put the corpus between 1.73 (cl100k) and 2.30 (o200k) — so anything at or above 2.5 means
        // the constant has drifted back towards a prose-English assumption without a new measurement.
        Assert.True(AiTokens.PaliCharsPerToken < 2.5,
            $"{AiTokens.PaliCharsPerToken} chars/token is above the measured range for Pāli");
        Assert.True(AiTokens.PaliCharsPerToken >= 1.5,
            $"{AiTokens.PaliCharsPerToken} chars/token is below anything measured");
    }

    [Fact]
    public void A_thousand_characters_of_Pali_is_estimated_well_above_the_old_figure()
    {
        var pali = new string('a', 1000);

        // The old estimator would have said 250. Under-reporting by ~2x is the defect, so the new figure must
        // be materially larger rather than incidentally different.
        Assert.True(AiTokens.Estimate(pali) >= 400, $"got {AiTokens.Estimate(pali)}");
    }

    [Fact]
    public void Every_part_counts_because_every_part_is_sent()
    {
        // The bug: the system prompt, the preset template and the reader's own question were all sent and none
        // of them counted. Estimating the parts together must exceed estimating any one of them.
        const string system = "You are helping a reader of Pāli texts.";
        const string user = "appamādo amatapadaṃ — explain this line.";

        Assert.Equal(
            AiTokens.Estimate(system + user),
            AiTokens.Estimate(system, user));
        Assert.True(AiTokens.Estimate(system, user) > AiTokens.Estimate(system));
    }

    [Fact]
    public void Absent_parts_contribute_nothing_rather_than_throwing()
    {
        // A prompt half can legitimately be empty, and an estimate is never worth a crash mid-turn.
        Assert.Equal(0, AiTokens.Estimate(null, "", "   ".Substring(0, 0)));
        Assert.Equal(AiTokens.Estimate("abcd"), AiTokens.Estimate(null, "abcd"));
    }
}
