#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Resultful
{
    public readonly partial struct Result<T, TError>
    {
        // Decision 10: `new Result<T, TError>()` doesn't compile; `default` is the zero value and throws on use.
        [Obsolete(ObsoleteMessages.Default, error: true)]
        public Result() => throw null!;

        // Decision 10: true for the zero value only. Never throws.
        public bool IsDefault => throw null!;

        // M2 named methods (05). Reading the value or the error of the zero value throws (08b).
        [MemberNotNullWhen(true, nameof(Value))]
        [MemberNotNullWhen(false, nameof(Error))]
        public bool IsSuccess => throw null!;

        [MemberNotNullWhen(false, nameof(Value))]
        [MemberNotNullWhen(true, nameof(Error))]
        public bool IsFailure => throw null!;

        // Throws InvalidOperationException on a failure (RF0003 flags a read without a check).
        public T Value => throw null!;

        // Throws InvalidOperationException on a success.
        public TError Error => throw null!;

        public bool TryGetValue([MaybeNullWhen(false)] out T value) => throw null!;
        public bool TryGetError([MaybeNullWhen(false)] out TError error) => throw null!;
        public void Deconstruct(out bool isSuccess, out T? value, out TError? error) => throw null!;

        // The first success wins. The function form runs only on a failure (the named form of `||`).
        public Result<T, TError> OrElse(Result<T, TError> other) => throw null!;
        public Result<T, TError> OrElse(Func<TError, Result<T, TError>> other) => throw null!;
        public T ValueOr(T fallback) => throw null!;

        // M2 async (19): CancellationToken flows to the step. Exceptions and cancellation propagate.
        public Task<Result<TResult, TError>> MapAsync<TResult>(Func<T, CancellationToken, Task<TResult>> mapFunc, CancellationToken cancellationToken) => throw null!;
        public Task<Result<T, TError>> TeeAsync(Func<T, CancellationToken, Task> asyncFunc, CancellationToken cancellationToken) => throw null!;

        // Equality, hashing and ToString never throw, including on the zero value (ZERO-TOSTRING).
        public bool Equals(Result<T, TError> other) => throw null!;
        public override bool Equals(object? obj) => throw null!;
        public override int GetHashCode() => throw null!;
        public override string ToString() => throw null!;
        public static bool operator ==(Result<T, TError> left, Result<T, TError> right) => throw null!;
        public static bool operator !=(Result<T, TError> left, Result<T, TError> right) => throw null!;
        public Task<Result<TResult, TError>> BindAsync<TResult>(Func<T, CancellationToken, Task<Result<TResult, TError>>> bindFunc, CancellationToken cancellationToken) => throw null!;
    }

    public readonly partial struct Result<T>
    {
        // Decision 10: `new Result<T>()` doesn't compile; `default` is the zero value and throws on use.
        [Obsolete(ObsoleteMessages.Default, error: true)]
        public Result() => throw null!;

        // Decision 10: true for the zero value only. Never throws.
        public bool IsDefault => throw null!;

        // M2 named methods (05). Reading the value or the error of the zero value throws (08b).
        [MemberNotNullWhen(true, nameof(Value))]
        [MemberNotNullWhen(false, nameof(Error))]
        public bool IsSuccess => throw null!;

        [MemberNotNullWhen(false, nameof(Value))]
        [MemberNotNullWhen(true, nameof(Error))]
        public bool IsFailure => throw null!;

        // Throws InvalidOperationException on a failure (RF0003 flags a read without a check).
        public T Value => throw null!;

        // Throws InvalidOperationException on a success.
        public Error Error => throw null!;

        public bool TryGetValue([MaybeNullWhen(false)] out T value) => throw null!;
        public bool TryGetError([MaybeNullWhen(false)] out Error error) => throw null!;
        public void Deconstruct(out bool isSuccess, out T? value, out Error? error) => throw null!;

        // The first success wins. The function form runs only on a failure (the named form of `||`).
        public Result<T> OrElse(Result<T> other) => throw null!;
        public Result<T> OrElse(Func<Error, Result<T>> other) => throw null!;
        public T ValueOr(T fallback) => throw null!;

        // M2 async (19): CancellationToken flows to the step. Exceptions and cancellation propagate.
        public Task<Result<TResult>> MapAsync<TResult>(Func<T, CancellationToken, Task<TResult>> mapFunc, CancellationToken cancellationToken) => throw null!;
        public Task<Result<T>> TeeAsync(Func<T, CancellationToken, Task> asyncFunc, CancellationToken cancellationToken) => throw null!;

        // Equality, hashing and ToString never throw, including on the zero value (ZERO-TOSTRING).
        public bool Equals(Result<T> other) => throw null!;
        public override bool Equals(object? obj) => throw null!;
        public override int GetHashCode() => throw null!;
        public override string ToString() => throw null!;
        public static bool operator ==(Result<T> left, Result<T> right) => throw null!;
        public static bool operator !=(Result<T> left, Result<T> right) => throw null!;
        public Task<Result<TResult>> BindAsync<TResult>(Func<T, CancellationToken, Task<Result<TResult>>> bindFunc, CancellationToken cancellationToken) => throw null!;

        // Decision 14: with no conversion from string to an error, Result<T> converts from its value, like
        // Result<T, TError> (`return row;`). `Result<string> r = "message"` is now a success (RF0004 flags it).
        public static implicit operator Result<T>(T value) => throw null!;

        // Decision 14: a failure from any Error, including a leaf (`Result<T> r = new OrderNotFound(1);`).
        public static implicit operator Result<T>(Error error) => throw null!;

        // A note on the error that keeps its type (decision 14). The Result<T, TError> form is Result.WithContext.
        public Result<T> WithContext(string message) => throw null!;

        // Keeps the error type when the function returns Error; MapError<TError> changes it.
        public Result<T> MapError(Func<Error, Error> errorMapFunc) => throw null!;
    }
}
