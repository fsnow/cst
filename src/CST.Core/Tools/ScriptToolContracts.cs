using System.Collections.Generic;
using CST.Conversion;

namespace CST.Tools
{
    /// <summary>
    /// A stateless script-conversion tool (AI_INTEGRATION.md surface C, #240). Converts Pāli text to a target
    /// script; the input is always auto-detected. The converters detect and convert per character-run, so
    /// mixed-script input is handled correctly in a single pass — the caller only names the desired output.
    /// No corpus/index — a thin wrapper over <see cref="ScriptConverter"/>.
    /// </summary>
    public interface IScriptTool
    {
        /// <summary>The script names that can be requested as output — excludes the auto-detect sentinel.</summary>
        IReadOnlyList<string> Scripts { get; }

        /// <summary>Convert <see cref="ConvertRequest.Text"/> to <see cref="ConvertRequest.OutputScript"/>.</summary>
        ConvertResult Convert(ConvertRequest request);
    }

    /// <summary>A conversion request — text plus the desired output script. Input is auto-detected (mixed-script safe).</summary>
    public sealed record ConvertRequest(
        string Text,
        Script OutputScript = Script.Latin);

    /// <summary>The converted text.</summary>
    public sealed record ConvertResult(
        string Text,
        Script OutputScript);
}
