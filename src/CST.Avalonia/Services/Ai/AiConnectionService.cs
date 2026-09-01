using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CST.Avalonia.Models;
using CST.Avalonia.Services.Ai.Credentials;
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

        /// <summary>
        /// Whether the preset list is available, and why not. (#737, requested by #739)
        ///
        /// <para>Needed because two of the three outcomes arrive as an empty list — "all added" is a quiet
        /// end state, "couldn't reach the catalogue" needs a sentence and a retry, and "not fetched yet"
        /// wants "loading". A bare list cannot tell them apart, and the loud case reads as a broken
        /// feature.</para>
        /// </summary>
        AiPresetState PresetState { get; }

        /// <summary>A finished sentence to show, or null.</summary>
        string? PresetProblem { get; }

        /// <summary>What a Retry button calls.</summary>
        Task RefreshPresetsAsync(CancellationToken ct = default);

        /// <summary>Presets with no connection yet, i.e. what an "add a provider" list should show. A preset
        /// drops out once added, so the catalogue always reads as "what you could add next".</summary>
        IReadOnlyList<AiProviderPreset> AvailablePresets { get; }

        /// <summary>The connection a request goes to, or null when nothing is configured.</summary>
        AiConnection? Active { get; }

        /// <summary>The model within <see cref="Active"/>, or null.</summary>
        string? ActiveModelId { get; }

        /// <summary>
        /// Records which of a connection's stored models the provider's listing still carries. (#728)
        ///
        /// <para>Call it <b>only</b> with the models of a fetch that succeeded. A failed fetch, or one from an
        /// endpoint that publishes no listing, must not call this at all: there is nothing to conclude, and
        /// concluding anyway would mark every model on a laptop whose runner is simply not started.</para>
        ///
        /// <para>An empty list is treated as no evidence rather than as total removal, and nothing is written
        /// unless a mark actually changed.</para>
        /// </summary>
        AiConnectionResult MarkListing(string connectionId, IReadOnlyList<string> listedIds);

        /// <summary>Raised when the list, or any connection's state, changes. Rebind on this.</summary>
        event EventHandler? ConnectionsChanged;

        AiConnectionResult Add(string id, AiConnectionDraft draft);

        /// <summary>Creates a connection from a preset, with its base URL, kind and headers pre-filled.</summary>
        /// <param name="inputs">Answers to the preset's <see cref="AiProviderPreset.Prompts"/> — resource
        /// name, account id and so on. Pass an empty dictionary when the preset asks for nothing.</param>
        /// <param name="environmentVariable">
        /// The environment variable the reader chose to authenticate with, or null for the ordinary path.
        /// (#714)
        ///
        /// <para><b>The NAME is the opt-in, not a flag beside it.</b> Discovery is automatic and adoption is
        /// not: finding a variable set makes a provider available, and only this — passed from a click — makes
        /// anything authenticate with it. Carrying the name rather than a boolean means a consent with nothing
        /// recorded cannot be expressed at all, where a flag-plus-name pair can be half-set and has to be
        /// defended against downstream.</para>
        ///
        /// <para>It is the name the reader was SHOWN, passed down from the row they pressed — not one
        /// re-derived from the preset here. The preset's variable list is refreshed from models.dev, so
        /// re-deriving it would let a reordering upstream change which credential a recorded connection uses,
        /// months later and with no second consent. That is the property this feature exists to prevent,
        /// arriving late instead of at the start. (fable, on #813)</para>
        ///
        /// <para>Defaulted null so every existing caller keeps the behaviour it had, and so adopting is
        /// something a caller has to say rather than something it can fall into.</para>
        /// </param>
        AiConnectionResult AddFromPreset(
            string presetId, IReadOnlyDictionary<string, string> inputs, string? environmentVariable = null);

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
        /// Turns a model on or off, adding it to the connection first if it is not there yet. (#674)
        ///
        /// <para>The verb a <i>fetched</i> listing needs. A provider's catalogue can run to hundreds of
        /// models and none of them belongs in <c>settings.json</c> until the reader has chosen it — storing
        /// all 414 so they can each carry a <c>false</c> would bloat the settings file to make a point the
        /// file's emptiness already makes. So the stored list stays what it has always been: the models this
        /// reader picked, whether they typed the id or promoted it from a listing.</para>
        ///
        /// <para>Turning one off keeps the entry rather than deleting it, so a display name the reader typed
        /// survives being switched off and on again.</para>
        /// </summary>
        AiConnectionResult EnableModel(
            string connectionId, string modelId, string displayName, bool enabled,
            AiModelEntry? facts = null);

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
        private readonly IAiPresetSource? _presets;
        private readonly IAiEnvironmentKeys? _environmentKeys;

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

        /// <summary>Guards <see cref="_reachability"/>, which a background turn writes and the UI reads.</summary>
        private readonly object _reachabilityGate = new();

        /// <param name="credentials">Optional: a platform with nowhere safe to put a key still runs, and an
        /// endpoint needing no key still works. Resolved with <c>GetService</c> for that reason.</param>
        public AiConnectionService(
            ISettingsService settings,
            IAiCredentialStore? credentials = null,
            IAiPresetSource? presets = null,
            IAiEnvironmentKeys? environmentKeys = null)
        {
            _settings = settings;
            _credentials = credentials;
            _presets = presets;
            _environmentKeys = environmentKeys;

            // The preset list changing is a change to what this service reports, so it reaches the UI on the
            // one event it already binds to rather than through a second channel.
            if (_presets is not null)
                _presets.PresetsChanged += (_, _) => RaiseChanged();

            // AND SO IS THE ENVIRONMENT ANSWERING. SourceFor consults _environmentKeys live, so a connection
            // adopted from the environment reports Keychain/Environment/None according to what the variable
            // holds at the moment it is asked — which means the answer CHANGES the instant the login-shell
            // probe lands, without anything here doing so.
            //
            // Nothing was listening. The probe is started at launch (App.axaml.cs PrimeShellEnvironment) and
            // finishes a few hundred milliseconds later; by then the model picker had already asked, been
            // told None, and greyed out every model with "no API key stored". Opening Settings > Providers
            // repaired it only as a side effect of constructing a view model that rebinds. So the Assistant
            // announced itself unconfigured on every launch to exactly the readers #714 and #817 exist for,
            // and the cure was to open a tab and change nothing. (#852)
            if (_environmentKeys is not null)
                _environmentKeys.Changed += (_, _) => RaiseChanged();
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
        /// <summary>Reads last-known reachability under the lock; see <see cref="ReportReachability"/>.</summary>
        private Reachability ReachabilityOf(string connectionId)
        {
            lock (_reachabilityGate)
                return _reachability.TryGetValue(connectionId, out var state) ? state : Reachability.Configured;
        }

        /// <summary>
        /// Where this connection's credential comes from. (#689, #714)
        ///
        /// <para><b>Stored wins.</b> Entering a key is a deliberate act and a variable in the environment is
        /// often forgotten — the maintainer was surprised by one on his own machine — so a reader who typed a
        /// key must not find the app quietly using something else instead. The reverse order would also make
        /// the stored key unreachable without unsetting a variable, which is not something the app can offer
        /// to do.</para>
        ///
        /// <para>A discovered credential counts only for a connection that was ADOPTED from the environment.
        /// Discovery alone never makes a connection authenticated: that is the opt-in step, and this method
        /// reports state rather than creating it.</para>
        /// </summary>
        private CredentialSource SourceFor(string connectionId)
        {
            // Read, not Get (#926). A stored key this build cannot read is a different state from no key, and
            // it must not fall through to the environment either: the whole point of "stored wins" above is
            // that a key the reader typed is not quietly replaced by a variable they forgot they set, and an
            // authorization the reader can still grant does not make their key stop counting.
            var read = _credentials?.Read(connectionId, AiCredentialNames.Primary);
            if (read?.State == CredentialState.Found) return CredentialSource.Keychain;
            if (read?.State == CredentialState.Unreadable) return CredentialSource.Unreadable;

            // Adopted from the environment, and the variable still holds something. When it does not — unset
            // between sessions, or renamed — this falls through to None, which reads as "no key" rather than
            // as an error, because that is what it is.
            // IdMatches, not Ordinal — one rule for id comparison in this file. The split it avoids is the
            // mechanism behind #805, where the resolver comparing Ordinal against this service's
            // OrdinalIgnoreCase sends a model to the wrong connection. (kestrel-cst-2)
            var record = Chat.Connections.FirstOrDefault(
                r => r is not null && IdMatches(r.Id, connectionId));
            if (record is not null
                && AiEnvironmentCredential.For(
                    record.UsesEnvironmentKey, record.EnvironmentVariable, _environmentKeys) is not null)
                return CredentialSource.Environment;

            return CredentialSource.None;
        }


        public IReadOnlyList<AiProviderPreset> Presets =>
            _presets?.Presets ?? AiPresetSource.SnapshotDefaults;

        public AiPresetState PresetState => _presets?.State ?? AiPresetState.Ready;

        public string? PresetProblem => _presets?.Problem;

        public Task RefreshPresetsAsync(CancellationToken ct = default) =>
            _presets?.RefreshAsync(ct) ?? Task.CompletedTask;

        public IReadOnlyList<AiProviderPreset> AvailablePresets =>
            Presets
                .Where(p => !Chat.Connections.Any(c => IdMatches(c.Id, p.Id)))
                .ToList();

        public AiConnection? Active =>
            Find(Chat.ActiveConnectionId) is { } record ? ToRuntime(record) : null;

        public string? ActiveModelId => Chat.ActiveModelId;

        public AiConnectionResult Add(string id, AiConnectionDraft draft)
        {
            var problem = ValidateId(id, existingAllowed: false);
            if (problem is not null) return AiConnectionResult.Fail(problem);

            if (DuplicateHeader(draft) is { } duplicate) return AiConnectionResult.Fail(duplicate);
            if (DuplicateModel(draft) is { } repeated) return AiConnectionResult.Fail(repeated);
            if (CollidingSecretHeader(draft) is { } collision) return AiConnectionResult.Fail(collision);
            if (UnfilledInput(id, draft) is { } unfilled) return AiConnectionResult.Fail(unfilled);

            // Empty rather than null: this connection is recorded as having NO preset, which is a different
            // fact from "nothing was recorded". See AiConnectionRecord.PresetId. (#766)
            var record = new AiConnectionRecord { Id = id, PresetId = "" };
            Apply(record, draft);
            Chat.Connections.Add(record);
            Chat.ActiveConnectionId ??= record.Id;

            return Saved(record);
        }

        public AiConnectionResult AddFromPreset(
            string presetId, IReadOnlyDictionary<string, string> inputs, string? environmentVariable = null)
        {
            var preset = Presets.FirstOrDefault(p => IdMatches(p.Id, presetId));
            if (preset is null) return AiConnectionResult.Fail($"'{presetId}' is not a known provider.");

            if (Find(preset.Id) is not null)
                return AiConnectionResult.Fail($"{preset.DisplayName} is already configured.");

            // A preset's prompts are required inputs: without them the base URL keeps its {placeholders} and
            // the connection can never send anything. Refuse here rather than create something unusable.
            foreach (var key in AiTemplate.PlaceholdersIn(preset.BaseUrl))
                if (!inputs.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                    return AiConnectionResult.Fail(
                        $"{preset.DisplayName} needs {PromptLabel(preset, key)} before it can be added.");

            if (SecretInUrl(preset) is { } inUrl) return AiConnectionResult.Fail(inUrl);

            var secretKeys = (preset.Prompts ?? Array.Empty<AiInputPrompt>())
                .Where(p => p.Secret)
                .Select(p => p.Key)
                .Where(k => inputs.ContainsKey(k) && !string.IsNullOrWhiteSpace(inputs[k]))
                .ToList();

            // Nowhere to put a secret is a refusal, not a silent downgrade to the plaintext file. #771 made
            // that call for headers and the reasoning is unchanged: writing the credential to settings.json
            // "for now" is the leak, and doing it without telling the reader is the worse half.
            if (secretKeys.Count > 0 && _credentials?.IsAvailable != true)
                return AiConnectionResult.Fail(
                    $"{preset.DisplayName} needs {PromptLabel(preset, secretKeys[0])} stored securely, and "
                    + "this machine has no credential store available.");

            var record = new AiConnectionRecord
            {
                Id = preset.Id,
                // The one moment the origin is a fact rather than an inference. (#766)
                PresetId = preset.Id,
                DisplayName = preset.DisplayName,
                Kind = preset.Kind == ChatProviderKind.Anthropic ? "anthropic" : "openai-compatible",
                BaseUrl = preset.BaseUrl,
                // A preset's headers are never secret: they are routing hints the catalogue publishes, and a
                // preset cannot carry a credential (#771).
                Headers = (preset.Headers ?? new Dictionary<string, string>())
                    .Select(h => new AiHeaderRecord { Name = h.Key, Value = h.Value })
                    .ToList(),
                // Every answer EXCEPT the secret ones. This is the line that keeps a credential out of the
                // settings file, so it is written as a filter rather than as a later removal: a copy-then-
                // delete would put the secret in the object that the save path reads, and correctness would
                // depend on the order of two statements. (#777)
                Inputs = inputs
                    .Where(pair => !secretKeys.Contains(pair.Key, StringComparer.Ordinal))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                SecretInputs = secretKeys.Count > 0 ? secretKeys : null,
                // Recorded only where the caller asked for it, and the two fields are set from ONE value so
                // they cannot disagree. Nothing here consults the environment: this stores the reader's
                // decision, and the decision is the feature. (#714)
                UsesEnvironmentKey = environmentVariable is not null,
                EnvironmentVariable = environmentVariable,
                AuthHeaderName = preset.AuthHeaderName,
                AuthScheme = preset.AuthScheme,
            };

            // Filed BEFORE the record is added, so a store that refuses cannot leave a connection behind that
            // claims to hold a secret nothing can fetch.
            foreach (var key in secretKeys)
                _credentials?.Set(record.Id, AiCredentialNames.Input(key), inputs[key]);

            Chat.Connections.Add(record);
            Chat.ActiveConnectionId ??= record.Id;

            return Saved(record);
        }

        public AiConnectionResult Update(string id, AiConnectionDraft draft)
        {
            if (Find(id) is not { } record) return AiConnectionResult.Fail($"No connection called '{id}'.");

            if (DuplicateHeader(draft) is { } duplicate) return AiConnectionResult.Fail(duplicate);
            if (DuplicateModel(draft) is { } repeated) return AiConnectionResult.Fail(repeated);
            if (CollidingSecretHeader(draft) is { } collision) return AiConnectionResult.Fail(collision);
            if (UnfilledInput(id, draft) is { } unfilled) return AiConnectionResult.Fail(unfilled);

            Apply(record, draft);
            return Saved(record);
        }

        /// <summary>
        /// Two secret headers whose names fold to one credential name, or null when there are none. (#771)
        ///
        /// <para>Header names are richer than credential names: <c>x.y</c> and <c>x-y</c> are different headers
        /// and both fold to <c>header-x-y</c>, so one would silently overwrite the other's secret and the
        /// endpoint would authenticate with the wrong one. Vanishingly rare - real header names are letters,
        /// digits and hyphens - and refused rather than tolerated because the failure is a 401 that names
        /// nothing, which is the same symptom #678 took a release to find.</para>
        ///
        /// <para>Checked here rather than in the sheet because this is the seam every write goes through, and
        /// <c>settings.json</c> is hand-edited.</para>
        /// </summary>
        /// <summary>
        /// Two header rows with the same name, or null when there are none. (#771, fable review)
        ///
        /// <para><b>A regression the shape change introduced.</b> While headers were a
        /// <c>Dictionary&lt;string, string&gt;</c> a duplicate was unrepresentable; a list stores one happily,
        /// and both request paths project back through <c>ToDictionary</c>, which throws. The reader gets
        /// "Something went wrong running that request" from the chat and "An item with the same key has
        /// already been added" from the Models tab — two dead ends naming nothing, persisted across sessions,
        /// with no screen pointing at the row that caused it.</para>
        ///
        /// <para>The natural way in is not exotic: converting a plaintext header to a secret one by adding a
        /// new row rather than ticking the box on the old one, and forgetting to delete the old row.</para>
        ///
        /// <para>Compared case-insensitively because HTTP header names are. HTTP does permit a repeated
        /// header, so this refuses something the protocol allows — deliberately: the previous shape refused it
        /// too, nothing in the app can express what a repeat would mean, and a refusal naming the header beats
        /// an exception naming nothing.</para>
        /// </summary>
        /// <summary>
        /// A base URL still carrying a {placeholder} nothing fills, or null when it is complete. (#767)
        ///
        /// <para><b>The add-from-preset path has always refused this and the two draft paths never did.</b>
        /// So an Azure connection whose resource name is cleared on the edit sheet saved cleanly, the sheet
        /// closed as though it had worked, and what was stored was a base URL still reading
        /// <c>https://{resourceName}.openai.azure.com/openai/v1</c>. The reader found out when a request went
        /// nowhere. Reachable in one action: open Azure from the Providers tab, clear the box, press Save.</para>
        ///
        /// <para><b>Add was missing it too</b>, which the issue did not report — found by reading the three
        /// paths side by side rather than by fixing the one that was named. A custom endpoint typed with a
        /// brace in it saved just as quietly.</para>
        ///
        /// <para>Blank counts as unfilled, exactly as it does on the preset path: expanding an empty answer
        /// leaves no placeholder behind, so a check that only looked for surviving braces would pass a URL
        /// with a hole in it.</para>
        /// </summary>
        private string? UnfilledInput(string id, AiConnectionDraft draft)
        {
            var preset = Presets.FirstOrDefault(p => IdMatches(p.Id, id));

            foreach (var key in AiTemplate.PlaceholdersIn(draft.BaseUrl))
                if (!draft.Inputs.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                    return $"{draft.DisplayName} needs "
                           + $"{(preset is null ? key : PromptLabel(preset, key))} before it can be saved.";

            return null;
        }

        private static string? DuplicateHeader(AiConnectionDraft draft)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in draft.Headers.Where(h => !string.IsNullOrWhiteSpace(h.Name)))
                if (!seen.Add(header.Name.Trim()))
                    return $"There are two {header.Name.Trim()} headers. Remove one.";

            return null;
        }

        /// <summary>
        /// Two model rows carrying one id, or null when there are none. (#870)
        ///
        /// <para>Ordinal and trimmed, because that is the comparison everything downstream makes: the models
        /// tab keys its stored list by the id verbatim, and <see cref="EnableModel"/> resolves a toggle by an
        /// ordinal match. Refused rather than silently collapsed because the reader typed both rows and only
        /// they know which one they meant — a save that quietly dropped one would look like the sheet had
        /// lost an edit.</para>
        ///
        /// <para>The same shape as <see cref="DuplicateHeader"/>, which the models never got: a repeated id
        /// used to reach settings.json and then throw out of the models tab on every subsequent Settings
        /// open, which is a lockout rather than a bad row.</para>
        /// </summary>
        private static string? DuplicateModel(AiConnectionDraft draft)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var model in draft.Models.Where(m => !string.IsNullOrWhiteSpace(m.Id)))
                if (!seen.Add(model.Id.Trim()))
                    return $"There are two {model.Id.Trim()} models. Remove one.";

            return null;
        }

        private static string? CollidingSecretHeader(AiConnectionDraft draft)
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var header in draft.Headers.Where(h => h.Secret && !string.IsNullOrWhiteSpace(h.Name)))
            {
                var name = AiCredentialNames.Header(header.Name);
                if (seen.TryGetValue(name, out var first))
                    return $"The {first} and {header.Name} headers cannot both be secret: "
                           + "they would share one stored value.";
                seen[name] = header.Name;
            }

            return null;
        }

        public AiConnectionResult Remove(string id)
        {
            if (Find(id) is not { } record) return AiConnectionResult.Fail($"No connection called '{id}'.");

            Chat.Connections.Remove(record);

            // The credentials go with the record. Removing a connection while leaving a secret behind would
            // leave an orphan in the keychain that nothing can ever reach or clean up - and that would be
            // silently re-adopted if someone later created a connection with the same id.
            //
            // EVERY name, not just the primary one (#759): a connection may file more than one secret, and an
            // orphan is invisible precisely because nothing reads it. This is the one place that has to know
            // the full set, so it is the one place to extend when a provider adds a name.
            foreach (var name in CredentialNamesOf(record))
                _credentials?.Delete(record.Id, name);

            // Do not leave the active pointer dangling at something that no longer exists - a stale id reads
            // as "configured" to anything that only checks for null.
            if (IdMatches(Chat.ActiveConnectionId, id))
            {
                Chat.ActiveConnectionId = Chat.Connections.FirstOrDefault()?.Id;
                Chat.ActiveModelId = null;
            }

            _settings.RequestSave();
            RaiseChanged();
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

            // Called from a background turn (AiChatOrchestrator), while the UI reads _reachability on the UI
            // thread. Both sides take the lock; without it this is a dictionary mutated during enumeration.
            lock (_reachabilityGate)
            {
                if (_reachability.TryGetValue(connectionId, out var current) && current == state) return;
                _reachability[connectionId] = state;
            }

            RaiseChanged();
        }

        /// <summary>
        /// Raises <see cref="ConnectionsChanged"/> on the UI thread. (fable review)
        ///
        /// <para><b>Why the hop lives here and not in each subscriber.</b> The only off-thread caller is
        /// <c>ReportReachability</c>, invoked from a chat turn whose continuations run under
        /// <c>ConfigureAwait(false)</c> — so the event arrived on a pool thread and three view models then
        /// mutated <c>ObservableCollection</c>s that Avalonia was bound to. That corrupts the bound list rather
        /// than throwing anywhere useful: the orchestrator's catch swallowed the exception, so the symptom was
        /// a silently stale Providers list and model picker. Hopping centrally fixes every existing subscriber
        /// and every future one, instead of relying on each to remember.</para>
        /// </summary>
        private void RaiseChanged()
        {
            var handler = ConnectionsChanged;
            if (handler is null) return;

            if (Dispatcher.UIThread.CheckAccess())
                handler(this, EventArgs.Empty);
            else
                Dispatcher.UIThread.Post(() => handler(this, EventArgs.Empty));
        }

        public AiConnectionResult MarkListing(string connectionId, IReadOnlyList<string> listedIds)
        {
            if (Find(connectionId) is not { } record)
                return AiConnectionResult.Fail($"No connection called '{connectionId}'.");

            // An empty listing is not a report that everything is gone. Endpoints answer 200 with an empty
            // data[] for reasons that have nothing to do with the reader's models - a key without listing
            // scope, a gateway with no upstream configured - and marking every stored model on that basis
            // would be the loudest possible way to say nothing. (#728)
            if (listedIds.Count == 0) return AiConnectionResult.Success(ToRuntime(record));

            var listed = new HashSet<string>(listedIds, StringComparer.Ordinal);
            var changed = false;

            foreach (var model in record.Models)
            {
                var missing = !listed.Contains(model.Id);
                if (missing == model.Missing) continue;
                model.Missing = missing;
                changed = true;
            }

            // Saving unconditionally would write settings.json and raise ConnectionsChanged every time the
            // Models tab is opened, rebuilding a list the reader is looking at to say nothing new.
            return changed ? Saved(record) : AiConnectionResult.Success(ToRuntime(record));
        }

        public AiConnectionResult EnableModel(
            string connectionId, string modelId, string displayName, bool enabled,
            AiModelEntry? facts = null)
        {
            if (Find(connectionId) is not { } record)
                return AiConnectionResult.Fail($"No connection called '{connectionId}'.");

            if (string.IsNullOrWhiteSpace(modelId))
                return AiConnectionResult.Fail("A model needs an id.");

            // Trim ONCE, before the lookup. Looking up the untrimmed id and storing the trimmed one meant a
            // provider listing whose id carries whitespace never matched what the last toggle had stored, so
            // every toggle appended another record under the same trimmed id — a duplicate the models tab
            // then threw on. (#870)
            modelId = modelId.Trim();

            var model = record.Models.FirstOrDefault(
                m => string.Equals(m.Id, modelId, StringComparison.Ordinal));

            if (model is null)
            {
                model = new AiModelRecord
                {
                    Id = modelId,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? modelId : displayName.Trim(),
                };
                record.Models.Add(model);
            }

            // Written on every call, not only on first add: a reader who turns a model off and on again after
            // a catalogue refresh should end up with what the provider says now, not what it said in a
            // session they have forgotten.
            if (facts is not null)
            {
                model.ContextLength = facts.ContextLength;
                model.SupportsReasoning = facts.SupportsReasoning;
                model.Inputs = facts.Inputs;
                model.ReasoningEfforts = facts.ReasoningEfforts?.ToList();
                model.DefaultReasoningEffort = facts.DefaultReasoningEffort;

                // Facts exist because the listing carried this model, so it is by definition not missing from
                // it. Clearing here as well as in MarkListing keeps the two from disagreeing. (#728)
                model.Missing = false;
            }

            model.Enabled = enabled;
            FollowTheChoice(record, model, enabled);
            return Saved(record);
        }

        public AiConnectionResult SetModelEnabled(string connectionId, string modelId, bool enabled)
        {
            if (Find(connectionId) is not { } record)
                return AiConnectionResult.Fail($"No connection called '{connectionId}'.");

            var model = record.Models.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.Ordinal));
            if (model is null) return AiConnectionResult.Fail($"'{modelId}' is not a model on {record.DisplayName}.");

            model.Enabled = enabled;
            FollowTheChoice(record, model, enabled);
            return Saved(record);
        }

        /// <summary>
        /// Keeps the active model in step with what the reader just switched on or off.
        ///
        /// <para><b>Turning a model on when nothing is active makes it active.</b> Enabling is the reader
        /// saying "this is one I want to use", and with a single enabled model there is nothing else it could
        /// mean. Without this the assistant answered "No model is configured" to someone looking at the model
        /// they had just switched on — and with only one enabled, the per-turn picker had nothing to choose
        /// between and so offered no way out at all.</para>
        ///
        /// <para><b>Turning the active model off moves the pointer.</b> To another enabled model on the same
        /// connection where there is one, and otherwise to nothing — leaving it pointing at a model the
        /// reader has just hidden would send requests to something the picker no longer lists.</para>
        /// </summary>
        private void FollowTheChoice(AiConnectionRecord record, AiModelRecord model, bool enabled)
        {
            if (enabled)
            {
                if (!string.IsNullOrEmpty(Chat.ActiveModelId)) return;
                Chat.ActiveConnectionId = record.Id;
                Chat.ActiveModelId = model.Id;
                return;
            }

            if (!IdMatches(Chat.ActiveConnectionId, record.Id)) return;
            if (!string.Equals(Chat.ActiveModelId, model.Id, StringComparison.Ordinal)) return;

            Chat.ActiveModelId = record.Models
                .FirstOrDefault(m => m.Enabled && !string.Equals(m.Id, model.Id, StringComparison.Ordinal))?.Id;
        }

        // ---- internals -------------------------------------------------------------------------------

        private AiConnectionRecord? Find(string? id) =>
            id is null ? null : Chat.Connections.FirstOrDefault(c => IdMatches(c.Id, id));

        private static bool IdMatches(string? a, string? b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private AiConnectionResult Saved(AiConnectionRecord record)
        {
            _settings.RequestSave();
            RaiseChanged();
            return AiConnectionResult.Success(ToRuntime(record));
        }

        private static void Apply(AiConnectionRecord record, AiConnectionDraft draft)
        {
            record.DisplayName = draft.DisplayName;
            record.Kind = draft.Kind == ChatProviderKind.Anthropic ? "anthropic" : "openai-compatible";
            record.BaseUrl = draft.BaseUrl;
            // Every field, not just the ones the editor shows. Rebuilding from the visible fields alone is
            // how an edit silently drops what the provider published - the same shape of bug as the auth
            // headers reset in the #689 review.
            record.Models = draft.Models
                .Select(m => new AiModelRecord
                {
                    Id = m.Id,
                    DisplayName = m.DisplayName,
                    Enabled = m.Enabled,
                    ContextLength = m.ContextLength,
                    SupportsReasoning = m.SupportsReasoning,
                    Inputs = m.Inputs,
                    Missing = m.Missing,
                    ReasoningEfforts = m.ReasoningEfforts?.ToList(),
                    DefaultReasoningEffort = m.DefaultReasoningEffort,
                })
                .ToList();
            // A secret header's value is NOT carried through the draft - the editor puts it in the credential
            // store directly, exactly as it does the API key - so what is persisted is the name and the mark.
            // Writing draft.Value here for a secret row is the one line that would put a credential in
            // settings.json, which is why it cannot be reached: the draft's Value is null when Secret (#771).
            record.Headers = draft.Headers
                .Select(h => new AiHeaderRecord
                {
                    Name = h.Name,
                    Value = h.Secret ? null : h.Value,
                    Secret = h.Secret,
                })
                .ToList();
            record.Inputs = new Dictionary<string, string>(draft.Inputs);
            record.AuthHeaderName = draft.AuthHeaderName;
            record.AuthScheme = draft.AuthScheme;
        }

        private AiConnection ToRuntime(AiConnectionRecord r) => new(
            r.Id,
            r.DisplayName,
            ChatProviderResolver.TryParseKind(r.Kind, out var kind) ? kind : ChatProviderKind.OpenAiCompatible,
            r.BaseUrl,
            r.Models
                .Select(m => new AiModelEntry(
                    m.Id, m.DisplayName, m.Enabled, m.ContextLength, m.SupportsReasoning, m.Inputs,
                    m.Missing, m.ReasoningEfforts, m.DefaultReasoningEffort))
                .ToList(),
            r.Headers.Select(h => new AiHeader(h.Name, h.Secret ? null : h.Value, h.Secret)).ToList(),
            new Dictionary<string, string>(r.Inputs),
            r.PresetId,
            r.SecretInputs?.ToList(),
            SourceFor(r.Id),
            ReachabilityOf(r.Id),
            r.AuthHeaderName,
            r.AuthScheme,
            r.UsesEnvironmentKey,
            r.EnvironmentVariable);

        /// <summary>
        /// Every credential name this connection could have filed a secret under. (#759)
        ///
        /// <para>Derived rather than recorded, so it cannot drift out of step with what was actually stored:
        /// a list in the settings file would be one more thing to keep true, and the failure mode of it being
        /// stale is an orphaned credential nobody can see.</para>
        /// </summary>
        /// <summary>
        /// A preset whose base URL substitutes a secret prompt, or null when none does. (#777)
        ///
        /// <para><b>Refused rather than discouraged.</b> A base URL is not a private place: it is written to
        /// the provider's own access logs and to any proxy between, it is shown in the Providers list and in
        /// the editor, and it is named in most of the error sentences this code is careful to produce — the
        /// #678 lesson about naming the endpoint works directly against keeping anything in it quiet. A
        /// header template is the legitimate destination for a secret, which is where Cloudflare's gateway
        /// token goes.</para>
        ///
        /// <para>Checked at the point of save rather than only where presets are authored, because the preset
        /// list is generated from a fetched catalogue (#733/#737) and this is the seam every connection goes
        /// through. Nothing in the catalogue can set <c>Secret</c> today — <c>ModelsDevCatalog</c> constructs
        /// no prompts at all — so this guards a foot-gun we would hand ourselves, which is exactly the kind
        /// worth making unreachable rather than remembering.</para>
        /// </summary>
        private static string? SecretInUrl(AiProviderPreset preset)
        {
            var secrets = (preset.Prompts ?? Array.Empty<AiInputPrompt>())
                .Where(p => p.Secret)
                .Select(p => p.Key)
                .ToHashSet(StringComparer.Ordinal);

            if (secrets.Count == 0) return null;

            foreach (var key in AiTemplate.PlaceholdersIn(preset.BaseUrl))
                if (secrets.Contains(key))
                    return $"{preset.DisplayName} cannot be added: its address would contain the "
                        + $"{PromptLabel(preset, key)}, and a secret must not go in a URL.";

            return null;
        }

        private static IEnumerable<string> CredentialNamesOf(AiConnectionRecord record)
        {
            yield return AiCredentialNames.Primary;

            foreach (var header in record.Headers.Where(h => h.Secret))
                yield return AiCredentialNames.Header(header.Name);

            // A secret prompt answer is filed here too, and an orphan is invisible precisely because nothing
            // reads it - the reason this method exists at all. (#777)
            foreach (var key in record.SecretInputs ?? Enumerable.Empty<string>())
                yield return AiCredentialNames.Input(key);
        }

        /// <summary>
        /// Ids are the reserved namespace a custom connection may not take, and they become the credential's
        /// account name — so a collision would mean one connection quietly inheriting another's key.
        /// </summary>
        private string? ValidateId(string id, bool existingAllowed)
        {
            if (string.IsNullOrWhiteSpace(id)) return "A connection needs an id.";
            if (!SlugPattern.IsMatch(id))
                return "An id may use only lowercase letters, numbers, hyphens and underscores.";
            if (Presets.Any(p => IdMatches(p.Id, id)))
                return $"'{id}' is the id of a built-in provider. Add it from the provider list instead.";
            if (!existingAllowed && Find(id) is not null)
                return $"There is already a connection called '{id}'.";
            return null;
        }

        private static string PromptLabel(AiProviderPreset preset, string key) =>
            preset.Prompts?.FirstOrDefault(p => p.Key == key)?.Message ?? key;
    }
}
