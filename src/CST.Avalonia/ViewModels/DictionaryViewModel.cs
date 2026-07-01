using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels.Dock;
using CST.Conversion;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace CST.Avalonia.ViewModels;

/// <summary>
/// The Pāli dictionary tool (#25). A dockable left-tool panel: pick a language, type a headword in
/// any script (IPE-normalized by the service), pick from the matching Words, and read the definition.
/// Definitions render natively (the data is plain text + <c>&lt;see&gt;</c> cross-references only, so no
/// WebView is needed); <c>&lt;see&gt;</c> links look up the referenced word, with back/forward history.
/// </summary>
public class DictionaryViewModel : ReactiveTool, IDisposable
{
    private static readonly Regex SeeTag = new(@"<see>(.*?)</see>", RegexOptions.Compiled | RegexOptions.Singleline);

    private readonly IDictionaryService _dictionaryService;
    private readonly IScriptService _scriptService;
    private readonly IFontService _fontService;
    private readonly IApplicationStateService _stateService;
    private readonly ILogger<DictionaryViewModel> _logger;
    private readonly CompositeDisposable _disposables = new();

    // <see>-link navigation history (manual typing does NOT push history; only followed links do).
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();

    private string _searchText = "";
    private string _selectedLanguage = "en";
    private DictionaryEntryViewModel? _selectedWord;
    private IReadOnlyList<MeaningSegment> _meaningSegments = Array.Empty<MeaningSegment>();
    private bool _canGoBack;
    private bool _canGoForward;

    // Parameterless ctor so the DataTemplate/dock can materialize it; resolves deps from the container.
    public DictionaryViewModel() : this(
        App.ServiceProvider?.GetService(typeof(IDictionaryService)) as IDictionaryService ?? throw new InvalidOperationException("DictionaryService not available"),
        App.ServiceProvider?.GetService(typeof(IScriptService)) as IScriptService ?? throw new InvalidOperationException("ScriptService not available"),
        App.ServiceProvider?.GetService(typeof(IFontService)) as IFontService ?? throw new InvalidOperationException("FontService not available"),
        App.ServiceProvider?.GetService(typeof(IApplicationStateService)) as IApplicationStateService ?? throw new InvalidOperationException("ApplicationStateService not available"),
        App.ServiceProvider?.GetService(typeof(ILogger<DictionaryViewModel>)) as ILogger<DictionaryViewModel> ?? throw new InvalidOperationException("Logger not available"))
    {
    }

    public DictionaryViewModel(
        IDictionaryService dictionaryService,
        IScriptService scriptService,
        IFontService fontService,
        IApplicationStateService stateService,
        ILogger<DictionaryViewModel> logger)
    {
        _dictionaryService = dictionaryService;
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

        AvailableLanguages = _dictionaryService.AvailableLanguages.Count > 0
            ? _dictionaryService.AvailableLanguages
            : new[] { "en" };
        // Restore the preferred definition language (#25); fall back to en, then whatever's available.
        var savedLanguage = _stateService.Current.DictionaryDialog.Language;
        _selectedLanguage = AvailableLanguages.Contains(savedLanguage) ? savedLanguage
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
            .Subscribe(lang =>
            {
                _stateService.Current.DictionaryDialog.Language = lang;
                _ = _stateService.SaveStateAsync();
            })
            .DisposeWith(_disposables);
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
        try
        {
            var results = await _dictionaryService.LookupAsync(SelectedLanguage, query ?? "");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Words.Clear();
                foreach (var w in results)
                    Words.Add(new DictionaryEntryViewModel(w, IpeToDisplay(w.Word)));

                // Auto-select the best (first) match so the definition shows immediately, like CST4.
                SelectedWord = Words.FirstOrDefault();
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dictionary lookup failed for '{Query}' ({Lang})", query, SelectedLanguage);
        }
    }

    private void UpdateMeaning()
    {
        var meaning = SelectedWord?.Source.Meaning;
        MeaningSegments = string.IsNullOrEmpty(meaning)
            ? Array.Empty<MeaningSegment>()
            : ParseMeaning(meaning);
    }

    // Split an HTML definition fragment into plain-text runs and <see>word</see> link runs.
    private IReadOnlyList<MeaningSegment> ParseMeaning(string html)
    {
        var segments = new List<MeaningSegment>();
        int pos = 0;
        foreach (Match m in SeeTag.Matches(html))
        {
            if (m.Index > pos)
                segments.Add(new MeaningSegment(Decode(html.Substring(pos, m.Index - pos)), false, null));

            var target = m.Groups[1].Value.Trim();
            segments.Add(new MeaningSegment(PaliToDisplay(target), true, target));
            pos = m.Index + m.Length;
        }
        if (pos < html.Length)
            segments.Add(new MeaningSegment(Decode(html.Substring(pos)), false, null));

        // Some source definitions carry a leading space (e.g. the "a" entry); don't render it as an indent.
        if (segments.Count > 0 && !segments[0].IsLink)
            segments[0] = segments[0] with { Text = segments[0].Text.TrimStart() };
        return segments;
    }

    private static string Decode(string s) => WebUtility.HtmlDecode(s);

    // A stored IPE headword -> the user's current display script.
    private string IpeToDisplay(string ipe)
    {
        try { return ScriptConverter.Convert(ipe, Script.Ipe, _scriptService.CurrentScript); }
        catch { return ipe; }
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
        _disposables.Dispose();
    }
}

/// <summary>A row in the Words list: the source entry plus its headword rendered in the display script.</summary>
public sealed class DictionaryEntryViewModel
{
    public DictionaryEntryViewModel(CST.Avalonia.Models.DictionaryWord source, string displayWord)
    {
        Source = source;
        DisplayWord = displayWord;
    }

    public CST.Avalonia.Models.DictionaryWord Source { get; }
    public string DisplayWord { get; }
}

/// <summary>A piece of a rendered definition: literal text, or a clickable <c>&lt;see&gt;</c> cross-reference.</summary>
public sealed record MeaningSegment(string Text, bool IsLink, string? Target);
