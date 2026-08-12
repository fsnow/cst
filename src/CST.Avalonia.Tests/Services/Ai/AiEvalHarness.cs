using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Services.Ai.Eval;
using CST.Avalonia.Services.Tools;
using CST.Navigation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// Runs the fixed evaluation set across the model matrix and writes a scored report. (#587, AI_SURFACE_B.md §13)
///
/// <para><b>These are the only surface-B tests that spend money, so they are opt-in and silent by default.</b>
/// It runs only when <c>CST_AI_EVAL=1</c>; otherwise it returns immediately. A live call on every
/// <c>dotnet test</c> would bill for a full run every time anyone touched an unrelated file.</para>
///
/// <para><b>It scores the RAW answer, so it calls the provider directly rather than through the orchestrator.</b>
/// The orchestrator strips quote markers on the way out, by design — and the markers are the measurement. It
/// still uses the real bundler, the real templates and the real prompt builder, so what is scored is what the
/// app would actually send.</para>
///
/// <para><b>What it does not do is decide anything.</b> It reports counts and evidence; the tier a model
/// belongs in is a judgement recorded by a person in <c>PALI_FIDELITY_CASES.md</c> and the model registry. A
/// harness trusted to promote and demote models would quietly become the definition of fidelity, which is the
/// failure that file is organized to avoid.</para>
///
/// <code>
///   CST_AI_EVAL=1 \
///   CST_AI_EVAL_BASE_URL=http://localhost:11434/v1 \
///   dotnet test --filter "FullyQualifiedName~AiEvalHarness"
/// </code>
/// </summary>
public class AiEvalHarness
{
    private readonly ITestOutputHelper _out;

    public AiEvalHarness(ITestOutputHelper output) => _out = output;

    // ---- The case set, as data -----------------------------------------------------------------------------

    private sealed record CaseSet(
        [property: JsonPropertyName("updated")] string? Updated,
        [property: JsonPropertyName("terminology")] Terminology? Terminology,
        [property: JsonPropertyName("models")] IReadOnlyList<string>? Models,
        [property: JsonPropertyName("cases")] IReadOnlyList<EvalCase>? Cases);

    private sealed record Terminology(
        [property: JsonPropertyName("discouraged")] IReadOnlyList<string>? Discouraged);

    private sealed record EvalCase(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("task")] string Task,
        [property: JsonPropertyName("bookId")] string BookId,
        [property: JsonPropertyName("paragraph")] int Paragraph,
        [property: JsonPropertyName("userQuestion")] string? UserQuestion = null,
        [property: JsonPropertyName("case")] string? Case = null,
        [property: JsonPropertyName("note")] string? Note = null,
        [property: JsonPropertyName("failureSignatures")] IReadOnlyList<string>? FailureSignatures = null);

    [Fact]
    public async Task Run()
    {
        if (Environment.GetEnvironmentVariable("CST_AI_EVAL") != "1")
        {
            _out.WriteLine("Skipped: set CST_AI_EVAL=1 to run. These are the only surface-B tests that spend money.");
            return;
        }

        var baseUrl = Environment.GetEnvironmentVariable("CST_AI_EVAL_BASE_URL")
                      ?? "http://localhost:11434/v1";
        var apiKey = Environment.GetEnvironmentVariable("CST_AI_EVAL_API_KEY");
        var xmlDir = Environment.GetEnvironmentVariable("CST_AI_EVAL_XML")
                     ?? Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                         "Library/Application Support/CSTReader/xml");

        var caseSet = LoadCaseSet();
        var models = (Environment.GetEnvironmentVariable("CST_AI_EVAL_MODELS")?.Split(',')
                      ?? caseSet.Models?.ToArray()
                      ?? Array.Empty<string>())
            .Select(m => m.Trim()).Where(m => m.Length > 0).ToList();

        Assert.NotEmpty(models);
        Assert.NotEmpty(caseSet.Cases ?? Array.Empty<EvalCase>());

        var settings = new Settings { XmlBooksDirectory = xmlDir };
        var settingsService = new Mock<ISettingsService>();
        settingsService.SetupGet(s => s.Settings).Returns(settings);

        var bundler = new AiContextBundler(
            new PassageTool(settingsService.Object), null, "eval", NullLogger<AiContextBundler>.Instance);
        var prompts = new PromptBuilder(new PromptTemplateStore(
            Path.Combine(Path.GetTempPath(), "cst-eval-" + Guid.NewGuid().ToString("N")),
            NullLogger<PromptTemplateStore>.Instance));
        var registry = new ModelRegistry(NullLogger<ModelRegistry>.Instance);

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var report = new StringBuilder();
        report.AppendLine($"# Surface B evaluation — {DateTime.Now:yyyy-MM-dd HH:mm}");
        report.AppendLine();
        report.AppendLine($"Case set updated {caseSet.Updated}. Endpoint `{baseUrl}`.");
        report.AppendLine();
        report.AppendLine("| model | tier | case | quotes | unbalanced | unmarked Pāli | ungrounded quotes | bad refs | terminology | signatures |");
        report.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---|---|");

        foreach (var model in models)
        {
            var provider = new OpenAiCompatibleProvider(
                http,
                new OpenAiCompatibleOptions(baseUrl, apiKey),
                NullLogger<OpenAiCompatibleProvider>.Instance);
            var tier = registry.Rate(model).Tier;

            foreach (var evalCase in caseSet.Cases!)
            {
                var (score, note) = await RunCaseAsync(
                    provider, model, evalCase, bundler, prompts, caseSet.Terminology?.Discouraged);

                if (score is null)
                {
                    report.AppendLine($"| `{model}` | {tier} | {evalCase.Id} | — | — | — | — | — | — | **{note}** |");
                    _out.WriteLine($"{model} / {evalCase.Id}: {note}");
                    continue;
                }

                report.AppendLine(
                    $"| `{model}` | {tier} | {evalCase.Id} | {score.Quotes} | {score.UnbalancedMarkers} | "
                    + $"{Cell(score.UnmarkedPali)} | {Cell(score.QuotesNotInPassage)} | "
                    + $"{Cell(score.UnsupportedReferences)} | {Cell(score.TerminologyLapses)} | "
                    + $"{Cell(score.FailureSignatures)} |");

                _out.WriteLine($"{model} / {evalCase.Id}: {(score.Clean ? "clean" : "flagged")}");
            }
        }

        report.AppendLine();
        report.AppendLine("Counts and evidence only. Tier changes are a judgement recorded by a person in");
        report.AppendLine("`PALI_FIDELITY_CASES.md` and `model-registry.json` — not by this harness.");

        var path = Environment.GetEnvironmentVariable("CST_AI_EVAL_REPORT")
                   ?? Path.Combine(Path.GetTempPath(), $"cst-ai-eval-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        await File.WriteAllTextAsync(path, report.ToString());
        _out.WriteLine($"\nReport: {path}");
        _out.WriteLine(report.ToString());
    }

    private static async Task<(AnswerScore? Score, string Note)> RunCaseAsync(
        IChatProvider provider,
        string model,
        EvalCase evalCase,
        IAiContextBundler bundler,
        IPromptBuilder prompts,
        IReadOnlyList<string>? discouraged)
    {
        if (!TryParseTask(evalCase.Task, out var task)) return (null, $"unknown task '{evalCase.Task}'");

        AiContextBundle bundle;
        RenderedPrompt prompt;
        try
        {
            bundle = await bundler.BuildAsync(new AiContextRequest(
                task, evalCase.BookId, "English",
                new NavigationReference.Paragraph(evalCase.Paragraph),
                UserQuestion: evalCase.UserQuestion));
            prompt = prompts.Build(bundle);
        }
        catch (Exception ex)
        {
            return (null, $"context failed: {ex.Message}");
        }

        var answer = new StringBuilder();
        try
        {
            var request = new ChatRequest(
                model, prompt.MaxOutputTokens, prompt.System,
                new[] { new ChatMessage(ChatRole.User, prompt.UserContent) });

            await foreach (var delta in provider.StreamAsync(request))
            {
                // Only the answer channel. Reasoning is segregated by the provider and is not what is scored —
                // a model may think in any style it likes.
                if (delta.Kind == ChatDeltaKind.Text && delta.Text is { Length: > 0 } text) answer.Append(text);
                if (delta.Kind == ChatDeltaKind.Error) return (null, $"stream error: {delta.Error!.Kind}");
            }
        }
        catch (AiException ex)
        {
            return (null, $"request failed: {ex.Error.Kind}");
        }

        if (answer.Length == 0) return (null, "empty answer (#601)");

        return (AnswerScorer.Score(
            answer.ToString(), AnswerScorer.QuotableText(bundle), bundle.Citation, discouraged,
            evalCase.FailureSignatures),
            "ok");
    }

    private static string Cell(IReadOnlyList<string> values) =>
        values.Count == 0 ? "-" : $"**{values.Count}**: " + string.Join("; ", values.Take(4)).Replace("|", "\\|");

    private static bool TryParseTask(string value, out AiTask task)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "explain": task = AiTask.Explain; return true;
            case "translate": task = AiTask.Translate; return true;
            case "grammar": task = AiTask.Grammar; return true;
            case "word-by-word" or "wordbyword": task = AiTask.WordByWord; return true;
            default: task = default; return false;
        }
    }

    private static CaseSet LoadCaseSet()
    {
        var path = FindCaseFile();
        Assert.True(File.Exists(path), $"case set not found at {path}");

        var json = File.ReadAllText(path);
        var set = JsonSerializer.Deserialize<CaseSet>(json);
        Assert.NotNull(set);
        return set!;
    }

    /// <summary>Walk up from the test binary to the repo, so the case set is read from the working tree.</summary>
    internal static string FindCaseFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "testing", "ai-eval", "cases.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return "docs/testing/ai-eval/cases.json";
    }

    /// <summary>
    /// The case set is data, so nothing about it is checked at compile time — and it is read only on a paid
    /// run, where a typo would surface as a wasted matrix. This runs every build instead.
    /// </summary>
    [Fact]
    public void The_case_set_is_well_formed()
    {
        var set = LoadCaseSet();

        Assert.NotEmpty(set.Cases!);
        Assert.NotEmpty(set.Models!);

        foreach (var c in set.Cases!)
        {
            Assert.True(TryParseTask(c.Task, out _), $"{c.Id}: unknown task '{c.Task}'");
            Assert.False(string.IsNullOrWhiteSpace(c.BookId), c.Id);
            Assert.True(c.Paragraph > 0, c.Id);

            // Every signature compiles. A broken pattern makes a case that can never fail, which is
            // indistinguishable from one every model passes.
            foreach (var pattern in c.FailureSignatures ?? Array.Empty<string>())
                _ = new System.Text.RegularExpressions.Regex(pattern);
        }

        // Distinct ids, since the report is keyed on them.
        Assert.Equal(set.Cases!.Count, set.Cases!.Select(c => c.Id).Distinct().Count());
    }
}
