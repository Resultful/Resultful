using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resultful.Text.Json;

/// <summary>
/// Converts an error union (decision 17). It is written as its innermost leaf, exactly as the leaf alone would be,
/// and read back through <c>cases</c>; <c>wrap</c> turns the leaf into the union, rebuilding any nesting. No reflection,
/// so it is AOT-safe. An unknown <c>"type"</c> throws <see cref="JsonException"/>.
/// </summary>
/// <typeparam name="TUnion">The union.</typeparam>
public sealed class ErrorUnionJsonConverter<TUnion> : JsonConverter<TUnion>, IErrorConverter where TUnion : struct, IErrorUnion
{
    private readonly Type[] _cases;
    private readonly Func<Error, TUnion> _wrap;

    /// <summary>Creates the converter.</summary>
    /// <param name="cases">The innermost leaves, for example <c>OrderError.Cases</c> from Resultful.Generators.</param>
    /// <param name="wrap">Turns a leaf read back into the union. A generated union supplies it.</param>
    /// <exception cref="ArgumentException">A case isn't a concrete <see cref="Error"/>, or two share a name.</exception>
    public ErrorUnionJsonConverter(IEnumerable<Type> cases, Func<Error, TUnion> wrap)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(wrap);
        _cases = [.. ErrorCaseRegistry.Validate(typeof(TUnion), cases)];
        _wrap = wrap;
    }

    Type[]? IErrorConverter.Cases => _cases;

    /// <inheritdoc />
    public override TUnion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _wrap(ErrorWire.Read(ref reader, typeof(TUnion), _cases, options));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TUnion value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);
        ErrorWire.Write(writer, value.Error, options);
    }
}
