# Dock Notepad Sample: ControlRecycling, StaticViewLocator & DockFluentTheme

**Source**: Dock library Notepad sample (`/Users/fsnow/github/wieslawsoltes/Dock/samples/Notepad`)
**Purpose**: Analysis of three key patterns used in production Dock applications

This document analyzes the Notepad sample application from the Dock library to understand how to properly implement:
1. **ControlRecycling** - Reuses visual controls for performance and state preservation
2. **StaticViewLocator** - Build-time View-ViewModel mapping for AOT compatibility
3. **DockFluentTheme** - Complete styling system for dock controls

---

## 1. ControlRecycling

### What It Does

ControlRecycling is a caching system that **reuses visual controls** when dockables (documents/tools) are reopened or moved around in the layout. Instead of creating a new control every time a document tab is shown, the same control instance is retrieved from a cache.

**Benefits:**
- **Preserves Control State** - Text in a TextBox, scroll position, etc. survives tab changes
- **Reduces Visual Churn** - No flicker or re-creation when moving/reopening tabs
- **Improves Performance** - Avoids repeated control instantiation

### Implementation in Notepad Sample

#### 1. Package Reference

```xml
<!-- Notepad.csproj -->
<PackageReference Include="Dock.Controls.Recycling" />
```

The recycling system is a separate NuGet package that works alongside the main Dock libraries.

#### 2. Application-Level Setup

```xml
<!-- App.axaml -->
<Application.Resources>
  <ControlRecycling x:Key="ControlRecyclingKey" />
</Application.Resources>

<Application.Styles>
  <Style Selector="DockControl">
    <Setter Property="(ControlRecyclingDataTemplate.ControlRecycling)"
            Value="{StaticResource ControlRecyclingKey}" />
  </Style>
</Application.Styles>
```

**How It Works:**
1. **Define Resource**: Create a `ControlRecycling` instance as an application resource
2. **Attach to DockControl**: Use the `ControlRecyclingDataTemplate.ControlRecycling` attached property to connect the recycling cache to all `DockControl` instances
3. **Automatic Caching**: When a View is created for a ViewModel, it's stored in the cache keyed by the ViewModel instance

#### 3. ID-Based Caching (Optional)

```xml
<ControlRecycling x:Key="ControlRecyclingKey" TryToUseIdAsKey="True" />
```

**Why Use ID-Based Caching:**
- Default behavior: Cache key is the **ViewModel instance** (reference equality)
- With `TryToUseIdAsKey="True"`: Cache key is the **ViewModel's `Id` property** (value equality)

**Use Case for IDs:**
When deserializing saved layouts, you get **new ViewModel instances** even though they represent the same logical document. ID-based caching ensures the same cached View is used:

```csharp
// Without ID-based caching:
var doc1 = new FileViewModel { Id = "File1", Title = "README.md" };
// ...user closes app, state serialized...
// ...user reopens app, state deserialized...
var doc2 = new FileViewModel { Id = "File1", Title = "README.md" };
// doc1 != doc2 (different instances) → cache miss, new View created

// With ID-based caching:
// doc1.GetControlRecyclingId() == doc2.GetControlRecyclingId() → cache hit!
```

#### 4. ViewModel Implementation

All dockable ViewModels implement `IControlRecyclingIdProvider`:

```csharp
// FileViewModel.cs
public class FileViewModel : Document  // Document extends DockableBase
{
    // Inherited from DockableBase:
    // public string Id { get; set; }
    // public string? GetControlRecyclingId() => Id;
}
```

The `Document` base class (from `Dock.Model.Mvvm.Controls`) already implements `GetControlRecyclingId()` by returning the `Id` property.

#### 5. Cache Lifecycle

**Automatic Management:**
```csharp
// When a ViewModel is displayed in a DockControl:
// 1. DockControl asks ViewLocator for a View
// 2. ViewLocator creates new View (if not cached)
// 3. ControlRecycling stores: cache[viewModel.Id] = view
// 4. Next time same ViewModel appears: return cached view

// When clearing cache (if needed):
controlRecycling.Clear();
```

### Key Insight

ControlRecycling works **invisibly** once configured. You don't call it directly - the `DockControl` automatically uses it when resolving Views from ViewModels through the `IDataTemplate` system.

---

## 2. StaticViewLocator

### What It Does

StaticViewLocator is a **source generator** that creates compile-time mappings between ViewModels and Views. Traditional ViewLocators use reflection and string manipulation at runtime; StaticViewLocator generates the mapping code during build.

**Benefits:**
- **AOT Compatible** - No reflection needed, works with Native AOT compilation
- **Build-Time Errors** - Missing Views are caught during compilation, not at runtime
- **Performance** - Direct dictionary lookup instead of reflection and type name parsing
- **Smaller Binaries** - No need to ship reflection metadata

### Implementation in Notepad Sample

#### 1. Package Reference

```xml
<!-- Notepad.csproj -->
<PackageReference Include="StaticViewLocator">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

**Note**: `PrivateAssets="all"` means this is a **build-time only** dependency. The source generator runs during compilation but isn't included in the output.

#### 2. ViewLocator Class

```csharp
// ViewLocator.cs
using StaticViewLocator;

namespace Notepad;

[StaticViewLocator]  // ← Triggers source generator
public partial class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var type = data.GetType();

        // s_views is generated by StaticViewLocator source generator
        if (s_views.TryGetValue(type, out var func))
        {
            return func.Invoke();
        }

        throw new Exception($"Unable to create view for type: {type}");
    }

    public bool Match(object? data)
    {
        if (data is null)
            return false;

        var type = data.GetType();
        return data is IDockable || s_views.ContainsKey(type);
    }
}
```

**Key Points:**
- `[StaticViewLocator]` attribute marks this class for code generation
- `partial class` allows the generator to add the `s_views` dictionary
- `s_views` is a `Dictionary<Type, Func<Control>>` generated at build time

#### 3. Generated Code (Conceptual)

The source generator scans your project and generates something like:

```csharp
// Auto-generated by StaticViewLocator
partial class ViewLocator
{
    private static readonly Dictionary<Type, Func<Control>> s_views = new()
    {
        [typeof(FileViewModel)] = () => new FileView(),
        [typeof(FindViewModel)] = () => new FindView(),
        [typeof(ReplaceViewModel)] = () => new ReplaceView(),
        [typeof(MainWindowViewModel)] = () => new MainWindow(),
        // ... etc for all ViewModel/View pairs
    };
}
```

#### 4. Application Registration

```xml
<!-- App.axaml -->
<Application.DataTemplates>
  <local:ViewLocator />
</Application.DataTemplates>
```

This registers the ViewLocator as the application-wide `IDataTemplate`, used by Avalonia to resolve Views from ViewModels.

#### 5. Naming Convention

The generator uses convention-based mapping:

| ViewModel Name | View Name | Rule |
|---------------|-----------|------|
| `FileViewModel` | `FileView` | Replace "ViewModel" with "View" |
| `MainWindowViewModel` | `MainWindow` | Replace "ViewModel" with "Window" |
| `FindViewModel` | `FindView` | Replace "ViewModel" with "View" |

**Namespace Handling:**
- ViewModels: `Notepad.ViewModels.Documents.FileViewModel`
- Views: `Notepad.Views.Documents.FileView`

The generator intelligently handles parallel namespace structures.

### Key Insight

StaticViewLocator is **zero-runtime-cost** View resolution. The mapping is baked into your compiled assembly, making it ideal for:
- Native AOT scenarios (Notepad uses `PublishAot="true"`)
- High-performance applications
- Applications that need predictable startup time

---

## 3. DockFluentTheme

### What It Does

DockFluentTheme is a **complete styling system** for all Dock controls that provides:
- Consistent visual appearance across all dock UI elements
- Theme-aware brushes that respond to Light/Dark mode
- Pre-styled templates for Documents, Tools, TabStrips, Splitters, etc.
- Icon geometry resources for dock-related UI

### Implementation in Notepad Sample

#### 1. Package Reference

```xml
<!-- Notepad.csproj -->
<ProjectReference Include="..\..\src\Dock.Avalonia.Themes.Fluent\Dock.Avalonia.Themes.Fluent.csproj" />
```

Alternatively as a NuGet package:
```xml
<PackageReference Include="Dock.Avalonia.Themes.Fluent" Version="11.3.0.15" />
```

#### 2. Application Styles

```xml
<!-- App.axaml -->
<Application.Styles>
  <FluentTheme />  <!-- ← Avalonia's base Fluent theme -->

  <DockFluentTheme />  <!-- ← Dock-specific theme that extends FluentTheme -->
</Application.Styles>
```

**Order Matters:**
1. `FluentTheme` provides base Avalonia controls styling
2. `DockFluentTheme` adds dock-specific control templates and resources

#### 3. What DockFluentTheme Includes

From `DockFluentTheme.axaml`:

```xml
<Styles.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <!-- Control templates for all dock control types -->
      <ResourceInclude Source="/Controls/DocumentTabStripItem.axaml" />
      <ResourceInclude Source="/Controls/DocumentTabStrip.axaml" />
      <ResourceInclude Source="/Controls/ToolTabStripItem.axaml" />
      <ResourceInclude Source="/Controls/ToolTabStrip.axaml" />
      <ResourceInclude Source="/Controls/DockTarget.axaml" />
      <ResourceInclude Source="/Controls/DocumentControl.axaml" />
      <ResourceInclude Source="/Controls/ToolControl.axaml" />
      <ResourceInclude Source="/Controls/DockControl.axaml" />
      <ResourceInclude Source="/Controls/HostWindow.axaml" />
      <!-- ... 30+ more control templates ... -->

      <!-- Color palette and theme resources -->
      <ResourceInclude Source="/Accents/Fluent.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Styles.Resources>
```

#### 4. Theme Resources (Fluent.axaml)

```xml
<!-- Color Palette -->
<SolidColorBrush x:Key="DockApplicationAccentBrushLow">#007ACC</SolidColorBrush>
<SolidColorBrush x:Key="DockApplicationAccentBrushMed">#1C97EA</SolidColorBrush>
<SolidColorBrush x:Key="DockApplicationAccentBrushHigh">#52B0EF</SolidColorBrush>
<SolidColorBrush x:Key="DockApplicationAccentForegroundBrush">#F0F0F0</SolidColorBrush>

<!-- Theme-Aware Brushes (respond to Light/Dark mode) -->
<SolidColorBrush x:Key="DockThemeBorderLowBrush"
                 Color="{DynamicResource SystemBaseMediumLowColor}" />
<SolidColorBrush x:Key="DockThemeBackgroundBrush"
                 Color="{DynamicResource RegionColor}" />
<SolidColorBrush x:Key="DockThemeForegroundBrush"
                 Color="{DynamicResource SystemBaseHighColor}" />

<!-- Icon Geometries -->
<StreamGeometry x:Key="DockIconAddDocumentGeometry">M8.41687 7.57953V2.41851...</StreamGeometry>
<StreamGeometry x:Key="DockIconCloseGeometry">M19,6.41L17.59,5L12,10.59...</StreamGeometry>
<StreamGeometry x:Key="DockIconPinGeometry">m0 1345.575 218.834 0 0-1121.5042...</StreamGeometry>
<!-- ... many more icon geometries ... -->

<!-- Typography -->
<sys:Double x:Key="DockFontSizeNormal">12</sys:Double>
```

#### 5. Usage in Application

**Window-Level Theme Usage:**

```xml
<!-- MainWindow.axaml -->
<Window Foreground="{DynamicResource DockThemeForegroundBrush}"
        BorderBrush="{DynamicResource DockThemeBorderLowBrush}">
  <!-- ... -->
</Window>
```

**Custom Control Styling:**

```xml
<!-- You can override theme resources -->
<Style Selector="DockControl">
  <Setter Property="Foreground" Value="{DynamicResource DockThemeForegroundBrush}" />
</Style>
```

#### 6. Dark Mode Support

The theme automatically responds to `RequestedThemeVariant`:

```xml
<!-- App.axaml -->
<Application RequestedThemeVariant="Light">
  <!-- OR -->
  <Application RequestedThemeVariant="Dark">
```

All `DockTheme*` brushes that reference `{DynamicResource}` colors will automatically switch.

### Key Insight

DockFluentTheme is **comprehensive** - it styles every visual element in the Dock system. You don't need to create custom templates for `DocumentControl`, `ToolControl`, `TabStrip`, etc. - they're all handled.

**What You Still Style Yourself:**
- The **content** of your documents/tools (e.g., `FileView.axaml` controls the TextBox appearance)
- Application chrome (menus, toolbars outside the dock)

**What DockFluentTheme Styles:**
- All dock infrastructure (tab strips, splitters, drag targets, floating windows)
- Dock-specific icons and chrome

---

## Putting It All Together

Here's how all three patterns work together in the Notepad sample:

### 1. Application Startup (`App.axaml`)

```xml
<Application xmlns="https://github.com/avaloniaui"
             Name="Notepad"
             RequestedThemeVariant="Light">

  <!-- StaticViewLocator: Resolve Views from ViewModels -->
  <Application.DataTemplates>
    <local:ViewLocator />
  </Application.DataTemplates>

  <!-- ControlRecycling: Cache Views for reuse -->
  <Application.Resources>
    <ControlRecycling x:Key="ControlRecyclingKey" TryToUseIdAsKey="True" />
  </Application.Resources>

  <Application.Styles>
    <!-- Base Avalonia theme -->
    <FluentTheme />

    <!-- Dock-specific styling -->
    <DockFluentTheme />

    <!-- Connect ControlRecycling to DockControl -->
    <Style Selector="DockControl">
      <Setter Property="(ControlRecyclingDataTemplate.ControlRecycling)"
              Value="{StaticResource ControlRecyclingKey}" />
    </Style>
  </Application.Styles>

</Application>
```

### 2. ViewModel Creation (`MainWindowViewModel.cs`)

```csharp
public class MainWindowViewModel : ObservableObject
{
    public IRootDock? Layout { get; set; }

    public MainWindowViewModel()
    {
        var factory = new NotepadFactory();
        Layout = factory.CreateLayout();
        factory.InitLayout(Layout);
    }
}
```

### 3. Factory Creates Dockables (`NotepadFactory.cs`)

```csharp
public class NotepadFactory : Factory
{
    public override IRootDock CreateLayout()
    {
        // Create document ViewModels with unique IDs for recycling
        var fileDoc = new FileViewModel
        {
            Id = "File1",  // ← Used by ControlRecycling cache
            Title = "Untitled",
            Text = ""
        };

        var documentDock = new DocumentDock
        {
            Id = "Files",
            VisibleDockables = CreateList<IDockable>(fileDoc)
        };

        // ... create layout hierarchy ...

        return rootDock;
    }
}
```

### 4. View Resolution Flow

```
User Opens Document
    ↓
DockControl needs View for FileViewModel
    ↓
DockControl checks ControlRecycling cache[fileViewModel.Id]
    ↓
If CACHE HIT:
    Return cached FileView

If CACHE MISS:
    ↓
    Query ViewLocator (IDataTemplate)
        ↓
    StaticViewLocator.Build(fileViewModel)
        ↓
    Lookup s_views[typeof(FileViewModel)]
        ↓
    Returns: () => new FileView()
        ↓
    Create FileView instance
        ↓
    ControlRecycling stores: cache["File1"] = fileViewInstance
        ↓
    Return fileViewInstance
```

### 5. View Rendering (`MainView.axaml`)

```xml
<UserControl>
  <Grid RowDefinitions="Auto,*">
    <MenuView Grid.Row="0" />

    <!-- DockControl uses all three systems: -->
    <!-- - DockFluentTheme styles it -->
    <!-- - ViewLocator resolves Views from Layout ViewModels -->
    <!-- - ControlRecycling caches those Views -->
    <DockControl Layout="{Binding Layout}" Grid.Row="1" />
  </Grid>
</UserControl>
```

---

## Recommendations for CST.Avalonia

Based on this analysis, here's what CST.Avalonia should adopt:

### ✅ Already Using

1. **DockFluentTheme** - CST already uses this pattern:
   ```xml
   <StyleInclude Source="avares://Dock.Avalonia/Themes/DockFluentTheme.axaml" />
   ```

### 🔄 Should Adopt

2. **ControlRecycling** - **Highly Recommended**

   **Why:** CST users open/close books frequently. ControlRecycling would:
   - Preserve scroll position when switching between books
   - Maintain search highlights when reopening a previously viewed book
   - Avoid re-rendering WebView content unnecessarily

   **Implementation:**
   ```xml
   <!-- App.axaml -->
   <Application.Resources>
     <ControlRecycling x:Key="ControlRecyclingKey" TryToUseIdAsKey="True" />
   </Application.Resources>

   <Application.Styles>
     <Style Selector="DockControl">
       <Setter Property="(ControlRecyclingDataTemplate.ControlRecycling)"
               Value="{StaticResource ControlRecyclingKey}" />
     </Style>
   </Application.Styles>
   ```

   **ViewModel Changes:**
   ```csharp
   // BookDisplayViewModel - ensure unique IDs
   public BookDisplayViewModel(Book book, ...)
   {
       Id = $"Book_{book.Index}_{book.FileName}";  // Unique per book
       Title = book.Title;
       // ...
   }
   ```

3. **StaticViewLocator** - **Nice to Have**

   **Current:** CST probably uses reflection-based ViewLocator

   **Benefits of StaticViewLocator:**
   - Better for Native AOT if CST ever targets that
   - Slightly faster startup
   - Compile-time safety (missing Views cause build errors)

   **Trade-off:**
   - Requires strict ViewModel/View naming convention
   - Adds source generator build step

   **Recommendation:** Consider for CST 6.0 or later - not critical for current architecture

---

## References

- **Dock Documentation**: `/Users/fsnow/github/wieslawsoltes/Dock/docs/dock-control-recycling.md`
- **Notepad Sample**: `/Users/fsnow/github/wieslawsoltes/Dock/samples/Notepad/`
- **StaticViewLocator**: NuGet package with source generator
- **DockFluentTheme**: `/Users/fsnow/github/wieslawsoltes/Dock/src/Dock.Avalonia.Themes.Fluent/`

---

## Summary

| Pattern | Purpose | CST Priority |
|---------|---------|--------------|
| **ControlRecycling** | Reuse Views to preserve state & improve performance | **HIGH** - Directly improves book switching UX |
| **StaticViewLocator** | AOT-compatible compile-time View-ViewModel mapping | **LOW** - Nice optimization, not critical |
| **DockFluentTheme** | Complete dock control styling system | **DONE** - Already in use |

The most impactful addition would be **ControlRecycling** with ID-based caching, which would preserve book scroll positions and WebView state when users switch between open documents.
