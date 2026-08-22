using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Threading;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Services.Ai.Credentials;
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
        private readonly IAiProviderLogos? _logos;
        private readonly IAiEnvironmentKeys? _environmentKeys;
        private readonly IShellEnvironment? _shellEnvironment;
        private AiConnectionEditorViewModel? _editor;
        private bool _disposed;
        private string? _problem;
        private string _presetSearch = "";
        private int _catalogueTotal;

        public AiConnectionsViewModel(
            IAiConnectionService? service,
            IAiCredentialStore? credentials = null,
            IAiProviderLogos? logos = null,
            IAiEnvironmentKeys? environmentKeys = null,
            IShellEnvironment? shellEnvironment = null)
        {
            _service = service;
            _credentials = credentials;
            _logos = logos;
            _environmentKeys = environmentKeys;
            _shellEnvironment = shellEnvironment;

            AddCustomCommand = ReactiveCommand.Create(AddCustom);
            RetryCatalogueCommand = ReactiveCommand.CreateFromTask(RetryCatalogueAsync);

            if (_service is not null)
            {
                _service.ConnectionsChanged += OnConnectionsChanged;
                Rebind();
            }

            // This tab IS the discovery surface, so opening it is the strongest signal that the probe is
            // wanted — a reader who has just enabled AI has never been through the startup gate. Prime is
            // idempotent, so the common case where startup already primed costs a field read. (#817)
            if (_shellEnvironment is not null)
            {
                _shellEnvironment.Prime();

                // Progressive disclosure, the same shape the catalogue already uses: rows appear when the
                // probe lands rather than the window waiting for it. Fire-and-forget by design — Completion
                // never faults, and there is nothing to report if it finds nothing.
                _shellEnvironment.Completion.ContinueWith(
                    _ => Dispatcher.UIThread.Post(() => { if (!_disposed) Rebind(); }),
                    TaskScheduler.Default);
            }
        }

        /// <summary>The logo resolver the rows use, or null when logos are unavailable — in tests, and
        /// wherever this view model is constructed without one. (#738)</summary>
        internal IAiProviderLogos? Logos => _logos;

        internal IAiConnectionService? Service => _service;

        /// <summary>What is configured now, in the order the reader added it.</summary>
        public ObservableCollection<AiConnectionRowViewModel> Connections { get; } = new();

        /// <summary>
        /// Every provider that can still be added, alphabetically, in one list.
        ///
        /// <para>It was two: local runners pinned above a catalogue collapsed behind a count. Both were
        /// wrong in use. The collapse made a populated list look like a broken one — the reader saw nothing
        /// until they typed — and the pinned section gave three providers a permanent position above a
        /// hundred and sixty others, which is prominence however mechanically it was justified.</para>
        ///
        /// <para>One alphabetical list ranks nothing and needs no explanation. Search filters it; the custom
        /// endpoint sits at the end, where OpenCode puts it, because it is the generic case rather than a
        /// provider.</para>
        /// </summary>
        public ObservableCollection<AiPresetRowViewModel> AvailablePresets { get; } = new();

        /// <summary>
        /// Providers this machine already holds a key for, and has not been told to use. (#714)
        ///
        /// <para><b>Its own section rather than a note on a catalogue row.</b> The catalogue is a search box
        /// over more than a hundred and sixty providers, so a line on a row is only ever read by someone who
        /// already went looking — which is precisely not the reader this exists for: the one who does not
        /// know the variable is set. That reader was the case in view. Above the search box, it is seen
        /// without being asked for.</para>
        ///
        /// <para><b>And nothing more than a list with a button.</b> No banner, no badge, no dismissal. There
        /// is nothing to decline because nothing is being pressed on the reader: it states what is on their
        /// machine and offers to use it, on a settings tab they opened deliberately. A "not now" would need
        /// somewhere to be stored, a way back, and an answer to what happens when the variable changes — all
        /// to suppress a two-line block nobody has to act on.</para>
        ///
        /// <para>Drawn from <see cref="IAiConnectionService.AvailablePresets"/>, so a provider the reader has
        /// already configured never appears here — whatever their environment holds, that decision is made.
        /// </para>
        /// </summary>
        public ObservableCollection<AiEnvironmentRowViewModel> FoundKeys { get; } = new();

        public bool HasFoundKeys => FoundKeys.Count > 0;

        /// <summary>True while nothing is configured, so the empty state can say so rather than showing a
        /// blank area above a catalogue and leaving the reader to infer it.</summary>
        public bool HasNoConnections => Connections.Count == 0;

        public bool HasAvailablePresets => AvailablePresets.Count > 0;

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
                Rebind();
            }
        }

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

        /// <summary>
        /// The generic mark, for the add-a-provider list's custom row. (#740)
        ///
        /// <para>That row is not a preset and has no view model of its own — it is a fixed row in the view —
        /// so it cannot inherit the fallback the others get. Without this it was the one row in a list of
        /// icons showing a plus sign in a coloured tile.</para>
        ///
        /// <para>Null when the bundled file cannot be written, which the view answers by keeping the
        /// tile.</para>
        /// </summary>
        public string? GenericLogoPath => GenericModelIcon.Path();

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
        private AiProviderPreset? PresetFor(string presetId) =>
            // OrdinalIgnoreCase, matching the service's one rule for connection ids. Harmless here today,
            // since these ids round-trip from the same objects — but a second rule in this file is exactly
            // what #805 is about. (fable review)
            _service?.Presets.FirstOrDefault(p => string.Equals(p.Id, presetId, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Adopts the key the environment holds for this provider. (#714)
        ///
        /// <para><b>One click where one click is honest, a sheet where it is not.</b> A catalogue preset needs
        /// nothing beyond the reader's consent, so pressing the button that names the variable is the whole
        /// act. Azure and Cloudflare declare environment variables AND need a prompt answered — a resource
        /// name, an account id — so adopting them in one click is not possible; those open the ordinary sheet
        /// carrying the choice already made, rather than asking for it twice.</para>
        /// </summary>
        internal void UseEnvironmentKey(string presetId, string variableName)
        {
            if (_service is null) return;
            Problem = null;

            if (PresetFor(presetId) is not { } preset) return;

            if (preset.Prompts is { Count: > 0 })
            {
                Editor = AiConnectionEditorViewModel.ForPreset(
                    _service, _credentials, preset, CloseEditor, adoptEnvironmentKey: variableName);
                return;
            }

            // The variable NAME the row displayed, not one re-derived from the preset: it is the one the
            // reader consented to, and it is what the connection records. (#714, fable on #813)
            var result = _service.AddFromPreset(
                presetId, new Dictionary<string, string>(), environmentVariable: variableName);
            Problem = result.Ok ? null : result.Problem;
        }

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
            _credentials.Delete(id, AiCredentialNames.Primary);
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

        /// <summary>
        /// Hands a documentation link to the operating system.
        ///
        /// <para>Guarded on the scheme: the URL comes from a fetched catalogue, and handing an arbitrary
        /// string to the shell is how a data file becomes a way to run something. http and https only.</para>
        /// </summary>
        internal static void OpenUrl(string? url)
        {
            if (!ShouldOpen(url, out var uri)) return;

            try
            {
                Process.Start(new ProcessStartInfo(uri!.AbsoluteUri) { UseShellExecute = true });
            }
            catch
            {
                // A browser that will not open is not worth taking the settings window down for.
            }
        }

        /// <summary>
        /// Whether this is a link we are willing to hand to the operating system.
        ///
        /// <para>Separated from the launching so it can be asserted directly. A test over
        /// <see cref="OpenUrl"/> can only observe that nothing was thrown — so if the scheme check were ever
        /// deleted, that test would pass while actually shell-opening <c>file:///etc/passwd</c> on whoever ran
        /// it. (fable review)</para>
        /// </summary>
        internal static bool ShouldOpen(string? url, out Uri? uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return false;
            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;

            uri = parsed;
            return true;
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
            // Set before unsubscribing: the shell probe's continuation is scheduled on the pool and may
            // already be on its way to the UI thread, and rebinding a disposed tab is how a closed Settings
            // window comes back to life holding a dead service. (#817)
            _disposed = true;
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

            // Counted BEFORE the search filter — see HasCatalogue.
            _catalogueTotal = _service.AvailablePresets.Count;

            Sync(AvailablePresets,
                _service.AvailablePresets
                    .Where(MatchesSearch)
                    .ToList(),
                p => p.Id,
                r => r.Id,
                p => new AiPresetRowViewModel(this, p),
                (row, p) => row.Update(p));

            // Deliberately NOT filtered by the search box. The search is for finding a provider in the
            // catalogue; this section answers a question the reader has not asked yet, and hiding it the
            // moment they type would take it away exactly when they are looking for the provider it is
            // about. (#714)
            //
            // Re-read on every rebind rather than cached — with one honest qualification. The process
            // environment is genuinely re-read here, so a variable set or unset with `launchctl setenv`, or
            // inherited from a terminal launch, behaves exactly as this always claimed.
            //
            // The shell snapshot (#817) is read once per launch, because reading it costs a login shell and
            // Rebind runs on every keystroke in the search box. That is not a new staleness: the process
            // environment is itself a snapshot taken at exec, so editing ~/.zshrc has never affected a
            // running instance either. A profile edit takes effect at next launch, and the docs say so.
            var found = _environmentKeys is null
                ? new List<AiEnvironmentKey>()
                : _environmentKeys.Discover(_service.AvailablePresets).ToList();

            Sync(FoundKeys, found,
                k => k.PresetId,
                r => r.Id,
                k => new AiEnvironmentRowViewModel(this, k, PresetFor(k.PresetId)),
                (row, k) => row.Update(k, PresetFor(k.PresetId)));

            this.RaisePropertyChanged(nameof(HasFoundKeys));
            this.RaisePropertyChanged(nameof(HasNoConnections));
            this.RaisePropertyChanged(nameof(HasAvailablePresets));
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

    /// <summary>
    /// The logo half of a row. (#738)
    ///
    /// <para>Shared by both row kinds because they want identical behaviour: start on the monogram, ask for a
    /// logo once, and switch only if one arrives. Nothing here decides how a logo is drawn — size, shape and
    /// placement are the view's.</para>
    /// </summary>
    public abstract class AiLogoRowViewModel : ViewModelBase
    {
        private string? _logoPath;
        private bool _asked;

        /// <summary>The models.dev provider id, or null for a row that has no provider behind it — a custom
        /// endpoint, where the monogram is the only honest mark.</summary>
        protected abstract string? ProviderId { get; }

        /// <summary>A cached SVG on disk, or null while none is known. Null is the ordinary case, not a
        /// failure: it means <see cref="Monogram"/> is what to draw.</summary>
        public string? LogoPath
        {
            get => _logoPath;
            private set
            {
                this.RaiseAndSetIfChanged(ref _logoPath, value);
                this.RaisePropertyChanged(nameof(HasLogo));
            }
        }

        public bool HasLogo => !string.IsNullOrEmpty(LogoPath);

        public abstract string Monogram { get; }

        public abstract int MonogramTone { get; }

        /// <summary>The in-flight lookup, so a test can await what the constructor started.</summary>
        internal Task? LogoLoad { get; private set; }

        /// <summary>
        /// Asks for the logo, once per row.
        ///
        /// <para>Started and not awaited on purpose: a row must draw immediately with its monogram rather than
        /// wait on a network call, and the logo replacing it a moment later is the intended sequence. Awaiting
        /// it would put a settings pane behind an HTTP request.</para>
        /// </summary>
        protected void LoadLogo(IAiProviderLogos? logos)
        {
            if (_asked || logos is null) return;
            _asked = true;

            var id = ProviderId;

            // A custom endpoint has no provider id, so there is nothing to ask for - but it still gets the
            // generic mark rather than being the one row in a list of icons that shows a letter.
            if (string.IsNullOrWhiteSpace(id))
            {
                LogoPath = GenericModelIcon.Path();
                return;
            }

            LogoLoad = ApplyLogoAsync(logos, id!);
        }

        private async Task ApplyLogoAsync(IAiProviderLogos logos, string id)
        {
            // Null means models.dev has no mark for this provider - every local runner, and anything it does
            // not carry. The generic sparkle is the answer there, not a letter tile. (#740)
            var path = await logos.GetLogoPathAsync(id) ?? GenericModelIcon.Path();
            if (path is null) return;   // could not even write the bundled one: keep the monogram

            // Same shape as the service's change event: set directly when already on the UI thread, post when
            // the fetch resumed on a pool thread. A property change raised off-thread reaches a binding.
            if (Dispatcher.UIThread.CheckAccess()) LogoPath = path;
            else Dispatcher.UIThread.Post(() => LogoPath = path);
        }
    }

    /// <summary>One configured endpoint, as a row in "Your connections". (#691)</summary>
    public class AiConnectionRowViewModel : AiLogoRowViewModel
    {
        private readonly AiConnectionsViewModel _owner;
        private AiConnection _connection;
        private bool _isConfirmingDelete;

        public AiConnectionRowViewModel(AiConnectionsViewModel owner, AiConnection connection)
        {
            _owner = owner;
            _connection = connection;

            EditLabel = owner.Service is null
                ? "Edit"
                : AiConnectionEditorViewModel.EditAction(owner.Service, connection);

            EditCommand = ReactiveCommand.Create(() => _owner.BeginEdit(Id));
            OpenDocCommand = ReactiveCommand.Create(() => AiConnectionsViewModel.OpenUrl(DocUrl));
            RemoveKeyCommand = ReactiveCommand.Create(() => _owner.RemoveKey(Id));
            DeleteCommand = ReactiveCommand.Create(() => { IsConfirmingDelete = true; });
            ConfirmDeleteCommand = ReactiveCommand.Create(() => _owner.Delete(Id));
            CancelDeleteCommand = ReactiveCommand.Create(() => { IsConfirmingDelete = false; });

            LoadLogo(owner.Logos);
        }

        /// <summary>
        /// The provider this connection was added from, recorded at creation since #766.
        ///
        /// <para>This was the second guess of the same shape the id match was: the comment here used to
        /// concede that "the connection does not record which preset it came from, and guessing from a renamed
        /// id would show one provider's mark on another's row". It records it now, so the mark follows the
        /// provider rather than the slug — a connection renamed by its reader keeps the right logo, and a
        /// custom endpoint whose slug the catalogue later grew into does not acquire one.</para>
        ///
        /// <para>Falls back to the id for a settings file written before that, where it is the same answer for
        /// every connection such a file can hold. A custom endpoint still gets its monogram.</para>
        /// </summary>
        protected override string? ProviderId => _connection.PresetId switch
        {
            // Added from the provider list, and we know which entry. The mark follows the provider, so a
            // connection the reader renamed keeps it.
            { Length: > 0 } preset => preset,

            // Recorded as a custom endpoint. No provider mark, and no lookup attempted: we KNOW there is no
            // provider behind it, so trying the id would be the guess this issue removes. The monogram is the
            // right answer and it is the answer we can give without asking anything. (#766)
            { } => null,

            // Nothing recorded — a file older than the field. The id is the same answer it always was, and
            // right for every connection such a file can hold.
            null => _connection.Id,
        };

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

        public override string Monogram => AiMonogram.For(DisplayName);

        public override int MonogramTone => AiMonogram.ToneFor(Id);

        public int ModelCount => _connection.Models.Count;

        public string ModelSummary => ModelCount == 1 ? "1 model" : $"{ModelCount} models";

        /// <summary>
        /// The address, on hover.
        ///
        /// <para>It was a permanent second line, and against a row that already carried a badge, a status, a
        /// count and four buttons it was the thing making the list hard to read. The reason for showing it
        /// stands — two local runners are two names the reader chose and nothing else tells them apart — so it
        /// moves to the tooltip rather than going away. Same trade as the model metadata on the Models
        /// tab.</para>
        /// </summary>
        public string RowTooltip => $"{DisplayName}\n{Endpoint}";

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
        /// The provider's own documentation, where the catalogue publishes one.
        ///
        /// <para>In practice a <b>models</b> page rather than an account page — nine of ten sampled point at a
        /// list of model ids. So it is for a reader with a working connection who needs to know what to run on
        /// it, not for one who cannot find their key: anyone who has pasted a key has already been to the
        /// provider. Null for a custom endpoint, which has no provider identity, and for the local runners,
        /// which the catalogue does not carry.</para>
        /// </summary>
        public string? DocUrl => AiProviderPresets.ById(_connection.Id)?.Doc;

        public bool HasDoc => !string.IsNullOrEmpty(DocUrl);

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
        /// <summary>
        /// What we actually know about whether this works.
        ///
        /// <para><b>No longer shown on the row.</b> Every connection reads "Not checked yet" until something
        /// contacts it, so in practice the line said the same thing on every row and carried no information
        /// at all — reported from use. Kept because the wording is the honest one and the per-turn picker
        /// still marks an unreachable connection, which is the case that was worth saying.</para>
        ///
        /// <para><b>Never "Connected".</b> A configured endpoint is not a reachable one, and the screen a
        /// reader consults to diagnose a failure must not be the one lying about it (#673).</para>
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

        /// <summary>What the edit button says, or null where the sheet would be empty and the button is not
        /// drawn at all. (<see cref="AiConnectionEditorViewModel.EditAction"/>)</summary>
        public string? EditLabel { get; }

        public bool CanEdit => EditLabel is not null;

        public ReactiveCommand<Unit, Unit> EditCommand { get; }

        public ReactiveCommand<Unit, Unit> OpenDocCommand { get; }

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
            this.RaisePropertyChanged(nameof(RowTooltip));
            this.RaisePropertyChanged(nameof(KeySourceBadge));
            this.RaisePropertyChanged(nameof(CanRemoveKey));
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(IsIncomplete));
            this.RaisePropertyChanged(nameof(IncompleteText));
            this.RaisePropertyChanged(nameof(DeleteConfirmText));
        }
    }

    /// <summary>One named endpoint in "Add a provider". (#691)</summary>
    public class AiPresetRowViewModel : AiLogoRowViewModel
    {
        private readonly AiConnectionsViewModel _owner;
        private AiProviderPreset _preset;

        public AiPresetRowViewModel(AiConnectionsViewModel owner, AiProviderPreset preset)
        {
            _owner = owner;
            _preset = preset;

            AddCommand = ReactiveCommand.Create(() => _owner.AddPreset(Id));

            LoadLogo(owner.Logos);
        }

        public string Id => _preset.Id;

        /// <summary>Always a real provider id: the catalogue is built from models.dev.</summary>
        protected override string? ProviderId => _preset.Id;

        public string DisplayName => _preset.DisplayName;

        public override string Monogram => AiMonogram.For(DisplayName);

        public override int MonogramTone => AiMonogram.ToneFor(Id);

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
    /// One provider whose API key is already sitting in this machine's environment. (#714)
    ///
    /// <para><b>The variable's NAME, never its value.</b> The value is not carried into this object, not
    /// rendered, and not logged: the name is the whole of what a reader needs in order to recognise the key
    /// — or to realise they had forgotten it was set, which is the case this feature exists for.</para>
    /// </summary>
    public class AiEnvironmentRowViewModel : AiLogoRowViewModel
    {
        private readonly AiConnectionsViewModel _owner;
        private AiEnvironmentKey _key;
        private AiProviderPreset? _preset;

        public AiEnvironmentRowViewModel(
            AiConnectionsViewModel owner, AiEnvironmentKey key, AiProviderPreset? preset)
        {
            _owner = owner;
            _key = key;
            _preset = preset;

            UseCommand = ReactiveCommand.Create(() => _owner.UseEnvironmentKey(Id, VariableName));

            LoadLogo(owner.Logos);
        }

        public string Id => _key.PresetId;

        protected override string? ProviderId => _key.PresetId;

        public string VariableName => _key.VariableName;

        /// <summary>The provider's own name where the catalogue is loaded, and the id otherwise — a row that
        /// says "openai" is still a row the reader can act on, where no row at all is not.</summary>
        public string DisplayName => _preset?.DisplayName ?? _key.PresetId;

        public override string Monogram => AiMonogram.For(DisplayName);

        public override int MonogramTone => AiMonogram.ToneFor(Id);

        /// <summary>
        /// What the row says beneath the provider's name.
        ///
        /// <para>A statement of fact rather than a recommendation. It does not say the key is valid — nothing
        /// here has tried it — only that the variable is set, which is the one thing that has been
        /// established.</para>
        /// </summary>
        public string VariableText => $"Key in {VariableName}";

        public ReactiveCommand<Unit, Unit> UseCommand { get; }

        internal void Update(AiEnvironmentKey key, AiProviderPreset? preset)
        {
            _key = key;
            _preset = preset;
            this.RaisePropertyChanged(nameof(VariableName));
            this.RaisePropertyChanged(nameof(VariableText));
            this.RaisePropertyChanged(nameof(DisplayName));
            this.RaisePropertyChanged(nameof(Monogram));
            this.RaisePropertyChanged(nameof(MonogramTone));
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
