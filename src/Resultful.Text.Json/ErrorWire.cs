using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Resultful.Text.Json;

/// <summary>
/// The error object: <c>{"type","message","context","cause"?,…leaf properties}</c>, in that order. The leaf's own
/// properties come from its <see cref="JsonTypeInfo"/> (reflection or a source-generated context, whichever the options
/// use), so nothing here reflects over the leaf. Reading streams from the caller's <see cref="Utf8JsonReader"/>: the
/// object is scanned once for its <c>"type"</c>, then read by the leaf's metadata, whose base members are renamed to the
/// wire keys.
/// </summary>
internal static class ErrorWire
{
    public const string TypeKey = "type";
    public const string MessageKey = "message";
    public const string ContextKey = "context";
    public const string CauseKey = "cause";

    private const string ValidationFailedPrefix = "ValidationFailed<";

    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> MetadataOptions = [];

    /// <summary>
    /// The leaf's simple name. A generic leaf adds its type arguments' wire names: <c>ValidationFailed&lt;PaymentError&gt;</c>,
    /// so that the closed type is read back.
    /// </summary>
    public static string WireName(Type leaf)
    {
        var name = leaf.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick < 0) return name;
        return name[..tick] + "<" + string.Join(",", leaf.GetGenericArguments().Select(WireName)) + ">";
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

        if (IsValidationFailed(error.GetType()))
        {
            // Written and read by hand, like its reading: no metadata needed for any ValidationFailed<X>.
            writer.WriteStartArray(ErrorCaseRegistry.Cache(options).ErrorsName);
            foreach (var item in error.Inner()) Write(writer, item, options);
            writer.WriteEndArray();
        }
        else
        {
            WriteLeafProperties(writer, error, options);
        }

        writer.WriteEndObject();
    }

    private static bool IsValidationFailed(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValidationFailed<>);

    private static void WriteLeafProperties(Utf8JsonWriter writer, Error error, JsonSerializerOptions options)
    {
        var info = Metadata(options).GetTypeInfo(error.GetType());
        if (info.Kind != JsonTypeInfoKind.Object) return;
        foreach (var property in info.Properties)
        {
            // The base members were renamed to the wire keys by FixWireNames, and are written above.
            if (property.Get is null || property.Name is MessageKey or ContextKey or CauseKey) continue;
            if (property.Name == TypeKey)
                throw new InvalidOperationException($"{error.GetType().Name} has a member written as \"type\", which the error object already uses. Rename it or mark it [JsonPropertyName].");
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

        var typeName = Scan(reader, declared, options);
        if (typeName.StartsWith(ValidationFailedPrefix, StringComparison.Ordinal) && typeName.EndsWith('>') && declared.IsAssignableFrom(typeof(ValidationFailed<Error>)))
        {
            return ReadValidationFailed(ref reader, typeName[ValidationFailedPrefix.Length..^1], options);
        }

        var leafType = Resolve(typeName, candidates)
            ?? throw new JsonException($"Unknown error type \"{typeName}\" for {declared.Name}. Register its leaves with AddErrorCases<{declared.Name}>(…).");
        var info = Metadata(options).GetTypeInfo(leafType);
        if (info.Kind != JsonTypeInfoKind.Object)
            throw new JsonException($"{leafType.Name} has no object metadata, so it can't be read as an error.");
        return JsonSerializer.Deserialize(ref reader, info) as Error ?? throw new JsonException($"{typeName} read back as null.");
    }

    /// <summary>
    /// Reads ahead on a copy of the reader: the object's <c>"type"</c>, and checks the wire keys. The caller's reader
    /// doesn't move. A converter is given the whole value buffered, so reading ahead is safe.
    /// </summary>
    private static string Scan(Utf8JsonReader reader, Type declared, JsonSerializerOptions options)
    {
        var comparison = options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var seen = new HashSet<string>(options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        string? typeName = null;
        var hasMessage = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString()!;
            if (!seen.Add(key)) throw new JsonException($"An error object for {declared.Name} repeats \"{key}\".");
            reader.Read();
            if (string.Equals(key, TypeKey, comparison))
            {
                typeName = reader.TokenType == JsonTokenType.String ? reader.GetString() : throw new JsonException($"The \"type\" of an error for {declared.Name} must be a string.");
            }
            else if (string.Equals(key, MessageKey, comparison))
            {
                hasMessage = reader.TokenType == JsonTokenType.String ? true : throw new JsonException($"The \"message\" of an error for {declared.Name} must be a string.");
            }
            else if (string.Equals(key, ContextKey, comparison))
            {
                ReadContext(ref reader, declared);
            }
            else if (string.Equals(key, CauseKey, comparison))
            {
                if (reader.TokenType is not (JsonTokenType.StartObject or JsonTokenType.Null))
                    throw new JsonException($"The \"cause\" of an error for {declared.Name} must be an error object or absent.");
                reader.Skip();
            }
            else
            {
                reader.Skip();
            }
        }

        if (typeName is null) throw new JsonException($"An error object for {declared.Name} has no \"type\".");
        if (!hasMessage) throw new JsonException($"A {typeName} has no \"message\".");
        return typeName;
    }

    private static string[] ReadContext(ref Utf8JsonReader reader, Type declared)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException($"The \"context\" of an error for {declared.Name} must be an array of strings.");
        var notes = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            notes.Add(reader.TokenType == JsonTokenType.String ? reader.GetString()! : throw new JsonException($"The \"context\" of an error for {declared.Name} must be an array of strings."));
        }

        return [.. notes];
    }

    /// <summary>
    /// <c>ValidationFailed&lt;X&gt;</c>, read into the closed type the name gives: what
    /// <c>Validation&lt;T, X&gt;.ToResult()</c> made. X is <see cref="Error"/>, a registered hierarchy or union key, or a
    /// registered leaf.
    /// </summary>
    private static Error ReadValidationFailed(ref Utf8JsonReader reader, string elementName, JsonSerializerOptions options)
    {
        var elementType = Resolve(elementName, ErrorCaseRegistry.ElementTypes(options))
            ?? throw new JsonException($"Unknown error type \"{elementName}\" in ValidationFailed<{elementName}>. Register it with AddErrorCases<{elementName}>(…).");
        var comparison = options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var errorsKey = ErrorCaseRegistry.Cache(options).ErrorsName;
        var elementCandidates = ErrorCaseRegistry.Candidates(options, elementType);
        string[] context = [];
        Error? cause = null;
        List<Error>? errors = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString();
            reader.Read();
            if (string.Equals(key, ContextKey, comparison))
            {
                context = ReadContext(ref reader, typeof(ValidationFailed<Error>));
            }
            else if (string.Equals(key, CauseKey, comparison))
            {
                cause = reader.TokenType == JsonTokenType.Null ? null : Read(ref reader, typeof(Error), ErrorCaseRegistry.Candidates(options, typeof(Error)), options);
            }
            else if (string.Equals(key, errorsKey, comparison))
            {
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException($"The {errorsKey} of a ValidationFailed must be an array.");
                errors = [];
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) errors.Add(Read(ref reader, elementType, elementCandidates, options));
            }
            else
            {
                reader.Skip();
            }
        }

        if (errors is null || errors.Count == 0) throw new JsonException($"A ValidationFailed<{elementName}> has at least one error in \"{errorsKey}\".");
        var failed = elementType == typeof(Error) ? new ValidationFailed<Error>(errors) : CreateValidationFailed(elementType, errors);
        return failed with { Context = context, Cause = cause };
    }

    /// <summary>
    /// Closes <c>ValidationFailed&lt;X&gt;</c> for an <see cref="Error"/> type X known only at run time. X is a reference
    /// type, so the closed type and <c>X[]</c> share the canonical code of <c>ValidationFailed&lt;Error&gt;</c> and
    /// <c>Error[]</c>, which this assembly uses: native AOT can build them without new code.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "X is always a reference type (an Error); shared canonical code exists. Checked by a native AOT publish.")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ValidationFailed<>))]
    private static Error CreateValidationFailed(Type elementType, List<Error> errors)
    {
        var array = Array.CreateInstance(elementType, errors.Count);
        for (var i = 0; i < errors.Count; i++) array.SetValue(errors[i], i);
        return (Error)Activator.CreateInstance(typeof(ValidationFailed<>).MakeGenericType(elementType), array)!;
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

    /// <summary>
    /// The options a leaf's metadata comes from: without Resultful's error converters (so a leaf's metadata lists its
    /// properties instead of pointing back at the converter), with <see cref="NestedErrorConverter"/> for members typed as
    /// an abstract <see cref="Error"/>, and with the base members renamed to the wire keys. Values are still written with
    /// the caller's options.
    /// </summary>
    private static JsonSerializerOptions Metadata(JsonSerializerOptions options)
    {
        foreach (var converter in options.Converters)
        {
            if (converter is NestedErrorConverter) return options;
        }

        return MetadataOptions.GetValue(options, static source =>
        {
            var copy = new JsonSerializerOptions(source);
            for (var i = copy.Converters.Count - 1; i >= 0; i--)
            {
                if (copy.Converters[i] is ResultfulJsonConverterFactory or IErrorConverter) copy.Converters.RemoveAt(i);
            }

            copy.Converters.Add(new NestedErrorConverter());
            copy.TypeInfoResolver = (source.TypeInfoResolver ?? throw new InvalidOperationException("The options have no TypeInfoResolver."))
                .WithAddedModifier(FixWireNames);
            return copy;
        });
    }

    /// <summary>
    /// On an error's metadata: <c>Message</c>, <c>Context</c> and <c>Cause</c> answer to the wire keys whatever the
    /// naming policy, and the <c>"type"</c> member is skipped. Constructor parameters still bind by member name.
    /// </summary>
    private static void FixWireNames(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object || !typeof(Error).IsAssignableFrom(info.Type)) return;
        foreach (var property in info.Properties)
        {
            if (IsBase(property.Name, nameof(Error.Message), info.Options)) property.Name = MessageKey;
            else if (IsBase(property.Name, nameof(Error.Context), info.Options)) property.Name = ContextKey;
            else if (IsBase(property.Name, nameof(Error.Cause), info.Options)) property.Name = CauseKey;
        }

        info.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
    }

    private static bool IsBase(string name, string member, JsonSerializerOptions options) =>
        string.Equals(name, member, StringComparison.OrdinalIgnoreCase) || name == options.PropertyNamingPolicy?.ConvertName(member);
}

/// <summary>
/// Reads and writes a member declared as an abstract <see cref="Error"/> (<see cref="Error.Cause"/>, or a member typed as
/// a hierarchy's base) inside a leaf's own metadata, through the registered cases. A concrete leaf keeps its object
/// metadata. Not generic: System.Text.Json casts it to the member's type, so it is AOT-safe.
/// </summary>
internal sealed class NestedErrorConverter : JsonConverter<Error>
{
    public override bool CanConvert(Type typeToConvert) => typeof(Error).IsAssignableFrom(typeToConvert) && typeToConvert.IsAbstract;

    public override Error Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ErrorWire.Read(ref reader, typeToConvert, ErrorCaseRegistry.Candidates(options, typeToConvert), options);

    public override void Write(Utf8JsonWriter writer, Error value, JsonSerializerOptions options) => ErrorWire.Write(writer, value, options);
}

/// <summary>Marks the converters that write an <see cref="Error"/> themselves.</summary>
internal interface IErrorConverter
{
    Type[]? Cases { get; }
}
