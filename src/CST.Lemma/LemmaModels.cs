namespace CST.Lemma;

/// <summary>One lemma a surface form can belong to (a DPD headword).</summary>
/// <param name="LemmaId">DPD headword id.</param>
/// <param name="Lemma">Lemma citation form, IAST, with homonym number (e.g. "paññāya 1").</param>
/// <param name="Pos">Part of speech (e.g. "pr", "fem", "adj", "ger").</param>
/// <param name="Gloss">Short meaning.</param>
/// <param name="DerivedFrom">The lemma this derives from (the regrouping key), or null.</param>
public sealed record LemmaCandidate(long LemmaId, string Lemma, string? Pos, string? Gloss, string? DerivedFrom);

/// <summary>
/// Back-lookup result: a surface form and the candidate lemmas it may belong to. A homograph yields
/// more than one candidate; the caller (or user) disambiguates. <see cref="Grammar"/> is the form's raw
/// grammatical analysis (JSON), for the disambiguation display; null when the asset omits it (lean scope).
/// </summary>
public sealed record FormResolution(string Form, IReadOnlyList<LemmaCandidate> Candidates, string? Grammar);

/// <summary>
/// Forward expansion of one lemma: its attested surface forms (IAST) and, when requested, its
/// <c>derived_from</c> family. Occurrence counts are NOT here — the caller joins forms to the search index.
/// </summary>
public sealed record LemmaExpansion(
    long LemmaId,
    string Lemma,
    string? Pos,
    string? Gloss,
    string? DerivedFrom,
    IReadOnlyList<string> Forms,
    IReadOnlyList<LemmaCandidate>? Family);

/// <summary>Provenance / version of the loaded DPD-lemma asset (from its <c>meta</c> table).</summary>
public sealed record DpdLemmaMeta(
    string Scope,
    string DpdVersion,
    string ConverterVersion,
    string SchemaVersion,
    string License,
    string Attribution);
