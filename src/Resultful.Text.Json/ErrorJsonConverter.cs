using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resultful.Text.Json;

/// <summary>
/// Converts any <typeparamref name="TError"/> as <c>{"type","message","context","cause"?,…leaf properties}</c>, in that
/// order. <c>"type"</c> is the leaf's simple name; reading resolves it through <c>cases</c>, with no reflection. An
/// unknown <c>"type"</c> throws <see cref="JsonException"/>.
/// </summary>
/// <remarks>
/// The leaf's own properties are written and read through its <c>JsonTypeInfo</c>, so in a native-AOT app list the
/// leaves in the source-generated context. <c>context</c> is always written, <c>cause</c> only when it is set; leaf
/// property names follow the options' naming policy.
/// </remarks>
/// <typeparam name="TError">The error type: a leaf, the base of a closed hierarchy, or <see cref="Error"/>.</typeparam>
public sealed class ErrorJsonConverter<TError> : JsonConverter<TError>, IErrorConverter where TError : Error
{
    private readonly Type[] _cases;

    /// <summary>Creates the converter.</summary>
    /// <param name="cases">The concrete leaves a <c>"type"</c> may name, for example <c>OrderError.Cases</c>.</param>
    /// <exception cref="ArgumentException">A case isn't a concrete <typeparamref name="TError"/>, or two share a name.</exception>
    public ErrorJsonConverter(IEnumerable<Type> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        _cases = [.. ErrorCaseRegistry.Validate(typeof(TError), cases)];
    }

    Type[]? IErrorConverter.Cases => _cases;

    /// <inheritdoc />
    public override TError Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var error = ErrorWire.Read(ref reader, typeof(TError), ErrorCaseRegistry.Candidates(options, typeof(TError), _cases), options);
        return error as TError ?? throw new JsonException($"{error.GetType().Name} isn't a {typeof(TError).Name}.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TError value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);
        ErrorWire.Write(writer, value, options);
    }
}
