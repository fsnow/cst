using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
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
        // Resolve the singleton VM. No `?? new DictionaryViewModel()` fallback: that ctor dereferences
        // App.ServiceProvider and would throw (also breaking the XAML previewer), never yielding a usable
        // instance. (DICT-6)
        DataContext = App.ServiceProvider?.GetService<DictionaryViewModel>();
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        WireMeaning();
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        // Drop the subscription when the panel leaves the tree (e.g. per float/unfloat) so a discarded
        // panel isn't kept alive by the app-lifetime singleton VM. Re-wired on re-attach. (DICT-5)
        _meaningSub?.Dispose();
        _meaningSub = null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e) => WireMeaning();

    private void WireMeaning()
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
            if (seg.IsSeparator)
            {
                // Break between the definitions of a merged (duplicate) headword: a blank line, so the
                // separator reads as a boundary instead of showing the raw sentinel (DICT-1).
                text.Inlines.Add(new LineBreak());
                text.Inlines.Add(new LineBreak());
            }
            else if (seg.IsLink)
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
