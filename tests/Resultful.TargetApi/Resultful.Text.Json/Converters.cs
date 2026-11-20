#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

// The wire format (DECISIONS-TAKEN 04c, accepted). Success {"ok":true,"value":…}; failure
// {"ok":false,"error":{"type":"OrderNotFound","message":…,"context":[…],"cause":{…},…leaf properties}};
// a Validation failure has "errors":[…]; Unit is {}; the zero value throws on write. Property order is fixed.
// An error union writes as its leaf. Reading resolves "type" through a registered case list, with no reflection;
// an unknown "type" throws JsonException.
namespace Resultful.Text.Json
{
    public static class ResultfulJsonSerializerOptionsExtensions
    {
        // Adds ResultfulJsonConverterFactory.
        public static JsonSerializerOptions AddResultful(this JsonSerializerOptions options) => throw null!;

        // Registers the leaves of a closed hierarchy or an error union for reading, e.g. `OrderError.Cases`,
        // the list Resultful.Generators writes. Writing needs no registration.
        public static JsonSerializerOptions AddErrorCases<TError>(this JsonSerializerOptions options, IEnumerable<Type> cases) => throw null!;
    }

    public sealed class ResultfulJsonConverterFactory : JsonConverterFactory
    {
        public ResultfulJsonConverterFactory() => throw null!;
        public override bool CanConvert(Type typeToConvert) => throw null!;
        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) => throw null!;
    }

    public sealed class ResultJsonConverter<T, TError> : JsonConverter<Result<T, TError>>
    {
        public ResultJsonConverter() => throw null!;
        public override Result<T, TError> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw null!;
        public override void Write(Utf8JsonWriter writer, Result<T, TError> value, JsonSerializerOptions options) => throw null!;
    }

    public sealed class ResultJsonConverter<T> : JsonConverter<Result<T>>
    {
        public ResultJsonConverter() => throw null!;
        public override Result<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw null!;
        public override void Write(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options) => throw null!;
    }

    public sealed class ValidationJsonConverter<T, TError> : JsonConverter<Validation<T, TError>>
    {
        public ValidationJsonConverter() => throw null!;
        public override Validation<T, TError> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw null!;
        public override void Write(Utf8JsonWriter writer, Validation<T, TError> value, JsonSerializerOptions options) => throw null!;
    }

    public sealed class ValidationJsonConverter<T> : JsonConverter<Validation<T>>
    {
        public ValidationJsonConverter() => throw null!;
        public override Validation<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw null!;
        public override void Write(Utf8JsonWriter writer, Validation<T> value, JsonSerializerOptions options) => throw null!;
    }

    public sealed class UnitJsonConverter : JsonConverter<Unit>
    {
        public UnitJsonConverter() => throw null!;
        public override Unit Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw null!;
        public override void Write(Utf8JsonWriter writer, Unit value, JsonSerializerOptions options) => throw null!;
    }

    // Writes any Error as {"type","message","context","cause",…leaf properties}; reads through the registered cases.
    public sealed class ErrorJsonConverter<TError> : JsonConverter<TError> where TError : Error
    {
        public ErrorJsonConverter(IEnumerable<Type> cases) => throw null!;
        public override TError Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw null!;
        public override void Write(Utf8JsonWriter writer, TError value, JsonSerializerOptions options) => throw null!;
    }

    // An error union writes as its leaf (decision 17) and reads back through its innermost cases; `wrap` turns the
    // leaf read back into the union (a generated union supplies it; no reflection, so AOT-safe).
    public sealed class ErrorUnionJsonConverter<TUnion> : JsonConverter<TUnion> where TUnion : struct, IErrorUnion
    {
        public ErrorUnionJsonConverter(IEnumerable<Type> cases, Func<Error, TUnion> wrap) => throw null!;
        public override TUnion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw null!;
        public override void Write(Utf8JsonWriter writer, TUnion value, JsonSerializerOptions options) => throw null!;
    }
}
