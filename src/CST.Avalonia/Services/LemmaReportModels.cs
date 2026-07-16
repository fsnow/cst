using System.Collections.Generic;

namespace CST.Avalonia.Services;

// The structured lemma report ("dossier"). Every *Pali field is stored in IPE (script-neutral, so the report
// caches once and renders to any script); English fields are verbatim. The renderer converts only the Pali ones.

public sealed record ReportRoot(
    string? RootPali, string? RootMeaning, long? RootGroup,
    string? SanskritRoot, string? DhatupathaPali, string? DhatupathaEnglish);   // SanskritRoot: verbatim IAST, not script-converted

public sealed record ReportForm(string FormPali, int Count, int BookCount, bool Homograph);

public sealed record ReportFamilyMember(
    long LemmaId, string LemmaPali, string? Pos, string? Gloss, int TotalOccurrences, string Group);

public sealed record ReportHomographSense(long LemmaId, string LemmaPali, string? Pos, string? Gloss, string? DerivedFromPali);

/// <summary>One paradigm form that is a homograph, with every lemma the surface string belongs to.</summary>
public sealed record ReportHomograph(string FormPali, int Count, IReadOnlyList<ReportHomographSense> Senses);

public sealed record LemmaReport(
    long LemmaId, string LemmaPali, string? Pos, string? Gloss, string? MeaningLit, string? DerivedFromPali,
    // etymology  (Sanskrit: verbatim IAST — outside the Pāli IPE inventory, so NOT script-converted)
    string? ConstructionPali, string? Sanskrit, string? Pattern, ReportRoot? Root, long? EbtCount,
    // corpus paradigm (this lemma)
    IReadOnlyList<ReportForm> Forms, int TotalOccurrences, int AttestedFormCount, int CandidateFormCount, bool ExpansionCapped,
    // DPD-cited example
    string? ExamplePali, string? ExampleSource, string? ExampleSutta,
    // word family (derived_from siblings, grouped) + the family:true union totals
    IReadOnlyList<ReportFamilyMember> Family, int FamilyTotalOccurrences, int FamilyFormCount,
    // homographs — EVERY paradigm form shared with other lemmas (its count can't be split)
    IReadOnlyList<ReportHomograph> Homographs,
    // provenance
    string DpdVersion, string Attribution);
