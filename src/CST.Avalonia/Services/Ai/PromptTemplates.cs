using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CST.Avalonia.Constants;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai;

/// <summary>
/// The names of the prompt templates. Strings rather than an enum because they are also FILE NAMES for the user
/// override, and an enum would put a rename one refactor away from orphaning everyone's edits.
/// </summary>
public static class PromptTemplateNames
{
    /// <summary>The shared system prompt — the grounding contract. Sent with every preset.</summary>
    public const string System = "system";

    public const string Explain = "explain";
    public const string Translate = "translate";
    public const string Grammar = "grammar";
    public const string WordByWord = "word-by-word";

    public static IReadOnlyList<string> All { get; } =
        new[] { System, Explain, Translate, Grammar, WordByWord };

    /// <summary>The instruction template for a preset.</summary>
    public static string ForTask(AiTask task) => task switch
    {
        AiTask.Explain => Explain,
        AiTask.Translate => Translate,
        AiTask.Grammar => Grammar,
        AiTask.WordByWord => WordByWord,
        _ => throw new ArgumentOutOfRangeException(nameof(task), task, "No prompt template for this task."),
    };
}

/// <summary>
/// The values a template may substitute. <b>Substitution only — no conditionals, no loops, no expressions.</b>
///
/// <para>That is a deliberate ceiling, because these templates are user-editable (#585) and a template language
/// with control flow is a small programming language the user can break in ways we then have to diagnose. The
/// cost is that a placeholder cannot be omitted when it has nothing to say — so instead every value renders as
/// something self-describing, and "the user selected nothing" is a sentence rather than an empty slot. That
/// turns out to be the better prompt anyway: a model told the selection is absent behaves differently from one
/// handed a blank.</para>
/// </summary>
public static class PromptPlaceholders
{
    public const string OutputLanguage = "outputLanguage";
    public const string Scope = "scope";
    public const string PaliOpen = "paliOpen";
    public const string PaliClose = "paliClose";

    public const string Passage = "passage";
    public const string Citation = "citation";
    public const string Book = "book";
    public const string Selection = "selection";
    public const string Lemmas = "lemmas";
    public const string Apparatus = "apparatus";
    public const string UserQuestion = "userQuestion";
    public const string Provisions = "provisions";

    /// <summary>Every placeholder any template may use. An unknown one is a template error, not an empty string.</summary>
    public static IReadOnlySet<string> Known { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        OutputLanguage, Scope, PaliOpen, PaliClose,
        Passage, Citation, Book, Selection, Lemmas, Apparatus, UserQuestion, Provisions,
    };

    /// <summary>
    /// What a template cannot do without. These are the load-bearing ones: a preset template that has lost
    /// <c>{{passage}}</c> asks the model to explain nothing, and one that has lost <c>{{citation}}</c> invites an
    /// answer with no scope attached — the exact failure AI_SURFACE_B.md §6 is written against. The system
    /// template's requirements carry the marking instruction and the answer language.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Required { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [PromptTemplateNames.System] = new[] { OutputLanguage, Scope, PaliOpen, PaliClose },
            [PromptTemplateNames.Explain] = new[] { Passage, Citation },
            [PromptTemplateNames.Translate] = new[] { Passage, Citation },
            [PromptTemplateNames.Grammar] = new[] { Passage, Citation, Lemmas },
            [PromptTemplateNames.WordByWord] = new[] { Passage, Citation, Lemmas },
        };
}

/// <summary>A template and where it came from.</summary>
/// <param name="IsUserEdited">True when a valid user override was loaded. Drives "reset to default" in Settings.</param>
public sealed record PromptTemplate(string Name, string Text, bool IsUserEdited);

/// <summary>Thrown when a template cannot be used. Carries the reasons so an editor can show them.</summary>
public sealed class PromptTemplateException : Exception
{
    public PromptTemplateException(string name, IReadOnlyList<string> problems)
        : base($"Prompt template '{name}' is not usable: {string.Join("; ", problems)}")
    {
        TemplateName = name;
        Problems = problems;
    }

    public string TemplateName { get; }
    public IReadOnlyList<string> Problems { get; }
}

/// <summary>Reads the prompt templates, preferring a valid user override to the built-in default.</summary>
public interface IPromptTemplateStore
{
    /// <summary>The template in force. Never throws for a bad override — see <see cref="PromptTemplateStore"/>.</summary>
    PromptTemplate Get(string name);

    /// <summary>The built-in text, whatever the user has done to their copy.</summary>
    string GetDefault(string name);

    /// <summary>Validate and persist a user override. Throws <see cref="PromptTemplateException"/> if invalid.</summary>
    void Save(string name, string text);

    /// <summary>Discard the user override and go back to the built-in.</summary>
    void Reset(string name);

    /// <summary>
    /// Overrides that were found but rejected, by template name — so Settings can say WHICH edit was ignored
    /// and why, rather than leaving the user with a template they believe is in force and is not.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> RejectedOverrides { get; }
}

/// <summary>
/// Prompt templates as data: built-in defaults are embedded resources, and the user may override any of them
/// with a file in <c>&lt;app data&gt;/ai-templates/</c>. (#582, AI_SURFACE_B.md §4)
///
/// <para><b>A bad override falls back rather than throwing.</b> An unusable template is a configuration mistake,
/// and taking the AI panel out of service for one would be a bad trade — but falling back SILENTLY would be
/// worse than either, because the user would go on believing their edit was in force while reading answers
/// produced by a different prompt. So: fall back, log, and record the reasons in
/// <see cref="RejectedOverrides"/> for Settings to surface. <see cref="Save"/> validates up front, so the
/// ordinary path is that a bad edit is refused at the point of editing and never reaches disk.</para>
///
/// <para><b>Validation is placeholder-shaped, not prose-shaped.</b> An unknown <c>{{placeholder}}</c> is
/// rejected because it would otherwise reach the model as a literal brace pair, and a missing REQUIRED one is
/// rejected because it silently removes the passage, the citation, or the marking instruction from the prompt.
/// What the user writes AROUND the placeholders is theirs — including instructions we would not have written.
/// The grounding contract is defended by making its inputs non-removable, not by policing wording.</para>
/// </summary>
public sealed class PromptTemplateStore : IPromptTemplateStore
{
    /// <summary>Matches <c>{{name}}</c>, tolerating inner spaces so <c>{{ passage }}</c> is not a mystery failure.</summary>
    internal static readonly Regex PlaceholderPattern = new(@"\{\{\s*([A-Za-z][A-Za-z0-9]*)\s*\}\}", RegexOptions.Compiled);

    private readonly string _overrideDirectory;
    private readonly ILogger<PromptTemplateStore> _logger;
    private readonly Dictionary<string, IReadOnlyList<string>> _rejected = new(StringComparer.Ordinal);

    public PromptTemplateStore(ILogger<PromptTemplateStore> logger)
        : this(Path.Combine(AppConstants.DataDirectory, "ai-templates"), logger)
    {
    }

    /// <summary>Test seam: point the store at a temp directory instead of the user's real one.</summary>
    internal PromptTemplateStore(string overrideDirectory, ILogger<PromptTemplateStore> logger)
    {
        _overrideDirectory = overrideDirectory;
        _logger = logger;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> RejectedOverrides => _rejected;

    public PromptTemplate Get(string name)
    {
        var builtIn = GetDefault(name);

        var path = OverridePath(name);
        string text;
        try
        {
            if (!File.Exists(path))
            {
                _rejected.Remove(name);
                return new PromptTemplate(name, builtIn, IsUserEdited: false);
            }

            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read the prompt template override at {Path}; using the built-in", path);
            _rejected[name] = new[] { "the override file could not be read" };
            return new PromptTemplate(name, builtIn, IsUserEdited: false);
        }

        var problems = Validate(name, text);
        if (problems.Count > 0)
        {
            _logger.LogWarning(
                "Prompt template override '{Name}' is not usable ({Problems}); using the built-in",
                name, string.Join("; ", problems));
            _rejected[name] = problems;
            return new PromptTemplate(name, builtIn, IsUserEdited: false);
        }

        _rejected.Remove(name);
        return new PromptTemplate(name, text, IsUserEdited: true);
    }

    public string GetDefault(string name)
    {
        if (!PromptTemplateNames.All.Contains(name))
            throw new ArgumentOutOfRangeException(nameof(name), name, "Not a prompt template name.");

        // Missing means a build problem — the resource is embedded from this same project — so it fails loudly
        // rather than degrading to an empty prompt, which would produce confident answers about nothing.
        return ReadResource($"Ai.{name}.md")
               ?? throw new PromptTemplateException(name, new[] { "the built-in template resource is missing" });
    }

    public void Save(string name, string text)
    {
        var problems = Validate(name, text);
        if (problems.Count > 0) throw new PromptTemplateException(name, problems);

        Directory.CreateDirectory(_overrideDirectory);
        File.WriteAllText(OverridePath(name), text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _rejected.Remove(name);
        _logger.LogInformation("Saved a user override for prompt template '{Name}'", name);
    }

    public void Reset(string name)
    {
        var path = OverridePath(name);
        if (File.Exists(path)) File.Delete(path);
        _rejected.Remove(name);
        _logger.LogInformation("Reset prompt template '{Name}' to the built-in default", name);
    }

    /// <summary>Everything wrong with a template, all at once — an editor showing one error at a time is a chore.</summary>
    internal static IReadOnlyList<string> Validate(string name, string text)
    {
        if (!PromptTemplateNames.All.Contains(name))
            throw new ArgumentOutOfRangeException(nameof(name), name, "Not a prompt template name.");

        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            problems.Add("the template is empty");
            return problems;
        }

        var used = PlaceholderPattern.Matches(text)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var unknown in used.Where(u => !PromptPlaceholders.Known.Contains(u)).OrderBy(u => u, StringComparer.Ordinal))
            problems.Add($"{{{{{unknown}}}}} is not a placeholder this template can use");

        foreach (var required in PromptPlaceholders.Required[name].Where(r => !used.Contains(r)))
            problems.Add($"{{{{{required}}}}} is required and is missing");

        return problems;
    }

    private string OverridePath(string name) => Path.Combine(_overrideDirectory, name + ".md");

    private static string? ReadResource(string endsWith)
    {
        var assembly = typeof(PromptTemplateStore).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
        if (resource is null) return null;

        using var stream = assembly.GetManifestResourceStream(resource);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
