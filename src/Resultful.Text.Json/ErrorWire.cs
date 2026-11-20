using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Resultful.Text.Json;

/// <summary>
/// The error object: <c>{"type","message","context","cause"?,…leaf properties}</c>, in that order. The leaf's own
/// properties come from its <see cref="JsonTypeInfo"/> (reflection or a source-generated context, whichever the options
/// use), so nothing here reflects over the leaf.
/// </summary>
internal static class ErrorWire
{
    private const string TypeKey = "type";
    private const string MessageKey = "message";
    private const string ContextKey = "context";
    private const string CauseKey = "cause";

    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> MetadataOptions = [];

    /// <summary>The leaf's simple name, without a generic arity (<c>ValidationFailed`1</c> is <c>ValidationFailed</c>).</summary>
    public static string WireName(Type leaf)
    {
        var name = leaf.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }

    public static void Write(Utf8JsonWriter writer, Error error, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(TypeKey, WireName(error.GetType()));
        writer.WriteString(MessageKey, error.Message);
        writer.WriteStartArray(ContextKey);
        foreach (var note in error.Context) writer.WriteStringValue(note);
        writer.WriteEndArray();
        if (error.Cause is { } cause)
        {
            writer.WritePropertyName(CauseKey);
            Write(writer, cause, options);
        }

        WriteLeafProperties(writer, error, options);
        writer.WriteEndObject();
    }

    private static void WriteLeafProperties(Utf8JsonWriter writer, Error error, JsonSerializerOptions options)
    {
        var info = Metadata(options).GetTypeInfo(error.GetType());
        if (info.Kind != JsonTypeInfoKind.Object) return;
        foreach (var property in info.Properties)
        {
            if (property.Get is null || IsBaseProperty(property.Name, options)) continue;
            if (IsWireKey(property.Name))
                throw new InvalidOperationException($"{error.GetType().Name}.{property.Name} would be written as \"{property.Name}\", which the error object already uses. Rename it or mark it [JsonPropertyName].");
            var value = property.Get(error);
            if (property.ShouldSerialize is { } shouldSerialize && !shouldSerialize(error, value)) continue;
            if (value is null && options.DefaultIgnoreCondition is JsonIgnoreCondition.WhenWritingNull or JsonIgnoreCondition.WhenWritingDefault) continue;
            writer.WritePropertyName(property.Name);
            WriteMember(writer, value, property.PropertyType, options);
        }
    }

    private static void WriteMember(Utf8JsonWriter writer, object? value, Type declared, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case Error error:
                Write(writer, error, options);
                break;
            case IErrorUnion union:
                Write(writer, union.Error, options);
                break;
            case IEnumerable<Error> errors:
                writer.WriteStartArray();
                foreach (var item in errors) Write(writer, item, options);
                writer.WriteEndArray();
                break;
            default:
                JsonSerializer.Serialize(writer, value, options.GetTypeInfo(declared));
                break;
        }
    }

    public static Error Read(ref Utf8JsonReader reader, Type declared, IReadOnlyList<Type> candidates, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected an error object for {declared.Name}, found {reader.TokenType}.");
        var node = JsonNode.Parse(ref reader, new JsonNodeOptions { PropertyNameCaseInsensitive = options.PropertyNameCaseInsensitive });
        return FromNode((JsonObject)node!, declared, candidates, options);
    }

    private static Error FromNode(JsonObject json, Type declared, IReadOnlyList<Type> candidates, JsonSerializerOptions options)
    {
        var typeName = json[TypeKey] is JsonValue typeValue && typeValue.GetValueKind() == JsonValueKind.String
            ? typeValue.GetValue<string>()
            : throw new JsonException($"An error object for {declared.Name} has no \"type\".");
        var leafType = Resolve(typeName, candidates)
            ?? throw new JsonException($"Unknown error type \"{typeName}\" for {declared.Name}. Register its leaves with AddErrorCases<{declared.Name}>(…).");

        var context = ReadContext(json[ContextKey], declared);
        var causeJson = json[CauseKey] switch
        {
            null => null,
            JsonObject o => o,
            _ => throw new JsonException($"The \"cause\" of a {typeName} must be an error object or absent."),
        };

        if (leafType == typeof(ValidationFailed<Error>))
        {
            var errors = json[Name(nameof(ValidationFailed<Error>.Errors), options)] as JsonArray ?? throw new JsonException("A ValidationFailed has no errors.");
            var items = new List<Error>(errors.Count);
            foreach (var item in errors)
            {
                items.Add(FromNode(item as JsonObject ?? throw new JsonException("Each of \"errors\" must be an error object."), typeof(Error), ErrorCaseRegistry.Candidates(options, typeof(Error)), options));
            }

            if (items.Count == 0) throw new JsonException("A ValidationFailed has at least one error.");
            var cause = causeJson is null ? null : FromNode(causeJson, typeof(Error), ErrorCaseRegistry.Candidates(options, typeof(Error)), options);
            return new ValidationFailed<Error>(items) { Context = context, Cause = cause };
        }

        var info = Metadata(options).GetTypeInfo(leafType);
        if (info.Kind != JsonTypeInfoKind.Object)
            throw new JsonException($"{leafType.Name} has no object metadata, so it can't be read as an error.");

        // Context and cause go in with the leaf's own members: they are init-only, which a source-generated setter
        // can't set afterwards, but its object creation can. A cause is read back by CauseConverter.
        var leafJson = new JsonObject(new JsonNodeOptions { PropertyNameCaseInsensitive = options.PropertyNameCaseInsensitive });
        foreach (var (key, value) in json)
        {
            if (key is TypeKey or ContextKey or CauseKey) continue;
            leafJson[key == MessageKey ? Name(nameof(Error.Message), options) : key] = value?.DeepClone();
        }

        leafJson[Name(nameof(Error.Context), options)] = new JsonArray([.. context.Select(note => JsonValue.Create(note))]);
        if (causeJson is not null) leafJson[Name(nameof(Error.Cause), options)] = causeJson.DeepClone();

        return leafJson.Deserialize(info) as Error ?? throw new JsonException($"{typeName} read back as null.");
    }

    private static string[] ReadContext(JsonNode? node, Type declared)
    {
        switch (node)
        {
            case null:
                return [];
            case JsonArray array:
                var notes = new string[array.Count];
                for (var i = 0; i < notes.Length; i++)
                {
                    notes[i] = array[i] is JsonValue v && v.GetValueKind() == JsonValueKind.String
                        ? v.GetValue<string>()
                        : throw new JsonException($"The \"context\" of a {declared.Name} must be an array of strings.");
                }

                return notes;
            default:
                throw new JsonException($"The \"context\" of a {declared.Name} must be an array of strings.");
        }
    }

    private static Type? Resolve(string typeName, IReadOnlyList<Type> candidates)
    {
        Type? found = null;
        foreach (var leaf in candidates)
        {
            if (!string.Equals(WireName(leaf), typeName, StringComparison.Ordinal)) continue;
            if (found is not null && found != leaf)
                throw new JsonException($"\"{typeName}\" could be {found.FullName} or {leaf.FullName}; register only one of them for this error type.");
            found = leaf;
        }

        return found;
    }

    private static string Name(string member, JsonSerializerOptions options) => options.PropertyNamingPolicy?.ConvertName(member) ?? member;

    private static bool IsBaseProperty(string name, JsonSerializerOptions options) =>
        name == Name(nameof(Error.Message), options) || name == Name(nameof(Error.Context), options) || name == Name(nameof(Error.Cause), options);

    private static bool IsWireKey(string name) => name is TypeKey or MessageKey or ContextKey or CauseKey;

    /// <summary>
    /// The options without Resultful's error converters, so that a leaf's metadata lists its properties instead of
    /// pointing back at the converter, plus <see cref="CauseConverter"/> for <see cref="Error.Cause"/>. Values are
    /// still written with the caller's options.
    /// </summary>
    private static JsonSerializerOptions Metadata(JsonSerializerOptions options) =>
        MetadataOptions.GetValue(options, static source =>
        {
            var copy = new JsonSerializerOptions(source);
            for (var i = copy.Converters.Count - 1; i >= 0; i--)
            {
                if (copy.Converters[i] is ResultfulJsonConverterFactory or IErrorConverter) copy.Converters.RemoveAt(i);
            }

            copy.Converters.Add(new CauseConverter());
            return copy;
        });
}

/// <summary>
/// Reads and writes a member declared as <see cref="Error"/> (a leaf's <see cref="Error.Cause"/>) inside a leaf's own
/// metadata. Only the abstract base: a concrete leaf keeps its object metadata. Not generic, so AOT-safe.
/// </summary>
internal sealed class CauseConverter : JsonConverter<Error>
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(Error);

    public override Error Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ErrorWire.Read(ref reader, typeof(Error), ErrorCaseRegistry.Candidates(options, typeof(Error)), options);

    public override void Write(Utf8JsonWriter writer, Error value, JsonSerializerOptions options) => ErrorWire.Write(writer, value, options);
}

/// <summary>Marks the converters that write an <see cref="Error"/> themselves.</summary>
internal interface IErrorConverter
{
    IReadOnlyList<Type>? Cases { get; }
}
