using System.Diagnostics.CodeAnalysis;
using HotChocolate;

namespace Resultful.HotChocolate;

/// <summary>
/// Converts a result to HotChocolate's <see cref="FieldResult{TResult}"/>, for resolvers that already return
/// <c>FieldResult</c>. A resolver can instead return the result itself once <c>AddResultfulErrors()</c> is called.
/// </summary>
/// <remarks>
/// The leaf types name the error types the field declares, as <c>FieldResult</c>'s type arguments do; there are at most
/// five, <c>FieldResult</c>'s own limit. An error union is unwrapped to its leaf first. A
/// <see cref="ValidationFailed{TError}"/> matches the leaf type <see cref="ResultfulValidationError"/>, with each error
/// listed under its type name: a conversion has no options, so it never uses
/// <see cref="ResultfulGraphQLOptions.ValidationKey"/>. A failure that matches no leaf type throws; <c>AddResultfulErrors()</c> turns that into a
/// top-level GraphQL error with <c>extensions.code</c> set to the error's type name, exactly as for a resolver that
/// returns the result, and no message outside the Development environment. The zero value (<c>default</c>) throws
/// <see cref="InvalidOperationException"/>, as reading it does.
/// </remarks>
public static class FieldResultExtensions
{
    /// <summary>Converts a <see cref="Result{T, TError}"/> to a <c>FieldResult&lt;T&gt;</c>: the value on success; it declares no error type, so every failure throws.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TError">The error type: a hierarchy's base, a leaf or an error union.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>The same success, or the same declared failure.</returns>
    /// <exception cref="InvalidOperationException">The result is the zero value (<c>default</c>).</exception>
    public static FieldResult<T> ToFieldResult<T, TError>(this Result<T, TError> result)
    {
        if (result.TryGetValue(out var value)) return new FieldResult<T>(value!);
        result.TryGetError(out var error);
        var leaf = ResultReader.Leaf(error!);
        throw Undeclared(leaf);
    }

    /// <summary>Converts a <see cref="Result{T, TError}"/> to a <c>FieldResult&lt;T, TLeaf1&gt;</c>: the value on success, the error when it is one of the leaf types.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TError">The error type: a hierarchy's base, a leaf or an error union.</typeparam>
    /// <typeparam name="TLeaf1">A leaf the field declares as an error type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>The same success, or the same declared failure.</returns>
    /// <exception cref="InvalidOperationException">The result is the zero value (<c>default</c>).</exception>
    public static FieldResult<T, TLeaf1> ToFieldResult<T, TError, TLeaf1>(this Result<T, TError> result)
    {
        if (result.TryGetValue(out var value)) return new FieldResult<T, TLeaf1>(value!);
        result.TryGetError(out var error);
        var leaf = ResultReader.Leaf(error!);
        if (Is<TLeaf1>(leaf, out var leaf1)) return new FieldResult<T, TLeaf1>(leaf1);
        throw Undeclared(leaf);
    }

    /// <summary>Converts a <see cref="Result{T, TError}"/> to a <c>FieldResult&lt;T, TLeaf1, TLeaf2&gt;</c>: the value on success, the error when it is one of the leaf types.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TError">The error type: a hierarchy's base, a leaf or an error union.</typeparam>
    /// <typeparam name="TLeaf1">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf2">A leaf the field declares as an error type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>The same success, or the same declared failure.</returns>
    /// <exception cref="InvalidOperationException">The result is the zero value (<c>default</c>).</exception>
    public static FieldResult<T, TLeaf1, TLeaf2> ToFieldResult<T, TError, TLeaf1, TLeaf2>(this Result<T, TError> result)
    {
        if (result.TryGetValue(out var value)) return new FieldResult<T, TLeaf1, TLeaf2>(value!);
        result.TryGetError(out var error);
        var leaf = ResultReader.Leaf(error!);
        if (Is<TLeaf1>(leaf, out var leaf1)) return new FieldResult<T, TLeaf1, TLeaf2>(leaf1);
        if (Is<TLeaf2>(leaf, out var leaf2)) return new FieldResult<T, TLeaf1, TLeaf2>(leaf2);
        throw Undeclared(leaf);
    }

    /// <summary>Converts a <see cref="Result{T, TError}"/> to a <c>FieldResult&lt;T, TLeaf1, TLeaf2, TLeaf3&gt;</c>: the value on success, the error when it is one of the leaf types.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TError">The error type: a hierarchy's base, a leaf or an error union.</typeparam>
    /// <typeparam name="TLeaf1">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf2">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf3">A leaf the field declares as an error type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>The same success, or the same declared failure.</returns>
    /// <exception cref="InvalidOperationException">The result is the zero value (<c>default</c>).</exception>
    public static FieldResult<T, TLeaf1, TLeaf2, TLeaf3> ToFieldResult<T, TError, TLeaf1, TLeaf2, TLeaf3>(this Result<T, TError> result)
    {
        if (result.TryGetValue(out var value)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3>(value!);
        result.TryGetError(out var error);
        var leaf = ResultReader.Leaf(error!);
        if (Is<TLeaf1>(leaf, out var leaf1)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3>(leaf1);
        if (Is<TLeaf2>(leaf, out var leaf2)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3>(leaf2);
        if (Is<TLeaf3>(leaf, out var leaf3)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3>(leaf3);
        throw Undeclared(leaf);
    }

    /// <summary>Converts a <see cref="Result{T, TError}"/> to a <c>FieldResult&lt;T, TLeaf1, TLeaf2, TLeaf3, TLeaf4&gt;</c>: the value on success, the error when it is one of the leaf types.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TError">The error type: a hierarchy's base, a leaf or an error union.</typeparam>
    /// <typeparam name="TLeaf1">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf2">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf3">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf4">A leaf the field declares as an error type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>The same success, or the same declared failure.</returns>
    /// <exception cref="InvalidOperationException">The result is the zero value (<c>default</c>).</exception>
    public static FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4> ToFieldResult<T, TError, TLeaf1, TLeaf2, TLeaf3, TLeaf4>(this Result<T, TError> result)
    {
        if (result.TryGetValue(out var value)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4>(value!);
        result.TryGetError(out var error);
        var leaf = ResultReader.Leaf(error!);
        if (Is<TLeaf1>(leaf, out var leaf1)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4>(leaf1);
        if (Is<TLeaf2>(leaf, out var leaf2)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4>(leaf2);
        if (Is<TLeaf3>(leaf, out var leaf3)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4>(leaf3);
        if (Is<TLeaf4>(leaf, out var leaf4)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4>(leaf4);
        throw Undeclared(leaf);
    }

    /// <summary>Converts a <see cref="Result{T, TError}"/> to a <c>FieldResult&lt;T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5&gt;</c>: the value on success, the error when it is one of the leaf types.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TError">The error type: a hierarchy's base, a leaf or an error union.</typeparam>
    /// <typeparam name="TLeaf1">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf2">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf3">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf4">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf5">A leaf the field declares as an error type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>The same success, or the same declared failure.</returns>
    /// <exception cref="InvalidOperationException">The result is the zero value (<c>default</c>).</exception>
    public static FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5> ToFieldResult<T, TError, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5>(this Result<T, TError> result)
    {
        if (result.TryGetValue(out var value)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5>(value!);
        result.TryGetError(out var error);
        var leaf = ResultReader.Leaf(error!);
        if (Is<TLeaf1>(leaf, out var leaf1)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5>(leaf1);
        if (Is<TLeaf2>(leaf, out var leaf2)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5>(leaf2);
        if (Is<TLeaf3>(leaf, out var leaf3)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5>(leaf3);
        if (Is<TLeaf4>(leaf, out var leaf4)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5>(leaf4);
        if (Is<TLeaf5>(leaf, out var leaf5)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5>(leaf5);
        throw Undeclared(leaf);
    }

    /// <summary>Converts a <see cref="Result{T}"/> to a <c>FieldResult&lt;T&gt;</c>: the value on success; it declares no error type, so every failure throws.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>The same success, or the same declared failure.</returns>
    /// <exception cref="InvalidOperationException">The result is the zero value (<c>default</c>).</exception>
    public static FieldResult<T> ToFieldResult<T>(this Result<T> result)
    {
        if (result.TryGetValue(out var value)) return new FieldResult<T>(value!);
        result.TryGetError(out var error);
        object leaf = error!;
        throw Undeclared(leaf);
    }

    /// <summary>Converts a <see cref="Result{T}"/> to a <c>FieldResult&lt;T, TLeaf1&gt;</c>: the value on success, the error when it is one of the leaf types.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TLeaf1">A leaf the field declares as an error type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>The same success, or the same declared failure.</returns>
    /// <exception cref="InvalidOperationException">The result is the zero value (<c>default</c>).</exception>
    public static FieldResult<T, TLeaf1> ToFieldResult<T, TLeaf1>(this Result<T> result)
    {
        if (result.TryGetValue(out var value)) return new FieldResult<T, TLeaf1>(value!);
        result.TryGetError(out var error);
        object leaf = error!;
        if (Is<TLeaf1>(leaf, out var leaf1)) return new FieldResult<T, TLeaf1>(leaf1);
        throw Undeclared(leaf);
    }

    /// <summary>Converts a <see cref="Result{T}"/> to a <c>FieldResult&lt;T, TLeaf1, TLeaf2&gt;</c>: the value on success, the error when it is one of the leaf types.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TLeaf1">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf2">A leaf the field declares as an error type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>The same success, or the same declared failure.</returns>
    /// <exception cref="InvalidOperationException">The result is the zero value (<c>default</c>).</exception>
    public static FieldResult<T, TLeaf1, TLeaf2> ToFieldResult<T, TLeaf1, TLeaf2>(this Result<T> result)
    {
        if (result.TryGetValue(out var value)) return new FieldResult<T, TLeaf1, TLeaf2>(value!);
        result.TryGetError(out var error);
        object leaf = error!;
        if (Is<TLeaf1>(leaf, out var leaf1)) return new FieldResult<T, TLeaf1, TLeaf2>(leaf1);
        if (Is<TLeaf2>(leaf, out var leaf2)) return new FieldResult<T, TLeaf1, TLeaf2>(leaf2);
        throw Undeclared(leaf);
    }

    /// <summary>Converts a <see cref="Result{T}"/> to a <c>FieldResult&lt;T, TLeaf1, TLeaf2, TLeaf3&gt;</c>: the value on success, the error when it is one of the leaf types.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TLeaf1">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf2">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf3">A leaf the field declares as an error type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>The same success, or the same declared failure.</returns>
    /// <exception cref="InvalidOperationException">The result is the zero value (<c>default</c>).</exception>
    public static FieldResult<T, TLeaf1, TLeaf2, TLeaf3> ToFieldResult<T, TLeaf1, TLeaf2, TLeaf3>(this Result<T> result)
    {
        if (result.TryGetValue(out var value)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3>(value!);
        result.TryGetError(out var error);
        object leaf = error!;
        if (Is<TLeaf1>(leaf, out var leaf1)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3>(leaf1);
        if (Is<TLeaf2>(leaf, out var leaf2)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3>(leaf2);
        if (Is<TLeaf3>(leaf, out var leaf3)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3>(leaf3);
        throw Undeclared(leaf);
    }

    /// <summary>Converts a <see cref="Result{T}"/> to a <c>FieldResult&lt;T, TLeaf1, TLeaf2, TLeaf3, TLeaf4&gt;</c>: the value on success, the error when it is one of the leaf types.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TLeaf1">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf2">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf3">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf4">A leaf the field declares as an error type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>The same success, or the same declared failure.</returns>
    /// <exception cref="InvalidOperationException">The result is the zero value (<c>default</c>).</exception>
    public static FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4> ToFieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4>(this Result<T> result)
    {
        if (result.TryGetValue(out var value)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4>(value!);
        result.TryGetError(out var error);
        object leaf = error!;
        if (Is<TLeaf1>(leaf, out var leaf1)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4>(leaf1);
        if (Is<TLeaf2>(leaf, out var leaf2)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4>(leaf2);
        if (Is<TLeaf3>(leaf, out var leaf3)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4>(leaf3);
        if (Is<TLeaf4>(leaf, out var leaf4)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4>(leaf4);
        throw Undeclared(leaf);
    }

    /// <summary>Converts a <see cref="Result{T}"/> to a <c>FieldResult&lt;T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5&gt;</c>: the value on success, the error when it is one of the leaf types.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TLeaf1">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf2">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf3">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf4">A leaf the field declares as an error type.</typeparam>
    /// <typeparam name="TLeaf5">A leaf the field declares as an error type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>The same success, or the same declared failure.</returns>
    /// <exception cref="InvalidOperationException">The result is the zero value (<c>default</c>).</exception>
    public static FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5> ToFieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5>(this Result<T> result)
    {
        if (result.TryGetValue(out var value)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5>(value!);
        result.TryGetError(out var error);
        object leaf = error!;
        if (Is<TLeaf1>(leaf, out var leaf1)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5>(leaf1);
        if (Is<TLeaf2>(leaf, out var leaf2)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5>(leaf2);
        if (Is<TLeaf3>(leaf, out var leaf3)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5>(leaf3);
        if (Is<TLeaf4>(leaf, out var leaf4)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5>(leaf4);
        if (Is<TLeaf5>(leaf, out var leaf5)) return new FieldResult<T, TLeaf1, TLeaf2, TLeaf3, TLeaf4, TLeaf5>(leaf5);
        throw Undeclared(leaf);
    }

    // A ValidationFailed matches ResultfulValidationError, listed by type name; anything else matches its own type.
    private static bool Is<TLeaf>(object leaf, [NotNullWhen(true)] out TLeaf? match)
    {
        if (typeof(TLeaf) == typeof(ResultfulValidationError) && leaf is Error error && ResultfulValidationError.IsValidationFailed(error.GetType()))
        {
            match = (TLeaf)(object)ResultfulValidationError.From(error.Inner(), ResultfulValidationError.TypeName);
            return true;
        }

        if (leaf is TLeaf typed)
        {
            match = typed;
            return true;
        }

        match = default;
        return false;
    }

    private static ResultfulErrorException Undeclared(object leaf) =>
        new(leaf as Error, ResultfulErrorException.CodeOf(leaf.GetType()));
}
