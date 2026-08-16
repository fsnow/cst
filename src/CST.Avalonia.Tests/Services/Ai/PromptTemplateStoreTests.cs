using System;
using System.IO;
using System.Linq;
using CST.Avalonia.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The prompt-template store: built-in defaults as embedded resources, user overrides on disk. (#582)
///
/// <para>The shipped templates are asserted here too. They are data, so nothing about them is checked at
/// compile time — a mistyped placeholder in a resource file would otherwise first show up as a literal brace
/// pair in a request the user paid for.</para>
/// </summary>
public class PromptTemplateStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly PromptTemplateStore _store;

    public PromptTemplateStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cst-prompts-" + Guid.NewGuid().ToString("N"));
        _store = new PromptTemplateStore(_dir, NullLogger<PromptTemplateStore>.Instance);
    }

    /// <summary>The smallest override that passes validation for the Explain preset.</summary>
    private const string ExplainMinimum = "{{citation}} {{passage}} {{selection}} {{userQuestion}}";

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData(PromptTemplateNames.System)]
    [InlineData(PromptTemplateNames.Explain)]
    [InlineData(PromptTemplateNames.Translate)]
    [InlineData(PromptTemplateNames.Grammar)]
    [InlineData(PromptTemplateNames.WordByWord)]
    [InlineData(PromptTemplateNames.ExplainSelection)]
    [InlineData(PromptTemplateNames.TranslateSelection)]
    [InlineData(PromptTemplateNames.GrammarSelection)]
    [InlineData(PromptTemplateNames.WordByWordSelection)]
    [InlineData(PromptTemplateNames.Ask)]
    [InlineData(PromptTemplateNames.AskSelection)]
    public void Every_shipped_template_passes_the_validation_it_imposes_on_the_user(string name)
    {
        // If a built-in cannot meet the bar we hold user edits to, either the template or the bar is wrong.
        Assert.Empty(PromptTemplateStore.Validate(name, _store.GetDefault(name)));
    }

    [Fact]
    public void Every_task_has_a_template_and_every_template_a_default()
    {
        // Both variants of every preset, because a task with no selection template is a task that throws the
        // first time a reader highlights something.
        foreach (var task in Enum.GetValues<AiTask>())
        foreach (var hasSelection in new[] { false, true })
        {
            var name = PromptTemplateNames.ForTask(task, hasSelection);
            Assert.Contains(name, PromptTemplateNames.All);
            Assert.False(string.IsNullOrWhiteSpace(_store.GetDefault(name)));
        }
    }

    [Fact]
    public void A_valid_override_replaces_the_built_in()
    {
        var text = "My own instructions.\n\n{{citation}}\n\n{{passage}}\n{{selection}}\n{{userQuestion}}";
        _store.Save(PromptTemplateNames.Explain, text);

        var template = _store.Get(PromptTemplateNames.Explain);

        Assert.Equal(text, template.Text);
        Assert.True(template.IsUserEdited);
    }

    [Fact]
    public void Reset_goes_back_to_the_built_in()
    {
        _store.Save(PromptTemplateNames.Explain, ExplainMinimum);
        _store.Reset(PromptTemplateNames.Explain);

        var template = _store.Get(PromptTemplateNames.Explain);

        Assert.False(template.IsUserEdited);
        Assert.Equal(_store.GetDefault(PromptTemplateNames.Explain), template.Text);
    }

    [Fact]
    public void Reset_on_an_untouched_template_is_not_an_error()
    {
        _store.Reset(PromptTemplateNames.Translate);

        Assert.False(_store.Get(PromptTemplateNames.Translate).IsUserEdited);
    }

    [Fact]
    public void Save_refuses_a_template_that_drops_the_passage()
    {
        // The failure this guards is silent and total: the model is asked to explain a passage it was never
        // given, and answers anyway.
        var error = Assert.Throws<PromptTemplateException>(
            () => _store.Save(PromptTemplateNames.Explain,
                "Explain it. {{citation}} {{selection}} {{userQuestion}}"));

        Assert.Contains("{{passage}}", error.Problems.Single());
    }

    [Fact]
    public void Save_refuses_an_unknown_placeholder()
    {
        // Left in, it would reach the model as a literal "{{glosses}}" — which reads to the model as a section
        // that should have been filled and was not.
        var error = Assert.Throws<PromptTemplateException>(
            () => _store.Save(PromptTemplateNames.Explain,
                "{{citation}} {{passage}} {{selection}} {{userQuestion}} {{glosses}}"));

        Assert.Contains("{{glosses}}", error.Problems.Single());
    }

    [Fact]
    public void Save_refuses_a_system_prompt_that_drops_the_marking_instruction()
    {
        var error = Assert.Throws<PromptTemplateException>(
            () => _store.Save(PromptTemplateNames.System, "Answer in {{outputLanguage}} about {{scope}}."));

        Assert.Equal(2, error.Problems.Count);
        Assert.Contains(error.Problems, p => p.Contains("{{paliOpen}}"));
        Assert.Contains(error.Problems, p => p.Contains("{{paliClose}}"));
    }

    [Fact]
    public void Validation_reports_every_problem_at_once()
    {
        var problems = PromptTemplateStore.Validate(PromptTemplateNames.Grammar, "{{nope}} {{alsoNope}}");

        // Two unknown placeholders and five missing required ones — an editor that showed these one per save
        // would be a chore to use.
        Assert.Equal(7, problems.Count);
    }

    [Fact]
    public void An_invalid_override_on_disk_falls_back_to_the_built_in_and_says_why()
    {
        // Reachable by hand-editing the file, which is the documented way to edit these until #585 ships a UI.
        // Falling back silently would leave the user reading answers from a prompt they believe they replaced.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "explain.md"), "no placeholders here at all");

        var template = _store.Get(PromptTemplateNames.Explain);

        Assert.False(template.IsUserEdited);
        Assert.Equal(_store.GetDefault(PromptTemplateNames.Explain), template.Text);
        Assert.Equal(4, _store.RejectedOverrides[PromptTemplateNames.Explain].Count);
    }

    [Fact]
    public void An_empty_override_is_rejected_rather_than_sent_as_an_empty_prompt()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "system.md"), "   \n\n  ");

        var template = _store.Get(PromptTemplateNames.System);

        Assert.False(template.IsUserEdited);
        Assert.Contains("empty", _store.RejectedOverrides[PromptTemplateNames.System].Single());
    }

    [Fact]
    public void A_rejection_clears_once_the_override_is_fixed()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "explain.md"), "broken");
        _store.Get(PromptTemplateNames.Explain);
        Assert.True(_store.RejectedOverrides.ContainsKey(PromptTemplateNames.Explain));

        _store.Save(PromptTemplateNames.Explain, ExplainMinimum);
        _store.Get(PromptTemplateNames.Explain);

        Assert.False(_store.RejectedOverrides.ContainsKey(PromptTemplateNames.Explain));
    }

    [Theory]
    [InlineData("{{citation}} {{passage}} {{selection}} {{userQuestion}} {{user_question}}")]
    [InlineData("{{citation}} {{passage}} {{selection}} {{userQuestion}} {{2lemmas}}")]
    [InlineData("{{citation}} {{passage}} {{selection}} {{userQuestion}} {{word-by-word}}")]
    [InlineData("{{citation}} {{passage}} {{selection}} {{userQuestion}} {{pass age}}")]
    [InlineData("{{citation}} {{passage}} {{selection}} {{userQuestion}} {{passage")]
    public void A_near_miss_placeholder_is_caught_rather_than_sent_as_literal_braces(string text)
    {
        // Found by adversarial review. These do not match the identifier grammar, so the unknown-NAME check
        // cannot see them at all — and without a second pass they sail through validation and reach the model
        // as literal braces, which is the precise outcome that check exists to prevent.
        var problems = PromptTemplateStore.Validate(PromptTemplateNames.Explain, text);

        Assert.Contains(problems, p => p.Contains("not a valid placeholder"));
    }

    [Fact]
    public void Save_refuses_a_template_that_drops_the_readers_own_selection_or_question()
    {
        // The one silence this design promises not to permit. The user selects a phrase, types a question, and
        // gets an answer that saw neither — worse than the equivalent for the passage, because they WATCHED
        // themselves supply the input.
        var error = Assert.Throws<PromptTemplateException>(
            () => _store.Save(PromptTemplateNames.Explain, "{{citation}} {{passage}}"));

        Assert.Contains(error.Problems, p => p.Contains("{{selection}}"));
        Assert.Contains(error.Problems, p => p.Contains("{{userQuestion}}"));
    }

    [Fact]
    public void Reset_refuses_a_name_that_would_escape_the_override_directory()
    {
        // Trusted callers today; #585 puts a Settings control in front of it.
        Assert.Throws<ArgumentOutOfRangeException>(() => _store.Reset("../../notes"));
    }

    [Fact]
    public void Concurrent_reads_do_not_corrupt_the_rejection_map()
    {
        // The store is a DI singleton and Get mutates on EVERY call, including the no-override path. #583's
        // orchestrator plus a surface-C agent hitting the API is concurrent Build by construction.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "grammar.md"), "broken");

        System.Threading.Tasks.Parallel.For(0, 200, _ =>
        {
            _store.Get(PromptTemplateNames.Explain);
            _store.Get(PromptTemplateNames.Grammar);
            _ = _store.RejectedOverrides.Count;
        });

        Assert.True(_store.RejectedOverrides.ContainsKey(PromptTemplateNames.Grammar));
        Assert.False(_store.RejectedOverrides.ContainsKey(PromptTemplateNames.Explain));
    }

    [Fact]
    public void Spaces_inside_a_placeholder_are_tolerated()
    {
        // "{{ passage }}" is the obvious thing to type and would otherwise fail as a MISSING required
        // placeholder — a message pointing at the one thing the user did include.
        Assert.Empty(PromptTemplateStore.Validate(
            PromptTemplateNames.Explain,
            "{{ citation }} {{ passage }} {{ selection }} {{ userQuestion }}"));
    }

    [Fact]
    public void An_unknown_template_name_is_a_programming_error_not_a_fallback()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _store.GetDefault("summarize"));
    }
}
