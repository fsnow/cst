using System.Threading;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// #87: the Search pane's inputs (query, mode, proximity, book-type filters) must be serialized to
/// SearchDialogState and restored. These cover capture, restore-on-construction, save-on-change, and that
/// restoration does not echo itself back as a save.
/// </summary>
public class SearchViewModelStateTests
{
    private static (SearchViewModel vm, Mock<IApplicationStateService> state) CreateVm(SearchDialogState? saved = null)
    {
        ReactiveUiTestInit.Ensure();

        var appState = new ApplicationState();
        if (saved != null)
            appState.SearchDialog = saved;

        var state = new Mock<IApplicationStateService>();
        state.SetupGet(x => x.Current).Returns(appState);

        var search = new Mock<ISearchService>();
        search.Setup(x => x.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new SearchResult()); // a stray debounced live-search must not NRE

        var vm = new SearchViewModel(
            search.Object,
            Mock.Of<IScriptService>(),
            Mock.Of<IFontService>(),
            state.Object,
            NullLogger<SearchViewModel>.Instance);
        return (vm, state);
    }

    [Fact]
    public void Construction_DoesNotRestore_RestoreIsAppliedSeparately()
    {
        // Restore is pushed by App after the async state load completes; construction alone must not
        // restore, because the VM can be built before the load finishes. (#87)
        var saved = new SearchDialogState
        {
            SearchText = "dhamma*",
            SearchMode = SearchMode.Regex,
            ProximityDistance = 7,
            IncludeVinaya = false,
            IncludeOther = false,
        };

        var (vm, _) = CreateVm(saved);

        Assert.True(string.IsNullOrEmpty(vm.SearchText)); // not restored at construction
        Assert.True(vm.IncludeVinaya);                    // still the default

        vm.ApplyState(saved); // App's post-load push

        Assert.Equal("dhamma*", vm.SearchText);
        Assert.Equal(SearchMode.Regex, vm.SelectedSearchMode.Value);
        Assert.Equal(7, vm.ProximityDistance);
        Assert.False(vm.IncludeVinaya);
        Assert.False(vm.IncludeOther);
        Assert.True(vm.IncludeSutta); // untouched default preserved
    }

    [Fact]
    public void CaptureState_ReflectsCurrentProperties()
    {
        var (vm, _) = CreateVm();
        vm.SearchText = "buddha";
        vm.ProximityDistance = 5;
        vm.IncludeTika = false;
        vm.IsTextTypesExpanded = true;
        vm.SelectedTerms.Add(new MatchingTermViewModel { Term = "buddho" });
        vm.SelectedTerms.Add(new MatchingTermViewModel { Term = "buddhassa" });

        var s = vm.CaptureState();

        Assert.Equal("buddha", s.SearchText);
        Assert.Equal(5, s.ProximityDistance);
        Assert.False(s.IncludeTika);
        Assert.Equal(SearchMode.Wildcard, s.SearchMode); // ctor default
        Assert.True(s.IsTextTypesExpanded);
        Assert.Equal(new[] { "buddho", "buddhassa" }, s.SelectedTerms);
    }

    [Fact]
    public void ApplyState_ThenCaptureState_RoundTrips()
    {
        var (vm, _) = CreateVm();
        var s = new SearchDialogState
        {
            SearchText = "sangha",
            SearchMode = SearchMode.Regex,
            ProximityDistance = 3,
            IncludeVinaya = false,
            IncludeSutta = true,
            IncludeAbhidhamma = false,
            IncludeMula = true,
            IncludeAttha = false,
            IncludeTika = true,
            IncludeOther = false,
            IsTextTypesExpanded = true,
        };

        vm.ApplyState(s);
        var c = vm.CaptureState();

        Assert.Equal(s.SearchText, c.SearchText);
        Assert.Equal(s.SearchMode, c.SearchMode);
        Assert.Equal(s.ProximityDistance, c.ProximityDistance);
        Assert.Equal(s.IsTextTypesExpanded, c.IsTextTypesExpanded);
        Assert.Equal(s.IncludeVinaya, c.IncludeVinaya);
        Assert.Equal(s.IncludeSutta, c.IncludeSutta);
        Assert.Equal(s.IncludeAbhidhamma, c.IncludeAbhidhamma);
        Assert.Equal(s.IncludeMula, c.IncludeMula);
        Assert.Equal(s.IncludeAttha, c.IncludeAttha);
        Assert.Equal(s.IncludeTika, c.IncludeTika);
        Assert.Equal(s.IncludeOther, c.IncludeOther);
    }

    [Fact]
    public void ChangingAPersistableProperty_SavesViaUpdateSearchDialogState()
    {
        var (vm, state) = CreateVm();
        state.Invocations.Clear(); // ignore anything from construction

        vm.IncludeMula = false;

        state.Verify(x => x.UpdateSearchDialogState(It.Is<SearchDialogState>(s => !s.IncludeMula)),
            Times.AtLeastOnce);
    }

    [Fact]
    public void ApplyState_DoesNotEchoBackAsSave()
    {
        // Restore runs after construction (App's post-load push), when the save handler is already live,
        // so ApplyState must not echo the restored values back as a save (the _suppressStateSave guard).
        var (vm, state) = CreateVm();
        state.Invocations.Clear(); // ignore anything from construction

        vm.ApplyState(new SearchDialogState { SearchText = "metta", IncludeVinaya = false });

        state.Verify(x => x.UpdateSearchDialogState(It.IsAny<SearchDialogState>()), Times.Never);
        Assert.Equal("metta", vm.SearchText);
        Assert.False(vm.IncludeVinaya);
    }
}
