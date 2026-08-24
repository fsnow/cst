namespace CST.Avalonia.ViewModels
{
    /// <summary>
    /// A document whose tab title is Pāli, and so wants the reader's font for the script it is shown in.
    /// (#836)
    ///
    /// <para><b>Why an interface rather than a property on the base document.</b> The tab strip's style is a
    /// compiled binding, which needs one type to bind against. Before this, that type was
    /// <c>BookDisplayViewModel</c> — which worked for books and silently did nothing for every other tab,
    /// leaving Welcome in the theme's UI font beside book titles in the reader's Latin face. A tab strip
    /// where one tab is furniture and the rest are text reads as a mistake, because it is one.</para>
    ///
    /// <para>Implementations answer for themselves which script they are in: a book reports the script it is
    /// displayed in, which is per-tab and not the toolbar's; Welcome reports Latin, because that is what its
    /// title is written in.</para>
    /// </summary>
    public interface IScriptFontedDocument
    {
        /// <summary>The font face for this document's script, or a fallback when none is resolvable.</summary>
        string CurrentScriptFontFamily { get; }

        /// <summary>The font size for this document's script.</summary>
        int CurrentScriptFontSize { get; }
    }
}
