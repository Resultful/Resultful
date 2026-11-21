using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Resultful.HotChocolate;

/// <summary>
/// The declared cases of one field: the leaves that become GraphQL error types, and whether a validation failure
/// becomes the <c>ValidationError</c> type. It turns a failure into the object GraphQL shows, or throws for one that
/// isn't declared.
/// </summary>
internal sealed class DeclaredCases
{
    private readonly HashSet<Type> _leaves;

    internal DeclaredCases(IEnumerable<Type> leaves, bool validation)
    {
        _leaves = [.. leaves];
        Validation = validation;
        var schemaTypes = new List<Type>(_leaves);
        if (validation) schemaTypes.Add(typeof(ResultfulValidationError));
        SchemaTypes = schemaTypes;
    }

    private DeclaredCases(HashSet<Type> leaves, bool validation, IReadOnlyList<Type> schemaTypes)
    {
        _leaves = leaves;
        Validation = validation;
        SchemaTypes = schemaTypes;
    }

    /// <summary>These cases, also matching <paramref name="extra"/>: error types the field declares another way.</summary>
    public DeclaredCases WithLeaves(IEnumerable<Type> extra) =>
        new([.. _leaves, .. extra], Validation, SchemaTypes);

    /// <summary>The runtime types of the field's error types, in order: the leaves, then the validation error.</summary>
    public IReadOnlyList<Type> SchemaTypes { get; }

    public bool Validation { get; }

    /// <summary>The cases <paramref name="field"/> declares, or <c>null</c> with the reason it can't declare any.</summary>
    public static DeclaredCases? For(ResultField field, ResultfulGraphQLOptions options, out string? problem)
    {
        var errorType = field.ErrorType;
        var isError = typeof(Error).IsAssignableFrom(errorType);
        problem = null;
        if (!isError && !typeof(IErrorUnion).IsAssignableFrom(errorType))
        {
            problem = $"{errorType.Name} isn't an Error or an error union, so it has no GraphQL error types.";
            return null;
        }

        if (field.Gathers) return new DeclaredCases([], validation: true);

        if (options.TryGetCases(errorType, out var registered))
        {
            var validation = errorType == typeof(Error) || registered.Any(t => ResultfulValidationError.IsValidationFailed(t));
            return new DeclaredCases(registered.Where(t => !ResultfulValidationError.IsValidationFailed(t)), validation);
        }

        if (errorType == typeof(Error)) return new DeclaredCases([], validation: true);
        if (ResultfulValidationError.IsValidationFailed(errorType)) return new DeclaredCases([], validation: true);
        if (isError && !errorType.IsAbstract) return new DeclaredCases([errorType], validation: false);

        problem = $"the cases of {errorType.Name} aren't registered: call AddErrorCases<{errorType.Name}>({errorType.Name}.Cases) in AddResultfulErrors.";
        return null;
    }

    /// <summary>
    /// The object GraphQL shows for <paramref name="failure"/>: the declared leaf itself, or a
    /// <see cref="ResultfulValidationError"/>. A failure that isn't declared throws <see cref="ResultfulErrorException"/>.
    /// </summary>
    public object ToGraphQL(object failure, Func<Error, string> key)
    {
        if (failure is IReadOnlyList<object> list) return ResultfulValidationError.From(list.Cast<Error>(), key);
        if (failure is not Error error) throw new ResultfulErrorException(null, ResultfulErrorException.CodeOf(failure.GetType()));
        if (Validation && ResultfulValidationError.IsValidationFailed(error.GetType())) return ResultfulValidationError.From(error.Inner(), key);
        for (var type = error.GetType(); type is not null && type != typeof(Error); type = type.BaseType)
        {
            if (_leaves.Contains(type)) return error;
        }

        throw new ResultfulErrorException(error, ResultfulErrorException.CodeOf(error.GetType()));
    }
}

/// <summary>
/// A failure that isn't a declared case of its field. <see cref="ResultfulErrorFilter"/> turns it into a top-level
/// GraphQL error with <c>extensions.code</c>; its message is the error's <see cref="Error.ToString"/>, which never
/// shows a leaf's own properties.
/// </summary>
internal sealed class ResultfulErrorException : Exception
{
    public ResultfulErrorException(Error? error, string code)
        : base(error?.ToString() ?? code)
    {
        Error = error;
        Code = code;
    }

    public Error? Error { get; }

    /// <summary>The error's type name, with generic arguments as C# writes them: <c>ValidationFailed&lt;Required&gt;</c>.</summary>
    public string Code { get; }

    public static string CodeOf(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick < 0 || !type.IsGenericType) return name;
        return name[..tick] + "<" + string.Join(",", type.GetGenericArguments().Select(CodeOf)) + ">";
    }
}

/// <summary>
/// Writes a <see cref="ResultfulErrorException"/> as a top-level GraphQL error: <c>extensions.code</c> is the error's
/// type name and nothing else is in <c>extensions</c>. The message is the error's <see cref="Error.ToString"/> in the
/// Development environment and a fixed sentence everywhere else, as an HTTP 500 from <c>Resultful.AspNetCore</c> is.
/// No host environment counts as Production: the safe side.
/// </summary>
internal sealed class ResultfulErrorFilter(IServiceProvider schemaServices) : IErrorFilter
{
    /// <summary>The message outside Development, ASP.NET Core's own for a 500, so nothing about the error leaks.</summary>
    internal const string HiddenMessage = "An error occurred while processing your request.";

    /// <summary>The log category of an undeclared failure.</summary>
    internal const string LogCategory = "Resultful.HotChocolate.ResultfulErrorFilter";

    private static readonly Action<ILogger, string, string, Exception?> Undeclared = LoggerMessage.Define<string, string>(
        LogLevel.Error, new EventId(1, "ResultUndeclaredError"), "A result failed with an error its field doesn't declare ({Code}): {Error}");

    private bool? _development;
    private ILogger? _logger;

    /// <summary>The message outside Development when an untyped resolver hands a result to a field that doesn't declare one.</summary>
    internal const string UntypedMessage = "A resolver returned a result to a field that doesn't declare one: return it from a typed resolver.";

    public IError OnError(IError error)
    {
        if (error.Exception is not ResultfulErrorException failure)
        {
            // HotChocolate's own "can't cast the parent" error names the result's CLR types: hide them outside
            // Development. Only an untyped (code-first object or schema-first) resolver gets here.
            return error.Code == "HC0053" && error.Message.Contains("is `Resultful.", StringComparison.Ordinal) && !IsDevelopment()
                ? ErrorBuilder.FromError(error).SetMessage(UntypedMessage).Build()
                : error;
        }

        IsDevelopment();

        // Logged once, at Error, with Error.ToString(): the type name, message, context and cause chain, never the
        // leaf's own properties. The client sees the code, and the same text only in Development.
        if (_logger is not null) Undeclared(_logger, failure.Code, failure.Message, null);
        return ErrorBuilder.FromError(error)
            .ClearExtensions()
            .SetMessage(_development == true ? failure.Message : HiddenMessage)
            .SetCode(failure.Code)
            .Build();
    }

    private bool IsDevelopment()
    {
        if (_development is null)
        {
            var root = schemaServices.GetService<IRootServiceProviderAccessor>()?.ServiceProvider ?? schemaServices;
            _development = root.GetService<IHostEnvironment>()?.IsDevelopment() == true;
            _logger = root.GetService<ILoggerFactory>()?.CreateLogger(LogCategory);
        }

        return _development.Value;
    }
}
