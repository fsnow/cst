# Reactive Dock ViewModels

**Location**: `src/CST.Avalonia/ViewModels/Dock/`
**Purpose**: Intermediate base classes that combine Dock's dockable controls with ReactiveUI's `ReactiveObject`

## Overview

The CST.Avalonia project uses `Dock.Model.Mvvm` which provides `Document` and `Tool` base classes that extend `ObservableObject` from CommunityToolkit.Mvvm. However, our ViewModels need ReactiveUI functionality (`IReactiveObject`) to use methods like `WhenAnyValue()`, `ReactiveCommand.Create()`, and other ReactiveUI patterns.

These intermediate base classes solve this problem by:
1. Extending `ReactiveObject` (providing `IReactiveObject` interface)
2. Implementing `IDocument` and `ITool` interfaces (Dock compatibility)
3. Implementing all `IDockable` members (full Dock functionality)

## Classes

### ReactiveDocument

Base class for document-style ViewModels that need both:
- ReactiveUI functionality (property change notifications, reactive commands, etc.)
- Dock layout system compatibility (IDocument interface)

**Usage:**
```csharp
public class MyDocumentViewModel : ReactiveDocument
{
    private string _content;
    public string Content
    {
        get => _content;
        set => this.RaiseAndSetIfChanged(ref _content, value);
    }

    public MyDocumentViewModel()
    {
        // All ReactiveUI methods are available
        this.WhenAnyValue(x => x.Content)
            .Subscribe(content => Console.WriteLine($"Content changed: {content}"));
    }
}
```

### ReactiveTool

Base class for tool-style ViewModels that need both:
- ReactiveUI functionality (property change notifications, reactive commands, etc.)
- Dock layout system compatibility (ITool interface)

**Usage:**
```csharp
public class MyToolViewModel : ReactiveTool
{
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

    public MyToolViewModel()
    {
        ToggleCommand = ReactiveCommand.Create(() => IsExpanded = !IsExpanded);
    }
}
```

## Migration Guide

### Before (Using Dock.Model.Mvvm.Controls.Document)

```csharp
using Dock.Model.Mvvm.Controls;

public class BookViewModel : Document
{
    private string _title;
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value); // CommunityToolkit.Mvvm method
    }
}
```

### After (Using ReactiveDocument)

```csharp
using CST.Avalonia.ViewModels.Dock;

public class BookViewModel : ReactiveDocument
{
    private string _title;
    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value); // ReactiveUI method
    }

    public BookViewModel()
    {
        // Now you can use ReactiveUI features!
        this.WhenAnyValue(x => x.Title)
            .Subscribe(title => Console.WriteLine($"Title: {title}"));
    }
}
```

## Key Benefits

1. **ReactiveUI Integration**: Full access to `IReactiveObject` methods and reactive programming patterns
2. **Dock Compatibility**: Works seamlessly with Dock.Avalonia layout system
3. **Type Safety**: Proper interface implementation ensures compile-time safety
4. **Single Inheritance**: No need for complex multiple inheritance or interface implementations in your ViewModels

## Technical Details

Both classes:
- Extend `ReactiveObject` from ReactiveUI
- Implement `IDocument` or `ITool` from Dock.Model.Controls
- Implement all `IDockable` properties and methods
- Use `RaiseAndSetIfChanged()` for property setters (ReactiveUI pattern)
- Include `[DataContract]` attributes for serialization support
- Support all Dock features (docking, pinning, floating, bounds tracking, etc.)

## Alternative Approach

If you prefer to use the official Dock.Model.ReactiveUI package instead, you can:

1. Add package reference to your .csproj:
   ```xml
   <PackageReference Include="Dock.Model.ReactiveUI" Version="11.3.0.15" />
   ```

2. Use the built-in classes:
   ```csharp
   using Dock.Model.ReactiveUI.Controls;

   public class MyViewModel : Document // Already includes ReactiveObject
   {
       // ...
   }
   ```

However, the custom base classes in this directory give you more control over the implementation and avoid adding another package dependency.
