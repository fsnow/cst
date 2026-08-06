using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CST.Avalonia.Models;

namespace CST.Avalonia.Services;

/// <summary>
/// One-time migrations of the user DATA directory (dictionaries, downloaded assets, indexes).
///
/// Distinct from <see cref="ApplicationStateValidator.Migrate"/>, which upgrades the shape of the state
/// FILE and is deliberately pure. These reshape what sits on disk around it, so they do I/O — but they
/// stay free of DI and of app services so they remain directly unit-testable against a temp directory.
///
/// Applied migrations are recorded by id in <see cref="ApplicationState.AppliedDataMigrations"/>, so each
/// runs exactly once. Ids are permanent: renaming one re-runs it on every existing install.
///
/// **Write every migration to be idempotent anyway.** The recorded id is the primary guard, but a
/// migration can also meet a half-finished state from an interrupted run, or a data directory restored
/// from backup while the state file moved on. Belt and braces is cheap here; a destructive migration that
/// assumes it is running on a pristine "before" state is not.
/// </summary>
public static class DataMigrations
{
    /// <summary>Inputs a migration may act on. No services, no DI — everything is a plain path.</summary>
    public sealed class Context
    {
        /// <summary>The user data root (…/CSTReader).</summary>
        public required string DataDirectory { get; init; }

        /// <summary>
        /// The bundled dictionaries shipped with the app, or null if they could not be located. A migration
        /// that deletes user-visible content should consult this rather than assume: if we cannot see what
        /// the app ships, we cannot safely conclude anything is redundant.
        /// </summary>
        public string? BundledDictionariesDirectory { get; init; }
    }

    public sealed record Migration(string Id, string Description, Action<Context, List<string>> Apply);

    /// <summary>Ordered. Append new migrations; never renumber or rename existing ids.</summary>
    public static readonly IReadOnlyList<Migration> All = new List<Migration>
    {
        new("2026-08-retire-en-hi-dictionary-ids",
            "Remove the superseded en/hi dictionary directories left behind by the #522 rename",
            RetireEnHiDictionaryIds),
    };

    /// <summary>
    /// Apply any migrations not yet recorded in <paramref name="state"/>, in order. Returns human-readable
    /// notes for logging. A failing migration is recorded as failed and does NOT block the others, and is
    /// NOT marked applied — so it will be retried next launch rather than silently skipped forever.
    /// </summary>
    public static IReadOnlyList<string> Run(ApplicationState state, Context context)
    {
        var notes = new List<string>();
        if (state == null) return notes;

        var applied = state.AppliedDataMigrations ??= new List<string>();

        foreach (var migration in All)
        {
            if (applied.Contains(migration.Id, StringComparer.Ordinal))
                continue;

            try
            {
                var before = notes.Count;
                migration.Apply(context, notes);
                applied.Add(migration.Id);

                // Only announce migrations that actually did something; a no-op on a clean install is the
                // common case and does not deserve a line in everyone's log.
                if (notes.Count == before)
                    notes.Add($"migration {migration.Id}: nothing to do");
            }
            catch (Exception ex)
            {
                notes.Add($"migration {migration.Id} FAILED (will retry next launch): {ex.Message}");
            }
        }

        return notes;
    }

    // ===== migrations =====

    /// <summary>
    /// #522 renamed the bundled dictionary ids (en → vri-childers, hi → vri-hindi) but nothing removed the
    /// superseded directories from an existing install, and seeding only ever writes what is MISSING. So an
    /// install carried over from beta 5 ends up with both generations on disk. Because
    /// <c>DictionaryService.AvailableLanguages</c> enumerates directories, and each retired id carries the
    /// same displayName as its replacement, every affected user sees each dictionary listed twice. (#564)
    ///
    /// Deliberately keyed off the BUNDLED dictionaries, not the seeded ones. On the first launch after
    /// upgrading, the replacement has not been seeded into the data directory yet — migrations run before
    /// DictionaryService is constructed — so a check against the data directory would find no replacement,
    /// do nothing, and leave the duplicates visible for that whole session.
    /// </summary>
    private static void RetireEnHiDictionaryIds(Context context, List<string> notes)
    {
        var retired = new (string Old, string New)[] { ("en", "vri-childers"), ("hi", "vri-hindi") };

        var dictionaries = Path.Combine(context.DataDirectory, "dictionaries");
        if (!Directory.Exists(dictionaries)) return;

        // Without sight of the bundled dictionaries we cannot tell "superseded" from "the only copy".
        if (context.BundledDictionariesDirectory is not { } bundled || !Directory.Exists(bundled))
        {
            notes.Add("retire-en-hi: bundled dictionaries not found; leaving the data directory alone");
            return;
        }

        foreach (var (oldId, newId) in retired)
        {
            var oldDir = Path.Combine(dictionaries, oldId);
            if (!Directory.Exists(oldDir)) continue;

            // The app must actually ship the replacement, or removing the old one loses the dictionary.
            if (!Directory.Exists(Path.Combine(bundled, newId)))
            {
                notes.Add($"retire-en-hi: '{oldId}' kept — the app does not ship '{newId}'");
                continue;
            }

            // Only remove what we put there. Anything else means the user (or a future drop-in source) has
            // added files, and a rename cleanup has no business deleting those.
            var unexpected = Directory.EnumerateFiles(oldDir)
                .Select(Path.GetFileName)
                .Where(name => !IsSeededFileName(name))
                .ToList();
            if (unexpected.Count > 0 || Directory.EnumerateDirectories(oldDir).Any())
            {
                notes.Add($"retire-en-hi: '{oldId}' kept — it holds files we did not seed ({string.Join(", ", unexpected)})");
                continue;
            }

            Directory.Delete(oldDir, recursive: true);
            notes.Add($"retire-en-hi: removed superseded dictionary id '{oldId}' (replaced by '{newId}')");
        }
    }

    // The only files seeding ever writes into a bundled dictionary directory: the flat-file dictionary
    // itself and the app-owned source.json metadata.
    private static bool IsSeededFileName(string? name) =>
        name is not null &&
        (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(name, "source.json", StringComparison.OrdinalIgnoreCase));
}
