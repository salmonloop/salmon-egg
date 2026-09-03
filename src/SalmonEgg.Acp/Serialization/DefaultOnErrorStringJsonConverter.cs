using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Serialization;

/// <summary>
/// Reads an optional string field, degrading a value of the wrong JSON type to "not provided".
/// </summary>
/// <remarks>
/// Implements <c>x-deserialize-default-on-error</c> for string fields such as
/// <c>SessionMode.description</c> and <c>SessionConfigOption.category</c>. Apply per property, not per
/// type: the marker belongs to the field, so a string read at an unmarked field must still fail loudly.
/// See <c>src/SalmonEgg.Acp/SchemaTolerance.Fields.txt</c> for the marked set and AGENTS.md
/// "protocol looseness must not be tightened in reverse" for why the degradation is required.
/// </remarks>
internal sealed class DefaultOnErrorStringJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            // Consume the offending value so the caller resumes on the next token.
            reader.Skip();
            return null;
        }

        return reader.GetString();
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}
