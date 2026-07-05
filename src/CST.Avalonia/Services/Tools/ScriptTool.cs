using System;
using System.Collections.Generic;
using System.Linq;
using CST.Conversion;
using CST.Tools;

namespace CST.Avalonia.Services.Tools
{
    /// <summary>
    /// <see cref="IScriptTool"/> over <see cref="ScriptConverter"/> (surface C, #240). Stateless: converts
    /// text to the requested script, always auto-detecting the input via <see cref="Script.Unknown"/> — so
    /// <see cref="ScriptConverter"/> splits mixed input into per-script runs and converts each correctly.
    /// </summary>
    public sealed class ScriptTool : IScriptTool
    {
        // Exclude Unknown (the auto-detect sentinel) and Ipe (a legacy CSCD *font* encoding that emits
        // non-Unicode glyph bytes, useless to an agent) from the advertised output scripts. (#186 cold test)
        private static readonly IReadOnlyList<string> AllScripts =
            Enum.GetNames<Script>()
                .Where(n => n != nameof(Script.Unknown) && n != nameof(Script.Ipe))
                .ToList();

        public IReadOnlyList<string> Scripts => AllScripts;

        public ConvertResult Convert(ConvertRequest request)
        {
            string converted = ScriptConverter.Convert(request.Text ?? string.Empty, Script.Unknown, request.OutputScript);
            return new ConvertResult(converted, request.OutputScript);
        }
    }
}
