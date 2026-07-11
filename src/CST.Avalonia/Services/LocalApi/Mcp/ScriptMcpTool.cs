using System.Collections.Generic;
using System.ComponentModel;
using CST.Tools;
using ModelContextProtocol.Server;

namespace CST.Avalonia.Services.LocalApi.Mcp
{
    /// <summary>
    /// MCP script tools over <see cref="IScriptTool"/> — <c>convert</c> (transliterate Pali between scripts,
    /// input auto-detected) and <c>scripts</c> (list the available output scripts). A thin, stateless wrapper
    /// over the converter; no corpus. (#191)
    /// </summary>
    [McpServerToolType]
    internal sealed class ScriptMcpTool
    {
        [McpServerTool(Name = "convert")]
        [Description("Transliterate Pali text to another script. The input script is auto-detected (mixed-script "
            + "input is handled), so you only name the desired output. All scripts convert losslessly EXCEPT "
            + "Cyrillic, a legacy transliteration with a known non-reversibility (revision planned) — its output "
            + "is expected, not a defect.")]
        public static ConvertResult Convert(
            IScriptTool script,
            [Description("The Pali text to convert (any script; auto-detected).")]
            string text,
            [Description("The script to convert the text into.")]
            OutputScript outputScript = OutputScript.Latin)
            => script.Convert(new ConvertRequest(text ?? string.Empty, McpScript.ToScript(outputScript)));

        [McpServerTool(Name = "scripts")]
        [Description("List the script names that can be requested as an output script by the other tools.")]
        public static IReadOnlyList<string> Scripts(IScriptTool script) => script.Scripts;
    }
}
