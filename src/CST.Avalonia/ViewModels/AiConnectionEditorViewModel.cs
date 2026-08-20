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
        private readonly string? _existingId;

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

            SaveCommand = ReactiveCommand.Create(Save);
            CancelCommand = ReactiveCommand.Create(() => _close(false));
            AddModelCommand = ReactiveCommand.Create(() => Models.Add(new AiModelRowViewModel(Models)));
            AddHeaderCommand = ReactiveCommand.Create(() => Headers.Add(new AiHeaderRowViewModel(Headers)));
            RemoveKeyCommand = ReactiveCommand.Create(RemoveKey);
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
        public static AiConnectionEditorViewModel ForPreset(
            IAiConnectionService service, IAiCredentialStore? credentials, AiProviderPreset preset,
            Action<bool> close)
        {
            var vm = new AiConnectionEditorViewModel(service, credentials, close, preset, null)
            {
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

        /// <summary>Editing what is already configured. Everything is editable except the id, which is the
        /// account the credential is filed under — changing it would orphan the key, and the failure would
        /// read as a rejected key rather than a lost one.</summary>
        public static AiConnectionEditorViewModel ForExisting(
            IAiConnectionService service, IAiCredentialStore? credentials, AiConnection connection,
            Action<bool> close)
        {
            var vm = new AiConnectionEditorViewModel(service, credentials, close, null, connection.Id)
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
                vm.Headers.Add(new AiHeaderRowViewModel(vm.Headers)
                {
                    Name = header.Key,
                    Value = header.Value,
                });

            foreach (var input in connection.Inputs)
                vm.Inputs.Add(new AiInputRowViewModel(
                    new AiInputPrompt(input.Key, input.Key), vm.OnInputChanged) { Value = input.Value });

            if (vm.Models.Count == 0) vm.Models.Add(new AiModelRowViewModel(vm.Models));
            return vm;
        }

        // ---- what the sheet shows ----------------------------------------------------------------------

        public bool IsPreset => _preset is not null;

        /// <summary>Id, display name, protocol and address are asked for only where they are not already
        /// known. A preset knows all four.</summary>
        public bool IsFullForm => _preset is null;

        public bool IsIdEditable => _existingId is null && _preset is null;

        /// <summary>A preset's address, shown read-only. Not a field to fill in, but worth seeing: it is the
        /// one fact that says where the reader's questions and money are about to go.</summary>
        public bool ShowFixedEndpoint => _preset is not null;

        public string FixedEndpoint => _preset?.BaseUrl ?? "";

        public string Title => _preset is not null
            ? $"Add {_preset.DisplayName}"
            : _existingId is not null ? $"Edit {_displayName}" : "Add a custom endpoint";

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
            _existingId is not null && _credentials?.GetApiKey(_existingId) is not null;

        public string KeyStatus => _existingId is null
            ? "Stored in the operating system's credential store, never in settings."
            : HasStoredKey
                ? $"A key is stored for {_displayName}. Paste a new one to replace it."
                : $"No key is stored for {_displayName}.";

        /// <summary>
        /// Whether the "optional" line under the key box applies.
        ///
        /// <para><b>Only a custom endpoint.</b> A named provider that reaches this sheet with a key box is one
        /// whose key is required — a provider needing none shows no box at all (<see cref="ShowKeyField"/>) —
        /// so telling that reader the box is optional contradicts the blurb three lines above it and invites
        /// them to save a connection that cannot answer. The header clause is equally wrong there: headers are
        /// asked for on a custom endpoint alone.</para>
        /// </summary>
        public bool HasKeyHint => _preset is null;

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
            var inputs = Inputs
                .Where(i => i.IsVisible && !string.IsNullOrWhiteSpace(i.Value))
                .ToDictionary(i => i.Key, i => i.Value.Trim(), StringComparer.Ordinal);

            var result = _preset is not null
                ? AddPreset(inputs)
                : _existingId is not null
                    ? _service.Update(_existingId, BuildDraft(inputs))
                    : _service.Add(Id.Trim(), BuildDraft(inputs));

            if (!result.Ok)
            {
                Problem = result.Problem;
                return;
            }

            StoreKey(result.Connection?.Id ?? _existingId ?? Id.Trim());
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
            var added = _service.AddFromPreset(_preset!.Id, inputs);
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
        private void StoreKey(string connectionId)
        {
            if (_credentials is null || string.IsNullOrWhiteSpace(ApiKeyEntry)) return;

            _credentials.SetApiKey(connectionId, ApiKeyEntry.Trim());
            ApiKeyEntry = "";
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

            _credentials.DeleteApiKey(_existingId);
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
                .ToDictionary(h => h.Name.Trim(), h => h.Value.Trim(), StringComparer.Ordinal),
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

        public AiHeaderRowViewModel(ObservableCollection<AiHeaderRowViewModel> owner)
        {
            _owner = owner;
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

        public AiInputRowViewModel(AiInputPrompt prompt, Action changed)
        {
            _prompt = prompt;
            _changed = changed;
        }

        public string Key => _prompt.Key;

        public string Message => _prompt.Message;

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
