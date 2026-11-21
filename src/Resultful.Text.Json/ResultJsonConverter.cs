using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resultful.Text.Json;

/// <summary>
/// Converts a <see cref="Result{T, TError}"/>: <c>{"ok":true,"value":…}</c> or <c>{"ok":false,"error":…}</c>. An
/// <see cref="Error"/> is written as <c>{"type","message","context","cause"?,…leaf properties}</c>, an error union as its
/// leaf, and any other <typeparamref name="TError"/> through the options. The zero value throws on write.
/// </summary>
/// <remarks>AOT-safe: create it directly, for example in <c>JsonMetadataServices.CreateValueInfo</c>.</remarks>
/// <typeparam name="T">The success value.</typeparam>
/// <typeparam name="TError">The error.</typeparam>
public sealed class ResultJsonConverter<T, TError> : JsonConverter<Result<T, TError>>
{
    /// <summary>Creates the converter.</summary>
    public ResultJsonConverter()
    {
    }

    /// <inheritdoc />
    public override Result<T, TError> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var read = ResultWire.Read<T, TError>(ref reader, typeof(Result<T, TError>), many: false, options);
        return read.Ok ? Result.Ok<T, TError>(read.Value) : Result.Fail<T, TError>(read.Error);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Result<T, TError> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);
        if (!value.IsSuccess && !value.IsFailure) throw ResultWire.ZeroValue(typeof(Result<T, TError>));
        if (value.TryGetValue(out var success)) ResultWire.WriteSuccess(writer, success, options);
        else ResultWire.WriteFailure(writer, value.Error, options);
    }
}

/// <summary>
/// Converts a <see cref="Result{T}"/>: <c>{"ok":true,"value":…}</c> or <c>{"ok":false,"error":{…}}</c>. The zero
/// value throws on write.
/// </summary>
/// <remarks>AOT-safe: create it directly, for example in <c>JsonMetadataServices.CreateValueInfo</c>.</remarks>
/// <typeparam name="T">The success value.</typeparam>
public sealed class ResultJsonConverter<T> : JsonConverter<Result<T>>
{
    /// <summary>Creates the converter.</summary>
    public ResultJsonConverter()
    {
    }

    /// <inheritdoc />
    public override Result<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var read = ResultWire.Read<T, Error>(ref reader, typeof(Result<T>), many: false, options);
        return read.Ok ? Result.Ok(read.Value) : Result.Fail<T>(read.Error);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);
        if (!value.IsSuccess && !value.IsFailure) throw ResultWire.ZeroValue(typeof(Result<T>));
        if (value.TryGetValue(out var success)) ResultWire.WriteSuccess(writer, success, options);
        else ResultWire.WriteFailure(writer, value.Error, options);
    }
}
