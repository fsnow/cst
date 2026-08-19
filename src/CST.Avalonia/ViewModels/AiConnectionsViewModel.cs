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
    /// The Providers tab of the AI settings: the endpoints a reader has configured, and a catalogue of named
    /// ones they could add. (#691)
    ///
    /// <para><b>Two stacked sections, not one list with an Add button.</b> "Your connections" is what is
    /// configured now; "Add a provider" is what could be added next, and a preset leaves the lower section the
    /// moment a connection using it exists. The screen this replaces could only ever report on whichever
    /// provider a dropdown happened to be showing.</para>
    ///
    /// <para><b>Everything goes through <see cref="IAiConnectionService"/>.</b> This view model never reads or
    /// writes <c>settings.json</c> and never touches the credential store — which also means it cannot
    /// accidentally key a credential by wire format, the bug in #678.</para>
    ///
    /// <para><b>Nothing here ranks, scores or recommends.</b> Both sections are ordered mechanically —
    /// connections in the order the reader added them, presets alphabetically by display name, which is the
    /// order the service hands them over in. There is no "recommended" badge and no pre-selection anywhere,
    /// because that is the model registry deleted in #670/#681 wearing different clothes.</para>
    /// </summary>
    public class AiConnectionsViewModel : ViewModelBase, IDisposable
    {
        private readonly IAiConnectionService? _service;
        private readonly IAiCredentialStore? _credentials;
        private AiConnectionEditorViewModel? _editor;
        private string? _problem;
        private string _presetSearch = "";
        private bool _isCatalogueExpanded;
        private int _catalogueTotal;

        public AiConnectionsViewModel(IAiConnectionService? service, IAiCredentialStore? credentials = null)
        {
            _service = service;
            _credentials = credentials;

            AddCustomCommand = ReactiveCommand.Create(AddCustom);
            RetryCatalogueCommand = ReactiveCommand.CreateFromTask(RetryCatalogueAsync);

            if (_service is not null)
            {
                _service.ConnectionsChanged += OnConnectionsChanged;
                Rebind();
            }
        }

        /// <summary>What is configured now, in the order the reader added it.</summary>
        public ObservableCollection<AiConnectionRowViewModel> Connections { get; } = new();

        /// <summary>
        /// Presets that need no key and no network — the local runners. Always shown, never collapsed, and
        /// still offered when the hosted catalogue is unreachable. (#739)
        ///
        /// <para><b>Not a "popular" section.</b> Its members are chosen by a fact — these work with no
        /// credential and no internet — rather than by anyone's view of which providers are worth having,
        /// which would be the ranking #670/#681 removed arriving as a layout decision.</para>
        /// </summary>
        public ObservableCollection<AiPresetRowViewModel> LocalPresets { get; } = new();

        /// <summary>
        /// Everything the catalogue offers, alphabetically. Collapsed until asked for, because ~166 rows
        /// above the fold is not a list anyone reads.
        /// </summary>
        public ObservableCollection<AiPresetRowViewModel> AvailablePresets { get; } = new();

        /// <summary>True while nothing is configured, so the empty state can say so rather than showing a
        /// blank area above a catalogue and leaving the reader to infer it.</summary>
        public bool HasNoConnections => Connections.Count == 0;

        public bool HasAvailablePresets => AvailablePresets.Count > 0;

        public bool HasLocalPresets => LocalPresets.Count > 0;

        /// <summary>
        /// Whether the hosted catalogue has anything in it at all, <b>before</b> the search filter.
        ///
        /// <para>The search box is gated on this rather than on the filtered count, and the distinction is
        /// not academic: gating on the filtered count meant that typing a string matching nothing hid the
        /// search box itself, mid-keystroke, leaving no control on screen able to clear the search. The
        /// catalogue was then gone for the life of the window.</para>
        /// </summary>
        public bool HasCatalogue => _catalogueTotal > 0;

        /// <summary>How many the catalogue offers — the filtered count while searching, so the number always
        /// describes the list beneath it.</summary>
        public string CatalogueCount => AvailablePresets.Count == 1
            ? "1 provider"
            : $"{AvailablePresets.Count} providers";

        /// <summary>A search that matched nothing says so. An empty bordered box is the "broken feature"
        /// reading this section exists to avoid.</summary>
        public bool HasNoMatches => HasCatalogue && AvailablePresets.Count == 0;

        /// <summary>
        /// Filters the catalogue. Searching also reveals it — a reader who types has asked for the list, and
        /// making them expand a section first would be a second gesture for one intention.
        /// </summary>
        public string PresetSearch
        {
            get => _presetSearch;
            set
            {
                this.RaiseAndSetIfChanged(ref _presetSearch, value);
                this.RaisePropertyChanged(nameof(IsCatalogueOpen));
                Rebind();
            }
        }

        public bool IsCatalogueExpanded
        {
            get => _isCatalogueExpanded;
            set
            {
                this.RaiseAndSetIfChanged(ref _isCatalogueExpanded, value);
                this.RaisePropertyChanged(nameof(IsCatalogueOpen));
            }
        }

        public bool IsCatalogueOpen => IsCatalogueExpanded || !string.IsNullOrWhiteSpace(PresetSearch);

        /// <summary>No attempt has finished yet — said quietly, because it is the ordinary first second of a
        /// fresh install rather than a problem.</summary>
        /// <summary>
        /// Said only while there is nothing to show.
        ///
        /// <para>The source seeds the built-in snapshot before any fetch finishes, so the state is
        /// <c>Loading</c> over a fully populated list — and a "looking for the provider list" line above 166
        /// rows contradicts itself. Worse, nothing ever initiates a fetch while the AI master switch is off,
        /// so that line would otherwise sit there permanently on a tab that is reachable with AI
        /// disabled.</para>
        /// </summary>
        public bool IsCatalogueLoading =>
            _service?.PresetState == AiPresetState.Loading && !HasCatalogue;

        /// <summary>
        /// The hosted catalogue is missing, and the reader is told so.
        ///
        /// <para>An empty section reads as a broken feature; a named failure with a retry reads as weather.
        /// The local runners and the custom route stay on screen throughout — they need no network, which
        /// makes them exactly the wrong thing to hide when the network is what failed.</para>
        /// </summary>
        public bool HasCatalogueProblem => _service?.PresetState == AiPresetState.Unavailable;

        /// <summary>
        /// The failure, worded for what is actually on screen.
        ///
        /// <para>A failed refresh keeps the previous list, so the service's sentence can end up above a
        /// catalogue announcing 166 providers — two statements that contradict each other. When something
        /// survived, this says so instead.</para>
        /// </summary>
        public string? CatalogueProblem => !HasCatalogueProblem
            ? null
            : HasCatalogue
                ? "Couldn't refresh the provider list — showing the built-in one."
                : _service?.PresetProblem;

        public ReactiveCommand<Unit, Unit> RetryCatalogueCommand { get; }

        /// <summary>
        /// The sheet, or null while the list is showing.
        ///
        /// <para>A sheet rather than an inline expansion, and it replaces the list rather than floating over
        /// it: the form needs room for a second step later (validate, then fetch models), and a full-pane
        /// swap gives it that without a scrim, a z-order, or a dialog window whose lifetime this view model
        /// would have to own.</para>
        /// </summary>
        public AiConnectionEditorViewModel? Editor
        {
            get => _editor;
            private set
            {
                this.RaiseAndSetIfChanged(ref _editor, value);
                this.RaisePropertyChanged(nameof(IsEditing));
                this.RaisePropertyChanged(nameof(IsListing));
            }
        }

        public bool IsEditing => Editor is not null;
        public bool IsListing => Editor is null;

        /// <summary>The service's own sentence when an operation was refused, shown verbatim. It already
        /// names the connection and the reason; restating it here in our own words could only lose one.</summary>
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

        public ReactiveCommand<Unit, Unit> AddCustomCommand { get; }

        /// <summary>Opens the full form for an endpoint that is in nobody's catalogue. Custom is first-class
        /// and always available: a preset must never be <i>required</i> to reach a provider.</summary>
        private void AddCustom()
        {
            if (_service is null) return;
            Problem = null;
            Editor = AiConnectionEditorViewModel.ForCustom(_service, _credentials, CloseEditor);
        }

        internal void BeginEdit(string id)
        {
            if (_service is null) return;
            var connection = _service.Connections.FirstOrDefault(
                c => string.Equals(c.Id, id, StringComparison.Ordinal));
            if (connection is null) return;

            Problem = null;
            Editor = AiConnectionEditorViewModel.ForExisting(_service, _credentials, connection, CloseEditor);
        }

        /// <summary>
        /// Opens the sheet for a named provider. <b>Always</b> the sheet, even for one that asks nothing.
        ///
        /// <para>The first cut added a key-less preset outright, reasoning that there was nothing to ask.
        /// Testing killed that twice over. The new row lands at the <i>top</i> of a page the reader has
        /// scrolled to the bottom of to reach the catalogue, so the click reads as having done nothing —
        /// and a provider added with neither a key nor a model id cannot answer a question, so the reader
        /// discovers the gap later and has to find Edit. OpenCode opens a sheet on every Connect.</para>
        /// </summary>
        internal void AddPreset(string presetId)
        {
            if (_service is null) return;
            Problem = null;

            var preset = _service.Presets.FirstOrDefault(
                p => string.Equals(p.Id, presetId, StringComparison.Ordinal));
            if (preset is null) return;

            Editor = AiConnectionEditorViewModel.ForPreset(_service, _credentials, preset, CloseEditor);
        }

        /// <summary>
        /// Deletes the connection and the models the reader typed into it.
        ///
        /// <para>Deliberately not paired with "stop billing me": those are two different intentions, and
        /// collapsing them is how a hand-entered model list gets destroyed by someone who only meant to remove
        /// a key. The narrower action needs a verb the service does not have yet — see the note on
        /// <see cref="AiConnectionRowViewModel.KeySourceBadge"/>.</para>
        /// </summary>
        internal void Delete(string id)
        {
            if (_service is null) return;
            var result = _service.Remove(id);
            Problem = result.Ok ? null : result.Problem;
        }

        /// <summary>
        /// Forgets one connection's key, leaving the connection and its models alone.
        ///
        /// <para>Reaches <see cref="IAiCredentialStore"/> directly, which is the one place this screen steps
        /// outside <see cref="IAiConnectionService"/>. The seam's reason for forbidding it was that the store
        /// was keyed by wire format, so the UI could not name a single connection's key without hitting
        /// another's — #678 removed that, and the store is now keyed by the very id this method is handed. It
        /// collapses to a one-line call the moment the service grows the verb.</para>
        /// </summary>
        internal void RemoveKey(string id)
        {
            if (_credentials is null) return;
            _credentials.DeleteApiKey(id);
            Rebind();
        }

        /// <summary>Matches on the display name and the id — a reader may know a provider by either.</summary>
        private bool MatchesSearch(AiProviderPreset preset)
        {
            // Trimmed: a trailing space is invisible and would otherwise match nothing at all, which for a
            // provider that plainly exists reads as the list being broken.
            var needle = PresetSearch.Trim();
            return needle.Length == 0 ||
                preset.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                preset.Id.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }

        private async Task RetryCatalogueAsync()
        {
            if (_service is null) return;
            await _service.RefreshPresetsAsync().ConfigureAwait(true);
            Rebind();
        }

        private void CloseEditor(bool saved)
        {
            Editor = null;
            if (saved) Rebind();
        }

        private void OnConnectionsChanged(object? sender, EventArgs e) => Rebind();

        /// <summary>
        /// Stops listening to the service.
        ///
        /// <para><b>Required, not tidiness.</b> The connection service is a singleton and a fresh Settings
        /// window builds a fresh view model every time it is opened, so without this each open leaves another
        /// subscriber alive — rebuilding collections nobody can see, on every connection change, for the rest
        /// of the session. The cost grows with how often the reader visits Settings, which for a screen whose
        /// whole job is being visited is the wrong direction.</para>
        /// </summary>
        public void Dispose()
        {
            if (_service is not null) _service.ConnectionsChanged -= OnConnectionsChanged;
        }

        /// <summary>
        /// Syncs both lists in place, keyed by id, rather than rebuilding them.
        ///
        /// <para><c>ConnectionsChanged</c> fires for state changes as well as add/remove — a reachability
        /// write-back moves one connection's <c>State</c> — so a wholesale rebuild would throw away scroll
        /// position and any focus in the list every time a probe returned. Updating the matching row is the
        /// difference between one row changing a word and the whole section flickering.</para>
        /// </summary>
        private void Rebind()
        {
            if (_service is null) return;

            Sync(Connections, _service.Connections,
                c => c.Id,
                r => r.Id,
                c => new AiConnectionRowViewModel(this, c),
                (row, c) => row.Update(c));

            // Split by a fact, not by an opinion: a local runner needs no key and no network, which is what
            // earns it a permanent place above a catalogue that may be neither present nor short.
            var local = AiProviderPresets.LocalOnly
                .Select(p => p.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);   // as the service matches ids

            // Counted BEFORE the search filter — see HasCatalogue.
            _catalogueTotal = _service.AvailablePresets.Count(p => !local.Contains(p.Id));

            Sync(LocalPresets, _service.AvailablePresets.Where(p => local.Contains(p.Id)).ToList(),
                p => p.Id,
                r => r.Id,
                p => new AiPresetRowViewModel(this, p),
                (row, p) => row.Update(p));

            Sync(AvailablePresets,
                _service.AvailablePresets
                    .Where(p => !local.Contains(p.Id))
                    .Where(MatchesSearch)
                    .ToList(),
                p => p.Id,
                r => r.Id,
                p => new AiPresetRowViewModel(this, p),
                (row, p) => row.Update(p));

            this.RaisePropertyChanged(nameof(HasNoConnections));
            this.RaisePropertyChanged(nameof(HasAvailablePresets));
            this.RaisePropertyChanged(nameof(HasLocalPresets));
            this.RaisePropertyChanged(nameof(CatalogueCount));
            this.RaisePropertyChanged(nameof(HasCatalogue));
            this.RaisePropertyChanged(nameof(HasNoMatches));
            this.RaisePropertyChanged(nameof(IsCatalogueLoading));
            this.RaisePropertyChanged(nameof(HasCatalogueProblem));
            this.RaisePropertyChanged(nameof(CatalogueProblem));
        }

        /// <summary>Makes <paramref name="rows"/> match <paramref name="source"/> by key, reusing rows that
        /// are still present so their bindings survive.</summary>
        private static void Sync<TRow, TSource>(
            ObservableCollection<TRow> rows,
            IReadOnlyList<TSource> source,
            Func<TSource, string> sourceKey,
            Func<TRow, string> rowKey,
            Func<TSource, TRow> create,
            Action<TRow, TSource> update)
        {
            for (int i = rows.Count - 1; i >= 0; i--)
                if (!source.Any(s => string.Equals(sourceKey(s), rowKey(rows[i]), StringComparison.Ordinal)))
                    rows.RemoveAt(i);

            for (int i = 0; i < source.Count; i++)
            {
                var key = sourceKey(source[i]);
                var existing = rows.FirstOrDefault(r => string.Equals(rowKey(r), key, StringComparison.Ordinal));

                if (existing is null)
                {
                    rows.Insert(Math.Min(i, rows.Count), create(source[i]));
                    continue;
                }

                update(existing, source[i]);

                var at = rows.IndexOf(existing);
                if (at != i) rows.Move(at, i);
            }
        }
    }

    /// <summary>One configured endpoint, as a row in "Your connections". (#691)</summary>
    public class AiConnectionRowViewModel : ViewModelBase
    {
        private readonly AiConnectionsViewModel _owner;
        private AiConnection _connection;
        private bool _isConfirmingDelete;

        public AiConnectionRowViewModel(AiConnectionsViewModel owner, AiConnection connection)
        {
            _owner = owner;
            _connection = connection;

            EditCommand = ReactiveCommand.Create(() => _owner.BeginEdit(Id));
            RemoveKeyCommand = ReactiveCommand.Create(() => _owner.RemoveKey(Id));
            DeleteCommand = ReactiveCommand.Create(() => { IsConfirmingDelete = true; });
            ConfirmDeleteCommand = ReactiveCommand.Create(() => _owner.Delete(Id));
            CancelDeleteCommand = ReactiveCommand.Create(() => { IsConfirmingDelete = false; });
        }

        public string Id => _connection.Id;

        public string DisplayName => _connection.DisplayName;

        /// <summary>
        /// The address, as a grey second line.
        ///
        /// <para>OpenCode shows the display name alone, which is opaque the moment a reader runs two local
        /// endpoints — two rows, two names they chose, and no way to tell which is on which port. The URL is
        /// already on screen in the editor and costs one line here.</para>
        /// </summary>
        public string Endpoint => _connection.ResolvedBaseUrl;

        public string Monogram => AiMonogram.For(DisplayName);

        public int MonogramTone => AiMonogram.ToneFor(Id);

        public int ModelCount => _connection.Models.Count;

        public string ModelSummary => ModelCount == 1 ? "1 model" : $"{ModelCount} models";

        /// <summary>
        /// Names where the credential came from, on <b>every</b> row.
        ///
        /// <para>OpenCode badges only the unusual row and overloads the slot across two different axes
        /// (credential source on one row, connection kind on another), which leaves the badge meaning nothing
        /// in particular. One axis, applied everywhere, is legible at a glance.</para>
        ///
        /// <para>"No key" is a legitimate resting state, not a warning: a local runner needs none, and a
        /// connection may authenticate entirely through its headers.</para>
        /// </summary>
        public string KeySourceBadge => _connection.KeySource switch
        {
            CredentialSource.Keychain => "Keychain",
            CredentialSource.Environment => "Environment",
            _ => "No key",
        };

        /// <summary>
        /// Whether this row offers to remove the stored key.
        ///
        /// <para>An environment-sourced credential gets an <b>empty action slot</b>, not a disabled button:
        /// the app cannot delete a credential it never stored, and a control there would promise something it
        /// cannot do. (A local "ignore this credential" flag is a separate affordance, and a better answer than
        /// the dead end OpenCode leaves — but it is not this.)</para>
        ///
        /// <para>False for a key we did not store, and false when there is none — in both cases the slot is
        /// simply empty. #678 made this reachable by filing keys under the connection's id.</para>
        /// </summary>
        public bool CanRemoveKey => _connection.KeySource == CredentialSource.Keychain;

        /// <summary>
        /// What we actually know about whether this works.
        ///
        /// <para><b>Never "Connected".</b> A configured endpoint is not a reachable one, and a settings page
        /// that claims otherwise is the screen a reader consults to diagnose the failure it is lying about —
        /// observed in OpenCode, where the assistant reported "cannot connect" while settings went on saying
        /// Connected. "Not checked yet" is honest and costs nothing.</para>
        /// </summary>
        public string StatusText => _connection.State switch
        {
            Reachability.Reachable => "Reachable",
            Reachability.Unreachable => "Not responding",
            _ => "Not checked yet",
        };

        /// <summary>True when the base URL still has an unanswered <c>{placeholder}</c> in it — an Azure
        /// connection with no resource name. Said here rather than discovered later as a DNS failure that
        /// names nothing.</summary>
        public bool IsIncomplete => _connection.IsIncomplete;

        public string IncompleteText =>
            "Not usable yet — still needs: " +
            string.Join(", ", AiTemplate.PlaceholdersIn(_connection.ResolvedBaseUrl));

        /// <summary>
        /// Whether this row is asking before it destroys anything.
        ///
        /// <para>Delete takes the connection's hand-typed model list with it, which is real user-authored work
        /// — the reason #691 wants "remove key" and "delete connection" kept apart in the first place. An
        /// inline second click is the cheapest honest guard: it needs no dialog, it is undoable by looking
        /// away, and the reversibility question is the one a mockup never asks.</para>
        /// </summary>
        public bool IsConfirmingDelete
        {
            get => _isConfirmingDelete;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isConfirmingDelete, value);
                this.RaisePropertyChanged(nameof(IsNotConfirmingDelete));
            }
        }

        public bool IsNotConfirmingDelete => !IsConfirmingDelete;

        /// <summary>Names what is about to go, because "3 models" is the part the reader would not think to
        /// check before clicking.</summary>
        public string DeleteConfirmText => ModelCount == 0
            ? $"Delete {DisplayName}?"
            : $"Delete {DisplayName} and its {ModelSummary}?";

        public ReactiveCommand<Unit, Unit> EditCommand { get; }

        public ReactiveCommand<Unit, Unit> RemoveKeyCommand { get; }

        public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

        public ReactiveCommand<Unit, Unit> ConfirmDeleteCommand { get; }

        public ReactiveCommand<Unit, Unit> CancelDeleteCommand { get; }

        internal void Update(AiConnection connection)
        {
            _connection = connection;
            this.RaisePropertyChanged(nameof(DisplayName));
            this.RaisePropertyChanged(nameof(Endpoint));
            this.RaisePropertyChanged(nameof(Monogram));
            this.RaisePropertyChanged(nameof(MonogramTone));
            this.RaisePropertyChanged(nameof(ModelCount));
            this.RaisePropertyChanged(nameof(ModelSummary));
            this.RaisePropertyChanged(nameof(KeySourceBadge));
            this.RaisePropertyChanged(nameof(CanRemoveKey));
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(IsIncomplete));
            this.RaisePropertyChanged(nameof(IncompleteText));
            this.RaisePropertyChanged(nameof(DeleteConfirmText));
        }
    }

    /// <summary>One named endpoint in "Add a provider". (#691)</summary>
    public class AiPresetRowViewModel : ViewModelBase
    {
        private readonly AiConnectionsViewModel _owner;
        private AiProviderPreset _preset;

        public AiPresetRowViewModel(AiConnectionsViewModel owner, AiProviderPreset preset)
        {
            _owner = owner;
            _preset = preset;

            AddCommand = ReactiveCommand.Create(() => _owner.AddPreset(Id));
        }

        public string Id => _preset.Id;

        public string DisplayName => _preset.DisplayName;

        public string Monogram => AiMonogram.For(DisplayName);

        public int MonogramTone => AiMonogram.ToneFor(Id);

        /// <summary>
        /// The only thing a row says beyond the provider's name.
        ///
        /// <para>Deliberately not a description. OpenCode's catalogue carries vendor blurbs — "GPT models for
        /// fast, capable general AI tasks", "Unified access to AI models with smart routing" — which is
        /// marketing copy presented as product information, and any line we wrote ourselves would be an
        /// opinion about a provider. A preset carries a base URL and how it authenticates; that is all it
        /// knows, so that is all this says.</para>
        /// </summary>
        public string RequirementText => _preset.RequiresKey ? "Needs an API key" : "No key needed";

        public ReactiveCommand<Unit, Unit> AddCommand { get; }

        internal void Update(AiProviderPreset preset)
        {
            _preset = preset;
            this.RaisePropertyChanged(nameof(DisplayName));
            this.RaisePropertyChanged(nameof(Monogram));
            this.RaisePropertyChanged(nameof(MonogramTone));
            this.RaisePropertyChanged(nameof(RequirementText));
        }
    }

    /// <summary>
    /// Stand-in provider marks: a letter on a coloured tile. (#691)
    ///
    /// <para>Real vendor logos are wanted here and are coming as their own change — 25 marks, each with its
    /// own licence terms and several needing a separate dark-mode asset, which is a sourcing exercise rather
    /// than a UI one. Until then a tile still does the job a logo does on this screen: give the eye something
    /// other than left-aligned text to find a row by.</para>
    ///
    /// <para>The tone is a hash of the id, so it is stable across runs and carries no meaning — deliberately,
    /// since a colour that meant something would be a judgment about a provider.</para>
    /// </summary>
    internal static class AiMonogram
    {
        /// <summary>How many tones the view defines. Kept here so the two cannot drift apart silently.</summary>
        internal const int ToneCount = 6;

        internal static string For(string displayName)
        {
            foreach (var c in displayName)
                if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
            return "?";
        }

        internal static int ToneFor(string id)
        {
            // FNV-1a, so the tone is a pure function of the id rather than of this process's string hashing,
            // which is randomised per run and would repaint the list on every launch.
            uint hash = 2166136261;
            foreach (var c in id)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (int)(hash % ToneCount);
        }
    }
}
