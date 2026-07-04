namespace CST.Tools
{
    /// <summary>
    /// The complete surface-C tool set as a single facade (AI_INTEGRATION.md §4.2) — search, navigation +
    /// catalog, passage fetch, and dictionary. The local HTTP API and the in-app AI surface (B) both call
    /// this; the out-of-process MCP adapter proxies to the HTTP endpoints that expose it. Composed from the
    /// focused interfaces so each tool stays independently implementable and testable.
    /// </summary>
    public interface ICorpusTools : ISearchTool, INavigationTool, IPassageTool, IDictionaryTool
    {
    }
}
