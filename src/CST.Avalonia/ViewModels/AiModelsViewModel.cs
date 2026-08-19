using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services.Ai;
using ReactiveUI;

namespace CST.Avalonia.ViewModels
{
    /// <summary>
    /// The Models tab: which of each connection's models the reader wants on their short list. (#692, #674)
    ///
    /// <para><b>Two sources, one list.</b> A connection's models are whatever the reader typed on the
    /// connection sheet, plus — for a provider that publishes a listing — whatever asking it returns. The
    /// typed ones are the floor and always work; the fetched ones are the upgrade, and a provider that
    /// publishes nothing is not a degraded case but the ordinary one for a local runner.</para>
    ///
    /// <para><b>Defaults differ by source, for one reason.</b> A model is on because a person put it there.
    /// Typing an id is putting it there, so a typed model arrives on. A fetched catalogue arrives because a
    /// key was pasted — nobody asked for its four hundred entries — so every one of them starts off and the
    /// reader promotes the handful they will switch between.</para>
    ///
    /// <para><b>Nothing here ranks anything.</b> Ordering is alphabetical within a group, groups follow the
    /// order connections were added, and the only filter is a mechanical one built from the provider's own
    /// published modality. No badge, no tier, no "recommended", and above all no pre-enabled subset — which
    /// is what upstream ships, computed from release dates, and is still a verdict for being arithmetic
    /// (#670/#681, #689).</para>
    /// </summary>
    public class AiModelsViewModel : ViewModelBase, IDisposable
    {
        private readonly IAiConnectionService? _service;
        private readonly IAiModelCatalog? _catalog;
        private string _search = "";
        private bool _textOnly = true;
        private bool _suppressRebind;

        public AiModelsViewModel(IAiConnectionService? service, IAiModelCatalog? catalog = null)
        {
            _service = service;
            _catalog = catalog;

            if (_service is not null)
            {
                _service.ConnectionsChanged += OnConnectionsChanged;
                Rebind();
            }
        }

        /// <summary>
        /// The flattened list the view renders: group headers and model rows in one sequence.
        ///
        /// <para>Flat so it can virtualize. A provider listing runs to hundreds of entries, and a nested
        /// items control inside the settings page's scroll viewer would build a control for every one of
        /// them.</para>
        /// </summary>
        public ObservableCollection<object> Rows { get; } = new();

        public ObservableCollection<AiModelGroupViewModel> Groups { get; } = new();

        public bool HasNoConnections => Groups.Count == 0;

        /// <summary>Filters <b>across</b> groups, not within the expanded one — at four hundred models the
        /// list is a search box with results, not a menu you scroll.</summary>
        public string Search
        {
            get => _search;
            set
            {
                this.RaiseAndSetIfChanged(ref _search, value);
                Reflow();
            }
        }

        public bool IsSearching => !string.IsNullOrWhiteSpace(Search);

        /// <summary>
        /// Hide models that cannot take text in and give text out.
        ///
        /// <para>Built entirely from the provider's published modality, so it removes models that cannot
        /// answer a question at all — a text-to-speech model, an image generator — and says nothing about
        /// which of the rest is better. Shown as a control and reversible, because a mechanical filter the
        /// reader cannot see or turn off starts to look like a judgment even when it isn't.</para>
        /// </summary>
        public bool TextOnly
        {
            get => _textOnly;
            set
            {
                this.RaiseAndSetIfChanged(ref _textOnly, value);
                Reflow();
            }
        }

        internal IAiConnectionService? Service => _service;

        internal IAiModelCatalog? Catalog => _catalog;

        /// <summary>
        /// Runs a change that must not rebuild the list.
        ///
        /// <para>Flipping one model's switch raises <c>ConnectionsChanged</c> like any other edit, and
        /// rebuilding <see cref="Rows"/> in response clears an <c>ObservableCollection</c> the list box is
        /// bound to — which sends the scroll position back to the top. At four hundred rows that means
        /// turning on a model near the bottom throws the reader back to the first one. The row updates
        /// itself; nothing else about the list has changed.</para>
        /// </summary>
        internal void Suppressed(Action change)
        {
            _suppressRebind = true;
            try { change(); }
            finally { _suppressRebind = false; }
        }

        internal void Reflow()
        {
            Rows.Clear();

            foreach (var group in Groups)
            {
                group.ApplyFilter(Search, TextOnly, IsSearching);

                // A group with nothing matching disappears entirely while searching, rather than sitting
                // there as a header promising results it does not have.
                if (IsSearching && group.Visible.Count == 0) continue;

                Rows.Add(group);
                if (!group.IsExpanded && !IsSearching) continue;

                foreach (var row in group.Visible) Rows.Add(row);
            }

            this.RaisePropertyChanged(nameof(HasNoConnections));
        }

        private void OnConnectionsChanged(object? sender, EventArgs e) => Rebind();

        /// <summary>Stops listening. See the note on <see cref="AiConnectionsViewModel.Dispose"/> — the
        /// service is a singleton and every Settings open builds one of these.</summary>
        public void Dispose()
        {
            if (_service is not null) _service.ConnectionsChanged -= OnConnectionsChanged;
        }

        private void Rebind()
        {
            if (_service is null || _suppressRebind) return;

            var connections = _service.Connections;

            for (int i = Groups.Count - 1; i >= 0; i--)
                if (!connections.Any(c => string.Equals(c.Id, Groups[i].Id, StringComparison.Ordinal)))
                    Groups.RemoveAt(i);

            for (int i = 0; i < connections.Count; i++)
            {
                var connection = connections[i];
                var existing = Groups.FirstOrDefault(
                    g => string.Equals(g.Id, connection.Id, StringComparison.Ordinal));

                if (existing is null)
                {
                    Groups.Insert(Math.Min(i, Groups.Count), new AiModelGroupViewModel(this, connection));
                    continue;
                }

                existing.Update(connection);
                var at = Groups.IndexOf(existing);
                if (at != i) Groups.Move(at, i);
            }

            Reflow();
        }
    }

    /// <summary>One connection's models, as a collapsible group. (#692)</summary>
    public class AiModelGroupViewModel : ViewModelBase
    {
        private readonly AiModelsViewModel _owner;
        private AiConnection _connection;
        private IReadOnlyList<AiCatalogModel> _fetched = Array.Empty<AiCatalogModel>();
        private bool _isExpanded;
        private bool _isFetching;
        private bool _hasFetched;
        private string? _fetchProblem;

        public AiModelGroupViewModel(AiModelsViewModel owner, AiConnection connection)
        {
            _owner = owner;
            _connection = connection;

            ToggleCommand = ReactiveCommand.Create(() => { IsExpanded = !IsExpanded; });
            FetchCommand = ReactiveCommand.CreateFromTask(FetchAsync);
        }

        public string Id => _connection.Id;

        public string DisplayName => _connection.DisplayName;

        public string Monogram => AiMonogram.For(DisplayName);

        public int MonogramTone => AiMonogram.ToneFor(Id);

        /// <summary>Every row this group could show, filtered and ordered.</summary>
        public List<AiCatalogRowViewModel> Visible { get; } = new();

        /// <summary>
        /// Expanding fetches, once, if the connection has never been asked.
        ///
        /// <para>Cheaper than a button nobody finds, and it is the moment the reader has said they want to
        /// see this provider's models. A failure leaves the typed list showing and reports why.</para>
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                this.RaiseAndSetIfChanged(ref _isExpanded, value);
                _owner.Reflow();
                if (value && !_hasFetched && !_isFetching) _ = FetchAsync();
            }
        }

        public bool IsFetching
        {
            get => _isFetching;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isFetching, value);
                this.RaisePropertyChanged(nameof(CanFetch));
            }
        }

        public bool CanFetch => !IsFetching;

        /// <summary>Why the last fetch produced nothing, in the service's own words — which name the endpoint
        /// rather than saying "cannot connect" and leaving the reader to guess to what.</summary>
        public string? FetchProblem
        {
            get => _fetchProblem;
            private set
            {
                this.RaiseAndSetIfChanged(ref _fetchProblem, value);
                this.RaisePropertyChanged(nameof(HasFetchProblem));
            }
        }

        public bool HasFetchProblem => !string.IsNullOrEmpty(FetchProblem);

        /// <summary>
        /// The number a reader needs to decide whether to expand — and, once a listing has been fetched, how
        /// much of it they have promoted. OpenCode omits the count entirely.
        /// </summary>
        public string CountText
        {
            get
            {
                var total = Visible.Count;
                var on = Visible.Count(r => r.Enabled);
                if (total == 0) return "no models yet";
                return on == total ? Plural(total) : $"{on} of {Plural(total)} on";
            }
        }

        private static string Plural(int n) => n == 1 ? "1 model" : $"{n} models";

        public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

        public ReactiveCommand<Unit, Unit> FetchCommand { get; }

        internal void Update(AiConnection connection)
        {
            _connection = connection;
            this.RaisePropertyChanged(nameof(DisplayName));
            this.RaisePropertyChanged(nameof(Monogram));
            this.RaisePropertyChanged(nameof(MonogramTone));
        }

        /// <summary>
        /// Merges the reader's stored list with anything fetched, then filters.
        ///
        /// <para>Stored models are always shown and never filtered out — the reader put them there, and
        /// hiding one behind a capability filter would look like it had been lost. Fetched models the reader
        /// has not touched are subject to both the search and the modality filter.</para>
        /// </summary>
        internal void ApplyFilter(string search, bool textOnly, bool searching)
        {
            var stored = _connection.Models.ToDictionary(m => m.Id, StringComparer.Ordinal);
            var rows = new List<AiCatalogRowViewModel>();

            foreach (var model in _connection.Models)
                rows.Add(new AiCatalogRowViewModel(
                    this, model.Id, model.DisplayName,
                    _fetched.FirstOrDefault(f => string.Equals(f.Id, model.Id, StringComparison.Ordinal)),
                    model.Enabled, typed: true));

            foreach (var model in _fetched)
            {
                if (stored.ContainsKey(model.Id)) continue;
                if (textOnly && !model.IsTextToText) continue;
                rows.Add(new AiCatalogRowViewModel(
                    this, model.Id, model.DisplayName, model, enabled: false, typed: false));
            }

            if (!string.IsNullOrWhiteSpace(search))
                rows = rows.Where(r => r.Matches(search)).ToList();

            rows.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

            Visible.Clear();
            Visible.AddRange(rows);
            this.RaisePropertyChanged(nameof(CountText));
        }

        private async Task FetchAsync()
        {
            if (_owner.Catalog is null) return;

            IsFetching = true;
            FetchProblem = null;
            try
            {
                var result = await _owner.Catalog.FetchAsync(_connection).ConfigureAwait(true);
                _hasFetched = true;

                if (result.Ok) _fetched = result.Models;
                else FetchProblem = result.Problem;
            }
            catch (Exception ex)
            {
                // Started from a property setter, so nothing is awaiting this and an escaping exception would
                // be an unobserved task rather than a message. The listing is additive; failing to get one is
                // a sentence on the group header, never a crash.
                _hasFetched = true;
                FetchProblem = $"Could not read {DisplayName}'s model list: {ex.Message}";
            }
            finally
            {
                IsFetching = false;
                _owner.Reflow();
            }
        }

        /// <summary>
        /// Promotes a model into the reader's stored list, or turns one off. Turning off keeps the entry, so
        /// a display name typed by hand survives being switched off and on.
        /// </summary>
        /// <remarks>
        /// The write is suppressed from rebuilding the list — see <see cref="AiModelsViewModel.Suppressed"/>
        /// — so the connection snapshot is refreshed here instead. Without that, the next genuine rebuild
        /// would rebuild from a record taken before the toggle and silently undo it on screen.
        /// </remarks>
        internal void SetEnabled(string modelId, string displayName, bool enabled)
        {
            if (_owner.Service is not { } service) return;

            _owner.Suppressed(() => service.EnableModel(Id, modelId, displayName, enabled));

            if (service.Connections.FirstOrDefault(
                    c => string.Equals(c.Id, Id, StringComparison.Ordinal)) is { } fresh)
                _connection = fresh;

            this.RaisePropertyChanged(nameof(CountText));
        }
    }

    /// <summary>One model, with whatever the provider published about it. (#674)</summary>
    public class AiCatalogRowViewModel : ViewModelBase
    {
        private readonly AiModelGroupViewModel _group;
        private readonly AiCatalogModel? _published;
        private bool _enabled;

        public AiCatalogRowViewModel(
            AiModelGroupViewModel group, string id, string displayName, AiCatalogModel? published,
            bool enabled, bool typed)
        {
            _group = group;
            _published = published;
            _enabled = enabled;

            ModelId = id;
            DisplayName = displayName;
            IsTyped = typed;
        }

        public string ModelId { get; }

        public string DisplayName { get; }

        /// <summary>True for a model the reader typed rather than one that arrived in a listing. Shown,
        /// because "I added this by hand" is the reason it appears even when the provider has never heard of
        /// it.</summary>
        public bool IsTyped { get; }

        /// <summary>
        /// The provider's own facts about the model, on one line: context window, price per million tokens,
        /// and whether it accepts a reasoning parameter.
        ///
        /// <para>Verbatim and attributed to the provider — that is what makes it safe where a table we
        /// maintained would not be. Empty for an endpoint that publishes nothing, which is most local
        /// runners, and the row degrades to a bare id rather than demanding metadata that does not exist.</para>
        /// </summary>
        public string Details
        {
            get
            {
                if (_published is null) return ModelId;

                var parts = new List<string> { ModelId };

                if (_published.ContextLength is { } context)
                    parts.Add($"{context / 1000:N0}K context");

                if (_published.PromptPricePerMillion is { } prompt &&
                    _published.CompletionPricePerMillion is { } completion)
                    parts.Add(prompt == 0 && completion == 0
                        ? "free"
                        : $"${Money(prompt)}/${Money(completion)} per M");

                if (_published.SupportsReasoning) parts.Add("reasoning");

                return string.Join("  ·  ", parts);
            }
        }

        private static string Money(decimal perMillion) =>
            perMillion >= 1m ? perMillion.ToString("0.##") : perMillion.ToString("0.###");

        /// <summary>
        /// Whether this model appears in the per-turn picker.
        ///
        /// <para>Off by default for anything that arrived in a listing, on for anything the reader typed. See
        /// the note on <see cref="AiModelsViewModel"/>: a model is on because a person put it there.</para>
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                this.RaiseAndSetIfChanged(ref _enabled, value);
                _group.SetEnabled(ModelId, DisplayName, value);
            }
        }

        /// <summary>Matches on both the id and the display name — a reader searching "nemotron" is as likely
        /// to be thinking of the wire id as of the label.</summary>
        internal bool Matches(string search) =>
            DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            ModelId.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
