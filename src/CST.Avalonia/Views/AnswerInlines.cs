using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using CST.Avalonia.Services.Ai;
using AvaloniaTextBlock = Avalonia.Controls.TextBlock;

namespace CST.Avalonia.Views;

/// <summary>
/// Binds parsed answer spans onto a text control's <c>Inlines</c>. (#586)
///
/// <para><b>One control, many runs — not one control per block.</b> A reader copies translations out of this
/// panel, and a drag-selection cannot cross separate controls in Avalonia. Rendering the answer as styled
/// <see cref="Run"/>s inside a single <c>SelectableTextBlock</c> is what keeps "select the whole answer and
/// copy it" working, and it is the same shape v1.1 needs in order to render Pāli quote spans in the reader's
/// own script.</para>
/// </summary>
public static class AnswerInlines
{
    public static readonly AttachedProperty<IReadOnlyList<AnswerSpan>?> SpansProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaTextBlock, IReadOnlyList<AnswerSpan>?>(
            "Spans", typeof(AnswerInlines));

    public static void SetSpans(AvaloniaTextBlock element, IReadOnlyList<AnswerSpan>? value) =>
        element.SetValue(SpansProperty, value);

    public static IReadOnlyList<AnswerSpan>? GetSpans(AvaloniaTextBlock element) =>
        element.GetValue(SpansProperty);

    static AnswerInlines()
    {
        SpansProperty.Changed.AddClassHandler<AvaloniaTextBlock>((control, args) =>
            Apply(control, args.NewValue as IReadOnlyList<AnswerSpan>));
    }

    private static void Apply(AvaloniaTextBlock control, IReadOnlyList<AnswerSpan>? spans)
    {
        var inlines = control.Inlines;
        if (inlines is null) return;

        inlines.Clear();
        if (spans is null) return;

        foreach (var span in spans)
        {
            var run = new Run(span.Text);

            if (span.Style.HasFlag(AnswerStyle.Bold)) run.FontWeight = FontWeight.SemiBold;
            if (span.Style.HasFlag(AnswerStyle.Italic)) run.FontStyle = FontStyle.Italic;
            if (span.Style.HasFlag(AnswerStyle.Monospace)) run.FontFamily = FontFamily.Parse("monospace");

            inlines.Add(run);
        }
    }
}
