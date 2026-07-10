using System;
using CST.Conversion;

namespace CST.Avalonia.Services.LocalApi.Mcp
{
    /// <summary>
    /// Agent-facing output scripts — the corpus's 14 scripts + Latin. Excludes the internal IPE font encoding
    /// and the auto-detect sentinel, so the MCP tool schema advertises a closed <c>enum</c> (findable by a
    /// small model) instead of a bare string. Member names match <see cref="Script"/> exactly.
    /// </summary>
    internal enum OutputScript
    {
        Latin,
        Devanagari,
        Bengali,
        Cyrillic,
        Gujarati,
        Gurmukhi,
        Kannada,
        Khmer,
        Malayalam,
        Myanmar,
        Sinhala,
        Telugu,
        Thai,
        Tibetan
    }

    internal static class McpScript
    {
        /// <summary>Map an agent-facing <see cref="OutputScript"/> to the internal <see cref="Script"/>
        /// (names match, so parse by name).</summary>
        public static Script ToScript(OutputScript s) => Enum.Parse<Script>(s.ToString());
    }
}
