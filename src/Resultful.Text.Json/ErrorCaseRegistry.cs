using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resultful.Text.Json;

/// <summary>
/// The cases registered with <see cref="ResultfulJsonSerializerOptionsExtensions.AddErrorCases{TError}"/>. It lives in
/// <see cref="JsonSerializerOptions.Converters"/> (converting nothing), so it survives a copy of the options.
/// </summary>
internal sealed class ErrorCaseRegistry : JsonConverterFactory
{
    /// <summary>The core's own leaves: always readable where the declared type allows them.</summary>
    private static readonly Type[] BuiltIn = [typeof(MessageError), typeof(ValidationFailed<Error>)];

    private readonly Dictionary<Type, List<Type>> _byError = [];

    public override bool CanConvert(Type typeToConvert) => false;

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) => null;

    public static ErrorCaseRegistry? Find(JsonSerializerOptions options)
    {
        foreach (var converter in options.Converters)
        {
            if (converter is ErrorCaseRegistry registry) return registry;
        }

        return null;
    }

    public static ErrorCaseRegistry GetOrAdd(JsonSerializerOptions options)
    {
        if (Find(options) is { } found) return found;
        var registry = new ErrorCaseRegistry();
        options.Converters.Add(registry);
        return registry;
    }

    public void Add(Type errorType, IEnumerable<Type> cases)
    {
        var list = Validate(errorType, cases);
        if (_byError.TryGetValue(errorType, out var existing))
        {
            list = Validate(errorType, existing.Concat(list).Distinct());
        }

        _byError[errorType] = list;
    }

    /// <summary>Checks a case list: concrete errors of the declared type, no two with the same simple name.</summary>
    public static List<Type> Validate(Type errorType, IEnumerable<Type> cases)
    {
        var list = new List<Type>();
        var names = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var leaf in cases)
        {
            if (leaf is null) throw new ArgumentException($"The cases of {errorType.Name} contain null.", nameof(cases));
            if (!typeof(Error).IsAssignableFrom(leaf) || leaf.IsAbstract || leaf.IsGenericTypeDefinition)
                throw new ArgumentException($"{leaf.Name} isn't a concrete Error, so it can't be a case of {errorType.Name}.", nameof(cases));
            if (typeof(Error).IsAssignableFrom(errorType) && !errorType.IsAssignableFrom(leaf))
                throw new ArgumentException($"{leaf.Name} isn't a {errorType.Name}, so it can't be one of its cases.", nameof(cases));
            var name = ErrorWire.WireName(leaf);
            if (names.TryGetValue(name, out var other) && other != leaf)
                throw new ArgumentException($"{other.FullName} and {leaf.FullName} are both written as \"{name}\", so one couldn't be read back (RFG0008).", nameof(cases));
            if (names.TryAdd(name, leaf)) list.Add(leaf);
        }

        return list;
    }

    /// <summary>The cases of an <see cref="ErrorJsonConverter{TError}"/> the caller added for this exact type, if any.</summary>
    public static IReadOnlyList<Type>? ExplicitCases(JsonSerializerOptions options, Type errorType)
    {
        foreach (var converter in options.Converters)
        {
            if (converter is IErrorConverter { Cases: { } cases } && converter.Type == errorType) return cases;
        }

        return null;
    }

    /// <summary>
    /// The leaves a <c>"type"</c> may name when <paramref name="declared"/> is read: its registered cases, or else every
    /// registered case that is a <paramref name="declared"/>; then the core's own leaves and <paramref name="declared"/>
    /// itself when they fit.
    /// </summary>
    public static IReadOnlyList<Type> Candidates(JsonSerializerOptions options, Type declared, IEnumerable<Type>? explicitCases = null)
    {
        var result = new List<Type>();
        if (explicitCases is not null)
        {
            result.AddRange(explicitCases);
        }
        else if (Find(options) is { } registry)
        {
            if (registry._byError.TryGetValue(declared, out var exact))
            {
                result.AddRange(exact);
            }
            else
            {
                foreach (var cases in registry._byError.Values)
                {
                    foreach (var leaf in cases)
                    {
                        if (declared.IsAssignableFrom(leaf) && !result.Contains(leaf)) result.Add(leaf);
                    }
                }
            }
        }

        foreach (var leaf in BuiltIn)
        {
            if (declared.IsAssignableFrom(leaf) && !result.Contains(leaf)) result.Add(leaf);
        }

        if (typeof(Error).IsAssignableFrom(declared) && !declared.IsAbstract && !result.Contains(declared)) result.Add(declared);
        return result;
    }
}
