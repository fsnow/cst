using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CST;
using CST.Navigation;
using CST.Search;

namespace CST.Avalonia.Services.Ai;

/// <summary>A prompt ready to send, plus what the panel should tell the user about how it was assembled.</summary>
/// <param name="System">The shared system prompt, rendered.</param>
/// <param name="UserContent">The single user turn: the context blocks and the preset's instruction.</param>
/// <param name="MaxOutputTokens">Output cap for this preset, or null for "do not specify one" — the current
/// default for every preset. See <see cref="PromptBuilder"/> for why, and <see cref="ChatRequest.MaxTokens"/>
/// for what each adapter does with a null.</param>
/// <param name="Notices">
/// Degradations worth showing beside the answer — a missing asset, a trimmed part, a selection the window does
/// not contain, a prompt edit that was rejected. The prompt states these to the MODEL; these state them to the
/// USER, which is the half that keeps "degrade visibly" honest. Empty on a clean build.
/// </param>
public sealed record RenderedPrompt(
    string System,
    string UserContent,
    int? MaxOutputTokens,
    IReadOnlyList<string> Notices);

/// <summary>Turns a context bundle into a prompt. See <see cref="PromptBuilder"/>.</summary>
public interface IPromptBuilder
{
    RenderedPrompt Build(AiContextBundle bundle);
}

/// <summary>
/// Renders the shared system prompt and the preset's instruction template against a context bundle. (#582)
///
/// <para><b>The division of labour.</b> #580's bundler decides WHAT to gather; this decides how it is said. The
/// split is what lets a bundle be asserted against the real corpus with no model in sight, and it is why the
/// templates can be user-editable without any of the gathering logic being exposed.</para>
///
/// <para><b>Every placeholder renders to a sentence, never to nothing.</b> The template engine has no
/// conditionals (see <see cref="PromptPlaceholders"/>), so an absent selection or a missing lemma asset must
/// still produce text — and the text says what is absent and why. That is the mechanism behind the plan's
/// requirement that the lemma presets "degrade visibly, never silently": a model told the analysis was
/// unavailable answers differently from one handed an empty heading, and differently again from one that was
/// never told. The same facts reach the user through <see cref="RenderedPrompt.Notices"/>.</para>
///
/// <para><b>The scope statement is the load-bearing one.</b> Injection's characteristic failure is not
/// hallucination but a truthful answer over a silently narrower scope than the user assumed — "explain this
/// sutta" answered confidently from three paragraphs. The system prompt therefore opens by stating what the
/// excerpt actually is, and the citation the app renders beside the answer comes from the same bundle data.
/// (AI_SURFACE_B.md §6)</para>
/// </summary>
public sealed class PromptBuilder : IPromptBuilder
{
    /// <summary>
    /// Per-preset output cap — <b>the seam, deliberately unfilled</b>. Every entry is currently null, meaning
    /// "do not specify a cap"; the table survives so #584/#585 can expose the cap per model in Settings without
    /// reintroducing the concept (fsnow, 2026-08-11: <i>"that leaves a placeholder in case we want to expose
    /// that in the model settings"</i>).
    ///
    /// <para><b>Why the values are gone.</b> An earlier version sized each preset to the expected length of its
    /// answer — 1500 for Explain, 3000 for word-by-word. That is the wrong quantity to predict. On a reasoning
    /// model the cap covers reasoning AND answer, and reasoning volume varies by an order of magnitude between
    /// models: <c>minimax-m3</c> needed 3,177 completion tokens to translate a two-line verse, against a
    /// budget of 2,400 (#601). The failure it buys is the worst-shaped one available — a translation cut off
    /// mid-verse, or a blank panel, on a request the user already paid for. Cost is controlled by reporting
    /// what each call spent (AI_SURFACE_B.md §10), not by a silent truncation.</para>
    ///
    /// <para>The table still earns its place at runtime: membership is what says a task is renderable at all,
    /// so an <see cref="AiTask"/> added without a budget entry fails loudly here rather than silently.</para>
    /// </summary>
    private static readonly Dictionary<AiTask, int?> OutputBudget = new()
    {
        [AiTask.Explain] = null,
        [AiTask.Translate] = null,
        [AiTask.Grammar] = null,
        [AiTask.WordByWord] = null,
    };

    private readonly IPromptTemplateStore _templates;

    public PromptBuilder(IPromptTemplateStore templates) => _templates = templates;

    public RenderedPrompt Build(AiContextBundle bundle)
    {
        if (!OutputBudget.TryGetValue(bundle.Task, out var maxTokens))
            throw new ArgumentOutOfRangeException(
                $"{nameof(bundle)}.{nameof(bundle.Task)}", bundle.Task, "No output budget for this task.");

        var presetName = PromptTemplateNames.ForTask(bundle.Task);
        var system = _templates.Get(PromptTemplateNames.System);
        var preset = _templates.Get(presetName);

        // Which parts the inventory may claim is decided by what the preset actually renders, so the two cannot
        // disagree — including after a user edit. Both templates are scanned because either may carry it.
        // Scanned with a separator: "{{" ending the system template and "apparatus}}" opening the preset are
        // each harmless alone, but concatenated they synthesize a placeholder neither template contains.
        var shown = PlaceholdersUsed(system.Text + "\n" + preset.Text);
        var values = BuildValues(bundle, shown);

        return new RenderedPrompt(
            Render(system.Text, values),
            Render(preset.Text, values),
            maxTokens,
            BuildNotices(bundle, shown, PromptTemplateNames.System, presetName));
    }

    /// <summary>The placeholder names a template refers to.</summary>
    private static IReadOnlySet<string> PlaceholdersUsed(string template) =>
        PromptTemplateStore.PlaceholderPattern.Matches(template)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The placeholder that shows a gathered part. A part with no entry here — or whose placeholder the template
    /// does not use — is not something the model was shown, so the inventory must not claim it was.
    /// </summary>
    private static readonly Dictionary<string, string> PartPlaceholder = new(StringComparer.Ordinal)
    {
        [BundlePartNames.Passage] = PromptPlaceholders.Passage,
        [BundlePartNames.Selection] = PromptPlaceholders.Selection,
        [BundlePartNames.Lemmas] = PromptPlaceholders.Lemmas,
        [BundlePartNames.Apparatus] = PromptPlaceholders.Apparatus,
    };

    /// <summary>
    /// How a part is named to the USER. The internal part names are the prompt's and the budget report's
    /// vocabulary, not a reader's — "the lemmas was cut" is neither English nor meaningful to someone who has
    /// never heard the bundle described.
    /// </summary>
    private static string PartLabel(string partName) => partName switch
    {
        BundlePartNames.Passage => "passage",
        BundlePartNames.Selection => "selection",
        BundlePartNames.Lemmas => "word analysis",
        BundlePartNames.Apparatus => "print apparatus",
        _ => partName,
    };

    private static bool IsShown(BundlePart part, IReadOnlySet<string> shown) =>
        PartPlaceholder.TryGetValue(part.Name, out var placeholder) && shown.Contains(placeholder);

    /// <summary>
    /// Substitute <c>{{name}}</c>. A placeholder with no value renders as empty — unreachable through
    /// <see cref="PromptTemplateStore"/>, which rejects unknown names before a template is ever used, and
    /// deliberately not an exception here so that a validation gap degrades to a gap in the prompt rather than
    /// to an AI panel that cannot open.
    /// </summary>
    internal static string Render(string template, IReadOnlyDictionary<string, string> values) =>
        PromptTemplateStore.PlaceholderPattern.Replace(
            template,
            match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : string.Empty);

    private static Dictionary<string, string> BuildValues(
        AiContextBundle bundle, IReadOnlySet<string> shown) => new(StringComparer.Ordinal)
    {
        [PromptPlaceholders.OutputLanguage] = bundle.OutputLanguage,
        [PromptPlaceholders.Scope] = Scope(bundle),
        [PromptPlaceholders.PaliOpen] = PaliQuoteMarkers.Open,
        [PromptPlaceholders.PaliClose] = PaliQuoteMarkers.Close,

        [PromptPlaceholders.Passage] = bundle.Passage.Text.Trim(),
        [PromptPlaceholders.Citation] = Citation(bundle.Citation),
        [PromptPlaceholders.Book] = Book(bundle.Book),
        [PromptPlaceholders.Selection] = Selection(bundle.Selection),
        [PromptPlaceholders.Lemmas] = Lemmas(bundle),
        [PromptPlaceholders.Apparatus] = Apparatus(bundle.Passage.Notes),
        [PromptPlaceholders.UserQuestion] = UserQuestion(bundle.UserQuestion),
        [PromptPlaceholders.Provisions] = Provisions(bundle.Budget, shown),
    };

    /// <summary>
    /// What the excerpt actually is. Says the two things the model cannot work out for itself: that this is a
    /// window rather than the text, and whether it runs on past the reference the citation names.
    /// </summary>
    private static string Scope(AiContextBundle bundle)
    {
        var where = $"{bundle.Citation.BookName}, at {bundle.Citation.NormalizedReference}";

        // How far the window ACTUALLY reached, measured rather than guessed (#602). Saying the number matters:
        // the budget is in characters and structurally blind, so on a verse text a modest budget covers dozens
        // of paragraphs, and a model told only "this may run on a little" will answer about all of them as
        // though that were what was asked.
        var extent = bundle.Budget.ParagraphsCovered switch
        {
            > 1 and int covered =>
                $"It is a reading window covering {covered} numbered paragraphs — the whole of what follows is "
                + "in view, so say which part of it you are answering about.",
            1 => "It is exactly the paragraph named, and nothing after it.",
            // Null means the book carries no paragraph numbering here — which is not the same as "one
            // paragraph", and must not be reported as though it were.
            _ => "It is a reading window starting there, not the whole text.",
        };

        return $"an excerpt from {where}. {extent}";
    }

    private static string Citation(CitationRef citation)
    {
        var pages = citation.Pages.Count == 0
            ? string.Empty
            : " — " + string.Join(", ", citation.Pages.Select(PageRef));

        return $"**Reference:** {citation.BookName}, {citation.NormalizedReference}{pages}";
    }

    private static string PageRef(SnippetPageRef page)
    {
        var edition = page.Edition switch
        {
            PageEdition.Vri => "VRI",
            PageEdition.Myanmar => "Myanmar",
            PageEdition.Pts => "PTS",
            PageEdition.Thai => "Thai",
            _ => "other",
        };
        return page.Volume > 0
            ? $"{edition} vol. {page.Volume} p. {page.Number}"
            : $"{edition} p. {page.Number}";
    }

    /// <summary>
    /// Where the passage sits in the canon. Cheap, and it forecloses a category error a model would otherwise
    /// have no way to avoid: a ṭīkā glossed as the word of the Buddha reads perfectly plausibly.
    /// </summary>
    private static string Book(BookContext book)
    {
        var pitaka = book.Pitaka switch
        {
            Pitaka.Vinaya => "Vinaya piṭaka",
            Pitaka.Sutta => "Sutta piṭaka",
            Pitaka.Abhidhamma => "Abhidhamma piṭaka",
            _ => "outside the three piṭakas",
        };

        var level = book.CommentaryLevel switch
        {
            CommentaryLevel.Mula => "mūla — the canonical text itself",
            CommentaryLevel.Atthakatha => "aṭṭhakathā — commentary on a canonical text",
            CommentaryLevel.Tika => "ṭīkā — sub-commentary, commenting on an aṭṭhakathā",
            _ => "an ancillary text",
        };

        return $"**Text:** {book.Name}\n**Place in the canon:** {pitaka}; {level}";
    }

    private static string Selection(SelectionContext? selection)
    {
        if (selection is null)
            return "The reader has not selected anything. Treat the whole passage as being in view.";

        // Distinct from "nothing selected": the user may well have highlighted something we failed to read, so
        // the model is told the difference rather than being allowed to assume the passage is all there was.
        if (selection.State == SelectionState.Unavailable)
        {
            return "The reader may have selected something, but it could not be read. Answer about the passage "
                 + "as a whole, and say that you were not able to see a selection.";
        }

        // A selection the window does not contain means the surrounding text may not support what is being
        // asked about — the difference between "explain this in context" and "explain this, and be aware the
        // context you have may be the wrong context".
        var note = selection.State == SelectionState.Located
            ? string.Empty
            : "\n\n(This selection was not found in the passage above. The reader may have scrolled, or the "
              + "selection may lie outside the window. Treat the surrounding text with corresponding caution.)";

        return $"The reader has selected:\n\n> {selection.Text}{note}";
    }

    private static string Lemmas(AiContextBundle bundle)
    {
        // Distinguish "the asset is missing" from "the asset is here and matched nothing". Both leave the
        // section empty; only the first is a thing the user can fix, and conflating them would have the model
        // reporting a download problem as a fact about the Pāli.
        var part = bundle.Budget.Parts.FirstOrDefault(p => p.Name == BundlePartNames.Lemmas);

        if (part is { State: BundlePartState.Unavailable })
        {
            return "No word analysis is available for this request — the word-analysis data is not installed. "
                 + "Work from the passage alone, and say that the analysis was unavailable rather than implying "
                 + "the words could not be analysed.";
        }

        // Order matters. Candidates in hand are reported as candidates whatever the budget report says about
        // them — the DATA is the ground truth here, and a bookkeeping mismatch must not hide real analysis.
        if (bundle.Lemmas.Count == 0)
        {
            // No part at all means no lookup was ATTEMPTED — the bundler gathers lemmas only for the
            // grammatical presets. Reachable through a user override that adds {{lemmas}} to Explain, and
            // "the lookup returned no candidates" there would describe a lookup that never ran.
            if (part is null)
                return "No word analysis was gathered for this request — this preset does not use it.";

            var why = part.State == BundlePartState.TrimmedForBudget
                ? " The list was also cut to fit the budget."
                : string.Empty;
            return "The lookup returned no candidates for the words in this passage. That is a gap in the "
                 + "lookup, not a statement about the words." + why;
        }

        var table = new StringBuilder();
        table.AppendLine("| form in the text | stem | part of speech | gloss |");
        table.AppendLine("|---|---|---|---|");
        foreach (var entry in bundle.Lemmas)
        {
            table.AppendLine(
                $"| {Cell(entry.Form)} | {Cell(entry.Lemma)} | {Cell(entry.PartOfSpeech)} | {Cell(entry.Gloss)} |");
        }

        if (part is { State: BundlePartState.TrimmedForBudget })
        {
            table.AppendLine();
            table.AppendLine(
                "(This list was cut to fit the budget. Words missing from it were not analysed, and no "
                + "conclusion follows from their absence.)");
        }

        return table.ToString().TrimEnd();
    }

    /// <summary>
    /// A pipe in a cell would break the row it sits in; a newline would break the table. Applied to every field,
    /// not just the gloss: the stem and part-of-speech come from the separately versioned DPD asset, which this
    /// code should not assume will stay pipe-free.
    /// </summary>
    private static string Cell(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "—"
            : value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Apparatus(IReadOnlyList<ApparatusNote> notes)
    {
        if (notes.Count == 0)
            return "There are no print notes in this window.";

        var text = new StringBuilder();
        foreach (var note in notes)
        {
            var reading = string.IsNullOrWhiteSpace(note.Reading) ? null : note.Reading!.Trim();
            var sigla = string.IsNullOrWhiteSpace(note.Sigla) ? null : note.Sigla!.Trim();

            var suffix = (reading, sigla) switch
            {
                (not null, not null) => $" — reading *{reading}* in {sigla}",
                (not null, null) => $" — reading *{reading}*",
                (null, not null) => $" — in {sigla}",
                _ => string.Empty,
            };

            text.AppendLine($"- {note.Text.Trim()}{suffix}");
        }

        text.AppendLine();
        text.AppendLine(
            "These are footnotes from the printed editions, recording variant readings. They are evidence about "
            + "the text, not part of it.");

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// The user's own question, verbatim and last, where it carries most weight.
    ///
    /// <para><b>Deliberately not length-capped.</b> A cap would silently discard part of what the user asked, to
    /// prevent an over-long request that the provider already reports precisely — <c>AiErrorKind.ContextTooLong</c>
    /// exists for it, and an explicit error the user can act on beats a truncation they cannot see.</para>
    /// </summary>
    private static string UserQuestion(string? question) =>
        string.IsNullOrWhiteSpace(question)
            ? string.Empty
            : $"## The reader asks\n\n{question.Trim()}";

    /// <summary>
    /// An inventory of what was gathered, in the prompt, so the model can qualify its answer instead of reading
    /// an empty section as an absence in the text.
    /// </summary>
    private static string Provisions(BudgetReport budget, IReadOnlySet<string> shown)
    {
        var parts = budget.Parts.Where(p => IsShown(p, shown)).ToList();
        if (parts.Count == 0) return "(no parts were recorded for this request)";

        var lines = parts.Select(part =>
        {
            var state = part.State switch
            {
                BundlePartState.Included => "given",
                BundlePartState.TrimmedForBudget => "given, but cut to fit the budget",
                BundlePartState.Unavailable => "NOT AVAILABLE",
                BundlePartState.Empty => "nothing to give",
                _ => part.State.ToString(),
            };
            var detail = string.IsNullOrWhiteSpace(part.Detail) ? string.Empty : $" ({part.Detail})";
            return $"- **{part.Name}** — {state}{detail}";
        });

        return string.Join("\n", lines);
    }

    /// <summary>
    /// The same facts the prompt tells the model, told to the user. A degradation the model is warned about and
    /// the user is not is still a silent one from where the user is sitting.
    /// </summary>
    private IReadOnlyList<string> BuildNotices(
        AiContextBundle bundle, IReadOnlySet<string> shown, params string[] templateNames)
    {
        var notices = new List<string>();

        // A prompt edit that was found and rejected is the one degradation the model cannot be told about — the
        // template carrying the news is the one that failed to load. Telling the user is the only channel left,
        // and without it they read answers produced by a prompt they believe they replaced.
        foreach (var name in templateNames)
        {
            if (_templates.RejectedOverrides.TryGetValue(name, out var problems))
            {
                notices.Add(
                    $"Your edited '{name}' prompt was not used ({string.Join("; ", problems)}). "
                    + "The built-in prompt was used instead.");
            }
        }

        // Only parts this prompt would actually have shown. Reporting a missing word-analysis asset beside an
        // Explain answer, which never asks for one, is a nag about something that did not affect the answer.
        foreach (var part in bundle.Budget.Parts.Where(p => IsShown(p, shown)))
        {
            if (part.State == BundlePartState.Unavailable)
                notices.Add($"The {PartLabel(part.Name)} could not be gathered: {part.Detail ?? "not available"}.");
            else if (part.State == BundlePartState.TrimmedForBudget)
                notices.Add($"The {PartLabel(part.Name)} was cut to fit the request budget.");
        }

        switch (bundle.Selection?.State)
        {
            case SelectionState.NotFoundInWindow:
                notices.Add("The selected text was not found in the passage window the model was given.");
                break;
            case SelectionState.Unavailable:
                // The one the user most needs: otherwise this reads as "the AI ignored my selection".
                notices.Add("Your selection could not be read, so the answer covers the whole passage.");
                break;
        }

        // Says the number. "May extend past the reference" was true of almost every request and read like a
        // rounding caveat; "covers 29 paragraphs" is a fact the reader can act on. (#602)
        if (bundle.Budget.ParagraphsCovered is > 1 and int covered)
        {
            notices.Add(
                $"The passage window covers {covered} paragraphs, not just the one cited — the answer may range "
                + "wider than you asked.");
        }

        return notices;
    }
}
