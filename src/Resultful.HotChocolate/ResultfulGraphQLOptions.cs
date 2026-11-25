namespace Resultful.HotChocolate;

/// <summary>
/// How results answer over GraphQL: the cases of each error type, which leaves show their own fields, the validation
/// error's name and keys, and whether an error type also shows the error's details.
/// </summary>
/// <remarks>
/// <para>
/// A resolver returning <see cref="Result{T, TError}"/>, <see cref="Result{T}"/>, <see cref="Validation{T, TError}"/>,
/// <see cref="Validation{T}"/> or a generated shape gets one GraphQL error type per case of its error: its declared
/// cases. A concrete error type is its own one case. A closed hierarchy or an error union has the cases registered
/// with <see cref="AddErrorCases{TError}"/>, for example <c>OrderError.Cases</c> from Resultful.Generators: they are
/// read from that list, with no reflection. A result whose error is <see cref="Error"/> itself has the cases registered
/// for <see cref="Error"/>, if any. A validation failure (a <see cref="ValidationFailed{TError}"/>, or the failed
/// errors of a validation) is the <c>ValidationError</c> type, <see cref="ResultfulValidationError"/>, wherever the
/// error can be one.
/// </para>
/// <para>
/// An error type shows <c>message</c> only, unless <see cref="ExposeLeafFields"/> or <see cref="ExposeFieldsOf{TLeaf}"/>
/// shows the leaf's own properties. A failure that isn't a declared case goes in the response's top-level
/// <c>errors</c>, with <c>extensions.code</c> set to the error's type name and a message only in the Development
/// environment.
/// </para>
/// </remarks>
public sealed class ResultfulGraphQLOptions
{
    private readonly Dictionary<Type, List<Type>> _cases = [];
    private readonly HashSet<Type> _exposed = [];
    private string _validationErrorTypeName = "ValidationError";
    private Func<Error, string> _validationKey = static error => error.GetType().Name;

    /// <summary>Creates options with no registered cases, type-name validation fields and no details.</summary>
    public ResultfulGraphQLOptions()
    {
    }

    /// <summary>
    /// The field each error of a validation failure is listed under in <c>ValidationError.fields</c>. Defaults to the
    /// error's type name, as <c>Resultful.AspNetCore</c>'s validation problem does. A null key is treated as an empty one.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public Func<Error, string> ValidationKey
    {
        get => _validationKey;
        set => _validationKey = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// When <c>true</c>, every error type also has a <c>details: String!</c> field: the error's
    /// <see cref="Error.ToString"/>, with its type, message, context and cause chain. <c>false</c> by default, so a
    /// client sees only <c>message</c> (and any exposed fields of the case); <see cref="Error.Context"/> and
    /// <see cref="Error.Cause"/> stay on the server.
    /// </summary>
    public bool IncludeDetails { get; set; }

    /// <summary>
    /// When <c>true</c>, every error type also shows its case's own public properties as fields, as HotChocolate shows
    /// any error type's; <c>[GraphQLIgnore]</c> still hides one. <c>false</c> by default: an error type shows
    /// <c>message</c> only, because a leaf's own properties may hold personal data, the rule
    /// <see cref="Error.ToString"/> and <c>Resultful.AspNetCore</c>'s problems follow. Use
    /// <see cref="ExposeFieldsOf{TLeaf}"/> to expose chosen leaves.
    /// </summary>
    public bool ExposeLeafFields { get; set; }

    /// <summary>
    /// The GraphQL name of the validation error type, <see cref="ResultfulValidationError"/>. <c>"ValidationError"</c>
    /// by default; change it when an error of the app already has that name.
    /// </summary>
    /// <exception cref="ArgumentException">The value is null or empty.</exception>
    public string ValidationErrorTypeName
    {
        get => _validationErrorTypeName;
        set => _validationErrorTypeName = string.IsNullOrEmpty(value) ? throw new ArgumentException("A GraphQL type name can't be empty.", nameof(value)) : value;
    }

    /// <summary>
    /// Shows <typeparamref name="TLeaf"/>'s own public properties as fields of its error type, next to
    /// <c>message</c>; <c>[GraphQLIgnore]</c> still hides one. Other leaves keep <c>message</c> only, unless
    /// <see cref="ExposeLeafFields"/> is set.
    /// </summary>
    /// <typeparam name="TLeaf">A concrete error type whose fields a client may read.</typeparam>
    /// <returns>These options, to chain more settings.</returns>
    public ResultfulGraphQLOptions ExposeFieldsOf<TLeaf>()
        where TLeaf : Error
    {
        _exposed.Add(typeof(TLeaf));
        return this;
    }

    internal bool ExposesFieldsOf(Type leaf) => ExposeLeafFields || _exposed.Contains(leaf);

    /// <summary>
    /// Registers the cases of a closed hierarchy or an error union, so that a result whose error is
    /// <typeparamref name="TError"/> declares one GraphQL error type per case. No reflection is used to find them.
    /// </summary>
    /// <typeparam name="TError">The hierarchy's base type, the error union, or <see cref="Error"/>.</typeparam>
    /// <param name="cases">
    /// The concrete leaves, for example <c>OrderError.Cases</c> from Resultful.Generators. A nested union's leaves are
    /// listed flat, as the generator lists them. Registering the same type again adds to its cases.
    /// </param>
    /// <returns>These options, to chain more registrations.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cases"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A case is null, isn't a concrete <see cref="Error"/>, or isn't a <typeparamref name="TError"/> when
    /// <typeparamref name="TError"/> is an error type.
    /// </exception>
    public ResultfulGraphQLOptions AddErrorCases<TError>(IEnumerable<Type> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var errorType = typeof(TError);
        var list = new List<Type>();
        foreach (var leaf in cases)
        {
            if (leaf is null) throw new ArgumentException($"The cases of {errorType.Name} contain null.", nameof(cases));
            if (!typeof(Error).IsAssignableFrom(leaf) || leaf.IsAbstract || leaf.ContainsGenericParameters)
                throw new ArgumentException($"{leaf.Name} isn't a concrete Error, so it can't be a case of {errorType.Name}.", nameof(cases));
            if (typeof(Error).IsAssignableFrom(errorType) && !errorType.IsAssignableFrom(leaf))
                throw new ArgumentException($"{leaf.Name} isn't a {errorType.Name}, so it can't be one of its cases.", nameof(cases));
            list.Add(leaf);
        }

        if (_cases.TryGetValue(errorType, out var existing))
        {
            existing.AddRange(list.Where(l => !existing.Contains(l)));
        }
        else
        {
            _cases[errorType] = list.Distinct().ToList();
        }

        return this;
    }

    internal bool TryGetCases(Type errorType, out IReadOnlyList<Type> cases)
    {
        var found = _cases.TryGetValue(errorType, out var list);
        cases = list ?? [];
        return found;
    }
}
