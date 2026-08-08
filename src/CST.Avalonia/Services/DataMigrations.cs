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
/// FILE and is deliberately pure. These reshape what sits on disk around it, so they do I/O - but they
/// stay free of DI and of app services so they remain directly unit-testable against a temp directory.
///
/// Applied migrations are recorded by id in <see cref="ApplicationState.AppliedDataMigrations"/>, so each
/// runs exactly once. Ids are permanent: renaming one re-runs it on every existing install.
///
/// <b>Write every migration to be idempotent anyway.</b> The recorded id is the primary guard, but a
/// migration can also meet a half-finished state from an interrupted run, or a data directory restored
/// from backup while the state file moved on. Belt and braces is cheap here; a destructive migration that
/// assumes it is running on a pristine "before" state is not.
/// </summary>
public static class DataMigrations
{
    /// <summary>Inputs a migration may act on. No services, no DI - everything is a plain path.</summary>
    public sealed class Context
    {
        /// <summary>The user data root (.../CSTReader).</summary>
        public required string DataDirectory { get; init; }

        /// <summary>
        /// The bundled dictionaries shipped with the app, or null if they could not be located. A migration
        /// that deletes user-visible content should consult this rather than assume: if we cannot see what
        /// the app ships, we cannot safely conclude anything is redundant.
        /// </summary>
        public string? BundledDictionariesDirectory { get; init; }
    }

    /// <summary>What the runner should do with a migration once it returns.</summary>
    public enum Outcome
    {
        /// <summary>Work is done, or provably unnecessary. Record it; never run again.</summary>
        Done,

        /// <summary>
        /// The environment was not in a state where the migration could decide safely - not a failure, and
        /// not "nothing to do". Leave it UNRECORDED so it re-attempts next launch. Recording an environment
        /// problem would forfeit the migration permanently on the strength of one bad launch.
        /// </summary>
        Retry,
    }

    public sealed record Migration(string Id, string Description, Func<Context, List<string>, Outcome> Apply);

    /// <summary>Ordered. Append new migrations; never renumber or rename existing ids.</summary>
    public static readonly IReadOnlyList<Migration> All = new List<Migration>
    {
        new("2026-08-retire-en-hi-dictionary-ids",
            "Remove the superseded en/hi dictionary directories left behind by the #522 rename",
            RetireEnHiDictionaryIds),
    };

    /// <summary>
    /// Apply any migrations not yet recorded in <paramref name="state"/>, in order. Returns human-readable
    /// notes for logging.
    ///
    /// A migration returning <see cref="Outcome.Retry"/>, or throwing, is NOT recorded and re-attempts next
    /// launch rather than being silently skipped forever. A throwing migration does not block the others.
    /// </summary>
    public static IReadOnlyList<string> Run(ApplicationState state, Context context) =>
        Run(state, context, All);

    // Overload taking the migration list so tests can drive the runner itself - the failure and retry paths
    // are part of the contract and are not reachable through the fixed static list.
    internal static IReadOnlyList<string> Run(ApplicationState state, Context context, IEnumerable<Migration> migrations)
    {
        var notes = new List<string>();
        if (state == null) return notes;

        var applied = state.AppliedDataMigrations ??= new List<string>();

        foreach (var migration in migrations)
        {
            if (applied.Contains(migration.Id, StringComparer.Ordinal))
                continue;

            try
            {
                var before = notes.Count;
                var outcome = migration.Apply(context, notes);

                if (outcome == Outcome.Retry)
                {
                    if (notes.Count == before)
                        notes.Add($"migration {migration.Id}: deferred, will retry next launch");
                    continue;
                }

                applied.Add(migration.Id);

                // Only announce migrations that actually did something; a no-op on a clean install is the
                // common case and does not deserve a line in everyone's log. This note is also what makes
                // notes.Count > 0 on a clean install, which is what gets the record persisted at all - do
                // not make it conditional.
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
    /// #522 renamed the bundled dictionary ids (en to vri-childers, hi to vri-hindi) but nothing removed the
    /// superseded directories from an existing install, and seeding only ever writes what is MISSING. So an
    /// install carried over from beta 5 ends up with both generations on disk. Because
    /// <c>DictionaryService.AvailableLanguages</c> enumerates directories, and each retired id carries the
    /// same displayName as its replacement, every affected user sees each dictionary listed twice. (#564)
    ///
    /// Keyed off the BUNDLED dictionaries rather than the seeded copy in the data directory. Both happen to
    /// be present by the time this runs today - DictionaryService is constructed during layout creation,
    /// well before the state load this hangs off - but that ordering is an accident of DI resolution and not
    /// something a destructive migration should depend on. What the app SHIPS is the stable fact.
    ///
    /// Downgrade note: beta 5 re-seeds en/hi from its own bundle, and its serializer drops the unknown
    /// appliedDataMigrations property when it next saves - so downgrade-then-upgrade usually erases the
    /// record and this simply runs again. If beta 5 never rewrites the state file, the duplicates return
    /// and stay.
    /// </summary>
    private static Outcome RetireEnHiDictionaryIds(Context context, List<string> notes)
    {
        var retired = new (string Old, string New)[] { ("en", "vri-childers"), ("hi", "vri-hindi") };

        // Without sight of the bundled dictionaries we cannot tell "superseded" from "the only copy". That
        // is an environment problem (an odd install layout, an unreadable ancestor), not a decision - so
        // retry rather than record, or one bad launch forfeits the cleanup permanently.
        if (context.BundledDictionariesDirectory is not { } bundled || !Directory.Exists(bundled))
        {
            notes.Add("retire-en-hi: bundled dictionaries not found; deferring (will retry next launch)");
            return Outcome.Retry;
        }

        var dictionaries = Path.Combine(context.DataDirectory, "dictionaries");
        if (!Directory.Exists(dictionaries)) return Outcome.Done;

        foreach (var (oldId, newId) in retired)
        {
            var oldDir = Path.Combine(dictionaries, oldId);
            if (!Directory.Exists(oldDir)) continue;

            // The app must actually ship the replacement, or removing the old one loses the dictionary.
            var replacement = Path.Combine(bundled, newId);
            if (!Directory.Exists(replacement))
            {
                notes.Add($"retire-en-hi: '{oldId}' kept - the app does not ship '{newId}'");
                continue;
            }

            if (RedundantAgainst(oldDir, replacement, out var reason))
            {
                Directory.Delete(oldDir, recursive: true);
                notes.Add($"retire-en-hi: removed superseded dictionary id '{oldId}' (replaced by '{newId}')");
            }
            else
            {
                notes.Add($"retire-en-hi: '{oldId}' kept - {reason}");
            }
        }

        return Outcome.Done;
    }

    /// <summary>
    /// True only when every file in <paramref name="oldDir"/> is provably reproduced by
    /// <paramref name="replacement"/>, so deleting it cannot lose anything.
    ///
    /// Name-matching alone is not enough, and an earlier draft got this wrong by treating any *.txt as
    /// "ours". Two ways that loses real data:
    ///   - the loader merges EVERY *.txt in a dictionary directory, so a user's own glossary dropped into
    ///     en/ is live content, not leftovers;
    ///   - seeding is write-if-missing precisely so an existing install's data is never clobbered, so a
    ///     user-EDITED dictionary file keeps its edits - and the shipped replacement does not have them.
    /// Requiring byte equality against what the app ships covers both: anything added or changed fails the
    /// comparison and the directory is kept.
    ///
    /// source.json is exempt because it is app-owned metadata that seeding already refreshes from the
    /// bundle whenever it differs (see <see cref="DictionaryService.SeedDictionaries"/>), so a difference
    /// there is ours, not the user's.
    /// </summary>
    private static bool RedundantAgainst(string oldDir, string replacement, out string reason)
    {
        if (Directory.EnumerateDirectories(oldDir).Any())
        {
            reason = "it holds a subdirectory we did not create";
            return false;
        }

        foreach (var file in Directory.EnumerateFiles(oldDir))
        {
            var name = Path.GetFileName(file);

            if (string.Equals(name, "source.json", StringComparison.OrdinalIgnoreCase))
                continue;   // app-owned metadata, refreshed from the bundle anyway

            var counterpart = Path.Combine(replacement, name);
            if (!File.Exists(counterpart))
            {
                reason = $"it holds '{name}', which the app does not ship";
                return false;
            }

            if (!FilesEqual(file, counterpart))
            {
                reason = $"'{name}' differs from the shipped copy (edited in place?)";
                return false;
            }
        }

        reason = "";
        return true;
    }

    private static bool FilesEqual(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}
