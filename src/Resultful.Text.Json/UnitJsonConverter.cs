using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resultful.Text.Json;

/// <summary>Converts <see cref="Unit"/> as <c>{}</c>. Reading accepts only an empty object.</summary>
public sealed class UnitJsonConverter : JsonConverter<Unit>
{
    /// <summary>Creates the converter.</summary>
    public UnitJsonConverter()
    {
    }

    /// <inheritdoc />
    public override Unit Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadUnit(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Unit value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteUnit(writer);
    }

    internal static void WriteUnit(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteEndObject();
    }

    internal static Unit ReadUnit(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject || !reader.Read() || reader.TokenType != JsonTokenType.EndObject)
            throw new JsonException("Unit is written as {}.");
        return Unit.Value;
    }
}
