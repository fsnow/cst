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
            string? name = reader.GetString();
            if (!Enum.TryParse<Script>(name, ignoreCase: true, out var script)
                || script is Script.Ipe or Script.Unknown)
                throw new JsonException($"Unknown or unsupported script '{name}'. See GET /v1/scripts for valid values.");
            return script;
        }

        public override void Write(Utf8JsonWriter writer, Script value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}
