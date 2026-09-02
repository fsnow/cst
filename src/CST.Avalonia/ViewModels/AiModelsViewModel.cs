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
    /// order connections were added, and the only filter is built from the provider's own published price.
    /// No badge, no tier, no "recommended", and above all no pre-enabled subset — which
    /// is what upstream ships, computed from release dates, and is still a verdict for being arithmetic
    /// (#670/#681, #689).</para>
    /// </summary>
    public class AiModelsViewModel : ViewModelBase, IDisposable
    {
        private readonly IAiConnectionService? _service;
        private readonly IAiModelCatalog? _catalog;
        private readonly IAiProviderLogos? _logos;
        private readonly IAiModelListingCache? _listings;
        private string _search = "";
        private bool _freeOnly;
        private bool _suppressRebind;

        public AiModelsViewModel(
            IAiConnectionService? service,
            IAiModelCatalog? catalog = null,
            IAiProviderLogos? logos = null,
            IAiModelListingCache? listings = null)
        {
            _service = service;
            _catalog = catalog;
            _logos = logos;
            _listings = listings;

            if (_service is not null)
            {
                _service.ConnectionsChanged += OnConnectionsChanged;
                Rebind();

                // Every connection is asked again when this tab is built, which is the moment the reader has
                // said "show me models". Not at application start: firing at every configured provider on
                // launch, for a feature that ships disabled and a reader who may never open Settings, is the
                // same thing AiCredentialStore refuses to do with the keychain and for the same reason.
                //
                // The cache is what makes the numbers right immediately; this is what keeps them current.
                // Neither is redundant - without the cache the count is the reader's own list until the
                // response lands, and offline it stays that way. (#790)
                RefreshAll();
            }
        }

        internal IAiModelListingCache? Listings => _listings;

        /// <summary>Asks every connection for its listing, without waiting and without reporting failures
        /// here: each group already shows its own outcome, and a provider being down must not stop the tab
        /// from rendering the others.</summary>
        private void RefreshAll()
        {
            foreach (var group in Groups) group.BeginRefresh();
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
        /// Hide models the provider charges for.
        ///
        /// <para>Built from published price, so it states a fact rather than a preference — and unlike the
        /// modality filter it replaced, it does something. Of OpenRouter's 415 models <b>every one</b> can
        /// answer in text, so "text models only" excluded nothing at all; 395 of them cost money.</para>
        ///
        /// <para><b>Off by default.</b> On would be a claim about what the reader wants to spend, and it
        /// would hide almost everything the first time they looked at their provider.</para>
        /// </summary>
        public bool FreeOnly
        {
            get => _freeOnly;
            set
            {
                this.RaiseAndSetIfChanged(ref _freeOnly, value);
                Reflow();
            }
        }

        internal IAiConnectionService? Service => _service;

        internal IAiModelCatalog? Catalog => _catalog;

        internal IAiProviderLogos? Logos => _logos;

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
                group.ApplyFilter(Search, FreeOnly, IsSearching);

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
                {
                    // Forgetting here, where a connection is actually going away, rather than deleting
                    // everything absent from the current list: a group only disappears because its connection
                    // did. (#790)
                    _listings?.Forget(Groups[i].Id);
                    Groups.RemoveAt(i);
                }

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
    /// <remarks>
    /// Carries a logo for the same reason the Providers rows do: these headers name the same connections, and
    /// a reader who has learnt to find OpenRouter by its mark on one tab should not have to fall back to
    /// reading letters on the next.
    /// </remarks>
    public class AiModelGroupViewModel : AiLogoRowViewModel
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

            LoadLogo(owner.Logos);

            // The provider's list, from the last time anyone asked - so the first paint is already the right
            // set rather than the reader's own saved models. _hasFetched stays FALSE: this is what is shown,
            // never what is concluded, and expanding still asks the endpoint. (#790)
            _fetched = owner.Listings?.Get(connection.Id) ?? Array.Empty<AiCatalogModel>();

            ToggleCommand = ReactiveCommand.Create(() => { IsExpanded = !IsExpanded; });
            OpenDocCommand = ReactiveCommand.Create(() => AiConnectionsViewModel.OpenUrl(DocUrl));
            FetchCommand = ReactiveCommand.CreateFromTask(FetchAsync);
        }

        /// <summary>Start a listing fetch without waiting for it, unless one has already happened or is in
        /// flight. Used when the tab is built, so a cached listing is brought up to date. (#790)</summary>
        internal void BeginRefresh()
        {
            if (_hasFetched || _isFetching) return;
            _ = FetchAsync();
        }

        public string Id => _connection.Id;

        public string DisplayName => _connection.DisplayName;

        public override string Monogram => AiMonogram.For(DisplayName);

        public override int MonogramTone => AiMonogram.ToneFor(Id);

        /// <summary>The RECORDED preset, not the connection id. A custom endpoint whose slug a later
        /// catalogue claims would otherwise borrow that provider's mark. (R5-6, #766)</summary>
        protected override string? ProviderId => _connection.PresetIdOrLegacyId;

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
                if (total == 0) return _hasFetched ? "no models" : "no models yet";

                // Plain: "2 models", or "2 of 13 models". The trailing "on" said which of the two numbers was
                // the enabled one, which the shape "2 of 13" already says.
                //
                // An earlier cut qualified the noun - saved / listed / matching - because the same words meant
                // three different things depending on whether the provider's listing had been fetched yet.
                // That was solving the wrong half. The reason a reader ever saw their own saved count is that
                // the listing lived in memory for one window and was discarded; caching it (#790) makes the
                // number the listing from the first paint, and a label explaining which set you are looking at
                // then explains a distinction that no longer exists. (#786)
                return on == total ? Plural(total) : $"{on} of {Plural(total)}";
            }
        }

        private static string Plural(int n) => n == 1 ? "1 model" : $"{n} models";

        /// <summary>
        /// The provider's models page, offered when this group has nothing in it.
        ///
        /// <para>The case with no other answer today: an endpoint that publishes no listing, or whose listing
        /// failed, leaves a reader needing a model id from somewhere. This is the somewhere. Hidden once the
        /// group has models, where it would be a link to information already on screen.</para>
        /// </summary>
        public string? DocUrl => _connection.PresetIdOrLegacyId is { } presetId
            ? AiProviderPresets.ById(presetId)?.Doc
            : null;

        /// <summary>
        /// Offered only once we have asked and come back with nothing.
        ///
        /// <para>Three states looked empty but are not the case this answers: <b>mid-fetch</b>, where it
        /// rendered beside "asking the provider for its models"; <b>before any fetch</b>, where a collapsed
        /// group would advertise a link although expanding it would have produced a listing; and <b>when a
        /// filter hid everything</b>, where a provider whose four hundred models are all paid would be
        /// described as publishing none. Only the last of those is even visible today, and all three said
        /// something untrue.</para>
        /// </summary>
        public bool ShowDoc =>
            _hasFetched && !IsFetching && !HasAnyModels && !string.IsNullOrEmpty(DocUrl);

        /// <summary>Whether this connection has any models at all, before the search and price filters — the
        /// question "does this provider publish a listing?" rather than "did the filter leave anything?".</summary>
        private bool HasAnyModels => _fetched.Count > 0 || _connection.Models.Count > 0;

        public ReactiveCommand<Unit, Unit> OpenDocCommand { get; }

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
        /// hiding one behind a price filter would look like it had been lost. Fetched models the reader has
        /// not touched are subject to both the search and the price filter.</para>
        /// </summary>
        internal void ApplyFilter(string search, bool freeOnly, bool searching)
        {
            // First-wins over the stored list rather than one row per record, and never a throw. The pair
            // this guards against is repaired at load (SettingsValidator) and refused on the way in
            // (AiConnectionService.DuplicateModel), so nothing should reach here — but this method runs
            // inside the AiSettingsViewModel constructor, where an ArgumentException (ToDictionary threw
            // one) is not a bad row: it locks the reader out of the entire Settings window, with no way
            // back in to remove whatever closed it. First-wins also matches what a toggle acts on, since
            // EnableModel/SetModelEnabled resolve an id by FirstOrDefault. (#870)
            var stored = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<AiCatalogRowViewModel>();

            foreach (var model in _connection.Models)
            {
                if (!stored.Add(model.Id)) continue;
                rows.Add(new AiCatalogRowViewModel(
                    this, model.Id, model.DisplayName,
                    _fetched.FirstOrDefault(f => string.Equals(f.Id, model.Id, StringComparison.Ordinal)),
                    model.Enabled, model.Missing));
            }

            foreach (var model in _fetched)
            {
                if (stored.Contains(model.Id)) continue;
                if (freeOnly && model.CostsMoney) continue;
                rows.Add(new AiCatalogRowViewModel(
                    this, model.Id, model.DisplayName, model, enabled: false));
            }

            if (!string.IsNullOrWhiteSpace(search))
                rows = rows.Where(r => r.Matches(search)).ToList();

            rows.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

            Visible.Clear();
            Visible.AddRange(rows);
            this.RaisePropertyChanged(nameof(CountText));
            this.RaisePropertyChanged(nameof(ShowDoc));
        }

        /// <summary>What the provider published about one model, or null when it published nothing — which is
        /// every hand-typed id and every endpoint with no listing.</summary>
        private AiModelEntry? Facts(string modelId)
        {
            var published = _fetched.FirstOrDefault(
                f => string.Equals(f.Id, modelId, StringComparison.Ordinal));
            if (published is null) return null;

            var inputs = published.InputModalities is { Count: > 0 } modalities
                ? string.Join(", ", modalities)
                : null;

            return new AiModelEntry(
                published.Id, published.DisplayName, true,
                published.ContextLength, published.SupportsReasoning, inputs,
                Missing: false,
                // Captured at the moment the reader promotes the model, for the same reason the rest of these
                // are: the listing is only fetched while the Models tab is open, and the per-turn picker has
                // to be able to offer the levels without one. (#671)
                ReasoningEfforts: published.ReasoningEfforts,
                DefaultReasoningEffort: published.DefaultReasoningEffort);
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

                if (result.Ok)
                {
                    _fetched = result.Models;

                    // Recorded from any successful fetch, complete or not: an incomplete listing is a fine
                    // thing to SHOW - every model in it is real - and showing it beats showing the reader's
                    // own saved list. The completeness gate below is about what we INFER, which is a
                    // different question and stays where it is. (#790, #728)
                    _owner.Listings?.Put(Id, result.Models);

                    // The listing is in memory only while this tab is open, so what it says about the
                    // reader's stored models has to be written down now or the per-turn picker will go on
                    // describing a model that no longer exists. Suppressed like the toggles, so recording it
                    // does not rebuild the list under the reader. (#728)
                    //
                    // Only a listing that is complete as far as the endpoint told us. A first page of a paged
                    // listing, or one whose entries we could only partly read, is a fine thing to SHOW - every
                    // model in it is real - but it cannot support the inference in the other direction, that
                    // what is absent has been retired. Marking from it would report a live model as gone,
                    // which is the false alarm this feature was written to avoid. (fable review)
                    if (result.Complete && _owner.Service is { } service)
                    {
                        _owner.Suppressed(() => service.MarkListing(
                            Id, _fetched.Select(m => m.Id).ToList()));
                        Refresh();
                    }

                    // A fetch can succeed and still not be the whole listing, and the two ways that happens
                    // are different facts that must not share a sentence. An entry we could not read is a
                    // HOLE - the models around it are all here, one is malformed - while a walk that stopped
                    // early means there are more models we never saw. Telling the first reader "there may be
                    // more" sends them looking for models that do not exist; telling the second "one entry
                    // was unreadable" understates it. (#769, egret)
                    //
                    // Said at all because until now it was said only to the log: the count read "3 models"
                    // whether the provider offered three or offered nine and we understood three, and the
                    // reader who knew better had no way to tell which. (#769)
                    FetchProblem = result.Skipped switch
                    {
                        0 when result.Complete => null,
                        0 => $"This is part of {DisplayName}'s listing — it did not send the rest.",
                        1 => $"One entry in {DisplayName}'s listing could not be read, so it is not shown.",
                        var n => $"{n} entries in {DisplayName}'s listing could not be read, "
                                 + "so they are not shown.",
                    };
                }
                else
                {
                    // A cached listing is still on screen behind this. Say so, or the reader sees an error
                    // line above a full list of models and reasonably concludes the failure did not matter -
                    // they cannot tell "this is what the provider lists" from "this is what it listed some
                    // time ago and we could not reach it just now". Absence of a fresh answer must not read
                    // as confirmation of the old one.
                    //
                    // The wording is ModelsDevCatalog's, deliberately: it says exactly this about its own
                    // cached copy, and a fifth phrasing for the same situation helps nobody. (#790)
                    FetchProblem = _fetched.Count > 0
                        ? $"Couldn't reach {DisplayName}. Showing the models it listed last time."
                        : result.Problem;
                }

                // Asking a provider for its models IS contacting it, and the answer is the same fact a chat
                // turn establishes. Without this a reader who had just fetched four hundred models from
                // OpenRouter was still told the connection had never been checked - the app had contacted
                // the endpoint and thrown the knowledge away.
                if (result.Reachable is { } reachable)
                    _owner.Service?.ReportReachability(Id, reachable);
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
                this.RaisePropertyChanged(nameof(ShowDoc));
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

            // The listing is only in memory while this tab is open, so what the provider published has to be
            // written down at the moment of promotion - otherwise the per-turn picker (#693) has nothing to
            // show and no way to ask.
            var facts = Facts(modelId);

            _owner.Suppressed(() => service.EnableModel(Id, modelId, displayName, enabled, facts));
            Refresh();

            this.RaisePropertyChanged(nameof(CountText));
        }

        /// <summary>Re-reads the connection after a suppressed write. Without it the next genuine rebuild
        /// would rebuild from a record taken before the write and silently undo it on screen.</summary>
        private void Refresh()
        {
            if (_owner.Service?.Connections.FirstOrDefault(
                    c => string.Equals(c.Id, Id, StringComparison.Ordinal)) is { } fresh)
                _connection = fresh;
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
            bool enabled, bool missing = false)
        {
            _group = group;
            _published = published;
            _enabled = enabled;

            ModelId = id;
            DisplayName = displayName;
            Missing = missing;
        }

        public string ModelId { get; }

        public string DisplayName { get; }

        /// <summary>
        /// The provider's listing no longer carries this model. (#728)
        ///
        /// <para>Said in the row rather than left to be discovered as a 404 at send time. Marked, never
        /// removed or switched off: the reader put it there, a listing is not authority over their
        /// configuration, and a provider that publishes an incomplete one would otherwise delete valid
        /// entries on their behalf.</para>
        /// </summary>
        public bool Missing { get; }

        /// <summary>
        /// The provider's own facts about the model — the wire id, context window, price per million tokens,
        /// and whether it accepts a reasoning parameter.
        ///
        /// <para>Verbatim and attributed to the provider, which is what makes it safe where a table we
        /// maintained would not be. An endpoint that publishes nothing leaves the id alone, rather than
        /// demanding metadata that does not exist.</para>
        ///
        /// <para><b>Shown on hover rather than in the row.</b> It was a permanent second line, which at four
        /// hundred rows is four hundred lines of small grey text between the reader and the names they came
        /// to read. The facts matter when choosing between two models, which is a moment, not a state — so
        /// they are a tooltip, and the list stays a list of names.</para>
        /// </summary>
        public string Details
        {
            get
            {
                // What was published about a model the listing has dropped is no longer a description of
                // anything the provider offers. Showing a context window and a price for it would be the app
                // describing something that does not exist - the specific harm in #728 - so the row falls
                // back to the id.
                if (_published is null || Missing) return ModelId;

                var facts = new List<string>();

                if (_published.ContextLength is { } context)
                    facts.Add($"{context / 1000:N0}K context");

                if (_published.PromptPricePerMillion is { } prompt &&
                    _published.CompletionPricePerMillion is { } completion)
                    facts.Add(prompt == 0 && completion == 0
                        ? "free"
                        : $"${Money(prompt)}/${Money(completion)} per M");

                if (_published.SupportsReasoning == true) facts.Add("reasoning");

                // A separate fact from the one above, and the distinction is the whole point of #671's
                // correction: "reasoning" means the model RETURNS reasoning content, "effort" means it takes
                // the knob. 51% of the models that satisfy the first do not satisfy the second, so collapsing
                // them would tell the reader something the provider never said.
                if (_published.AcceptsReasoningEffort == true) facts.Add("effort");

                // The id on its own line: it is the string the reader would copy, and burying it in a run of
                // facts separated by dots makes it hard to pick out.
                return facts.Count == 0 ? ModelId : ModelId + "\n" + string.Join("  ·  ", facts);
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
