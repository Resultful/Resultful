using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resultful.Text.Json;

/// <summary>
/// Makes the converter for any <see cref="Result{T}"/>, <see cref="Result{T, TError}"/>, <see cref="Validation{T}"/>,
/// <see cref="Validation{T, TError}"/>, <see cref="Unit"/> or <see cref="Error"/>. Errors are read through the cases
/// registered with <see cref="ResultfulJsonSerializerOptionsExtensions.AddErrorCases{TError}"/>.
/// </summary>
/// <remarks>
/// It closes the generic converters at run time, so it needs dynamic code. In a native-AOT app, add the converters
/// themselves instead (<c>new ResultJsonConverter&lt;Order&gt;()</c>).
/// </remarks>
[RequiresDynamicCode(DynamicCodeMessage)]
public sealed class ResultfulJsonConverterFactory : JsonConverterFactory
{
    internal const string DynamicCodeMessage =
        "ResultfulJsonConverterFactory makes generic converters at run time. For native AOT, add the converters themselves (new ResultJsonConverter<T>(), …), for example with JsonMetadataServices.CreateValueInfo.";

    /// <summary>Creates the factory.</summary>
    public ResultfulJsonConverterFactory()
    {
    }

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        if (typeToConvert == typeof(Unit) || typeof(Error).IsAssignableFrom(typeToConvert)) return true;
        if (!typeToConvert.IsGenericType) return false;
        var definition = typeToConvert.GetGenericTypeDefinition();
        return definition == typeof(Result<,>) || definition == typeof(Result<>)
            || definition == typeof(Validation<,>) || definition == typeof(Validation<>);
    }

    /// <inheritdoc />
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        ArgumentNullException.ThrowIfNull(options);
        if (typeToConvert == typeof(Unit)) return new UnitJsonConverter();
        if (typeof(Error).IsAssignableFrom(typeToConvert))
        {
            var cases = ErrorCaseRegistry.Candidates(options, typeToConvert, ErrorCaseRegistry.ExplicitCases(options, typeToConvert));
            return (JsonConverter)Activator.CreateInstance(typeof(ErrorJsonConverter<>).MakeGenericType(typeToConvert), [cases])!;
        }

        var definition = typeToConvert.GetGenericTypeDefinition();
        var converter = definition == typeof(Result<,>) ? typeof(ResultJsonConverter<,>)
            : definition == typeof(Result<>) ? typeof(ResultJsonConverter<>)
            : definition == typeof(Validation<,>) ? typeof(ValidationJsonConverter<,>)
            : definition == typeof(Validation<>) ? typeof(ValidationJsonConverter<>)
            : throw new ArgumentException($"{typeToConvert.Name} isn't a Resultful type.", nameof(typeToConvert));
        return (JsonConverter)Activator.CreateInstance(converter.MakeGenericType(typeToConvert.GetGenericArguments()))!;
    }
}
