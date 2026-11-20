#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Resultful
{
    // Decision 14: a T, or a non-empty list of TError. Gathers every failure. No Bind and no + (DECISIONS-TAKEN 04c).
    [Resultful.CodeAnalysis.MustObserve]
    public readonly struct Validation<T, TError> : IEquatable<Validation<T, TError>>
    {
        [Obsolete(ObsoleteMessages.Default, error: true)]
        public Validation() => throw null!;

        public bool IsDefault => throw null!;

        [MemberNotNullWhen(true, nameof(Value))]
        [MemberNotNullWhen(false, nameof(Errors))]
        public bool IsSuccess => throw null!;

        [MemberNotNullWhen(false, nameof(Value))]
        [MemberNotNullWhen(true, nameof(Errors))]
        public bool IsFailure => throw null!;

        public T Value => throw null!;

        // Never empty on a failure (VAL-NONEMPTY). Throws on a success.
        public IReadOnlyList<TError> Errors => throw null!;

        public bool TryGetValue([MaybeNullWhen(false)] out T value) => throw null!;
        public bool TryGetErrors([MaybeNullWhen(false)] out IReadOnlyList<TError> errors) => throw null!;
        public void Deconstruct(out bool isSuccess, out T? value, out IReadOnlyList<TError>? errors) => throw null!;

        public static implicit operator Validation<T, TError>(T value) => throw null!;
        public static implicit operator Validation<T, TError>(TError error) => throw null!;
        // One error is a list of one. Nothing is lost.
        public static implicit operator Validation<T, TError>(Result<T, TError> result) => throw null!;

        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<IReadOnlyList<TError>, TResult> errorFunc) => throw null!;
        public void Switch(Action<T> successfulFunc, Action<IReadOnlyList<TError>> errorFunc) => throw null!;
        public Validation<TResult, TError> Map<TResult>(Func<T, TResult> mapFunc) => throw null!;
        public Validation<T, TErrorResult> MapError<TErrorResult>(Func<TError, TErrorResult> errorMapFunc) => throw null!;
        public Validation<T, TError> Tee(Action<T> teeAction) => throw null!;

        // Many errors become one only when the caller says how.
        public Result<T, TError> ToResult(Func<IReadOnlyList<TError>, TError> combine) => throw null!;

        // Errors compare element by element (VAL-EQ).
        public bool Equals(Validation<T, TError> other) => throw null!;
        public override bool Equals(object? obj) => throw null!;
        public override int GetHashCode() => throw null!;
        public override string ToString() => throw null!;
        public static bool operator ==(Validation<T, TError> left, Validation<T, TError> right) => throw null!;
        public static bool operator !=(Validation<T, TError> left, Validation<T, TError> right) => throw null!;
    }

    // Validation<T> means Validation<T, Error>: a separate type in C#.
    [Resultful.CodeAnalysis.MustObserve]
    public readonly struct Validation<T> : IEquatable<Validation<T>>
    {
        [Obsolete(ObsoleteMessages.Default, error: true)]
        public Validation() => throw null!;

        public bool IsDefault => throw null!;

        [MemberNotNullWhen(true, nameof(Value))]
        [MemberNotNullWhen(false, nameof(Errors))]
        public bool IsSuccess => throw null!;

        [MemberNotNullWhen(false, nameof(Value))]
        [MemberNotNullWhen(true, nameof(Errors))]
        public bool IsFailure => throw null!;

        public T Value => throw null!;

        // Never empty on a failure (VAL-NONEMPTY). Throws on a success.
        public IReadOnlyList<Error> Errors => throw null!;

        public bool TryGetValue([MaybeNullWhen(false)] out T value) => throw null!;
        public bool TryGetErrors([MaybeNullWhen(false)] out IReadOnlyList<Error> errors) => throw null!;
        public void Deconstruct(out bool isSuccess, out T? value, out IReadOnlyList<Error>? errors) => throw null!;

        public static implicit operator Validation<T>(T value) => throw null!;
        public static implicit operator Validation<T>(Error error) => throw null!;
        // One error is a list of one. Nothing is lost.
        public static implicit operator Validation<T>(Result<T> result) => throw null!;

        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<IReadOnlyList<Error>, TResult> errorFunc) => throw null!;
        public void Switch(Action<T> successfulFunc, Action<IReadOnlyList<Error>> errorFunc) => throw null!;
        public Validation<TResult> Map<TResult>(Func<T, TResult> mapFunc) => throw null!;
        public Validation<T, TErrorResult> MapError<TErrorResult>(Func<Error, TErrorResult> errorMapFunc) => throw null!;
        public Validation<T> Tee(Action<T> teeAction) => throw null!;

        // Many errors become one only when the caller says how.
        public Result<T> ToResult(Func<IReadOnlyList<Error>, Error> combine) => throw null!;

        // Errors compare element by element (VAL-EQ).
        public bool Equals(Validation<T> other) => throw null!;
        public override bool Equals(object? obj) => throw null!;
        public override int GetHashCode() => throw null!;
        public override string ToString() => throw null!;
        public static bool operator ==(Validation<T> left, Validation<T> right) => throw null!;
        public static bool operator !=(Validation<T> left, Validation<T> right) => throw null!;

        // The list travels as one ValidationFailed<Error>, which TryFind searches.
        public Result<T> ToResult() => throw null!;
    }

    public static class Validation
    {
        public static Validation<T, TError> Ok<T, TError>(T value) => throw null!;
        public static Validation<T, TError> Fail<T, TError>(TError error) => throw null!;
        // Throws ArgumentException when errors is empty (VAL-NONEMPTY).
        public static Validation<T, TError> Fail<T, TError>(IEnumerable<TError> errors) => throw null!;
        public static Validation<T> Ok<T>(T value) => throw null!;
        public static Validation<T> Fail<T>(Error error) => throw null!;
        public static Validation<T> Fail<T>(IEnumerable<Error> errors) => throw null!;

        // The named form of `&`: both values, or every error from both sides, in order.
        public static Validation<(T1, T2), TError> Combine<T1, T2, TError>(Validation<T1, TError> v1, Validation<T2, TError> v2) => throw null!;
        public static Validation<(T1, T2)> Combine<T1, T2>(Validation<T1> v1, Validation<T2> v2) => throw null!;

        // More than two at once, named, with no nested tuples.
        public static Validation<TResult, TError> Combine<T1, T2, TError, TResult>(Validation<T1, TError> v1, Validation<T2, TError> v2, Func<T1, T2, TResult> combine) => throw null!;
        public static Validation<TResult, TError> Combine<T1, T2, T3, TError, TResult>(Validation<T1, TError> v1, Validation<T2, TError> v2, Validation<T3, TError> v3, Func<T1, T2, T3, TResult> combine) => throw null!;
        public static Validation<TResult, TError> Combine<T1, T2, T3, T4, TError, TResult>(Validation<T1, TError> v1, Validation<T2, TError> v2, Validation<T3, TError> v3, Validation<T4, TError> v4, Func<T1, T2, T3, T4, TResult> combine) => throw null!;
        public static Validation<TResult, TError> Combine<T1, T2, T3, T4, T5, TError, TResult>(Validation<T1, TError> v1, Validation<T2, TError> v2, Validation<T3, TError> v3, Validation<T4, TError> v4, Validation<T5, TError> v5, Func<T1, T2, T3, T4, T5, TResult> combine) => throw null!;
        public static Validation<TResult, TError> Combine<T1, T2, T3, T4, T5, T6, TError, TResult>(Validation<T1, TError> v1, Validation<T2, TError> v2, Validation<T3, TError> v3, Validation<T4, TError> v4, Validation<T5, TError> v5, Validation<T6, TError> v6, Func<T1, T2, T3, T4, T5, T6, TResult> combine) => throw null!;
        public static Validation<TResult, TError> Combine<T1, T2, T3, T4, T5, T6, T7, TError, TResult>(Validation<T1, TError> v1, Validation<T2, TError> v2, Validation<T3, TError> v3, Validation<T4, TError> v4, Validation<T5, TError> v5, Validation<T6, TError> v6, Validation<T7, TError> v7, Func<T1, T2, T3, T4, T5, T6, T7, TResult> combine) => throw null!;
        public static Validation<TResult, TError> Combine<T1, T2, T3, T4, T5, T6, T7, T8, TError, TResult>(Validation<T1, TError> v1, Validation<T2, TError> v2, Validation<T3, TError> v3, Validation<T4, TError> v4, Validation<T5, TError> v5, Validation<T6, TError> v6, Validation<T7, TError> v7, Validation<T8, TError> v8, Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult> combine) => throw null!;
        public static Validation<TResult> Combine<T1, T2, TResult>(Validation<T1> v1, Validation<T2> v2, Func<T1, T2, TResult> combine) => throw null!;
        public static Validation<TResult> Combine<T1, T2, T3, TResult>(Validation<T1> v1, Validation<T2> v2, Validation<T3> v3, Func<T1, T2, T3, TResult> combine) => throw null!;
        public static Validation<TResult> Combine<T1, T2, T3, T4, TResult>(Validation<T1> v1, Validation<T2> v2, Validation<T3> v3, Validation<T4> v4, Func<T1, T2, T3, T4, TResult> combine) => throw null!;
        public static Validation<TResult> Combine<T1, T2, T3, T4, T5, TResult>(Validation<T1> v1, Validation<T2> v2, Validation<T3> v3, Validation<T4> v4, Validation<T5> v5, Func<T1, T2, T3, T4, T5, TResult> combine) => throw null!;
        public static Validation<TResult> Combine<T1, T2, T3, T4, T5, T6, TResult>(Validation<T1> v1, Validation<T2> v2, Validation<T3> v3, Validation<T4> v4, Validation<T5> v5, Validation<T6> v6, Func<T1, T2, T3, T4, T5, T6, TResult> combine) => throw null!;
        public static Validation<TResult> Combine<T1, T2, T3, T4, T5, T6, T7, TResult>(Validation<T1> v1, Validation<T2> v2, Validation<T3> v3, Validation<T4> v4, Validation<T5> v5, Validation<T6> v6, Validation<T7> v7, Func<T1, T2, T3, T4, T5, T6, T7, TResult> combine) => throw null!;
        public static Validation<TResult> Combine<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(Validation<T1> v1, Validation<T2> v2, Validation<T3> v3, Validation<T4> v4, Validation<T5> v5, Validation<T6> v6, Validation<T7> v7, Validation<T8> v8, Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult> combine) => throw null!;

        // Decision 14: when the errors derive from Error, the list travels as one ValidationFailed<TError>.
        public static Result<T> ToResult<T, TError>(this Validation<T, TError> validation) where TError : Error => throw null!;
    }
}
