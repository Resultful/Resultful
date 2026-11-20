using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resultful.Text.Json;

/// <summary>
/// Converts a <see cref="Validation{T, TError}"/>: <c>{"ok":true,"value":…}</c> or <c>{"ok":false,"errors":[…]}</c>,
/// never empty. The zero value throws on write.
/// </summary>
/// <remarks>AOT-safe: create it directly, for example in <c>JsonMetadataServices.CreateValueInfo</c>.</remarks>
/// <typeparam name="T">The success value.</typeparam>
/// <typeparam name="TError">The error.</typeparam>
public sealed class ValidationJsonConverter<T, TError> : JsonConverter<Validation<T, TError>>
{
    /// <summary>Creates the converter.</summary>
    public ValidationJsonConverter()
    {
    }

    /// <inheritdoc />
    public override Validation<T, TError> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var read = ResultWire.Read<T, TError>(ref reader, typeof(Validation<T, TError>), many: true, options);
        return read.Ok ? Validation.Ok<T, TError>(read.Value) : Validation.Fail<T, TError>(read.Errors!);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Validation<T, TError> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);
        if (value.IsDefault) throw ResultWire.ZeroValue(typeof(Validation<T, TError>));
        if (value.TryGetValue(out var success)) ResultWire.WriteSuccess(writer, success, options);
        else ResultWire.WriteFailures(writer, value.Errors, options);
    }
}

/// <summary>
/// Converts a <see cref="Validation{T}"/>: <c>{"ok":true,"value":…}</c> or <c>{"ok":false,"errors":[{…}]}</c>, never
/// empty. The zero value throws on write.
/// </summary>
/// <remarks>AOT-safe: create it directly, for example in <c>JsonMetadataServices.CreateValueInfo</c>.</remarks>
/// <typeparam name="T">The success value.</typeparam>
public sealed class ValidationJsonConverter<T> : JsonConverter<Validation<T>>
{
    /// <summary>Creates the converter.</summary>
    public ValidationJsonConverter()
    {
    }

    /// <inheritdoc />
    public override Validation<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var read = ResultWire.Read<T, Error>(ref reader, typeof(Validation<T>), many: true, options);
        return read.Ok ? Validation.Ok(read.Value) : Validation.Fail<T>(read.Errors!);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Validation<T> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);
        if (value.IsDefault) throw ResultWire.ZeroValue(typeof(Validation<T>));
        if (value.TryGetValue(out var success)) ResultWire.WriteSuccess(writer, success, options);
        else ResultWire.WriteFailures(writer, value.Errors, options);
    }
}
