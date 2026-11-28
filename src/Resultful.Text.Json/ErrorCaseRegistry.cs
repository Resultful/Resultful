using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resultful.Text.Json;

/// <summary>
/// The cases registered with <see cref="ResultfulJsonSerializerOptionsExtensions.AddErrorCases{TError}"/>. It lives in
/// <see cref="JsonSerializerOptions.Converters"/> (converting nothing), so it survives a copy of the options.
/// </summary>
internal sealed class ErrorCaseRegistry : JsonConverterFactory
{
    /// <summary>The core's own leaf: always readable where the declared type allows it. ValidationFailed is read by name.</summary>
    private static readonly Type[] BuiltIn = [typeof(MessageError)];

    private static readonly ConditionalWeakTable<JsonSerializerOptions, WireCache> Caches = [];

    private readonly ErrorCases _cases = new();

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

    public void Add(Type errorType, IEnumerable<Type> cases) => _cases.Add(errorType, cases);

    /// <summary>Checks a case list: <see cref="ErrorCases.Validate"/>.</summary>
    public static List<Type> Validate(Type errorType, IEnumerable<Type> cases) => ErrorCases.Validate(errorType, cases);

    /// <summary>The cases of an <see cref="ErrorJsonConverter{TError}"/> the caller added for this exact type, if any.</summary>
    public static Type[]? ExplicitCases(JsonSerializerOptions options, Type errorType)
    {
        foreach (var converter in options.Converters)
        {
            if (converter is IErrorConverter { Cases: { } cases } && converter.Type == errorType) return cases;
        }

        return null;
    }

    public static WireCache Cache(JsonSerializerOptions options) => Caches.GetValue(options, static o => new WireCache(o));

    /// <summary>
    /// The leaves a <c>"type"</c> may name when <paramref name="declared"/> is read, cached per options: the cases of an
    /// explicit <see cref="ErrorJsonConverter{TError}"/>, else its registered cases, else every registered case that is a
    /// <paramref name="declared"/>; then the core's own leaves and <paramref name="declared"/> itself when they fit.
    /// </summary>
    public static IReadOnlyList<Type> Candidates(JsonSerializerOptions options, Type declared, Type[]? explicitCases = null) =>
        Cache(options).Candidates(declared, explicitCases);

    /// <summary>The types a <c>ValidationFailed&lt;X&gt;</c> may close over: <see cref="Error"/>, registered keys and leaves.</summary>
    public static IReadOnlyList<Type> ElementTypes(JsonSerializerOptions options) => Cache(options).ElementTypes;

    private static List<Type> Compute(JsonSerializerOptions options, Type declared, Type[]? explicitCases)
    {
        var result = new List<Type>();
        explicitCases ??= ExplicitCases(options, declared);
        if (explicitCases is not null)
        {
            result.AddRange(explicitCases);
        }
        else if (Find(options) is { } registry)
        {
            if (registry._cases.TryGetCases(declared, out var exact))
            {
                result.AddRange(exact);
            }
            else
            {
                foreach (var (_, cases) in registry._cases.All)
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

    /// <summary>What the converters look up on every read, computed once per options (which are read-only by then).</summary>
    internal sealed class WireCache(JsonSerializerOptions options)
    {
        private readonly ConcurrentDictionary<(Type Declared, Type[]? Explicit), IReadOnlyList<Type>> _candidates = new();
        private IReadOnlyList<Type>? _elementTypes;

        public string ErrorsName { get; } = options.PropertyNamingPolicy?.ConvertName(nameof(ValidationFailed<Error>.Errors)) ?? nameof(ValidationFailed<Error>.Errors);

        public IReadOnlyList<Type> Candidates(Type declared, Type[]? explicitCases) =>
            _candidates.GetOrAdd((declared, explicitCases), static (key, o) => Compute(o, key.Declared, key.Explicit), options);

        public IReadOnlyList<Type> ElementTypes => _elementTypes ??= ComputeElementTypes();

        private List<Type> ComputeElementTypes()
        {
            var result = new List<Type> { typeof(Error) };
            foreach (var leaf in BuiltIn) result.Add(leaf);
            if (Find(options) is { } registry)
            {
                foreach (var (key, cases) in registry._cases.All)
                {
                    if (typeof(Error).IsAssignableFrom(key) && !result.Contains(key)) result.Add(key);
                    foreach (var leaf in cases)
                    {
                        if (!result.Contains(leaf)) result.Add(leaf);
                    }
                }
            }

            return result;
        }
    }
}
