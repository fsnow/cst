using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using ReactiveUI;

namespace CST.Avalonia.ViewModels
{
    /// <summary>
    /// The model chip in the Assistant's composer, and the list it opens. (#693)
    ///
    /// <para><b>This is the primitive the whole provider rework exists for.</b> Comparing two models on the
    /// same passage is the actual task — not configuring one and reading — and it wants to be one click from
    /// the answer on screen rather than a trip to Settings and back.</para>
    ///
    /// <para><b>Grouped by connection, not flat.</b> Claude Desktop's picker is flat, which works at four
    /// models. Once several endpoints are configured, grouping is what stops "the 8B I run locally" and "the
    /// hosted 70B" reading as interchangeable rows.</para>
    ///
    /// <para><b>The enabled subset only.</b> The full listing lives in Settings → Models; this is the short
    /// list the reader built there, so it needs no virtualization and the search box is for comfort rather
    /// than necessity.</para>
    /// </summary>
    public class AiModelPickerViewModel : ViewModelBase, IDisposable
    {
        private readonly IAiConnectionService? _service;
        private readonly Action? _changed;
        private string _search = "";
        private bool _isOpen;

        public AiModelPickerViewModel(IAiConnectionService? service, Action? changed = null)
        {
            _service = service;
            _changed = changed;

            ManageCommand = ReactiveCommand.Create(Manage);

            if (_service is not null)
            {
                _service.ConnectionsChanged += OnConnectionsChanged;
                Rebuild();
            }
        }

        public ObservableCollection<AiPickerGroupViewModel> Groups { get; } = new();

        /// <summary>
        /// What the chip says at rest — the current model's name, readable without opening anything.
        ///
        /// <para>The display name rather than the id: the reader typed or promoted a name for it, and
        /// <c>nvidia/nemotron-nano-9b-v2</c> is not what anyone calls the thing they are talking to.</para>
        /// </summary>
        public string CurrentLabel
        {
            get
            {
                if (_service?.Active is not { } connection) return "No model";
                if (_service.ActiveModelId is not { Length: > 0 } id) return "Choose a model";

                return connection.Models.FirstOrDefault(
                    m => string.Equals(m.Id, id, StringComparison.Ordinal))?.DisplayName ?? id;
            }
        }

        /// <summary>
        /// Shown as soon as one model is enabled.
        ///
        /// <para>It used to require <i>two</i>, on the reasoning that a chip offering a single model cannot
        /// do anything. That was wrong twice: the chip is also the only place that says which model will
        /// answer, and — until the service learned to follow an enable — it was the only control that could
        /// set the active model at all, so hiding it at one enabled model left no way to configure the
        /// assistant.</para>
        /// </summary>
        public bool HasChoices => Groups.Sum(g => g.Models.Count) > 0;

        public bool IsOpen
        {
            get => _isOpen;
            set => this.RaiseAndSetIfChanged(ref _isOpen, value);
        }

        /// <summary>Present because the list can grow, focused on open. At a handful of models it is
        /// unnecessary; it costs nothing and stops being unnecessary the moment someone enables twenty.</summary>
        public string Search
        {
            get => _search;
            set
            {
                this.RaiseAndSetIfChanged(ref _search, value);
                Rebuild();
            }
        }

        public bool HasNothingMatching => Groups.Count == 0;

        public ReactiveCommand<Unit, Unit> ManageCommand { get; }

        /// <summary>
        /// Chooses what the next message uses.
        ///
        /// <para>Switching to a model on another connection changes the base URL and the credential with it —
        /// the service's job, and the reason a picker that moved only the model id would fail confusingly.</para>
        ///
        /// <para>Persisted rather than session-only: the reader's last choice is the one they will most often
        /// want again, and a picker that silently reverted on restart would make "which model answered this?"
        /// a question they had to keep checking.</para>
        /// </summary>
        internal void Select(string connectionId, string modelId)
        {
            if (_service is null) return;

            _service.SetActive(connectionId, modelId);
            IsOpen = false;
            Search = "";
            _changed?.Invoke();
        }

        /// <summary>
        /// Opens Settings on the Models tab — the screen this control's label names.
        ///
        /// <para>It used to open Settings at whatever came first, leaving the reader to find the AI category
        /// and then the right tab: a link that says "Manage models" and lands somewhere else is a small lie
        /// the reader pays for every time.</para>
        /// </summary>
        private void Manage()
        {
            IsOpen = false;
            _ = App.ShowSettingsWindow("AI", AiSettingsViewModel.ModelsTab);
        }

        private void Rebuild()
        {
            Groups.Clear();
            if (_service is null) return;

            foreach (var connection in _service.Connections)
            {
                var models = connection.Models
                    .Where(m => m.Enabled)
                    .Where(m => Matches(m))
                    .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(m => new AiPickerModelViewModel(this, connection, m,
                        isCurrent: IsCurrent(connection, m)))
                    .ToList();

                if (models.Count == 0) continue;

                Groups.Add(new AiPickerGroupViewModel(connection, models));
            }

            this.RaisePropertyChanged(nameof(CurrentLabel));
            this.RaisePropertyChanged(nameof(HasChoices));
            this.RaisePropertyChanged(nameof(HasNothingMatching));
        }

        private bool Matches(AiModelEntry model) =>
            string.IsNullOrWhiteSpace(Search) ||
            model.DisplayName.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
            model.Id.Contains(Search, StringComparison.OrdinalIgnoreCase);

        private bool IsCurrent(AiConnection connection, AiModelEntry model) =>
            string.Equals(_service?.Active?.Id, connection.Id, StringComparison.Ordinal) &&
            string.Equals(_service?.ActiveModelId, model.Id, StringComparison.Ordinal);

        private void OnConnectionsChanged(object? sender, EventArgs e) => Rebuild();

        /// <summary>Stops listening. This one lives on the singleton assistant rather than being rebuilt per
        /// window, so it does not leak today — it is disposable so that stays true if the assistant is ever
        /// made per-panel.</summary>
        public void Dispose()
        {
            if (_service is not null) _service.ConnectionsChanged -= OnConnectionsChanged;
        }

        internal void Refresh() => Rebuild();
    }

    /// <summary>One connection's enabled models, under its name. (#693)</summary>
    public class AiPickerGroupViewModel : ViewModelBase
    {
        public AiPickerGroupViewModel(AiConnection connection, IReadOnlyList<AiPickerModelViewModel> models)
        {
            DisplayName = connection.DisplayName;
            Monogram = AiMonogram.For(connection.DisplayName);
            MonogramTone = AiMonogram.ToneFor(connection.Id);
            Models = models;

            // Marked, never hidden: the reader may be about to start their local runner, and a provider that
            // vanished from the picker because it was asleep would look like a lost configuration.
            Note = connection.State == Reachability.Unreachable ? "not responding" : "";
        }

        public string DisplayName { get; }
        public string Monogram { get; }
        public int MonogramTone { get; }
        public string Note { get; }
        public bool HasNote => Note.Length > 0;
        public IReadOnlyList<AiPickerModelViewModel> Models { get; }
    }

    /// <summary>One label/value row on the model hover card. (#693)</summary>
    public sealed record AiPickerFact(string Label, string Value);

    /// <summary>One pickable model. (#693)</summary>
    public class AiPickerModelViewModel : ViewModelBase
    {
        private readonly AiModelPickerViewModel _owner;
        private readonly string _connectionId;

        public AiPickerModelViewModel(
            AiModelPickerViewModel owner, AiConnection connection, AiModelEntry model, bool isCurrent)
        {
            _owner = owner;
            _connectionId = connection.Id;

            ModelId = model.Id;
            DisplayName = model.DisplayName;
            ProviderName = connection.DisplayName;
            IsCurrent = isCurrent;
            Missing = model.Missing;
            Unusable = ReasonItCannotBeUsed(connection);

            // Only what the provider actually said. A model that published no context length gets no context
            // row - rendering the absence as "0" would state a falsehood about the model, which is worse
            // than saying nothing.
            Facts = BuildFacts(connection, model);

            SelectCommand = ReactiveCommand.Create(() => _owner.Select(_connectionId, ModelId));
        }

        public string ModelId { get; }

        public string DisplayName { get; }

        /// <summary>Marked with a check, so the reader can see what answered the last question without
        /// opening anything else.</summary>
        public bool IsCurrent { get; }

        /// <summary>
        /// Why this model cannot be used, or empty when it can.
        ///
        /// <para>Said here rather than discovered as a 401 that names nothing — which is the failure this
        /// exists to prevent.</para>
        /// </summary>
        public string Unusable { get; }

        public bool IsUsable => Unusable.Length == 0;

        /// <summary>
        /// The provider's listing no longer carries this model. (#728)
        ///
        /// <para><b>Marked, not disabled.</b> A listing is not authority over the reader's configuration, and
        /// the mark is only ever set from a fetch that succeeded — so it is worth saying and not worth acting
        /// on. Whether the request works is still the provider's answer to give.</para>
        /// </summary>
        public bool Missing { get; }

        /// <summary>Shown only where nothing more serious is already in that space: a model on a connection
        /// with no key stored has a reason it cannot be used at all, and that is the one to read.</summary>
        public bool ShowMissingNote => Missing && IsUsable;

        /// <summary>The connection this model belongs to. Named on the hover card because with several
        /// endpoints configured, "Gemma 4" alone does not say whether it is the local one.</summary>
        public string ProviderName { get; }

        /// <summary>
        /// The hover card's rows: what is known about this model, and nothing else.
        ///
        /// <para>Only ever what the provider published and we wrote down when the model was promoted.
        /// OpenCode's equivalent card shows "Context 0" and "No reasoning" for a local model that published
        /// neither, which reads as fact and is not one — an absent row is the honest rendering of an absent
        /// field.</para>
        /// </summary>
        public IReadOnlyList<AiPickerFact> Facts { get; }

        public bool HasFacts => Facts.Count > 0;

        public ReactiveCommand<Unit, Unit> SelectCommand { get; }

        private static IReadOnlyList<AiPickerFact> BuildFacts(AiConnection connection, AiModelEntry model)
        {
            var facts = new List<AiPickerFact>
            {
                new("Model", model.DisplayName),
                new("Provider", connection.DisplayName),
            };

            // What the provider published about a model it no longer lists describes nothing it offers. Left
            // in, the card would confidently state a context window and a reasoning flag for a model that has
            // been retired, in the same shape it describes real ones - a worse failure than the silence that
            // preceded the cache. So the card says the one thing that is still true. (#728)
            if (model.Missing)
            {
                facts.Add(new AiPickerFact("Status", $"not in {connection.DisplayName}'s model list"));
                if (!string.Equals(model.Id, model.DisplayName, StringComparison.Ordinal))
                    facts.Add(new AiPickerFact("Id", model.Id));
                return facts;
            }

            if (model.Inputs is { Length: > 0 } inputs) facts.Add(new AiPickerFact("Inputs", inputs));

            // Three states, not two. Null is the provider having said nothing - a local runner publishes no
            // parameter list at all - and "No reasoning" there would assert something about the model that
            // nobody has established.
            if (model.SupportsReasoning is { } reasoning)
                facts.Add(new AiPickerFact("Reasoning", reasoning ? "Allows reasoning" : "No reasoning"));

            // Written out in full rather than rounded to "1,000K": the card has the room, and a context
            // window is a number readers compare exactly.
            if (model.ContextLength is { } context)
                facts.Add(new AiPickerFact("Context", context.ToString("N0")));

            // The wire id last, and only when it differs from the name - it is the string a reader would
            // copy, but it is not what they came to the card to read.
            if (!string.Equals(model.Id, model.DisplayName, StringComparison.Ordinal))
                facts.Add(new AiPickerFact("Id", model.Id));

            return facts;
        }

        /// <summary>
        /// Only reasons we can state as fact.
        ///
        /// <para>A missing key is a problem for a provider whose preset says it requires one, and that is a
        /// provider fact from the extraction rather than a guess. For a custom endpoint nothing is claimed:
        /// plenty of them need no key at all, and disabling a model on a hunch would be worse than letting
        /// the request explain itself.</para>
        /// </summary>
        private static string ReasonItCannotBeUsed(AiConnection connection)
        {
            if (connection.IsIncomplete) return "not finished being set up";

            var preset = AiProviderPresets.ById(connection.Id);
            if (preset is { RequiresKey: true } && connection.KeySource == CredentialSource.None)
                return "no API key stored";

            return "";
        }
    }

    /// <summary>
    /// The per-turn reasoning-effort chip, beside the model chip. (#671)
    ///
    /// <para><b>Only the levels the provider published for the model in front of you.</b> Effort support is
    /// per-model rather than per-provider, and the vocabularies genuinely differ — <c>low/medium/high</c> at
    /// most providers, <c>minimal/low/medium/high</c> at OpenAI, <c>none/default</c> on Groq's Qwen3,
    /// <c>low/high/max</c> at DeepSeek, and 130+ distinct published sets across models.dev. There is no
    /// universal scale to offer, so this offers the model's own list or nothing at all. #671 is explicit that
    /// the alternative — a table predicting which models take which values — is the curated capability
    /// registry #670 forbids.</para>
    ///
    /// <para><b>"Provider default" is the first position and it is not a placeholder.</b> Omitting the field
    /// IS the default: the provider then applies its own, so there is nothing to send for that position and
    /// nothing we have to know. It is also the only correct answer for a reasoning model that publishes no
    /// levels, and it keeps this app from choosing a level on a model's behalf. Where the provider publishes
    /// what its default is, the position says so — its word, not our choice.</para>
    /// </summary>
    public class AiEffortPickerViewModel : ViewModelBase, IDisposable
    {
        private readonly IAiConnectionService? _service;
        private readonly ISettingsService? _settings;
        private bool _isOpen;

        public AiEffortPickerViewModel(
            IAiConnectionService? service, ISettingsService? settings)
        {
            _service = service;
            _settings = settings;

            if (_service is not null)
            {
                _service.ConnectionsChanged += OnConnectionsChanged;
                Rebuild();
            }
        }

        public ObservableCollection<AiEffortChoiceViewModel> Choices { get; } = new();

        /// <summary>
        /// Hidden unless the model in front of the reader published levels to choose between.
        ///
        /// <para>A chip that is always present but empty for most models would be worse than absent: it would
        /// imply the app knows something about a model it knows nothing about.</para>
        /// </summary>
        public bool HasChoices => Choices.Count > 1;

        public bool IsOpen
        {
            get => _isOpen;
            set => this.RaiseAndSetIfChanged(ref _isOpen, value);
        }

        /// <summary>What the chip says at rest. Never bare — "Effort" alone would not say which way it is
        /// set, and the default position is the one most readers will never move.</summary>
        public string CurrentLabel =>
            Choices.FirstOrDefault(c => c.IsCurrent)?.ChipLabel ?? "Effort: default";

        private string? Chosen => _settings?.Settings.Ai.Chat.ReasoningEffort;

        private AiModelEntry? ActiveModel
        {
            get
            {
                if (_service?.Active is not { } connection) return null;
                if (_service.ActiveModelId is not { Length: > 0 } id) return null;

                return connection.Models.FirstOrDefault(
                    m => string.Equals(m.Id, id, StringComparison.Ordinal));
            }
        }

        internal void Rebuild()
        {
            Choices.Clear();

            var model = ActiveModel;
            var published = model?.ReasoningEfforts;
            if (published is not { Count: > 0 })
            {
                this.RaisePropertyChanged(nameof(HasChoices));
                this.RaisePropertyChanged(nameof(CurrentLabel));
                return;
            }

            var chosen = Chosen;
            var theirs = model!.DefaultReasoningEffort;
            var knowTheirDefault = theirs is { Length: > 0 } && published.Contains(theirs, StringComparer.Ordinal);

            // What will actually happen on the next turn, which is what the tick has to describe.
            //
            // A stored choice only counts while it is in THIS model's vocabulary — the wire guard drops it
            // otherwise, so "max" chosen on DeepSeek is inert on a model offering low/medium/high. Where the
            // provider states its own default, that is what happens when nothing is sent, so that level is
            // current. Where it does not, nothing here can say what happens, and the extra row carries it.
            var willSend = !string.IsNullOrWhiteSpace(chosen)
                           && published.Contains(chosen!, StringComparer.Ordinal);
            var current = willSend ? chosen : (knowTheirDefault ? theirs : null);

            // No separate "Provider default" row where the provider named its default: the reader wants to
            // know what will happen, and a row saying "default" above a list containing that same default
            // says it twice and answers it once. Nothing is sent while their choice matches it, exactly as
            // before — the tick describes the outcome, not the payload.
            if (!knowTheirDefault)
                Choices.Add(new AiEffortChoiceViewModel(
                    null, "Provider default", current is null, Choose));

            foreach (var value in published)
                Choices.Add(new AiEffortChoiceViewModel(
                    value, value, string.Equals(value, current, StringComparison.Ordinal), Choose));

            this.RaisePropertyChanged(nameof(HasChoices));
            this.RaisePropertyChanged(nameof(CurrentLabel));
        }

        private void Choose(string? value)
        {
            if (_settings is null) return;

            _settings.Settings.Ai.Chat.ReasoningEffort = value;
            _settings.RequestSave();

            IsOpen = false;
            Rebuild();
        }

        private void OnConnectionsChanged(object? sender, EventArgs e) => Rebuild();

        public void Dispose()
        {
            if (_service is not null) _service.ConnectionsChanged -= OnConnectionsChanged;
        }
    }

    /// <summary>One position in the effort chip.</summary>
    /// <param name="Value">What goes on the wire, or null for the default position, which sends nothing.</param>
    public class AiEffortChoiceViewModel : ViewModelBase
    {
        private readonly Action<string?> _choose;

        public AiEffortChoiceViewModel(
            string? value, string label, bool isCurrent, Action<string?> choose)
        {
            Value = value;
            Label = label;
            IsCurrent = isCurrent;
            _choose = choose;
            ChooseCommand = ReactiveCommand.Create(() => _choose(Value));
        }

        public string? Value { get; }
        public string Label { get; }

        public bool IsCurrent { get; }

        /// <summary>What the chip shows when this position is the current one.</summary>
        public string ChipLabel => Value is null ? "Effort: default" : $"Effort: {Label}";

        public ReactiveCommand<Unit, Unit> ChooseCommand { get; }
    }
}
