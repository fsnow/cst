using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CST.Avalonia.Models
{
    /// <summary>
    /// Reads a connection's headers whether they were written as the current array of records or as the
    /// object-of-name-to-value that preceded it. (#771, #784)
    ///
    /// <para><b>Why this exists.</b> #771 changed the persisted shape from
    /// <c>Dictionary&lt;string, string&gt;</c> to <c>List&lt;AiHeaderRecord&gt;</c>, because a secret header
    /// has a name and no value in this file and a dictionary cannot say that. The change was made on the
    /// reasoning that no released build had ever written AI configuration, so there was no installed base to
    /// migrate. That was true and it was not the whole question: <c>SettingsService</c> reverts the
    /// <b>entire file</b> to defaults on any parse failure, so an older shape in one property discards every
    /// setting the file holds — fonts, layout, book directory, and every AI connection with the model lists
    /// the reader had built up by hand.</para>
    ///
    /// <para><b>An old value is not a corrupt file.</b> Deserialisation is the wrong place to be strict:
    /// refusing a shape we ourselves wrote last week costs the reader everything and tells them nothing. The
    /// old shape is unambiguous and maps exactly — a name and a plaintext value, never a secret, since the
    /// mark did not exist when it was written.</para>
    ///
    /// <para>Deliberately permanent rather than a migration to delete later. It is a dozen lines, it cannot
    /// misfire on a current file, and the alternative is that anyone restoring an old backup or moving a
    /// profile between machines silently loses their settings.</para>
    /// </summary>
    public sealed class AiHeaderRecordListConverter : JsonConverter<List<AiHeaderRecord>>
    {
        /// <summary>
        /// Without this, System.Text.Json assigns null for <c>"Headers": null</c> without consulting the
        /// converter at all — so the list is null rather than empty and the next thing to enumerate it throws.
        /// Found by the test for it, which is the shape of hole this whole class exists to close.
        /// </summary>
        public override bool HandleNull => true;

        public override List<AiHeaderRecord> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var headers = new List<AiHeaderRecord>();

            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return headers;

                // The shape before #771: {"X-Title":"CST Reader","cf-aig-authorization":"Bearer …"}.
                case JsonTokenType.StartObject:
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName) continue;

                        var name = reader.GetString() ?? "";
                        if (!reader.Read()) break;

                        headers.Add(new AiHeaderRecord
                        {
                            Name = name,
                            // Secret is false by construction: nothing could have marked one when this shape
                            // was written, so every value here is a plaintext value the reader can still see.
                            Value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null,
                        });
                    }
                    return headers;

                case JsonTokenType.StartArray:
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.StartObject) continue;

                        // Read the element with the converter's own options minus this converter, which would
                        // otherwise recurse on the element type.
                        if (JsonSerializer.Deserialize<AiHeaderRecord>(ref reader, Inner(options)) is { } record)
                            headers.Add(record);
                    }
                    return headers;

                default:
                    // Anything else is genuinely unreadable. Yielding no headers loses a routing hint the
                    // reader can retype; throwing here would lose the whole settings file, which is the
                    // failure this class exists to prevent.
                    reader.Skip();
                    return headers;
            }
        }

        public override void Write(
            Utf8JsonWriter writer, List<AiHeaderRecord> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var header in value)
                JsonSerializer.Serialize(writer, header, Inner(options));
            writer.WriteEndArray();
        }

        /// <summary>The same options without this converter, so serialising an element does not re-enter it.</summary>
        private static JsonSerializerOptions Inner(JsonSerializerOptions options)
        {
            var inner = new JsonSerializerOptions(options);
            for (int i = inner.Converters.Count - 1; i >= 0; i--)
                if (inner.Converters[i] is AiHeaderRecordListConverter)
                    inner.Converters.RemoveAt(i);
            return inner;
        }
    }
}
