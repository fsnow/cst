using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using CST.Conversion;

namespace CST.Avalonia.Services.LocalApi
{
    /// <summary>
    /// Boundary policy for the <see cref="Script"/> enum on the agent-facing API: accept the public output
    /// scripts by name (case-insensitive), but REJECT <see cref="Script.Ipe"/> (a legacy CSCD *font* encoding
    /// that emits non-Unicode glyph bytes) and <see cref="Script.Unknown"/> (the auto-detect *input* sentinel,
    /// never a valid output). IPE is purely internal and must never be exposed to clients. Rejecting on read
    /// turns a bad <c>outputScript</c> into a clean 400 instead of glyph soup — for every body endpoint at once,
    /// since they all deserialize <c>outputScript</c> through this. (#186 cold test)
    /// </summary>
    internal sealed class ScriptJsonConverter : JsonConverter<Script>
    {
        public override Script Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // A non-string token (e.g. `"outputScript": 15`) throws InvalidOperationException from GetString(),
            // which the minimal-API binding surfaces as a 500 rather than the intended 400 — so reject it here. (#304)
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("outputScript must be a script name (a string). See GET /v1/scripts for valid values.");
            string? name = reader.GetString();
            // Enum.TryParse accepts numeric strings ("99"), yielding an UNDEFINED Script that isn't Ipe/Unknown and
            // falls through the converter to empty output — require a DEFINED value. (#304)
            if (!Enum.TryParse<Script>(name, ignoreCase: true, out var script)
                || !Enum.IsDefined(script)
                || script is Script.Ipe or Script.Unknown)
                throw new JsonException($"Unknown or unsupported script '{name}'. See GET /v1/scripts for valid values.");
            return script;
        }

        public override void Write(Utf8JsonWriter writer, Script value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}
