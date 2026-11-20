using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Resultful.Text.Json;

/// <summary>
/// The envelope shared by results and validations: <c>{"ok":true,"value":…}</c>, <c>{"ok":false,"error":…}</c> or
/// <c>{"ok":false,"errors":[…]}</c>. Every value goes through the options' <see cref="JsonTypeInfo{T}"/>, so a
/// source-generated context works.
/// </summary>
internal static class ResultWire
{
    public static InvalidOperationException ZeroValue(Type type) =>
        new($"This {Describe(type)} is the zero value (default): it was never made with Ok or Fail, so it can't be written.");

    public static void WriteSuccess<T>(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("ok"u8, true);
        writer.WritePropertyName("value"u8);
        WriteValue(writer, value, options);
        writer.WriteEndObject();
    }

    public static void WriteFailure<TError>(Utf8JsonWriter writer, TError error, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("ok"u8, false);
        writer.WritePropertyName("error"u8);
        WriteError(writer, error, options);
        writer.WriteEndObject();
    }

    public static void WriteFailures<TError>(Utf8JsonWriter writer, IReadOnlyList<TError> errors, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("ok"u8, false);
        writer.WriteStartArray("errors"u8);
        foreach (var error in errors) WriteError(writer, error, options);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteValue<T>(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (typeof(T) == typeof(Unit))
        {
            UnitJsonConverter.WriteUnit(writer);
            return;
        }

        JsonSerializer.Serialize(writer, value, Info<T>(options));
    }

    private static void WriteError<TError>(Utf8JsonWriter writer, TError error, JsonSerializerOptions options)
    {
        switch (error)
        {
            case null:
                writer.WriteNullValue();
                break;
            case Error e:
                ErrorWire.Write(writer, e, options);
                break;
            case IErrorUnion union:
                ErrorWire.Write(writer, union.Error, options);
                break;
            default:
                JsonSerializer.Serialize(writer, error, Info<TError>(options));
                break;
        }
    }

    /// <summary>What was read. <see cref="Ok"/> is checked against which members were present.</summary>
    public struct Envelope<T, TError>
    {
        public bool Ok;
        public T Value;
        public TError Error;
        public List<TError>? Errors;
    }

    /// <summary>Reads the envelope. Members may come in any order; unknown members are skipped unless disallowed.</summary>
    public static Envelope<T, TError> Read<T, TError>(ref Utf8JsonReader reader, Type type, bool many, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected {{\"ok\":…}} for a {Describe(type)}, found {reader.TokenType}.");

        var envelope = default(Envelope<T, TError>);
        bool? ok = null;
        bool hasValue = false, hasError = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString();
            reader.Read();
            if (Is(key, "ok", options))
            {
                ok = reader.TokenType switch
                {
                    JsonTokenType.True => true,
                    JsonTokenType.False => false,
                    _ => throw new JsonException($"\"ok\" must be true or false in a {Describe(type)}."),
                };
            }
            else if (Is(key, "value", options))
            {
                envelope.Value = ReadValue<T>(ref reader, options);
                hasValue = true;
            }
            else if (!many && Is(key, "error", options))
            {
                envelope.Error = ReadError<TError>(ref reader, options);
                hasError = true;
            }
            else if (many && Is(key, "errors", options))
            {
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException($"\"errors\" must be an array in a {Describe(type)}.");
                envelope.Errors = [];
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) envelope.Errors.Add(ReadError<TError>(ref reader, options));
                if (envelope.Errors.Count == 0) throw new JsonException($"A failed {Describe(type)} has at least one error.");
                hasError = true;
            }
            else if (options.UnmappedMemberHandling == JsonUnmappedMemberHandling.Disallow)
            {
                throw new JsonException($"Unexpected member \"{key}\" in a {Describe(type)}.");
            }
            else
            {
                reader.Skip();
            }
        }

        var errorKey = many ? "errors" : "error";
        switch (ok)
        {
            case null:
                throw new JsonException($"A {Describe(type)} has no \"ok\".");
            case true when hasError:
                throw new JsonException($"A successful {Describe(type)} has an \"{errorKey}\".");
            case true when !hasValue:
                if (typeof(T) != typeof(Unit)) throw new JsonException($"A successful {Describe(type)} has no \"value\".");
                envelope.Value = (T)(object)Unit.Value;
                break;
            case false when hasValue:
                throw new JsonException($"A failed {Describe(type)} has a \"value\".");
            case false when !hasError:
                throw new JsonException($"A failed {Describe(type)} has no \"{errorKey}\".");
        }

        envelope.Ok = ok.Value;
        return envelope;
    }

    private static T ReadValue<T>(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        if (typeof(T) == typeof(Unit)) return (T)(object)UnitJsonConverter.ReadUnit(ref reader);
        return JsonSerializer.Deserialize(ref reader, Info<T>(options))!;
    }

    private static TError ReadError<TError>(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        if (typeof(Error).IsAssignableFrom(typeof(TError)))
        {
            var cases = ErrorCaseRegistry.Candidates(options, typeof(TError), ErrorCaseRegistry.ExplicitCases(options, typeof(TError)));
            return (TError)(object)ErrorWire.Read(ref reader, typeof(TError), cases, options);
        }

        if (typeof(IErrorUnion).IsAssignableFrom(typeof(TError)))
        {
            foreach (var converter in options.Converters)
            {
                if (converter is IErrorConverter and JsonConverter<TError> union) return union.Read(ref reader, typeof(TError), options)!;
            }

            throw new JsonException($"Reading {typeof(TError).Name} needs its cases: add new ErrorUnionJsonConverter<{typeof(TError).Name}>(cases, wrap) to the options.");
        }

        return JsonSerializer.Deserialize(ref reader, Info<TError>(options))!;
    }

    private static JsonTypeInfo<TValue> Info<TValue>(JsonSerializerOptions options) =>
#if NET11_0_OR_GREATER
        options.GetTypeInfo<TValue>();
#else
        (JsonTypeInfo<TValue>)options.GetTypeInfo(typeof(TValue));
#endif

    private static bool Is(string? key, string expected, JsonSerializerOptions options) =>
        string.Equals(key, expected, options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Describe(Type type) => type.IsGenericType ? WireName(type) + "<" + string.Join(", ", type.GetGenericArguments().Select(a => a.Name)) + ">" : type.Name;

    private static string WireName(Type type) => ErrorWire.WireName(type);
}
