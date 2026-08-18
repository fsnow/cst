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
    public class AiModelPickerViewModel : ViewModelBase
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
                _service.ConnectionsChanged += (_, _) => Rebuild();
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

        /// <summary>Hidden entirely until there is a choice to make. A chip offering one model, or none, is a
        /// control that cannot do anything.</summary>
        public bool HasChoices => Groups.Sum(g => g.Models.Count) > 1;

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

        private void Manage()
        {
            IsOpen = false;
            _ = App.ShowSettingsWindow();
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
            IsCurrent = isCurrent;
            Unusable = ReasonItCannotBeUsed(connection);

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

        public ReactiveCommand<Unit, Unit> SelectCommand { get; }

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
}
