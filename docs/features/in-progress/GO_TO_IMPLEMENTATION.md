# Go To Dialog - Implementation Plan for CST.Avalonia

**Status**: Planning
**Date**: November 23, 2025
**Related CST4 Documentation**: `docs/features/planned/GO_TO.md`

## 1. Executive Summary

This document outlines the implementation plan for the "Go To" navigation dialog that allows users to jump to specific paragraphs or page numbers within the currently active book window. The feature will leverage existing navigation infrastructure (`NavigateToAnchor` in BookDisplayView) and the JavaScript anchor cache system.

## 2. Feature Overview

**Purpose**: Quick navigation to specific locations in a book by paragraph or page number.

**Access Points**:
- Keyboard shortcut: **Ctrl+G** (from within book window)
- Toolbar button: **"Go To"** button on main toolbar
- Menu item: **View → Go To** (future enhancement)

**User Experience**:
1. User opens Go To dialog
2. Dialog shows available navigation types (disables unavailable ones)
3. User enters a number (e.g., "25" or "V25")
4. User selects navigation type (Paragraph, VRI Page, Myanmar Page, etc.)
5. Dialog navigates to location and closes

## 3. Architecture Design

### 3.1. Component Overview

```
GoToDialogViewModel (new)
├── Navigation logic for constructing anchor names
├── Validation for user input
├── Available page type tracking
└── Commands: Navigate, Cancel

GoToDialog.axaml (new)
├── Radio buttons for page type selection
├── TextBox for number input
├── Auto-selection logic (e.g., "V25" → VRI Page selected)
└── OK/Cancel buttons

BookDisplayViewModel (modified)
├── Add GoToCommand (triggered by Ctrl+G)
├── Pass current page availability to dialog
└── Request navigation via existing NavigateToAnchor event

BookDisplayView (modified)
├── Handle Ctrl+G keyboard shortcut
└── Open GoToDialog with current ViewModel context

SimpleTabbedWindow (modified)
├── Add "Go To" toolbar button
└── Enable button only when book window is active
```

### 3.2. Existing Infrastructure to Leverage

**Already Implemented** ✅:
1. **NavigateToAnchor(string anchor)** - BookDisplayView.axaml.cs:1753
   - Takes anchor name (e.g., "para123", "V1.0023")
   - Uses JavaScript to scroll element into view
   - Thread-safe with JS lock management

2. **Anchor Cache System** - BookDisplayView.axaml.cs:730-900
   - JavaScript cache of all anchors in document
   - Page anchors: V (VRI), M (Myanmar), P (PTS), T (Thai), O (Other)
   - Paragraph anchors: "para" prefix
   - Chapter anchors: pattern like "dn1", "dn1_1"

3. **Page Availability Tracking** - BookDisplayViewModel.cs:58-63
   - Properties: VriPage, MyanmarPage, PtsPage, ThaiPage, OtherPage, CurrentParagraph
   - Value "*" indicates page type not available in current book

4. **Chapter Navigation Event** - BookDisplayViewModel.cs:48
   - `NavigateToChapterRequested` event already wired to NavigateToAnchor

## 4. Implementation Details

### 4.1. Create GoToDialogViewModel

**File**: `src/CST.Avalonia/ViewModels/GoToDialogViewModel.cs`

**Properties**:
```csharp
public class GoToDialogViewModel : ReactiveObject
{
    // Input
    private string _inputNumber = "";
    public string InputNumber
    {
        get => _inputNumber;
        set
        {
            this.RaiseAndSetIfChanged(ref _inputNumber, value);
            ParseAndAutoSelect(value); // Auto-select page type
        }
    }

    // Navigation type selection
    private NavigationType _selectedType = NavigationType.Paragraph;
    public NavigationType SelectedType
    {
        get => _selectedType;
        set => this.RaiseAndSetIfChanged(ref _selectedType, value);
    }

    // Availability flags (from BookDisplayViewModel)
    public bool IsParagraphAvailable { get; set; } = true; // Always available
    public bool IsVriPageAvailable { get; set; }
    public bool IsMyanmarPageAvailable { get; set; }
    public bool IsPtsPageAvailable { get; set; }
    public bool IsThaiPageAvailable { get; set; }
    public bool IsOtherPageAvailable { get; set; }

    // Result
    public string? ConstructedAnchor { get; private set; }
    public bool DialogResult { get; private set; }

    // Commands
    public ReactiveCommand<Unit, Unit> NavigateCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
}

public enum NavigationType
{
    Paragraph,
    VriPage,
    MyanmarPage,
    PtsPage,
    ThaiPage,
    OtherPage
}
```

**Constructor**:
```csharp
public GoToDialogViewModel(BookDisplayViewModel bookViewModel)
{
    // Set availability based on current book
    IsVriPageAvailable = bookViewModel.VriPage != "*";
    IsMyanmarPageAvailable = bookViewModel.MyanmarPage != "*";
    IsPtsPageAvailable = bookViewModel.PtsPage != "*";
    IsThaiPageAvailable = bookViewModel.ThaiPage != "*";
    IsOtherPageAvailable = bookViewModel.OtherPage != "*";

    // Commands
    var canNavigate = this.WhenAnyValue(
        x => x.InputNumber,
        x => !string.IsNullOrWhiteSpace(x));

    NavigateCommand = ReactiveCommand.Create(Navigate, canNavigate);
    CancelCommand = ReactiveCommand.Create(Cancel);
}
```

**Navigation Logic**:
```csharp
private void Navigate()
{
    ConstructedAnchor = BuildAnchorName();
    DialogResult = true;
}

private void Cancel()
{
    DialogResult = false;
}

private string BuildAnchorName()
{
    // Extract numeric part (remove any prefix letters)
    var numericPart = Regex.Replace(InputNumber, @"[^\d]", "");

    if (string.IsNullOrEmpty(numericPart))
        return "";

    switch (SelectedType)
    {
        case NavigationType.Paragraph:
            // Paragraph anchors: "para123"
            // TODO: Handle Multi book suffix (e.g., "para123_an5")
            return $"para{numericPart}";

        case NavigationType.VriPage:
            // VRI page anchors: "V1.0023" (4-digit padding)
            return FormatPageAnchor("V", numericPart);

        case NavigationType.MyanmarPage:
            // Myanmar page anchors: "M1.0023"
            return FormatPageAnchor("M", numericPart);

        case NavigationType.PtsPage:
            // PTS page anchors: "P1.0023"
            return FormatPageAnchor("P", numericPart);

        case NavigationType.ThaiPage:
            // Thai page anchors: "T1.0023"
            return FormatPageAnchor("T", numericPart);

        case NavigationType.OtherPage:
            // Other page anchors: "O1.0023"
            return FormatPageAnchor("O", numericPart);

        default:
            return "";
    }
}

private string FormatPageAnchor(string prefix, string number)
{
    // CST4 format: "V1.0023" (prefix + "1." + 4-digit padded number)
    // Parse number to int, then pad with zeros
    if (int.TryParse(number, out int pageNum))
    {
        return $"{prefix}1.{pageNum:D4}";
    }
    return "";
}
```

**Auto-Selection Logic** (CST4 Feature):
```csharp
private void ParseAndAutoSelect(string input)
{
    if (string.IsNullOrWhiteSpace(input))
        return;

    // Check if user typed a prefix letter
    var firstChar = input.ToUpperInvariant()[0];

    switch (firstChar)
    {
        case 'V':
            if (IsVriPageAvailable)
            {
                SelectedType = NavigationType.VriPage;
                // Remove the prefix letter from input
                InputNumber = input.Substring(1);
            }
            break;

        case 'M':
            if (IsMyanmarPageAvailable)
            {
                SelectedType = NavigationType.MyanmarPage;
                InputNumber = input.Substring(1);
            }
            break;

        case 'P':
            if (IsPtsPageAvailable)
            {
                SelectedType = NavigationType.PtsPage;
                InputNumber = input.Substring(1);
            }
            break;

        case 'T':
            if (IsThaiPageAvailable)
            {
                SelectedType = NavigationType.ThaiPage;
                InputNumber = input.Substring(1);
            }
            break;
    }
}
```

### 4.2. Create GoToDialog.axaml

**File**: `src/CST.Avalonia/Views/GoToDialog.axaml`

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:CST.Avalonia.ViewModels"
        x:Class="CST.Avalonia.Views.GoToDialog"
        x:DataType="vm:GoToDialogViewModel"
        Title="Go To"
        Width="350" Height="280"
        WindowStartupLocation="CenterOwner"
        CanResize="False">

    <Design.DataContext>
        <vm:GoToDialogViewModel/>
    </Design.DataContext>

    <StackPanel Margin="20" Spacing="15">
        <!-- Title -->
        <TextBlock Text="Navigate to:" FontSize="14" FontWeight="SemiBold"/>

        <!-- Navigation Type Selection -->
        <StackPanel Spacing="8">
            <RadioButton GroupName="NavType"
                         IsChecked="{Binding SelectedType, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=Paragraph}"
                         Content="Paragraph" />

            <RadioButton GroupName="NavType"
                         IsChecked="{Binding SelectedType, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=VriPage}"
                         IsEnabled="{Binding IsVriPageAvailable}"
                         Content="VRI Page" />

            <RadioButton GroupName="NavType"
                         IsChecked="{Binding SelectedType, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=MyanmarPage}"
                         IsEnabled="{Binding IsMyanmarPageAvailable}"
                         Content="Myanmar Page" />

            <RadioButton GroupName="NavType"
                         IsChecked="{Binding SelectedType, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=PtsPage}"
                         IsEnabled="{Binding IsPtsPageAvailable}"
                         Content="PTS Page" />

            <RadioButton GroupName="NavType"
                         IsChecked="{Binding SelectedType, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=ThaiPage}"
                         IsEnabled="{Binding IsThaiPageAvailable}"
                         Content="Thai Page" />

            <RadioButton GroupName="NavType"
                         IsChecked="{Binding SelectedType, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=OtherPage}"
                         IsEnabled="{Binding IsOtherPageAvailable}"
                         Content="Other Page" />
        </StackPanel>

        <!-- Number Input -->
        <StackPanel Spacing="5">
            <TextBlock Text="Number:" />
            <TextBox Text="{Binding InputNumber}"
                     Watermark="Enter number (e.g., 25 or V25)"
                     Width="250"
                     HorizontalAlignment="Left"/>
        </StackPanel>

        <!-- Buttons -->
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="10" Margin="0,10,0,0">
            <Button Content="OK"
                    Command="{Binding NavigateCommand}"
                    IsDefault="True"
                    Width="80"/>
            <Button Content="Cancel"
                    Command="{Binding CancelCommand}"
                    IsCancel="True"
                    Width="80"/>
        </StackPanel>
    </StackPanel>
</Window>
```

**Note**: Need to create `EnumToBoolConverter` for radio button binding or use simpler approach with individual boolean properties.

### 4.3. Create GoToDialog Code-Behind

**File**: `src/CST.Avalonia/Views/GoToDialog.axaml.cs`

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CST.Avalonia.ViewModels;

namespace CST.Avalonia.Views
{
    public partial class GoToDialog : Window
    {
        public GoToDialog()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public GoToDialog(GoToDialogViewModel viewModel) : this()
        {
            DataContext = viewModel;

            // Wire up command completion to close dialog
            viewModel.NavigateCommand.Subscribe(_ =>
            {
                Close(viewModel.DialogResult);
            });

            viewModel.CancelCommand.Subscribe(_ =>
            {
                Close(false);
            });
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
```

### 4.4. Add Ctrl+G Keyboard Shortcut

**File**: `src/CST.Avalonia/Views/BookDisplayView.axaml.cs`

Modify the existing `OnKeyDown` method (line 99):

```csharp
private void OnKeyDown(object? sender, KeyEventArgs e)
{
    _logger.Debug("KEYBOARD: BookDisplayView OnKeyDown. Key: {Key}, Modifiers: {Modifiers}", e.Key, e.KeyModifiers);

    // Handle Ctrl+G for Go To dialog
    if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.G)
    {
        _logger.Information("Ctrl+G pressed - opening Go To dialog");
        _viewModel?.OpenGoToDialogCommand?.Execute(null);
        e.Handled = true;
        return;
    }

    // Existing keyboard handling...
}
```

### 4.5. Add GoToCommand to BookDisplayViewModel

**File**: `src/CST.Avalonia/ViewModels/BookDisplayViewModel.cs`

Add property and command:

```csharp
// Add to class properties
public ReactiveCommand<Unit, Unit>? OpenGoToDialogCommand { get; set; }

// Event for requesting Go To dialog (handled by SimpleTabbedWindow)
public event Action? OpenGoToDialogRequested;

// In constructor, create command
OpenGoToDialogCommand = ReactiveCommand.Create(() =>
{
    OpenGoToDialogRequested?.Invoke();
});
```

### 4.6. Wire Up Go To Dialog in SimpleTabbedWindow

**File**: `src/CST.Avalonia/Views/SimpleTabbedWindow.cs`

Add method to handle Go To dialog:

```csharp
private void OnOpenGoToDialogRequested(BookDisplayViewModel bookViewModel)
{
    _logger.Information("Opening Go To dialog");

    // Create ViewModel with current book context
    var dialogViewModel = new GoToDialogViewModel(bookViewModel);

    // Create and show dialog
    var dialog = new GoToDialog(dialogViewModel);
    var result = await dialog.ShowDialog<bool>(this);

    if (result && !string.IsNullOrEmpty(dialogViewModel.ConstructedAnchor))
    {
        _logger.Information("Go To navigation requested: {Anchor}", dialogViewModel.ConstructedAnchor);

        // Trigger navigation via existing event
        bookViewModel.NavigateToChapterRequested?.Invoke(dialogViewModel.ConstructedAnchor);
    }
}

// In initialization, wire up event
private void WireUpBookDisplayEvents(BookDisplayViewModel viewModel)
{
    viewModel.OpenGoToDialogRequested += () => OnOpenGoToDialogRequested(viewModel);
    // ... other events
}
```

### 4.7. Add Go To Toolbar Button

**File**: `src/CST.Avalonia/Views/SimpleTabbedWindow.axaml`

Add button to toolbar (after Pali Script selector):

```xml
<Separator/>

<!-- Go To Button -->
<Button x:Name="GoToButton"
        ToolTip.Tip="Go to paragraph or page number (Ctrl+G)"
        Margin="5,0"
        Padding="8,4"
        VerticalAlignment="Center">
    <StackPanel Orientation="Horizontal" Spacing="5">
        <TextBlock Text="📍" FontSize="16"/>
        <TextBlock Text="Go To" VerticalAlignment="Center"/>
    </StackPanel>
</Button>
```

**Wire up in code-behind**:

```csharp
private void InitializeComponent()
{
    AvaloniaXamlLoader.Load(this);
    _paliScriptCombo = this.FindControl<ComboBox>("PaliScriptCombo");
    _goToButton = this.FindControl<Button>("GoToButton");

    // Wire up Go To button
    if (_goToButton != null)
    {
        _goToButton.Click += OnGoToButtonClick;
    }
}

private void OnGoToButtonClick(object? sender, RoutedEventArgs e)
{
    // Get active book ViewModel
    var layoutViewModel = DataContext as LayoutViewModel;
    var activeDocument = layoutViewModel?.Layout?.ActiveDockable;

    if (activeDocument is BookDisplayViewModel bookViewModel)
    {
        OnOpenGoToDialogRequested(bookViewModel);
    }
}

// Enable/disable based on active window type
private void UpdateToolbarState()
{
    var activeDocument = (DataContext as LayoutViewModel)?.Layout?.ActiveDockable;
    bool hasActiveBook = activeDocument is BookDisplayViewModel;

    if (_goToButton != null)
    {
        _goToButton.IsEnabled = hasActiveBook;
    }
}
```

## 5. Special Cases & Edge Cases

### 5.1. Multi-Type Books

**Issue**: Some books (type `Multi`) require a suffix on paragraph anchors (e.g., "para123_an5").

**CST4 Solution** (FormBookDisplay.cs:724-727):
```csharp
if (bookType == 2) // Multi type
{
    goToAnchor = prefix + number + "_" + book.Code;
}
```

**CST.Avalonia Solution**:
```csharp
// In GoToDialogViewModel constructor, pass Book object
private readonly Book _book;

// In BuildAnchorName()
case NavigationType.Paragraph:
    var anchor = $"para{numericPart}";

    // Handle Multi-type books (type == 2)
    if (_book.Type == 2)
    {
        anchor += $"_{_book.Code}"; // e.g., "para123_an5"
    }

    return anchor;
```

### 5.2. Missing Paragraph Numbers (Ranges/Gaps)

**Issue**: Paragraph anchors may have gaps (e.g., para1, para5, para10).

**CST4 Solution** (FormBookDisplay.cs:730):
```csharp
goToAnchor = FindPreviousAnchor("para", number);
```

**CST.Avalonia Solution**:
- Current `NavigateToAnchor` will silently fail if anchor doesn't exist
- **Enhancement**: Query JavaScript anchor cache to find nearest previous anchor
- Implement `FindPreviousAnchor` logic in GoToDialogViewModel:

```csharp
private async Task<string?> FindNearestParagraphAnchor(int targetNumber)
{
    // Query JavaScript anchor cache
    var script = $@"
    (function() {{
        if (!window.cstAnchorCache || !window.cstAnchorCache.paragraphAnchors) {{
            return null;
        }}

        var target = {targetNumber};
        var best = null;

        for (var name in window.cstAnchorCache.paragraphAnchors) {{
            var match = name.match(/para(\d+)/);
            if (match) {{
                var num = parseInt(match[1]);
                if (num <= target && (!best || num > best)) {{
                    best = num;
                }}
            }}
        }}

        return best ? 'para' + best : null;
    }})();";

    // Execute and return result
    // ... (requires WebView access - may need to move this to BookDisplayView)
}
```

**Simplified Alternative**: Let NavigateToAnchor handle missing anchors gracefully (current behavior).

### 5.3. Page Number Format Variations

**CST4 Discovery** (FormBookDisplay.cs:736-748):
- CST4 loops through padding variations to find page anchors
- Tries "V1.0025", "V1.00025", etc.

**CST.Avalonia Solution**:
- Start with standard 4-digit padding: "V1.0025"
- If navigation fails, could try variations (future enhancement)
- Current implementation uses fixed format

## 6. Implementation Sequence

### Phase 1: Core Dialog (Day 1)
1. ✅ Create GoToDialogViewModel with navigation logic
2. ✅ Create GoToDialog.axaml with radio buttons and input
3. ✅ Implement anchor name construction (paragraph, page types)
4. ✅ Test dialog shows and constructs correct anchors

### Phase 2: Integration (Day 2)
5. ✅ Add OpenGoToDialogCommand to BookDisplayViewModel
6. ✅ Add Ctrl+G keyboard shortcut to BookDisplayView
7. ✅ Wire up dialog in SimpleTabbedWindow
8. ✅ Test navigation works end-to-end

### Phase 3: UI Enhancement (Day 3)
9. ✅ Add Go To toolbar button
10. ✅ Enable/disable button based on active window
11. ✅ Implement auto-selection text parsing (V25 → VRI Page)
12. ✅ Test usability with all page types

### Phase 4: Polish & Edge Cases (Day 4)
13. ✅ Handle Multi-type book suffixes
14. ✅ Test with books having missing page types
15. ✅ Add input validation and error handling
16. ✅ Update documentation

## 7. Testing Strategy

### Manual Testing Checklist

**Basic Navigation**:
- [ ] Open book → Press Ctrl+G → Dialog opens
- [ ] Enter paragraph number → Click OK → Navigates to paragraph
- [ ] Enter VRI page number → Navigates to VRI page
- [ ] Test all 6 navigation types (Paragraph, V, M, P, T, O)

**Context Awareness**:
- [ ] Open book without PTS pages → PTS Page radio button disabled
- [ ] Open book with all page types → All radio buttons enabled
- [ ] Switch books → Go To reflects new book's available pages

**Text Parsing**:
- [ ] Type "V25" → VRI Page auto-selected, number shows "25"
- [ ] Type "M100" → Myanmar Page auto-selected
- [ ] Type "P50" → PTS Page auto-selected
- [ ] Typing letter for disabled page type does nothing

**Edge Cases**:
- [ ] Enter non-existent paragraph number → Graceful handling
- [ ] Empty input → OK button disabled
- [ ] Multi-type book → Paragraph anchors have correct suffix
- [ ] Very large page numbers → Format correctly

**UI/UX**:
- [ ] Dialog centers over book window
- [ ] Enter key triggers navigation
- [ ] Escape key cancels dialog
- [ ] Toolbar button enabled only with active book
- [ ] Toolbar button disabled when no book open

## 8. Known Limitations & Future Enhancements

**Current Limitations**:
1. **No Fuzzy Matching**: If exact anchor doesn't exist, navigation silently fails
   - **Enhancement**: Implement FindPreviousAnchor logic
   - **Priority**: Medium - affects user experience with gaps in numbering

2. **Fixed Page Format**: Uses 4-digit padding only
   - **Enhancement**: Try multiple padding variations
   - **Priority**: Low - standard format works for vast majority

3. **No Recent History**: Dialog doesn't remember last used type
   - **Enhancement**: Save last selected navigation type in settings
   - **Priority**: Low - nice-to-have usability improvement

**Future Enhancements**:
- **Jump to Last Position**: Add button to return to previous location
- **Bookmark System**: Save frequently visited locations
- **Chapter List Integration**: Add chapter names to dropdown
- **Keyboard-Only Navigation**: Tab/Arrow key navigation in dialog

## 9. Documentation Updates

After implementation, update:
- `CLAUDE.md`: Move from Outstanding Work → Current Functionality
- Create `docs/features/implemented/ui/GO_TO.md` (postmortem)
- Update user documentation with Ctrl+G shortcut
- Add Go To feature to keyboard shortcuts reference

## 10. References

- **CST4 Implementation**: `docs/features/planned/GO_TO.md`
- **Existing Navigation**: BookDisplayView.axaml.cs:1753 (NavigateToAnchor)
- **Anchor Cache**: BookDisplayView.axaml.cs:730-900 (BuildAnchorPositionCache)
- **Page Tracking**: BookDisplayViewModel.cs:58-63 (Page reference properties)
