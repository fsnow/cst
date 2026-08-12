using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai;

/// <summary>
/// How far a model is trusted on <b>this</b> corpus. Not a general quality ranking — a claim about Pāli
/// fidelity, which is why every non-recommended entry cites evidence.
/// </summary>
public enum ModelTier
{
    /// <summary>
    /// Not in the registry. <b>This is not a criticism</b> — see <see cref="ModelRegistry"/> for why the
    /// distinction from <see cref="DiscouragedForTranslation"/> is the whole point.
    /// </summary>
    Unrated,

    /// <summary>Trusted for translating canonical text. Claude-first, per AI_INTEGRATION.md §11.1.</summary>
    Recommended,

    /// <summary>Passed the fidelity set, or is untested but has no evidence against it.</summary>
    Permitted,

    /// <summary>Failed the fidelity set on the corpus itself. Advised against for translation — never blocked.</summary>
    DiscouragedForTranslation,
}

/// <summary>What the registry knows about one model.</summary>
/// <param name="Note">Why it sits where it does, in one sentence, for a human reading Settings.</param>
/// <param name="Evidence">Where the claim comes from. Absent only for the recommended tier, which follows from
/// the standing policy rather than from a measurement.</param>
public sealed record ModelRating(ModelTier Tier, string? Note = null, string? Evidence = null);

/// <summary>Rates a configured model, and says what (if anything) to warn about.</summary>
public interface IModelRegistry
{
    /// <summary>The tier for a model id as the user typed it. Never throws; unknown ids are Unrated.</summary>
    ModelRating Rate(string? modelId);

    /// <summary>
    /// The advisory to show for a task, or null when none is warranted. Only ever advice: the user's choice of
    /// model is theirs, and a reader who wants a local model for privacy has a good reason we do not get to
    /// override (AI_INTEGRATION.md §11.1 — curate, advise, never block).
    /// </summary>
    string? Advisory(AiTask task, string? modelId);
}

/// <summary>
/// The fidelity advisory's data and lookup. (#584, AI_SURFACE_B.md §7)
///
/// <para><b>Why a registry at all.</b> Model quality is a fidelity feature here rather than a preference: this
/// is a sacred-text corpus, and hallucination risk runs inverse to model quality times grounding. A model that
/// inverts <i>appamāda</i> or reads <i>matā</i> as "mother" produces fluent, confident, wrong Pāli that a reader
/// who came to this app <b>because they cannot yet read Pāli unaided</b> has no way to catch.</para>
///
/// <para><b>Unrated is not discouraged, and that is the whole design.</b> Frontier models appear constantly. A
/// registry that treated anything it had not heard of as suspect would have flagged half of today's good models
/// a year ago, and would decay into noise the moment it stopped being updated — at which point users learn to
/// dismiss it, including on the entries that matter. So the default for an unknown model is a mild "not
/// evaluated", and the strong warning is reserved for models with <b>evidence against them on this corpus</b>.</para>
///
/// <para><b>Every non-recommended entry cites its evidence.</b> A registry that says a model is not recommended
/// for translating canonical text has to be able to answer <i>why</i> — otherwise it is an opinion with a
/// version number.</para>
/// </summary>
public sealed class ModelRegistry : IModelRegistry
{
    private sealed record Entry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("tier")] string Tier,
        [property: JsonPropertyName("note")] string? Note = null,
        [property: JsonPropertyName("evidence")] string? Evidence = null);

    private sealed record Document(
        [property: JsonPropertyName("updated")] string? Updated,
        [property: JsonPropertyName("models")] IReadOnlyList<Entry>? Models);

    private static readonly ModelRating UnratedRating = new(ModelTier.Unrated);

    private readonly IReadOnlyList<(string Id, ModelRating Rating)> _entries;
    private readonly ILogger<ModelRegistry> _logger;

    public ModelRegistry(ILogger<ModelRegistry> logger)
    {
        _logger = logger;
        _entries = Load(logger);
    }

    /// <summary>The date the shipped registry was last revised — worth showing beside an advisory.</summary>
    public static string? Updated { get; private set; }

    public ModelRating Rate(string? modelId)
    {
        var id = NormalizeId(modelId);
        if (id is null) return UnratedRating;

        foreach (var (entryId, rating) in _entries)
            if (Matches(entryId, id))
                return rating;

        return UnratedRating;
    }

    public string? Advisory(AiTask task, string? modelId)
    {
        // Only translation carries an advisory. Explaining or parsing a passage the model can see is a far
        // smaller fidelity surface than producing English that a reader will take as the meaning of the Pāli —
        // and an advisory attached to everything is one nobody reads.
        if (task != AiTask.Translate) return null;

        var rating = Rate(modelId);
        return rating.Tier switch
        {
            ModelTier.Recommended => null,

            ModelTier.DiscouragedForTranslation =>
                $"This model is not recommended for translating canonical text. {rating.Note} "
                + "Check its output against the Pāli.",

            ModelTier.Permitted =>
                "This model is not in the recommended tier for translating canonical text. "
                + "Check its output against the Pāli.",

            // Deliberately softer, and deliberately says WHY it is softer: "we have not tested this" is a
            // different statement from "this got it wrong", and conflating them is what makes advisories noise.
            _ => "This model has not been evaluated on this corpus. Check its output against the Pāli.",
        };
    }

    /// <summary>
    /// Reduce a user-typed model id to the form the registry is keyed on.
    ///
    /// <para>Three things get stripped, each because the same model reaches us spelled several ways:
    /// <b>case</b>; a leading <c>vendor/</c> segment, since an aggregator serves Claude as
    /// <c>anthropic/claude-opus-5</c> and it is still Claude; and a trailing <c>-cloud</c> / <c>:cloud</c>
    /// deployment suffix, which names where a model runs rather than which model it is.</para>
    ///
    /// <para><b>Size is never stripped.</b> <c>gpt-oss:20b</c> and <c>gpt-oss:120b</c> are different models and
    /// are rated differently — folding them together would put a warning on the wrong one.</para>
    /// </summary>
    internal static string? NormalizeId(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return null;

        var id = modelId.Trim().ToLowerInvariant();

        var slash = id.LastIndexOf('/');
        if (slash >= 0 && slash < id.Length - 1) id = id[(slash + 1)..];

        foreach (var suffix in new[] { "-cloud", ":cloud" })
            if (id.EndsWith(suffix, StringComparison.Ordinal))
                id = id[..^suffix.Length];

        return id.Length == 0 ? null : id;
    }

    /// <summary>
    /// Exact match, or the registered id as a prefix ending at a separator — so a dated snapshot
    /// (<c>claude-haiku-4-5-20251001</c>) resolves to its family, while <c>gpt-oss:120b</c> can never match
    /// <c>gpt-oss:1200b</c>. Registering a truncated id would defeat the boundary rule, which is why the data
    /// file says to register full ids only.
    /// </summary>
    internal static bool Matches(string entryId, string normalizedId)
    {
        if (string.Equals(entryId, normalizedId, StringComparison.Ordinal)) return true;
        if (!normalizedId.StartsWith(entryId, StringComparison.Ordinal)) return false;

        var next = normalizedId[entryId.Length];
        return next is '-' or ':' or '.' or '@';
    }

    private static IReadOnlyList<(string, ModelRating)> Load(ILogger<ModelRegistry> logger)
    {
        var json = ReadResource("Ai.model-registry.json");
        if (json is null)
        {
            // Degrade to "everything is unrated" rather than failing: an absent registry must not take the
            // assistant out of service, and unrated is the honest thing to say when we know nothing.
            logger.LogError("The model registry resource is missing; every model will be reported as unrated");
            return Array.Empty<(string, ModelRating)>();
        }

        Document? document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(json);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "The model registry could not be parsed; every model will be reported as unrated");
            return Array.Empty<(string, ModelRating)>();
        }

        Updated = document?.Updated;

        var entries = new List<(string, ModelRating)>();
        foreach (var entry in document?.Models ?? Array.Empty<Entry>())
        {
            var id = NormalizeId(entry.Id);
            if (id is null) continue;

            if (!TryParseTier(entry.Tier, out var tier))
            {
                logger.LogWarning("Model registry entry '{Id}' has an unknown tier '{Tier}'; skipping",
                    entry.Id, entry.Tier);
                continue;
            }

            entries.Add((id, new ModelRating(tier, entry.Note, entry.Evidence)));
        }

        // Longest id first, so a more specific entry wins over a family prefix that would also match.
        return entries.OrderByDescending(e => e.Item1.Length).ToList();
    }

    internal static bool TryParseTier(string? value, out ModelTier tier)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "recommended": tier = ModelTier.Recommended; return true;
            case "permitted": tier = ModelTier.Permitted; return true;
            case "discouraged-for-translation": tier = ModelTier.DiscouragedForTranslation; return true;
            case "unrated": tier = ModelTier.Unrated; return true;
            default: tier = ModelTier.Unrated; return false;
        }
    }

    private static string? ReadResource(string endsWith)
    {
        var assembly = typeof(ModelRegistry).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
        if (name is null) return null;

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
