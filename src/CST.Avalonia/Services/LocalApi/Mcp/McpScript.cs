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
        /// <summary>Map an agent-facing <see cref="OutputScript"/> to the internal <see cref="Script"/> (names
        /// match, so parse by name). The MCP SDK's enum converter accepts INTEGERS, so an out-of-range value like
        /// <c>15</c> would otherwise <c>ToString()</c> to <c>"15"</c> and <c>Enum.Parse</c> into a wrong/undefined
        /// <see cref="Script"/> — including <see cref="Script.Ipe"/> (ordinal 15), leaking the internal encoding.
        /// Reject anything not a defined <see cref="OutputScript"/>. (#304)</summary>
        public static Script ToScript(OutputScript s) =>
            Enum.IsDefined(s)
                ? Enum.Parse<Script>(s.ToString())
                : throw new ArgumentException($"Unknown outputScript value '{(int)s}'. Use the 'scripts' tool for valid values.", nameof(s));
    }
}
