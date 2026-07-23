using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Dictionaries;
using CST.Avalonia.ViewModels.Dock;
using CST.Conversion;
using CST.Tools;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace CST.Avalonia.ViewModels;

/// <summary>
/// The Pāli dictionary tool (#25, #466). A dockable left-tool panel: pick a SOURCE (a specific dictionary —
/// Childers, DPD, Pāli–Hindi, DPPN — not a language, since sources can share one), type a headword in any
/// script, pick from the matching Words, and read the definition. Sources come from the shared
/// <see cref="DictionarySourceRegistry"/>, the same set the /v1 API serves, so UI and API can't drift.
/// </summary>
public class DictionaryViewModel : ReactiveTool, IDisposable
{
    private readonly DictionarySourceRegistry _registry;
    private readonly IScriptService _scriptService;
    private readonly IFontService _fontService;
    private readonly IApplicationStateService _stateService;
    private readonly ILogger<DictionaryViewModel> _logger;
    private readonly CompositeDisposable _disposables = new();

    // Live script/font-change handlers, so the panel re-renders in the new script/font (DICT-2).
    private Action<Script>? _scriptChangedHandler;
    private EventHandler? _fontChangedHandler;

    // <see>-link navigation history (manual typing does NOT push history; only followed links do).
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();

    private string _searchText = "";
    private string _selectedLanguage = "en";
    private DictionaryEntryViewModel? _selectedWord;
    private IReadOnlyList<MeaningSegment> _meaningSegments = Array.Empty<MeaningSegment>();
    private bool _canGoBack;
    private bool _canGoForward;

    public DictionaryViewModel(
        DictionarySourceRegistry registry,
        IScriptService scriptService,
        IFontService fontService,
        IApplicationStateService stateService,
        ILogger<DictionaryViewModel> logger)
    {
        _registry = registry;
        _scriptService = scriptService;
        _fontService = fontService;
        _stateService = stateService;
        _logger = logger;

        Id = "DictionaryTool";
        Title = "Dictionary";
        CanPin = false;
        CanClose = false;
        CanFloat = true;    // floatable/moveable like the other tools
        CanDrag = true;

        // The picker's items are SOURCE ids from the shared registry (Stage 2 will show DisplayName instead).
        // Only installed sources appear; a fresh install with no derived asset degrades cleanly to en/hi.
        var available = _registry.Available.Select(s => s.Id).ToList();
        AvailableLanguages = available.Count > 0 ? available : new[] { "en" };
        // Restore the preferred SOURCE (#466), migrating from the older Language key; fall back to en, then
        // whatever's available.
        var savedSource = _stateService.Current.DictionaryDialog.SourceId;
        if (string.IsNullOrEmpty(savedSource))
            savedSource = _stateService.Current.DictionaryDialog.Language;   // migrate #25 → #466
        _selectedLanguage = AvailableLanguages.Contains(savedSource) ? savedSource
            : AvailableLanguages.Contains("en") ? "en"
            : AvailableLanguages[0];

        Words = new ObservableCollection<DictionaryEntryViewModel>();

        BackCommand = ReactiveCommand.Create(GoBack, this.WhenAnyValue(x => x.CanGoBack));
        ForwardCommand = ReactiveCommand.Create(GoForward, this.WhenAnyValue(x => x.CanGoForward));
        NavigateToWordCommand = ReactiveCommand.Create<string>(NavigateToWord);

        // Real-time lookup as the query or language changes (throttled, like the Search tool). The
        // throttle fires off the UI thread; LookupAsync marshals its collection updates back.
        this.WhenAnyValue(x => x.SearchText, x => x.SelectedLanguage)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .Subscribe(pair => { _ = LookupAsync(SearchText); })
            .DisposeWith(_disposables);

        // Rebuild the meaning display whenever the selected headword changes.
        this.WhenAnyValue(x => x.SelectedWord)
            .Subscribe(_ => UpdateMeaning())
            .DisposeWith(_disposables);

        // Remember the preferred definition language across sessions. Skip(1) ignores the initial value
        // set above so construction doesn't write state. (#25)
        this.WhenAnyValue(x => x.SelectedLanguage)
            .Skip(1)
            .Subscribe(sourceId =>
            {
                _stateService.Current.DictionaryDialog.SourceId = sourceId;
                _stateService.MarkDirty();   // persist via the timer/shutdown save, not a full off-thread save per change (STATE-2)
            })
            .DisposeWith(_disposables);

        // Re-render headwords + meaning when the global script or font settings change (mirrors the
        // Search tool). Unsubscribed in Dispose. (DICT-2)
        _scriptChangedHandler = script => Dispatcher.UIThread.Post(() =>
        {
            this.RaisePropertyChanged(nameof(CurrentScriptFontFamily));
            this.RaisePropertyChanged(nameof(CurrentScriptFontSize));
            _ = LookupAsync(SearchText);   // rebuild DisplayWords + <see> links in the new script
        });
        _scriptService.ScriptChanged += _scriptChangedHandler;

        _fontChangedHandler = (_, _) => Dispatcher.UIThread.Post(() =>
        {
            this.RaisePropertyChanged(nameof(CurrentScriptFontFamily));
            this.RaisePropertyChanged(nameof(CurrentScriptFontSize));
        });
        _fontService.FontSettingsChanged += _fontChangedHandler;
    }

    public IReadOnlyList<string> AvailableLanguages { get; }

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set => this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    public ObservableCollection<DictionaryEntryViewModel> Words { get; }

    public DictionaryEntryViewModel? SelectedWord
    {
        get => _selectedWord;
        set => this.RaiseAndSetIfChanged(ref _selectedWord, value);
    }

    /// <summary>The selected definition, split into plain-text and clickable link segments.</summary>
    public IReadOnlyList<MeaningSegment> MeaningSegments
    {
        get => _meaningSegments;
        private set => this.RaiseAndSetIfChanged(ref _meaningSegments, value);
    }

    public bool CanGoBack
    {
        get => _canGoBack;
        private set => this.RaiseAndSetIfChanged(ref _canGoBack, value);
    }

    public bool CanGoForward
    {
        get => _canGoForward;
        private set => this.RaiseAndSetIfChanged(ref _canGoForward, value);
    }

    // Pāli headwords display in the user's current script + font (like the Search tool).
    public string CurrentScriptFontFamily => _fontService.GetScriptFontFamily(_scriptService.CurrentScript) ?? "Helvetica";
    public int CurrentScriptFontSize => _fontService.GetScriptFontSize(_scriptService.CurrentScript);

    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> ForwardCommand { get; }
    public ReactiveCommand<string, Unit> NavigateToWordCommand { get; }

    private async Task LookupAsync(string query)
    {
        var sourceId = SelectedLanguage;   // capture: results are only valid if these still hold on completion
        try
        {
            var source = _registry.ById(sourceId);
            IReadOnlyList<DictionaryEntry> results = source == null
                ? Array.Empty<DictionaryEntry>()
                // The source returns Headword already in the requested output script and the definition as
                // MeaningHtml (for flat-file, the same <see>-tagged text the native renderer handles). (#466)
                : await source.LookupAsync(new DictionaryRequest(sourceId, query ?? "", _scriptService.CurrentScript, 500));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Ignore a stale completion: a newer query/source has superseded this lookup, so its results
                // must not clobber the current ones (e.g. a slow first-time load finishing after a fast
                // cached lookup). (DICT-3)
                if (query != SearchText || sourceId != SelectedLanguage)
                    return;

                Words.Clear();
                foreach (var e in results)
                    Words.Add(new DictionaryEntryViewModel(e));

                // Auto-select the best (first) match so the definition shows immediately, like CST4.
                SelectedWord = Words.FirstOrDefault();
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dictionary lookup failed for '{Query}' ({Source})", query, sourceId);
        }
    }

    private void UpdateMeaning()
    {
        MeaningSegments = MeaningParser.Parse(SelectedWord?.Source.MeaningHtml, PaliToDisplay);
    }

    // A cross-reference word from a <see> tag (stored in Latin) -> current display script.
    private string PaliToDisplay(string word)
    {
        try { return ScriptConverter.Convert(Any2Ipe.Convert(word), Script.Ipe, _scriptService.CurrentScript); }
        catch { return word; }
    }

    // Follow a <see> link: remember where we are, then look the referenced word up.
    private void NavigateToWord(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        if (!string.IsNullOrEmpty(SearchText))
        {
            _backStack.Push(SearchText);
            _forwardStack.Clear();
            UpdateHistoryState();
        }
        SearchText = PaliToDisplay(target);
    }

    private void GoBack()
    {
        if (_backStack.Count == 0) return;
        _forwardStack.Push(SearchText);
        SearchText = _backStack.Pop();
        UpdateHistoryState();
    }

    private void GoForward()
    {
        if (_forwardStack.Count == 0) return;
        _backStack.Push(SearchText);
        SearchText = _forwardStack.Pop();
        UpdateHistoryState();
    }

    private void UpdateHistoryState()
    {
        CanGoBack = _backStack.Count > 0;
        CanGoForward = _forwardStack.Count > 0;
    }

    public void Dispose()
    {
        if (_scriptChangedHandler != null)
            _scriptService.ScriptChanged -= _scriptChangedHandler;
        if (_fontChangedHandler != null)
            _fontService.FontSettingsChanged -= _fontChangedHandler;
        _disposables.Dispose();
    }
}

/// <summary>A row in the Words list: the source entry, whose Headword is already in the display script
/// (the source converts it — no IPE reconstruction needed, which was lossy for proper names). (#466)</summary>
public sealed class DictionaryEntryViewModel
{
    public DictionaryEntryViewModel(DictionaryEntry source)
    {
        Source = source;
        DisplayWord = source.Headword;
    }

    public DictionaryEntry Source { get; }
    public string DisplayWord { get; }
}

/// <summary>
/// A piece of a rendered definition: literal text, a clickable <c>&lt;see&gt;</c> cross-reference
/// (<see cref="IsLink"/>), or a break between the definitions of a merged headword
/// (<see cref="IsSeparator"/>).
/// </summary>
public sealed record MeaningSegment(string Text, bool IsLink, string? Target, bool IsSeparator = false)
{
    /// <summary>A break rendered between the definitions of a merged (duplicate) headword.</summary>
    public static readonly MeaningSegment Separator = new(string.Empty, false, null, true);
}
