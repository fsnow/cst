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
    private IDictionarySource? _selectedSource;
    private DictionaryEntryViewModel? _selectedWord;
    private string _meaningDocumentHtml = "";
    private string _attribution = "";
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

        // The picker lists the installed SOURCES from the shared registry, shown by DisplayName. Only
        // installed sources appear; a fresh install with no derived asset degrades cleanly to en/hi.
        Sources = _registry.Available;
        LongestSourceName = Sources.Select(s => s.DisplayName)
            .OrderByDescending(n => n?.Length ?? 0)
            .FirstOrDefault() ?? "";
        // Restore the preferred SOURCE by id (#466), migrating from the older Language key; fall back to
        // en, then the first available.
        var savedId = _stateService.Current.DictionaryDialog.SourceId;
        if (string.IsNullOrEmpty(savedId))
            savedId = _stateService.Current.DictionaryDialog.Language;   // migrate #25 → #466
        _selectedSource = Sources.FirstOrDefault(s => string.Equals(s.Id, savedId, StringComparison.OrdinalIgnoreCase))
            ?? Sources.FirstOrDefault(s => string.Equals(s.Id, "en", StringComparison.OrdinalIgnoreCase))
            ?? Sources.FirstOrDefault();

        Words = new ObservableCollection<DictionaryEntryViewModel>();

        BackCommand = ReactiveCommand.Create(GoBack, this.WhenAnyValue(x => x.CanGoBack));
        ForwardCommand = ReactiveCommand.Create(GoForward, this.WhenAnyValue(x => x.CanGoForward));
        NavigateToWordCommand = ReactiveCommand.Create<string>(NavigateToWord);

        // Real-time lookup as the query or source changes (throttled, like the Search tool). The
        // throttle fires off the UI thread; LookupAsync marshals its collection updates back.
        this.WhenAnyValue(x => x.SearchText, x => x.SelectedSource)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .Subscribe(change => { _ = LookupAsync(SearchText); })
            .DisposeWith(_disposables);

        // Rebuild the meaning display whenever the selected headword changes.
        this.WhenAnyValue(x => x.SelectedWord)
            .Subscribe(_ => UpdateMeaning())
            .DisposeWith(_disposables);

        // Per-source attribution line, refreshed when the source changes (initial value included — no Skip).
        this.WhenAnyValue(x => x.SelectedSource)
            .Subscribe(source => Attribution = FormatAttribution(source?.Attribution))
            .DisposeWith(_disposables);

        // Remember the preferred source across sessions. Skip(1) ignores the initial value set above so
        // construction doesn't write state. (#466)
        this.WhenAnyValue(x => x.SelectedSource)
            .Skip(1)
            .Subscribe(source =>
            {
                if (source != null)
                {
                    _stateService.Current.DictionaryDialog.SourceId = source.Id;
                    _stateService.MarkDirty();   // persist via the timer/shutdown save, not a full off-thread save per change (STATE-2)
                }
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
            UpdateMeaning();   // re-render the meaning document at the new font (#466)
        });
        _fontService.FontSettingsChanged += _fontChangedHandler;
    }

    /// <summary>The installed dictionary sources for the picker (shown by <c>DisplayName</c>). (#466)</summary>
    public IReadOnlyList<IDictionarySource> Sources { get; }

    /// <summary>The longest source <c>DisplayName</c> — a hidden measurer renders it to pin the picker
    /// column to a fixed width (the longest name), so the box + dropdown don't resize as you switch
    /// sources. (#466)</summary>
    public string LongestSourceName { get; }

    public IDictionarySource? SelectedSource
    {
        get => _selectedSource;
        set => this.RaiseAndSetIfChanged(ref _selectedSource, value);
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

    /// <summary>The selected definition as a full HTML document for the meaning WebView — the source's
    /// MeaningHtml wrapped in a CSP host page, with &lt;see&gt; cross-references turned into links. (#466)</summary>
    public string MeaningDocumentHtml
    {
        get => _meaningDocumentHtml;
        private set => this.RaiseAndSetIfChanged(ref _meaningDocumentHtml, value);
    }

    /// <summary>A one-line citation for the selected source, shown under the meaning (never hard-coded — it
    /// comes from the source's recorded attribution; blank when unrecorded). (#466, #268)</summary>
    public string Attribution
    {
        get => _attribution;
        private set => this.RaiseAndSetIfChanged(ref _attribution, value);
    }

    // Compose the recorded attribution fields into a compact one-liner, skipping any that are unset.
    private static string FormatAttribution(CST.Tools.DictionarySourceInfo? a)
    {
        if (a == null) return "";
        var parts = new System.Collections.Generic.List<string>();
        void Add(string? s) { if (!string.IsNullOrWhiteSpace(s)) parts.Add(s.Trim()); }
        Add(a.Title);
        Add(a.Compiler);
        Add(a.Edition);
        Add(a.Year);
        Add(a.Publisher);
        return string.Join(" · ", parts);
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
        var source = SelectedSource;   // capture: results are only valid if these still hold on completion
        try
        {
            IReadOnlyList<DictionaryEntry> results = source == null
                ? Array.Empty<DictionaryEntry>()
                // The source returns Headword already in the requested output script and the definition as
                // MeaningHtml (for flat-file, the same <see>-tagged text the native renderer handles). (#466)
                : await source.LookupAsync(new DictionaryRequest(source.Id, query ?? "", _scriptService.CurrentScript, 500));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Ignore a stale completion: a newer query/source has superseded this lookup, so its results
                // must not clobber the current ones (e.g. a slow first-time load finishing after a fast
                // cached lookup). (DICT-3)
                if (query != SearchText || !ReferenceEquals(source, SelectedSource))
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
            _logger.LogWarning(ex, "Dictionary lookup failed for '{Query}' ({Source})", query, source?.Id);
        }
    }

    private void UpdateMeaning()
    {
        // Definitions are prose (mostly English/Hindi with Pāli terms); render just under the headword
        // size — the current script font size minus one — as a comfortable reading size. (#466)
        MeaningDocumentHtml = DictionaryHtmlRenderer.Render(
            SelectedWord?.Source.MeaningHtml,
            PaliToDisplay,
            DictionaryService.MeaningSeparator,
            CurrentScriptFontFamily,
            Math.Max(1, CurrentScriptFontSize - 1));
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
