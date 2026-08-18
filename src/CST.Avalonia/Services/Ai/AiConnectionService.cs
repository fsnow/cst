using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CST.Avalonia.Models;
using CST.Avalonia.Models.Ai;

namespace CST.Avalonia.Services.Ai
{
    /// <summary>The outcome of an operation that can fail for a reason worth showing the reader.</summary>
    public sealed record AiConnectionResult(bool Ok, string? Problem = null, AiConnection? Connection = null)
    {
        public static AiConnectionResult Success(AiConnection c) => new(true, null, c);
        public static AiConnectionResult Fail(string problem) => new(false, problem);
    }

    /// <summary>
    /// Everything the UI needs to manage endpoints, without touching <c>settings.json</c> or the credential
    /// store itself. (#689; consumed by #691, #692, #693)
    /// </summary>
    public interface IAiConnectionService
    {
        /// <summary>What is configured now, in the order the reader added it.</summary>
        IReadOnlyList<AiConnection> Connections { get; }

        /// <summary>Named endpoints a reader can add. Facts only — no model lists, no rankings.</summary>
        IReadOnlyList<AiProviderPreset> Presets { get; }

        /// <summary>Presets with no connection yet, i.e. what an "add a provider" list should show. A preset
        /// drops out once added, so the catalogue always reads as "what you could add next".</summary>
        IReadOnlyList<AiProviderPreset> AvailablePresets { get; }

        /// <summary>The connection a request goes to, or null when nothing is configured.</summary>
        AiConnection? Active { get; }

        /// <summary>The model within <see cref="Active"/>, or null.</summary>
        string? ActiveModelId { get; }

        /// <summary>Raised when the list, or any connection's state, changes. Rebind on this.</summary>
        event EventHandler? ConnectionsChanged;

        AiConnectionResult Add(string id, AiConnectionDraft draft);

        /// <summary>Creates a connection from a preset, with its base URL, kind and headers pre-filled.</summary>
        /// <param name="inputs">Answers to the preset's <see cref="AiProviderPreset.Prompts"/> — resource
        /// name, account id and so on. Pass an empty dictionary when the preset asks for nothing.</param>
        AiConnectionResult AddFromPreset(string presetId, IReadOnlyDictionary<string, string> inputs);

        /// <summary>Edits everything except the id, which is immutable because the credential is filed under it.</summary>
        AiConnectionResult Update(string id, AiConnectionDraft draft);

        /// <summary>Removes the connection and its models. Does not touch the credential — that is a separate
        /// action, because for a custom endpoint the hand-entered model list is real user work and destroying
        /// it on an action meant only to stop billing would be data loss.</summary>
        AiConnectionResult Remove(string id);

        /// <summary>Chooses what the next request uses. A null model clears the choice.</summary>
        AiConnectionResult SetActive(string connectionId, string? modelId);

        /// <summary>Turns one model on or off in the per-turn picker.</summary>
        AiConnectionResult SetModelEnabled(string connectionId, string modelId, bool enabled);

        /// <summary>
        /// Records what a real request just learned about an endpoint. (#673)
        ///
        /// <para>This is what stops Settings claiming "Connected" while the assistant reports it cannot
        /// connect. The app already knows — it made the request — and the whole defect is that the knowledge
        /// never reaches the surface a reader consults to diagnose. Both now read one fact.</para>
        /// </summary>
        void ReportReachability(string connectionId, bool reachable);
    }

    /// <summary>
    /// Settings-backed implementation. (#689)
    ///
    /// <para><b>No credential handling yet, by design.</b> <see cref="AiConnection.KeySource"/> reports
    /// <c>None</c> and <see cref="AiConnection.State"/> reports <c>Configured</c> until the credential
    /// re-keying and the reachability write-back land. Both are already on the record, so neither addition
    /// changes a signature the UI binds to — which is what lets the three UI issues start now rather than
    /// waiting on keychain plumbing.</para>
    /// </summary>
    public sealed class AiConnectionService : IAiConnectionService
    {
        private static readonly Regex SlugPattern = new("^[a-z0-9][a-z0-9_-]*$", RegexOptions.Compiled);

        private readonly ISettingsService _settings;
        private readonly IAiCredentialStore? _credentials;

        /// <summary>
        /// Last-known reachability, in memory only.
        ///
        /// <para><b>Deliberately not persisted.</b> "Unreachable" is a fact about a moment — a laptop that was
        /// offline, a local runner that was not started yet — and writing it to settings would greet the reader
        /// with a red endpoint on the next launch that no amount of fixing clears until something happens to
        /// retry it. Every connection starts each session as <c>Configured</c>, which is the honest state:
        /// not yet checked.</para>
        /// </summary>
        private readonly Dictionary<string, Reachability> _reachability = new(StringComparer.OrdinalIgnoreCase);

        /// <param name="credentials">Optional: a platform with nowhere safe to put a key still runs, and an
        /// endpoint needing no key still works. Resolved with <c>GetService</c> for that reason.</param>
        public AiConnectionService(ISettingsService settings, IAiCredentialStore? credentials = null)
        {
            _settings = settings;
            _credentials = credentials;
        }

        public event EventHandler? ConnectionsChanged;

        private ChatSettings Chat => _settings.Settings.Ai.Chat;

        public IReadOnlyList<AiConnection> Connections =>
            Chat.Connections.Select(ToRuntime).ToList();

        /// <summary>
        /// Where this connection's credential came from. (#678, #689)
        ///
        /// <para>Only <c>Keychain</c> and <c>None</c> today. <c>Environment</c> arrives with the
        /// <c>CST_AI_*</c> discovery work, and when it does the rule is that a found credential makes a
        /// provider <i>available</i>, never <i>connected</i> — so it will be reported here without a
        /// connection existing until the reader acts.</para>
        /// </summary>
        private CredentialSource SourceFor(string connectionId) =>
            _credentials?.GetApiKey(connectionId) is not null
                ? CredentialSource.Keychain
                : CredentialSource.None;

        public IReadOnlyList<AiProviderPreset> Presets => AiProviderPresets.All;

        public IReadOnlyList<AiProviderPreset> AvailablePresets =>
            AiProviderPresets.All
                .Where(p => !Chat.Connections.Any(c => IdMatches(c.Id, p.Id)))
                .ToList();

        public AiConnection? Active =>
            Find(Chat.ActiveConnectionId) is { } record ? ToRuntime(record) : null;

        public string? ActiveModelId => Chat.ActiveModelId;

        public AiConnectionResult Add(string id, AiConnectionDraft draft)
        {
            var problem = ValidateId(id, existingAllowed: false);
            if (problem is not null) return AiConnectionResult.Fail(problem);

            var record = new AiConnectionRecord { Id = id };
            Apply(record, draft);
            Chat.Connections.Add(record);
            Chat.ActiveConnectionId ??= record.Id;

            return Saved(record);
        }

        public AiConnectionResult AddFromPreset(string presetId, IReadOnlyDictionary<string, string> inputs)
        {
            var preset = AiProviderPresets.ById(presetId);
            if (preset is null) return AiConnectionResult.Fail($"'{presetId}' is not a known provider.");

            if (Find(preset.Id) is not null)
                return AiConnectionResult.Fail($"{preset.DisplayName} is already configured.");

            // A preset's prompts are required inputs: without them the base URL keeps its {placeholders} and
            // the connection can never send anything. Refuse here rather than create something unusable.
            foreach (var key in AiTemplate.PlaceholdersIn(preset.BaseUrl))
                if (!inputs.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                    return AiConnectionResult.Fail(
                        $"{preset.DisplayName} needs {PromptLabel(preset, key)} before it can be added.");

            var record = new AiConnectionRecord
            {
                Id = preset.Id,
                DisplayName = preset.DisplayName,
                Kind = preset.Kind == ChatProviderKind.Anthropic ? "anthropic" : "openai-compatible",
                BaseUrl = preset.BaseUrl,
                Headers = new Dictionary<string, string>(preset.Headers ?? new Dictionary<string, string>()),
                Inputs = new Dictionary<string, string>(inputs),
                AuthHeaderName = preset.AuthHeaderName,
                AuthScheme = preset.AuthScheme,
            };
            Chat.Connections.Add(record);
            Chat.ActiveConnectionId ??= record.Id;

            return Saved(record);
        }

        public AiConnectionResult Update(string id, AiConnectionDraft draft)
        {
            if (Find(id) is not { } record) return AiConnectionResult.Fail($"No connection called '{id}'.");

            Apply(record, draft);
            return Saved(record);
        }

        public AiConnectionResult Remove(string id)
        {
            if (Find(id) is not { } record) return AiConnectionResult.Fail($"No connection called '{id}'.");

            Chat.Connections.Remove(record);

            // The credential goes with the record. Removing a connection while leaving its key behind would
            // leave an orphan in the keychain that nothing can ever reach or clean up - and that would be
            // silently re-adopted if someone later created a connection with the same id.
            _credentials?.DeleteApiKey(record.Id);

            // Do not leave the active pointer dangling at something that no longer exists - a stale id reads
            // as "configured" to anything that only checks for null.
            if (IdMatches(Chat.ActiveConnectionId, id))
            {
                Chat.ActiveConnectionId = Chat.Connections.FirstOrDefault()?.Id;
                Chat.ActiveModelId = null;
            }

            _settings.RequestSave();
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);
            return new AiConnectionResult(true);
        }

        public AiConnectionResult SetActive(string connectionId, string? modelId)
        {
            if (Find(connectionId) is not { } record)
                return AiConnectionResult.Fail($"No connection called '{connectionId}'.");

            if (modelId is not null &&
                !record.Models.Any(m => string.Equals(m.Id, modelId, StringComparison.Ordinal)))
                return AiConnectionResult.Fail($"'{modelId}' is not a model on {record.DisplayName}.");

            Chat.ActiveConnectionId = record.Id;
            Chat.ActiveModelId = modelId;
            return Saved(record);
        }

        public void ReportReachability(string connectionId, bool reachable)
        {
            var state = reachable ? Reachability.Reachable : Reachability.Unreachable;

            if (_reachability.TryGetValue(connectionId, out var current) && current == state) return;

            _reachability[connectionId] = state;
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        }

        public AiConnectionResult SetModelEnabled(string connectionId, string modelId, bool enabled)
        {
            if (Find(connectionId) is not { } record)
                return AiConnectionResult.Fail($"No connection called '{connectionId}'.");

            var model = record.Models.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.Ordinal));
            if (model is null) return AiConnectionResult.Fail($"'{modelId}' is not a model on {record.DisplayName}.");

            model.Enabled = enabled;
            return Saved(record);
        }

        // ---- internals -------------------------------------------------------------------------------

        private AiConnectionRecord? Find(string? id) =>
            id is null ? null : Chat.Connections.FirstOrDefault(c => IdMatches(c.Id, id));

        private static bool IdMatches(string? a, string? b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private AiConnectionResult Saved(AiConnectionRecord record)
        {
            _settings.RequestSave();
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);
            return AiConnectionResult.Success(ToRuntime(record));
        }

        private static void Apply(AiConnectionRecord record, AiConnectionDraft draft)
        {
            record.DisplayName = draft.DisplayName;
            record.Kind = draft.Kind == ChatProviderKind.Anthropic ? "anthropic" : "openai-compatible";
            record.BaseUrl = draft.BaseUrl;
            record.Models = draft.Models
                .Select(m => new AiModelRecord { Id = m.Id, DisplayName = m.DisplayName, Enabled = m.Enabled })
                .ToList();
            record.Headers = new Dictionary<string, string>(draft.Headers);
            record.Inputs = new Dictionary<string, string>(draft.Inputs);
            record.AuthHeaderName = draft.AuthHeaderName;
            record.AuthScheme = draft.AuthScheme;
        }

        private AiConnection ToRuntime(AiConnectionRecord r) => new(
            r.Id,
            r.DisplayName,
            ChatProviderResolver.TryParseKind(r.Kind, out var kind) ? kind : ChatProviderKind.OpenAiCompatible,
            r.BaseUrl,
            r.Models.Select(m => new AiModelEntry(m.Id, m.DisplayName, m.Enabled)).ToList(),
            new Dictionary<string, string>(r.Headers),
            new Dictionary<string, string>(r.Inputs),
            SourceFor(r.Id),
            _reachability.TryGetValue(r.Id, out var state) ? state : Reachability.Configured,
            r.AuthHeaderName,
            r.AuthScheme);

        /// <summary>
        /// Ids are the reserved namespace a custom connection may not take, and they become the credential's
        /// account name — so a collision would mean one connection quietly inheriting another's key.
        /// </summary>
        private string? ValidateId(string id, bool existingAllowed)
        {
            if (string.IsNullOrWhiteSpace(id)) return "A connection needs an id.";
            if (!SlugPattern.IsMatch(id))
                return "An id may use only lowercase letters, numbers, hyphens and underscores.";
            if (AiProviderPresets.IsReservedId(id))
                return $"'{id}' is the id of a built-in provider. Add it from the provider list instead.";
            if (!existingAllowed && Find(id) is not null)
                return $"There is already a connection called '{id}'.";
            return null;
        }

        private static string PromptLabel(AiProviderPreset preset, string key) =>
            preset.Prompts?.FirstOrDefault(p => p.Key == key)?.Message ?? key;
    }
}
