using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Resultful.Text.Json;

/// <summary>Sets up <see cref="JsonSerializerOptions"/> for Resultful's wire format.</summary>
public static class ResultfulJsonSerializerOptionsExtensions
{
    /// <summary>
    /// Adds <see cref="ResultfulJsonConverterFactory"/>, which converts every <see cref="Result{T}"/>,
    /// <see cref="Result{T, TError}"/>, <see cref="Validation{T}"/>, <see cref="Validation{T, TError}"/>,
    /// <see cref="Unit"/> and <see cref="Error"/>. Adding it twice has no effect.
    /// </summary>
    /// <remarks>
    /// The factory makes generic converters at run time, so it isn't native-AOT safe, and a library that registers
    /// Resultful for its callers shouldn't call this. For native AOT, give each result type converter-backed metadata
    /// (<c>JsonMetadataServices.CreateValueInfo&lt;Result&lt;Order&gt;&gt;(options, new ResultJsonConverter&lt;Order&gt;())</c>)
    /// from an <c>IJsonTypeInfoResolver</c>, combined with the source-generated context through
    /// <c>JsonTypeInfoResolver.Combine</c>, and register the error cases with <see cref="AddErrorCases{TError}"/>.
    /// </remarks>
    /// <param name="options">The options to change. They must not have been used yet.</param>
    /// <returns>The same options, for chaining.</returns>
    [RequiresDynamicCode(ResultfulJsonConverterFactory.DynamicCodeMessage)]
    public static JsonSerializerOptions AddResultful(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var converter in options.Converters)
        {
            if (converter is ResultfulJsonConverterFactory) return options;
        }

        options.Converters.Add(new ResultfulJsonConverterFactory());
        return options;
    }

    /// <summary>
    /// Registers the leaves of a closed hierarchy or an error union, so that an error written as
    /// <c>{"type":"OrderNotFound",…}</c> can be read back as <typeparamref name="TError"/>. Writing needs no
    /// registration. No reflection is used to find the leaves, so this is AOT-safe.
    /// </summary>
    /// <typeparam name="TError">The hierarchy's base type, or the error union.</typeparam>
    /// <param name="options">The options to change. They must not have been used yet.</param>
    /// <param name="cases">The concrete leaves, for example <c>OrderError.Cases</c> from Resultful.Generators.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The options are already in use (read-only).</exception>
    /// <exception cref="ArgumentException">
    /// A case isn't a concrete <see cref="Error"/>, isn't a <typeparamref name="TError"/>, or two cases have the same
    /// simple name (the generator reports that as RFG0008).
    /// </exception>
    public static JsonSerializerOptions AddErrorCases<TError>(this JsonSerializerOptions options, IEnumerable<Type> cases)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cases);
        if (options.IsReadOnly) throw new InvalidOperationException("These options are already in use and can't change: register the error cases before the first serialization.");
        ErrorCaseRegistry.GetOrAdd(options).Add(typeof(TError), cases);
        return options;
    }
}
