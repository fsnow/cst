using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using CST.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace CST.Avalonia.Views;

public partial class DictionaryPanel : UserControl
{
    private IDisposable? _meaningSub;

    public DictionaryPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DataContext = App.ServiceProvider?.GetService<DictionaryViewModel>() ?? new DictionaryViewModel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _meaningSub?.Dispose();
        _meaningSub = null;
        if (DataContext is DictionaryViewModel vm)
        {
            // Definitions are rendered as native inlines (plain text + clickable <see> links); the
            // Inlines collection can't be bound, so we rebuild it whenever the selection changes.
            // MeaningSegments changes on the UI thread (SelectedWord is set inside a UI-thread dispatch).
            _meaningSub = vm.WhenAnyValue(x => x.MeaningSegments)
                .Subscribe(segments => BuildMeaning(vm, segments));
        }
    }

    private void BuildMeaning(DictionaryViewModel vm, IReadOnlyList<MeaningSegment> segments)
    {
        var text = this.FindControl<TextBlock>("MeaningText");
        if (text?.Inlines == null)
            return;

        text.Inlines.Clear();
        foreach (var seg in segments)
        {
            if (seg.IsLink)
            {
                var link = new TextBlock { Text = seg.Text };
                link.Classes.Add("dict-link");
                var target = seg.Target ?? seg.Text;
                link.PointerPressed += (_, _) => vm.NavigateToWordCommand.Execute(target).Subscribe();
                text.Inlines.Add(new InlineUIContainer(link));
            }
            else
            {
                text.Inlines.Add(new Run(seg.Text));
            }
        }
    }
}
