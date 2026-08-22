using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services.Ai;
using ReactiveUI;

namespace CST.Avalonia.ViewModels
{
    /// <summary>The wire protocol an endpoint speaks, as a dropdown entry. (#691)</summary>
    /// <remarks>
    /// Two entries, and neither is a brand. The OpenAI-compatible shape reaches OpenRouter, DeepSeek,
    /// Together, Ollama and LM Studio alike, and what selects between them is the base URL. Deliberately kept
    /// <b>independent of the base URL</b> in both directions: several providers serve the Anthropic Messages
    /// protocol at their own hosts, so neither field may be inferred from the other.
    /// </remarks>
    public sealed record AiKindChoice(ChatProviderKind Kind, string Display);

    /// <summary>
    /// The connection sheet — adding a named provider, adding a custom endpoint, or editing one. (#691)
    ///
    /// <para><b>Every add opens this sheet, including a preset that asks nothing.</b> The first cut added
    /// key-less presets outright, on the reasoning that there was nothing to ask. Tested, that is wrong twice
    /// over: the new row lands at the top of a scrolled page where the reader cannot see it, so the click
    /// reads as having done nothing at all — and adding OpenRouter without its key or a model id produces a
    /// connection that cannot answer a question, which the reader then has to discover and go back to Edit.
    /// OpenCode opens a sheet on every Connect, and it is right to.</para>
    ///
    /// <para>One view model for all three cases because they are the same form with different fields showing.
    /// A preset already knows its address and protocol, so those are shown rather than asked.</para>
    /// </summary>
    public class AiConnectionEditorViewModel : ViewModelBase
    {
        private static readonly AiKindChoice[] KindChoices =
        {
            new(ChatProviderKind.OpenAiCompatible, "OpenAI-compatible endpoint"),
            new(ChatProviderKind.Anthropic, "Claude (Anthropic)"),
        };

        private readonly IAiConnectionService _service;
        private readonly IAiCredentialStore? _credentials;
        private readonly Action<bool> _close;
        private readonly AiProviderPreset? _preset;

        /// <summary>The environment variable this connection was opened to adopt, or null. Name only — the
        /// value is never read here, never rendered and never stored. (#714)</summary>
        private string? _adoptEnvironmentVariable;
        private readonly string? _existingId;
        private readonly bool _keyRequired;

        /// <summary>The credential names this connection's secret headers occupied when the sheet opened, so a
        /// header that is renamed, unmarked or removed takes its stored secret with it rather than leaving an
        /// orphan nothing can reach. (#771)</summary>
        private readonly List<string> _secretHeadersAtOpen = new();

        private string _id = "";
        private string _displayName = "";
        private AiKindChoice _kind = KindChoices[0];
        private string _baseUrl = "";
        private string _apiKeyEntry = "";
        private string? _problem;
        private bool _idEdited;

        private AiConnectionEditorViewModel(
            IAiConnectionService service, IAiCredentialStore? credentials, Action<bool> close,
            AiProviderPreset? preset, string? existingId)
        {
            _service = service;
            _credentials = credentials;
            _close = close;
            _preset = preset;
            _existingId = existingId;
            _keyRequired = preset?.RequiresKey ?? false;

            SaveCommand = ReactiveCommand.Create(Save);
            CancelCommand = ReactiveCommand.Create(() => _close(false));
            AddModelCommand = ReactiveCommand.Create(() => Models.Add(new AiModelRowViewModel(Models)));
            AddHeaderCommand = ReactiveCommand.Create(
                () => Headers.Add(new AiHeaderRowViewModel(Headers, CanStoreKeys)));
            RemoveKeyCommand = ReactiveCommand.Create(RemoveKey);
        }

        /// <summary>
        /// The preset an already-configured connection was added from, or null for a custom endpoint.
        ///
        /// <para>Matching on the id is exact rather than a guess: a custom connection is refused a preset's id
        /// outright (<i>"'deepseek' is the id of a built-in provider"</i>), so an id that matches one came from
        /// it. Not stored as <see cref="_preset"/> — that field decides which <i>fields</i> the sheet shows,
        /// and an edit must keep showing all of them.</para>
        /// </summary>
        private static AiProviderPreset? OriginPreset(IAiConnectionService service, AiConnection? connection)
        {
            if (connection is null) return null;

            // Recorded at creation since #766, and an EMPTY string is a recorded answer: this connection is
            // a custom endpoint. Only null means nothing was recorded.
            if (connection.PresetId is { } recorded)
                return recorded.Length == 0
                    ? null
                    : service.Presets.FirstOrDefault(
                        p => string.Equals(p.Id, recorded, StringComparison.OrdinalIgnoreCase));

            // Null on a settings file written before that, where matching the id is still right: a custom
            // connection was refused any preset's id, and the only presets that could have existed are the
            // ones that did. What it is NOT right for is a custom endpoint whose slug the catalogue grew into
            // afterwards - which is the bug, and which cannot happen to a connection created from here on.
            return service.Presets.FirstOrDefault(
                p => string.Equals(p.Id, connection.Id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// What the row's edit button should say, or null where it should not be there at all. (#691)
        ///
        /// <para>One function rather than a visibility flag beside a label, so the two cannot disagree — the
        /// button that appears is the one whose word was chosen.</para>
        ///
        /// <para><b>"Replace key"</b> for the ~150 providers that are a base URL and a bearer token: that is
        /// the entire sheet, and it is the thing readers actually do — a key rotates, expires, or hits a daily
        /// cap. Naming it stops the button implying that a named provider's settings are the reader's to
        /// change. <b>"Edit"</b> where the sheet holds more: a custom endpoint, whose every field is the
        /// reader's, and a provider that asks something besides a key (Azure's resource name, Cloudflare's
        /// account id). <b>Nothing</b> for a local runner from the provider list, which needs no key and asks
        /// nothing, so the sheet would open with a title, a sentence and two buttons.</para>
        ///
        /// <para>Delete-and-re-add is <i>not</i> the substitute OpenCode can make it: deleting takes the
        /// reader's enabled models with it, and a re-fetched catalogue comes back all-off by #674's rule, so
        /// rotating a key would cost them their short list.</para>
        ///
        /// <para>Deliberately not symmetric with Add, which opens a sheet even for a provider that asks
        /// nothing: there the sheet is how the reader confirms an add they can see happen.</para>
        /// </summary>
        public static string? EditAction(IAiConnectionService service, AiConnection connection)
        {
            var preset = OriginPreset(service, connection);
            if (preset is null || preset.Prompts?.Count > 0) return "Edit";
            return preset.RequiresKey ? "Replace key" : null;
        }

        /// <summary>An endpoint in nobody's catalogue. The generic mechanism the named ones are a shortcut
        /// for, and always available — a preset must never be required to reach a provider.</summary>
        public static AiConnectionEditorViewModel ForCustom(
            IAiConnectionService service, IAiCredentialStore? credentials, Action<bool> close)
        {
            var vm = new AiConnectionEditorViewModel(service, credentials, close, null, null);
            vm.Models.Add(new AiModelRowViewModel(vm.Models));
            return vm;
        }

        /// <summary>
        /// A named provider: its address and protocol are known, so the sheet asks only for what is left —
        /// whatever the preset declares it needs, a key if it takes one, and the models to start with.
        ///
        /// <para>The extra fields come from the preset's own <see cref="AiProviderPreset.Prompts"/>, so this
        /// is one form driven by data rather than a hand-written dialog per provider. A provider arriving in a
        /// later upstream sync needing a field we have never heard of gets a working dialog for free.</para>
        /// </summary>
        /// <param name="adoptEnvironmentKey">
        /// Opened from the "Found in your environment" section, so this connection will authenticate with the
        /// variable the reader's machine already holds. (#714)
        ///
        /// <para>Azure and Cloudflare are why this path exists at all: both declare environment variables AND
        /// need a prompt answered, so neither can be adopted in one click. The sheet asks for what is missing
        /// and carries the reader's choice through to the save — it does not re-ask it, because they made it
        /// by pressing a button that named the variable.</para>
        /// </param>
        public static AiConnectionEditorViewModel ForPreset(
            IAiConnectionService service, IAiCredentialStore? credentials, AiProviderPreset preset,
            Action<bool> close, string? adoptEnvironmentKey = null)
        {
            var vm = new AiConnectionEditorViewModel(service, credentials, close, preset, null)
            {
                _adoptEnvironmentVariable = adoptEnvironmentKey,
                _id = preset.Id,
                _displayName = preset.DisplayName,
                _baseUrl = preset.BaseUrl,
                _kind = KindChoices.First(k => k.Kind == preset.Kind),
            };

            foreach (var prompt in preset.Prompts ?? Array.Empty<AiInputPrompt>())
                vm.Inputs.Add(new AiInputRowViewModel(prompt, vm.OnInputChanged));

            vm.RefreshInputVisibility();
            return vm;
        }

        /// <summary>
        /// Editing what is already configured.
        ///
        /// <para><b>A connection added from a provider list is edited on the same form it was added on</b> —
        /// the key, and whatever that provider asks for besides. Anything else about it belongs to the
        /// preset: its address, its protocol and the auth shape those imply are not the reader's to override,
        /// and offering them made a one-field form look like an infrastructure panel. It was also load-bearing
        /// for a bug — a rename through that form silently reset Azure's auth shape to Bearer, and nothing on
        /// screen showed the shape. Where a provider genuinely stops behaving as its preset says, the answer
        /// is to add it as a custom endpoint, which is the same mechanism with the address left to the
        /// reader.</para>
        ///
        /// <para>Only the id is never editable, on either form: it is the account the credential is filed
        /// under, so changing it would orphan the key and the failure would read as a rejected key rather
        /// than a lost one.</para>
        /// </summary>
        public static AiConnectionEditorViewModel ForExisting(
            IAiConnectionService service, IAiCredentialStore? credentials, AiConnection connection,
            Action<bool> close)
        {
            var preset = OriginPreset(service, connection);
            var vm = new AiConnectionEditorViewModel(service, credentials, close, preset, connection.Id)
            {
                _id = connection.Id,
                _displayName = connection.DisplayName,
                _baseUrl = connection.BaseUrl,
                _kind = KindChoices.FirstOrDefault(k => k.Kind == connection.Kind) ?? KindChoices[0],
                _idEdited = true,

                // The auth shape is not editable in this form, but it MUST survive an edit. Azure sends its
                // credential in `api-key` with no scheme and expects `Authorization` absent; letting these
                // fall back to the draft defaults turned a rename into a 401 on every subsequent request.
                // (fable review)
                _authHeaderName = connection.AuthHeaderName,
                _authScheme = connection.AuthScheme,

                // The adoption has to survive an edit too, and for the same reason the auth shape does.
                // Without it the sheet forgets this connection authenticates from the environment, decides a
                // required key is missing, and REFUSES TO SAVE — telling a reader whose credential works
                // perfectly that the provider needs an API key. Their only ways out were pasting the
                // environment's key into the keychain, which is the copy the adoption promises never to make,
                // or deleting and re-adding, which destroys the model list this edit path exists to preserve.
                // (#714, fable review)
                _adoptEnvironmentVariable = connection.UsesEnvironmentKey
                    ? connection.EnvironmentVariable
                    : null,
            };

            foreach (var model in connection.Models)
                vm.Models.Add(new AiModelRowViewModel(vm.Models)
                {
                    ModelId = model.Id,
                    DisplayName = model.DisplayName,
                    Enabled = model.Enabled,
                    Published = model,
                });

            foreach (var header in connection.Headers)
                vm.Headers.Add(new AiHeaderRowViewModel(vm.Headers, vm.CanStoreKeys)
                {
                    Name = header.Name,
                    // Null for a secret one, by construction - the runtime record does not carry it. What the
                    // reader sees is an empty box and a "stored" note, the same shape as the API key.
                    Value = header.Value ?? "",
                    IsSecret = header.Secret,
                    ArrivedSecret = header.Secret,
                    OriginalName = header.Name,
                    HasStoredSecret = header.Secret
                        && credentials?.Get(connection.Id, AiCredentialNames.Header(header.Name)) is not null,
                });

            vm._secretHeadersAtOpen.AddRange(connection.Headers
                .Where(h => h.Secret)
                .Select(h => AiCredentialNames.Header(h.Name)));

            foreach (var input in connection.Inputs)
                vm.Inputs.Add(new AiInputRowViewModel(
                    // The preset's own wording where there is one, so an edit asks "Resource name" exactly as
                    // the add did rather than falling back to the raw key.
                    preset?.Prompts?.FirstOrDefault(p => p.Key == input.Key)
                        ?? new AiInputPrompt(input.Key, input.Key),
                    vm.OnInputChanged) { Value = input.Value });

            // A secret answer is not in Inputs — that is the point of it — so without this it would have no
            // row at all, and a reader whose gateway token was rotated would have no way to enter the new one
            // short of deleting the connection. The row opens EMPTY and says so, exactly as a secret header's
            // does: a stored credential is never read back into a screen. (#777)
            foreach (var key in connection.SecretInputs ?? Array.Empty<string>())
                vm.Inputs.Add(new AiInputRowViewModel(
                    preset?.Prompts?.FirstOrDefault(p => p.Key == key)
                        ?? new AiInputPrompt(key, key, Secret: true),
                    vm.OnInputChanged,
                    hasStoredSecret: true));

            if (vm.Models.Count == 0) vm.Models.Add(new AiModelRowViewModel(vm.Models));
            vm.RefreshInputVisibility();
            return vm;
        }

        // ---- what the sheet shows ----------------------------------------------------------------------

        public bool IsPreset => _preset is not null;

        /// <summary>Id, display name, protocol and address are asked for only where they are not already
        /// known. A preset knows all four.</summary>
        public bool IsFullForm => _preset is null;

        public bool IsIdEditable => _existingId is null && _preset is null;

        /// <summary>Says at the top of the sheet what the button that opened it said.</summary>
        public string Title => _existingId is null
            ? _preset is not null ? $"Add {_preset.DisplayName}" : "Add a custom endpoint"
            : _preset is not null && !(_preset.Prompts?.Count > 0)
                ? $"Replace the {_preset.DisplayName} API key"
                : $"Edit {_displayName}";

        /// <summary>
        /// One sentence saying what this sheet is for, worded after OpenCode's — which says in a line what
        /// our old screen took three stacked paragraphs to not quite say.
        /// </summary>
        public string Blurb
        {
            get
            {
                if (_preset is null)
                    return _existingId is not null
                        ? "Everything except the id can be changed."
                        : "Any endpoint that speaks one of the two protocols below. This is the same mechanism the named providers use, with the address left to you.";

                if (_existingId is not null)
                    return $"{_preset.DisplayName}'s address and protocol come from the provider list, and its "
                        + "models are on the Models tab. What is left is what it asks you for.";

                return _preset.RequiresKey
                    ? $"Enter your {_preset.DisplayName} API key to use {_preset.DisplayName} models in CST Reader. Its model list is fetched afterwards, on the Models tab."
                    : $"{_preset.DisplayName} runs on your own machine and needs no key. Its model list is fetched afterwards, on the Models tab.";
            }
        }

        public AiKindChoice[] Kinds => KindChoices;

        // ---- fields ------------------------------------------------------------------------------------

        /// <summary>Lowercase slug, immutable once created, and the account the credential is filed under.
        /// Defaulted from the host so a reader who does not care never has to invent one.</summary>
        public string Id
        {
            get => _id;
            set
            {
                _idEdited = true;
                this.RaiseAndSetIfChanged(ref _id, value);
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                this.RaiseAndSetIfChanged(ref _displayName, value);
                this.RaisePropertyChanged(nameof(Title));
            }
        }

        public AiKindChoice Kind
        {
            get => _kind;
            set => this.RaiseAndSetIfChanged(ref _kind, value);
        }

        public string BaseUrl
        {
            get => _baseUrl;
            set
            {
                this.RaiseAndSetIfChanged(ref _baseUrl, value);
                SuggestIdFromHost();
            }
        }

        /// <summary>
        /// The key, held only long enough to hand over.
        ///
        /// <para>Asked for <b>here</b> rather than on a shared field elsewhere in Settings, because a single
        /// key box has no way to say which connection it belongs to — the reader pastes an OpenRouter key
        /// while some other endpoint happens to be the active one, and it is filed under that. That is #678's
        /// collision with a new cause, and a sheet that already knows whose key it is asking for cannot make
        /// the mistake.</para>
        /// </summary>
        public string ApiKeyEntry
        {
            get => _apiKeyEntry;
            set => this.RaiseAndSetIfChanged(ref _apiKeyEntry, value);
        }

        /// <summary>Whether to ask for a key at all. A local runner needs none, and saying so is better than
        /// an empty box the reader wonders about.</summary>
        public bool ShowKeyField => _preset is null || _preset.RequiresKey;

        public bool CanStoreKeys => _credentials?.IsAvailable == true;

        /// <summary>Why a key cannot be stored on this machine, in the platform's own words. Null when it
        /// can.</summary>
        public string? KeyUnavailable => CanStoreKeys ? null : _credentials?.Unavailable;

        public bool HasKeyUnavailable => !string.IsNullOrEmpty(KeyUnavailable);

        /// <summary>Whether a key is already filed under this id — only knowable for a connection that
        /// exists.</summary>
        public bool HasStoredKey =>
            _existingId is not null && _credentials?.Get(_existingId, AiCredentialNames.Primary) is not null;

        public string KeyStatus => _existingId is null
            ? "Stored in the operating system's credential store, never in settings."
            : HasStoredKey
                ? $"A key is stored for {_displayName}. Paste a new one to replace it."
                // "No key is stored" is true and reads as "you have no key", which on an adopted connection is
                // false — it authenticates from the environment, and saying otherwise sends the reader to
                // paste one they do not need. (#714, fable review)
                : _adoptEnvironmentVariable is { } variable
                    ? $"{_displayName} uses the key in {variable}. Paste one here only to use a different key."
                    : $"No key is stored for {_displayName}.";

        /// <summary>
        /// Whether the "optional" line under the key box applies.
        ///
        /// <para><b>Not where the provider requires a key.</b> Telling that reader the box is optional
        /// contradicts the blurb three lines above it and invites them to save a connection that cannot
        /// answer. The header clause is equally wrong there: headers are asked for on a custom endpoint
        /// alone.</para>
        ///
        /// <para><b>Adding is not the only way in.</b> Gating on "is this a preset sheet" leaves the line on
        /// the sheet a reader is most likely to be reading it on — Edit, reached precisely <i>because</i> the
        /// key is missing or wrong. That sheet carries no preset, so the requirement is recovered from the
        /// connection's id instead (<see cref="OriginPreset"/>).</para>
        /// </summary>
        public bool HasKeyHint => !_keyRequired;

        /// <summary>
        /// A provider that requires a key, with none supplied here and none already stored. (#761)
        ///
        /// <para>Refused rather than saved, because what it creates otherwise looks exactly like a working
        /// connection — the provider's own logo and name on the Providers tab, its models listed on the next
        /// tab — and only announces itself as a 401 later, at the moment the reader was trying to read
        /// something. The Models tab does catch it ("no API key stored"), but that is one screen past where
        /// the reader thought they had finished.</para>
        ///
        /// <para><b>Not where no key can be stored at all.</b> The sheet already says so in caution colour,
        /// and no key can be filed by any route on that machine, so a refusal on top of the explanation would
        /// leave the reader nowhere to go — a row that cannot answer is the lesser evil there.</para>
        /// </summary>
        /// <para><b>Nor where the reader is adopting the key their environment already holds.</b> They have a
        /// key; it simply is not one we store. Demanding a typed one here would refuse the very thing the
        /// button they pressed offered to do. (#714)</para>
        private bool MissingRequiredKey =>
            _keyRequired && CanStoreKeys && _adoptEnvironmentVariable is null
            && string.IsNullOrWhiteSpace(ApiKeyEntry) && !HasStoredKey;

        /// <summary>
        /// The variable this sheet was opened to adopt, named for the reader. (#714)
        ///
        /// <para>The NAME, never the value. A settings screen that prints a credential is the last thing this
        /// app should grow, and the name is the whole of what the reader needs in order to recognise — or
        /// disown — the key that is about to be used.</para>
        /// </summary>
        public string? EnvironmentKeyNote => _adoptEnvironmentVariable is null
            ? null
            : $"This connection uses the key in {_adoptEnvironmentVariable}. It stays in your environment — "
              + "nothing is copied here, and you do not need to paste a key below.";

        public bool HasEnvironmentKeyNote => EnvironmentKeyNote is not null;

        /// <summary>
        /// A header marked secret with nothing to store and nothing already stored. (#771)
        ///
        /// <para>Same reasoning as <see cref="MissingRequiredKey"/>: saved anyway it produces a connection that
        /// looks configured and 401s on every request, and the reader finds out later, from the provider,
        /// while trying to read something. Refusing needs no <see cref="CanStoreKeys"/> exemption because the
        /// row cannot be marked secret at all where there is nowhere to put one.</para>
        /// </summary>
        private AiHeaderRowViewModel? UnstoredSecretHeader => Headers.FirstOrDefault(
            h => h.KeepsSecret
                 && !string.IsNullOrWhiteSpace(h.Name)
                 && string.IsNullOrWhiteSpace(h.Value)
                 && !h.HasStoredSecret);

        /// <summary>
        /// A row that arrived with a stored secret, has been unmarked, and has nothing typed in its box.
        /// (#771, fable review)
        ///
        /// <para>Unmarking reads as declassifying — moving the value from the keychain into the settings file
        /// — and for a row the reader just typed, it is. For a row with a <i>stored</i> secret it cannot be:
        /// the value was never read back into the box, so there is nothing to move, and the save would delete
        /// the credential and persist an empty header that both request paths then send blank. That is the
        /// anonymous 401 this whole feature exists to remove, arriving by the door marked exit.</para>
        /// </summary>
        private AiHeaderRowViewModel? EmptiedSecretHeader => Headers.FirstOrDefault(
            h => !h.IsSecret && h.ArrivedSecret
                 && !string.IsNullOrWhiteSpace(h.Name)
                 && string.IsNullOrWhiteSpace(h.Value));

        public string KeyHint => "Optional — leave it empty if this endpoint needs no key, or if you authenticate with a header below.";

        /// <summary>
        /// Whether to ask for model ids at all.
        ///
        /// <para><b>A named provider is not asked.</b> Its listing is fetched once the connection exists, so a
        /// model box here would make the reader decide whether they need to type an id <i>before</i> seeing
        /// what asking the provider returns — which is the wrong moment, and for OpenRouter's four hundred it
        /// is a box nobody should ever fill in. The escape hatch for an id a listing omits lives on the Models
        /// tab, next to the listing that omitted it.</para>
        ///
        /// <para>A custom endpoint still asks, and must: it may publish no listing at all, which is the
        /// ordinary case for a local runner, and then typing is the only way in.</para>
        /// </summary>
        public bool ShowModels => _preset is null;

        public string ModelsHint =>
            "This endpoint's own list is fetched on the Models tab if it publishes one. Type ids here for an "
            + "endpoint that does not.";

        /// <summary>The service's refusal, verbatim — a duplicate id, a reserved one, a missing input. Shown
        /// rather than swallowed: a collision would mean one connection inheriting another's credential.</summary>
        public string? Problem
        {
            get => _problem;
            private set
            {
                this.RaiseAndSetIfChanged(ref _problem, value);
                this.RaisePropertyChanged(nameof(HasProblem));
            }
        }

        public bool HasProblem => !string.IsNullOrEmpty(Problem);

        /// <summary>The models this endpoint offers, typed by hand. Short by construction, works offline, and
        /// needs no catalogue — a fetched listing is the upgrade for providers that publish one (#674), never
        /// the mechanism this depends on.</summary>
        public ObservableCollection<AiModelRowViewModel> Models { get; } = new();

        /// <summary>The escape hatch that makes an absent API key coherent: Azure's <c>api-key</c>, gateway
        /// tokens, anything that is not a bearer credential. A preset already carries whatever it needs.</summary>
        public ObservableCollection<AiHeaderRowViewModel> Headers { get; } = new();

        public bool ShowHeaders => _preset is null;

        /// <summary>The preset's own questions, generated from its declared prompts.</summary>
        public ObservableCollection<AiInputRowViewModel> Inputs { get; } = new();

        public string SaveText => _existingId is null ? "Add" : "Save";

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Unit> AddModelCommand { get; }
        public ReactiveCommand<Unit, Unit> AddHeaderCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveKeyCommand { get; }

        // ---- saving ------------------------------------------------------------------------------------

        private void Save()
        {
            if (MissingRequiredKey)
            {
                Problem = $"{_displayName} needs an API key. Paste one to continue.";
                return;
            }

            if (UnstoredSecretHeader is { } unstored)
            {
                // Not "clear the mark to keep it in the settings file": the box is empty, so there is nothing
                // to keep, and for a renamed row clearing the mark would abandon the stored value too.
                Problem = $"The {unstored.Name.Trim()} header is marked secret but has no value. "
                          + "Paste one, or remove the row.";
                return;
            }

            if (EmptiedSecretHeader is { } emptied)
            {
                Problem = $"The {emptied.Name.Trim()} header's value is stored in the keychain and cannot be "
                          + "read back. Type it in to move it into the settings file, or mark it secret again.";
                return;
            }

            var typed = Inputs
                .Where(i => i.IsVisible && !string.IsNullOrWhiteSpace(i.Value))
                .ToDictionary(i => i.Key, i => i.Value.Trim(), StringComparer.Ordinal);

            // The draft's dictionary becomes `record.Inputs` verbatim, and `record.Inputs` is written to
            // settings.json - so a secret answer must not be in it. `typed` keeps them for AddFromPreset,
            // which is the one caller allowed to see them, and files them in the credential store itself.
            // (#777)
            var inputs = typed
                .Where(pair => !Inputs.Any(i =>
                    i.IsSecret && string.Equals(i.Key, pair.Key, StringComparison.Ordinal)))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            // Order matters: an edit updates even when it carries a preset, or saving one would try to add a
            // second connection under an id that is already taken.
            var result = _existingId is not null
                ? _service.Update(_existingId, BuildDraft(inputs))
                : _preset is not null
                    ? AddPreset(typed)
                    : _service.Add(Id.Trim(), BuildDraft(inputs));

            if (!result.Ok)
            {
                Problem = result.Problem;
                return;
            }

            var savedId = result.Connection?.Id ?? _existingId ?? Id.Trim();
            StoreKey(savedId);
            StoreHeaderSecrets(savedId);
            StoreInputSecrets(savedId, typed);
            _close(true);
        }

        /// <summary>
        /// Creates the connection from the preset, then applies the models typed on the sheet.
        ///
        /// <para>Two calls because <c>AddFromPreset</c> takes only the inputs — the update is built from the
        /// connection the service just created rather than from this form, so the address, protocol and any
        /// headers the preset set up are carried across exactly as the service wrote them and only the model
        /// list comes from the reader.</para>
        /// </summary>
        private AiConnectionResult AddPreset(IReadOnlyDictionary<string, string> inputs)
        {
            // The name the reader was shown on the row they pressed, carried through the sheet unchanged.
            var added = _service.AddFromPreset(
                _preset!.Id, inputs, environmentVariable: _adoptEnvironmentVariable);
            if (!added.Ok || added.Connection is not { } created) return added;

            var models = TypedModels();
            if (models.Count == 0) return added;

            var updated = _service.Update(created.Id, new AiConnectionDraft(
                created.DisplayName, created.Kind, created.BaseUrl, models, created.Headers, created.Inputs,
                // Carried explicitly: a preset's auth shape (Azure's `api-key`, no scheme) would otherwise be
                // reset to Bearer by this very first update. (fable review)
                created.AuthHeaderName, created.AuthScheme));

            return updated.Ok ? updated : added;
        }

        /// <summary>Hands the key over once the connection it belongs to exists, so it is filed under an id
        /// that is already real.</summary>
        /// <summary>
        /// Files a secret prompt answer the reader typed on this sheet. (#777)
        ///
        /// <para>Runs on the EDIT path. The add path does not need it — <c>AddFromPreset</c> is handed the
        /// secrets and files them itself, because it is also the code that decides which keys are secret and
        /// records them on the connection, and splitting that decision across two objects is how the two come
        /// to disagree.</para>
        ///
        /// <para>An empty box keeps what is stored rather than clearing it: the box is empty on every edit by
        /// design, since a stored secret is never read back into a screen. There is no sweep to match
        /// <c>StoreHeaderSecrets</c>' because an input key comes from the preset's own prompt list and cannot
        /// be renamed or removed on this sheet — if that ever changes, the orphan it would leave is the same
        /// invisible one, and this is where the sweep belongs.</para>
        /// </summary>
        private void StoreInputSecrets(string connectionId, IReadOnlyDictionary<string, string> typed)
        {
            if (_credentials is null) return;

            foreach (var row in Inputs.Where(i => i.IsSecret))
                if (typed.TryGetValue(row.Key, out var value) && !string.IsNullOrWhiteSpace(value))
                    _credentials.Set(connectionId, AiCredentialNames.Input(row.Key), value);
        }

        private void StoreKey(string connectionId)
        {
            if (_credentials is null || string.IsNullOrWhiteSpace(ApiKeyEntry)) return;

            _credentials.Set(connectionId, AiCredentialNames.Primary, ApiKeyEntry.Trim());
            ApiKeyEntry = "";
        }

        /// <summary>
        /// Files each secret header's value, and sweeps the ones that are no longer secret. (#771)
        ///
        /// <para><b>The sweep runs first and by name.</b> A header that was renamed, unmarked or deleted leaves
        /// a credential under its old name, and an orphan in the keychain is invisible by definition — nothing
        /// reads it, so nothing reports it. Comparing what the sheet opened with against what it is saving is
        /// the only place that difference is known.</para>
        ///
        /// <para>A row whose value box is empty keeps what is stored rather than clearing it: the box is empty
        /// on every edit, because a stored secret is never read back into a screen.</para>
        /// </summary>
        private void StoreHeaderSecrets(string connectionId)
        {
            if (_credentials is null) return;

            // FIRST, carry a renamed row's secret to its new name. (#771, fable review)
            //
            // A stored secret is never read back into the box, and the box says so - "stored, type to
            // replace". A reader renaming a header because the provider renamed it therefore leaves it empty,
            // which is what the screen tells them to do. Without this the sweep below would delete the old
            // name and the store loop would skip the row for having no typed value: the credential is gone,
            // the save reports success, and every request afterwards is refused for a secret the reader has
            // no way to recover except from the provider.
            foreach (var row in Headers.Where(h => h.KeepsSecret && !string.IsNullOrWhiteSpace(h.Name)))
            {
                if (row.OriginalName is not { } was) continue;

                var from = AiCredentialNames.Header(was);
                var to = AiCredentialNames.Header(row.Name.Trim());
                if (string.Equals(from, to, StringComparison.Ordinal)) continue;

                if (_credentials.Get(connectionId, from) is { } carried)
                    _credentials.Set(connectionId, to, carried);
            }

            var keeping = Headers
                .Where(h => h.KeepsSecret && !string.IsNullOrWhiteSpace(h.Name))
                .Select(h => AiCredentialNames.Header(h.Name.Trim()))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var gone in _secretHeadersAtOpen.Where(name => !keeping.Contains(name)))
                _credentials.Delete(connectionId, gone);

            // A typed value wins over anything carried above, so renaming and rotating in one edit does both.
            foreach (var row in Headers.Where(
                         h => h.KeepsSecret
                              && !string.IsNullOrWhiteSpace(h.Name)
                              && !string.IsNullOrWhiteSpace(h.Value)))
            {
                _credentials.Set(connectionId, AiCredentialNames.Header(row.Name.Trim()), row.Value.Trim());
                row.Value = "";
            }
        }

        /// <summary>
        /// Forgets the stored key without touching the connection.
        ///
        /// <para>The narrower of the two destructive actions, and the reason they are separate: a hand-typed
        /// model list is real user work, and destroying it on an action meant only to stop billing would be
        /// data loss.</para>
        /// </summary>
        private void RemoveKey()
        {
            if (_credentials is null || _existingId is null) return;

            _credentials.Delete(_existingId, AiCredentialNames.Primary);
            ApiKeyEntry = "";
            this.RaisePropertyChanged(nameof(HasStoredKey));
            this.RaisePropertyChanged(nameof(KeyStatus));
        }

        /// <summary>
        /// The model rows as entries, carrying through everything the form does not show.
        ///
        /// <para>Rebuilding from the visible fields alone is how an edit silently drops what the provider
        /// published — the reader renames a connection and the per-turn picker's hover card goes blank. The
        /// same shape as the auth-header reset found in the #689 review, which is why it is worth stating
        /// rather than leaving to the reader of the expression.</para>
        /// </summary>
        private List<AiModelEntry> TypedModels() => Models
            .Where(m => !string.IsNullOrWhiteSpace(m.ModelId))
            .Select(m => (m.Published ?? new AiModelEntry(m.ModelId.Trim(), m.ModelId.Trim())) with
            {
                Id = m.ModelId.Trim(),

                // A retyped id is a different model, so it does not inherit the old one's mark. Without this,
                // a reader who saw "no longer listed" and corrected the id to the provider's new name got a
                // corrected model still claiming to be gone, across sessions, until the Models tab was next
                // opened. (#728, fable review)
                Missing = m.Published is { } published
                    && string.Equals(published.Id, m.ModelId.Trim(), StringComparison.Ordinal)
                    && published.Missing,
                DisplayName = string.IsNullOrWhiteSpace(m.DisplayName)
                    ? m.ModelId.Trim()
                    : m.DisplayName.Trim(),
                Enabled = m.Enabled,
            })
            .ToList();

        /// <summary>
        /// The connection's auth shape, carried through an edit unchanged. Not editable here — a custom
        /// endpoint needing a non-bearer header is #701's territory — but a draft that omitted them silently
        /// reset a preset's shape to Bearer, which broke Azure on the first rename. (fable review)
        /// </summary>
        private string _authHeaderName = "Authorization";
        private string? _authScheme = "Bearer";

        private AiConnectionDraft BuildDraft(IReadOnlyDictionary<string, string> inputs) => new(
            string.IsNullOrWhiteSpace(DisplayName) ? Id.Trim() : DisplayName.Trim(),
            Kind.Kind,
            BaseUrl.Trim(),
            TypedModels(),
            Headers
                .Where(h => !string.IsNullOrWhiteSpace(h.Name))
                // A secret row's value is deliberately dropped here rather than carried: the draft is what
                // reaches settings.json, and the secret goes to the credential store on its own path (#771).
                .Select(h => new AiHeader(h.Name.Trim(), h.Value.Trim(), h.KeepsSecret))
                .ToList(),
            inputs,
            _authHeaderName,
            _authScheme);

        /// <summary>
        /// Fills the id in from the host while the reader has not typed one of their own.
        ///
        /// <para>Only a default: the id is what the credential is filed under, so it must stay the reader's to
        /// choose. Deriving it permanently is the mistake this avoids — a URL-derived key changes the moment a
        /// port does, and the key appears to have been rejected rather than lost.</para>
        /// </summary>
        private void SuggestIdFromHost()
        {
            if (_idEdited || !IsIdEditable) return;
            if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri)) return;

            var slug = new string(uri.Host
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '-')
                .ToArray())
                .Trim('-');

            if (slug.Length == 0) return;

            this.RaiseAndSetIfChanged(ref _id, slug, nameof(Id));
        }

        private void OnInputChanged() => RefreshInputVisibility();

        /// <summary>Re-evaluates each prompt's <c>When</c> condition against what has been answered so far, so
        /// a conditional field appears as its trigger is typed. Azure wants a resource name <i>or</i> an
        /// explicit URL, and asking for both at once is wrong.</summary>
        private void RefreshInputVisibility()
        {
            var answered = Inputs.ToDictionary(i => i.Key, i => i.Value, StringComparer.Ordinal);
            foreach (var input in Inputs) input.Evaluate(answered);
        }
    }

    /// <summary>One <c>model-id</c> plus the name a human reads. Two strings on purpose: the id goes on the
    /// wire, the name goes in the picker, and they are rarely the same.</summary>
    public class AiModelRowViewModel : ViewModelBase
    {
        private readonly ObservableCollection<AiModelRowViewModel> _owner;
        private string _modelId = "";
        private string _displayName = "";
        private bool _enabled = true;

        public AiModelRowViewModel(ObservableCollection<AiModelRowViewModel> owner)
        {
            _owner = owner;
            RemoveCommand = ReactiveCommand.Create(() => { _owner.Remove(this); });
        }

        public string ModelId
        {
            get => _modelId;
            set => this.RaiseAndSetIfChanged(ref _modelId, value);
        }

        public string DisplayName
        {
            get => _displayName;
            set => this.RaiseAndSetIfChanged(ref _displayName, value);
        }

        /// <summary>
        /// On by default, and carried through an edit untouched.
        ///
        /// <para><b>A model is on because a person put it there.</b> Typing an id is putting it there — nobody
        /// types one they do not mean to use, and nobody types two hundred — so a hand-entered list is
        /// single-digit by construction and all-on gives the short per-turn picker (#693) that is the whole
        /// point. The opposite default belongs to a <i>fetched</i> catalogue (#674), where hundreds arrive
        /// because a key was pasted rather than because anyone asked.</para>
        ///
        /// <para>Untouched on edit so a rename cannot silently restore a list the reader pruned. Neither
        /// default may become a pre-enabled <i>subset</i>, however mechanically it is computed — see
        /// #689.</para>
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set => this.RaiseAndSetIfChanged(ref _enabled, value);
        }

        /// <summary>The entry this row was loaded from, so fields the form does not show — what the provider
        /// published about the model — survive an edit. Null for a row the reader has just added.</summary>
        public AiModelEntry? Published { get; set; }

        public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
    }

    /// <summary>One extra request header. Never the credential — that lives in the OS credential store.</summary>
    public class AiHeaderRowViewModel : ViewModelBase
    {
        private readonly ObservableCollection<AiHeaderRowViewModel> _owner;
        private string _name = "";
        private string _value = "";
        private bool _isSecret;

        public AiHeaderRowViewModel(ObservableCollection<AiHeaderRowViewModel> owner, bool canStoreSecrets = true)
        {
            _owner = owner;
            CanBeSecret = canStoreSecrets;
            RemoveCommand = ReactiveCommand.Create(() => { _owner.Remove(this); });
        }

        public string Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }

        public string Value
        {
            get => _value;
            set => this.RaiseAndSetIfChanged(ref _value, value);
        }

        /// <summary>
        /// Whether this value is a credential, and so belongs in the OS credential store rather than in
        /// <c>settings.json</c>. (#771)
        ///
        /// <para><b>The reader's call, not ours.</b> The same header name is a routing hint at one provider
        /// and a token at another, and a guess either way is wrong in a way they cannot see: guessing secret
        /// hides a value they wanted to read back, and guessing not writes a token to a file that gets
        /// screenshotted.</para>
        /// </summary>
        public bool IsSecret
        {
            get => _isSecret;
            set
            {
                this.RaiseAndSetIfChanged(ref _isSecret, value);
                this.RaisePropertyChanged(nameof(ShowStoredNote));
                this.RaisePropertyChanged(nameof(ValueMask));
                this.RaisePropertyChanged(nameof(ValueWatermark));
            }
        }

        /// <summary>Masks the value box while the row is secret. Escaped rather than a literal bullet, per the
        /// project rule against non-Latin glyphs in source.</summary>
        public char ValueMask => IsSecret ? '\u2022' : '\0';

        /// <summary>
        /// What the empty value box says. A stored secret is never read back into a screen, so on an edit the
        /// box is empty for a header that is in fact configured — without this it reads as missing, and the
        /// reader retypes a credential they did not need to.
        /// </summary>
        public string ValueWatermark => ShowStoredNote ? "stored \u2014 type to replace" : "value";

        /// <summary>False where there is nowhere to put a secret — Linux, or a Windows profile whose data
        /// folder cannot be written. The row still works; its value simply stays in the settings file, which
        /// the sheet says out loud rather than silently.</summary>
        public bool CanBeSecret { get; }

        /// <summary>Set when this row arrived with a secret already in the store. The value box stays empty —
        /// a stored secret is never read back into a screen — so this is what tells the reader the row is
        /// configured rather than blank.</summary>
        public bool HasStoredSecret { get; init; }

        /// <summary>The header name this row had when the sheet opened, or null for a row the reader added.
        /// Renaming a secret header moves its credential, and without this the old one would be orphaned in
        /// the keychain where nothing can ever reach it.</summary>
        public string? OriginalName { get; init; }

        /// <summary>
        /// Whether this row was already marked secret when the sheet opened. (#771, fable review)
        ///
        /// <para><b>The mark is data, not a capability of the machine doing the editing.</b> Without this, a
        /// row that arrived secret is persisted unmarked wherever <see cref="CanBeSecret"/> is false — a
        /// Windows profile whose data folder is briefly unwritable, or the settings file opened on Linux. The
        /// value box is empty, because a stored secret is never read back, so what gets written is a blank
        /// plaintext header: the credential is orphaned beyond even the delete sweep's reach, and the
        /// missing-secret refusal can no longer fire because there is no longer a mark to fire on. The reader
        /// renamed a connection and silently lost their gateway token.</para>
        /// </summary>
        public bool ArrivedSecret { get; init; }

        /// <summary>Whether this row's mark survives a save here — either the store can take it, or it came
        /// with one already and this machine has no business dropping it.</summary>
        public bool KeepsSecret => IsSecret && (CanBeSecret || ArrivedSecret);

        public bool ShowStoredNote => IsSecret && HasStoredSecret;

        public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
    }

    /// <summary>One field a preset asked for, rendered from its own declaration. A closed list where the
    /// preset supplies options, free text otherwise.</summary>
    public class AiInputRowViewModel : ViewModelBase
    {
        private readonly AiInputPrompt _prompt;
        private readonly Action _changed;
        private string _value = "";
        private bool _isVisible = true;

        public AiInputRowViewModel(AiInputPrompt prompt, Action changed, bool hasStoredSecret = false)
        {
            _prompt = prompt;
            _changed = changed;
            HasStoredSecret = hasStoredSecret;
        }

        public string Key => _prompt.Key;

        public string Message => _prompt.Message;

        /// <summary>Whether this answer is a credential, and so is never persisted to the settings file or
        /// read back into this screen. (#777)</summary>
        public bool IsSecret => _prompt.Secret;

        /// <summary>Whether the credential store already holds an answer for this prompt.</summary>
        public bool HasStoredSecret { get; }

        /// <summary>
        /// The masking character, or <c>'\0'</c> for none — Avalonia reads NUL as "show the text", so this
        /// is the whole of the mask rather than a separate flag. (#777)
        /// </summary>
        public char ValueMask => IsSecret ? '\u2022' : '\0';

        /// <summary>
        /// What the empty box says. A stored secret is never read back, so on an edit the box is empty for a
        /// prompt that HAS an answer — and an empty box with the ordinary placeholder under it reads as "this
        /// was never filled in", which invites a reader to retype a credential they did not need to.
        /// </summary>
        public string? ValueWatermark => IsSecret && HasStoredSecret
            ? "Stored — type to replace"
            : _prompt.Placeholder;

        public string? Placeholder => _prompt.Placeholder;

        public IReadOnlyList<AiPromptOption> Options => _prompt.Options ?? Array.Empty<AiPromptOption>();

        public bool HasOptions => Options.Count > 0;

        public bool IsFreeText => Options.Count == 0;

        public string Value
        {
            get => _value;
            set
            {
                this.RaiseAndSetIfChanged(ref _value, value);
                _changed();
            }
        }

        /// <summary>Whether this prompt applies, given what has been answered so far.</summary>
        public bool IsVisible
        {
            get => _isVisible;
            private set => this.RaiseAndSetIfChanged(ref _isVisible, value);
        }

        internal void Evaluate(IReadOnlyDictionary<string, string> answered) =>
            IsVisible = _prompt.When is null || _prompt.When.IsSatisfiedBy(answered);
    }
}
