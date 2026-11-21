namespace Resultful.HotChocolate;

/// <summary>
/// What a resolver's return type is, read from its generic arguments when the schema is built: the success type, the
/// error type and a reader for the boxed result. A <c>Task</c> or <c>ValueTask</c> is unwrapped first, as HotChocolate
/// awaits it before the field's middleware sees the result.
/// </summary>
internal sealed class ResultField
{
    private ResultField(Type valueType, Type errorType, bool gathers, ResultReader reader)
    {
        ValueType = valueType;
        ErrorType = errorType;
        Gathers = gathers;
        Reader = reader;
    }

    /// <summary>The success type: the field's GraphQL type.</summary>
    public Type ValueType { get; }

    /// <summary>The error type, whose cases the field declares.</summary>
    public Type ErrorType { get; }

    /// <summary><c>true</c> for a validation, which fails with a list of errors: the <c>ValidationError</c> type.</summary>
    public bool Gathers { get; }

    public ResultReader Reader { get; }

    /// <summary>The type a <c>Task</c> or <c>ValueTask</c> holds, or the type itself.</summary>
    public static Type Unwrap(Type type) =>
        type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>))
            ? type.GenericTypeArguments[0]
            : type;

    /// <summary>The item type of a list (an <see cref="IEnumerable{T}"/> other than a string), unwrapped from a task; else <c>null</c>.</summary>
    public static Type? ElementOfList(Type? type)
    {
        if (type is null) return null;
        type = Unwrap(type);
        if (type == typeof(string)) return null;
        if (type.IsArray) return type.GetElementType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return type.GenericTypeArguments[0];
        foreach (var contract in type.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return contract.GenericTypeArguments[0];
        }

        return null;
    }

    /// <summary><c>true</c> for the core's own result and validation types, whose first type argument is the success type.</summary>
    public static bool IsCoreResult(Type type)
    {
        if (!type.IsGenericType) return false;
        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(Result<,>) || definition == typeof(Result<>) || definition == typeof(Validation<,>) || definition == typeof(Validation<>);
    }

    public static ResultField? TryCreate(Type? type)
    {
        if (type is null) return null;
        type = Unwrap(type);
        if (!type.IsValueType) return null;
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GenericTypeArguments;
            if (definition == typeof(Result<,>)) return Create(typeof(ResultReader<,>), arguments, gathers: false);
            if (definition == typeof(Result<>)) return Create(typeof(ResultReader<>), [arguments[0], typeof(Error)], gathers: false);
            if (definition == typeof(Validation<,>)) return Create(typeof(ValidationReader<,>), arguments, gathers: true);
            if (definition == typeof(Validation<>)) return Create(typeof(ValidationReader<>), [arguments[0], typeof(Error)], gathers: true);
        }

        // A generated shape implements one of these (decision 16 item 8).
        foreach (var contract in type.GetInterfaces())
        {
            if (!contract.IsGenericType) continue;
            var definition = contract.GetGenericTypeDefinition();
            if (definition == typeof(IResultShape<,>)) return Create(typeof(ResultReader<,>), contract.GenericTypeArguments, gathers: false);
            if (definition == typeof(IValidationShape<,>)) return Create(typeof(ValidationReader<,>), contract.GenericTypeArguments, gathers: true);
        }

        return null;
    }

    private static ResultField Create(Type reader, Type[] arguments, bool gathers)
    {
        var closed = reader.GetGenericArguments().Length == 1 ? reader.MakeGenericType(arguments[0]) : reader.MakeGenericType(arguments);
        return new ResultField(arguments[0], arguments[1], gathers, (ResultReader)Activator.CreateInstance(closed)!);
    }
}

/// <summary>Reads a boxed result: the success value, or the failure as the leaf <see cref="Error"/> or the list of errors.</summary>
internal abstract class ResultReader
{
    /// <summary>
    /// <c>true</c> with the value on success; <c>false</c> with the failure: the leaf error (an error union unwrapped
    /// to its leaf), the list of leaf errors of a validation, or the error itself when it isn't an
    /// <see cref="Error"/>. The zero value throws, as reading it does in the core.
    /// </summary>
    public abstract bool TryRead(object result, out object? value, out object failure);

    /// <summary>The leaf of an error: itself, or an error union's leaf; the value itself when it is neither.</summary>
    public static object Leaf(object error) => error switch
    {
        Error e => e,
        IErrorUnion u => u.Error,
        _ => error,
    };

    protected static IReadOnlyList<object> Leaves<TError>(IReadOnlyList<TError> errors)
    {
        var leaves = new object[errors.Count];
        for (var i = 0; i < leaves.Length; i++) leaves[i] = Leaf(errors[i]!);
        return leaves;
    }
}

internal sealed class ResultReader<T, TError> : ResultReader
{
    public override bool TryRead(object result, out object? value, out object failure)
    {
        var inner = result is Result<T, TError> r ? r : ((IResultShape<T, TError>)result).AsResult();
        if (inner.TryGetValue(out var v))
        {
            value = v;
            failure = null!;
            return true;
        }

        inner.TryGetError(out var error);
        value = null;
        failure = Leaf(error!);
        return false;
    }
}

internal sealed class ResultReader<T> : ResultReader
{
    public override bool TryRead(object result, out object? value, out object failure)
    {
        var inner = (Result<T>)result;
        if (inner.TryGetValue(out var v))
        {
            value = v;
            failure = null!;
            return true;
        }

        inner.TryGetError(out var error);
        value = null;
        failure = error!;
        return false;
    }
}

internal sealed class ValidationReader<T, TError> : ResultReader
{
    public override bool TryRead(object result, out object? value, out object failure)
    {
        var inner = result is Validation<T, TError> r ? r : ((IValidationShape<T, TError>)result).AsValidation();
        if (inner.TryGetValue(out var v))
        {
            value = v;
            failure = null!;
            return true;
        }

        inner.TryGetErrors(out var errors);
        value = null;
        failure = Leaves(errors!);
        return false;
    }
}

internal sealed class ValidationReader<T> : ResultReader
{
    public override bool TryRead(object result, out object? value, out object failure)
    {
        var inner = (Validation<T>)result;
        if (inner.TryGetValue(out var v))
        {
            value = v;
            failure = null!;
            return true;
        }

        inner.TryGetErrors(out var errors);
        value = null;
        failure = errors!;
        return false;
    }
}
