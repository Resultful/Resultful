#nullable enable
#if NET
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resultful
{
    // M2 async (19), net10.0 and later: ValueTask receivers and IAsyncEnumerable helpers.
    // netstandard2.0 has neither type without a package, and the core references nothing (decision 1).
    public static partial class Result
    {
        public static ValueTask<Result<TResult, TError>> Map<TResult, T, TError>(this ValueTask<Result<T, TError>> value, Func<T, TResult> mapFunc) => throw null!;
        public static ValueTask<Result<TResult, TError>> Bind<TResult, T, TError>(this ValueTask<Result<T, TError>> value, Func<T, Result<TResult, TError>> bindFunc) => throw null!;
        public static ValueTask<Result<TResult, TError>> BindAsync<TResult, T, TError>(this ValueTask<Result<T, TError>> value, Func<T, ValueTask<Result<TResult, TError>>> bindFunc) => throw null!;
        public static ValueTask<Result<T, TErrorResult>> MapError<T, TError, TErrorResult>(this ValueTask<Result<T, TError>> value, Func<TError, TErrorResult> errorMapFunc) => throw null!;
        public static ValueTask<TResult> Match<T, TError, TResult>(this ValueTask<Result<T, TError>> value, Func<T, TResult> successFunc, Func<TError, TResult> errorFunc) => throw null!;
        public static ValueTask<Result<T, TError>> Tee<T, TError>(this ValueTask<Result<T, TError>> value, Action<T> action) => throw null!;

        public static ValueTask<Result<TResult>> Map<T, TResult>(this ValueTask<Result<T>> value, Func<T, TResult> mapFunc) => throw null!;
        public static ValueTask<Result<TResult>> Bind<TResult, T>(this ValueTask<Result<T>> value, Func<T, Result<TResult>> bindFunc) => throw null!;
        public static ValueTask<Result<TResult>> BindAsync<TResult, T>(this ValueTask<Result<T>> value, Func<T, ValueTask<Result<TResult>>> bindFunc) => throw null!;
        public static ValueTask<Result<T, TError>> MapError<T, TError>(this ValueTask<Result<T>> value, Func<Error, TError> errorMapFunc) => throw null!;
        public static ValueTask<TResult> Match<T, TResult>(this ValueTask<Result<T>> value, Func<T, TResult> successFunc, Func<Error, TResult> errorFunc) => throw null!;
        public static ValueTask<Result<T>> Tee<T>(this ValueTask<Result<T>> value, Action<T> action) => throw null!;

        // Stops at the first failure.
        public static ValueTask<Result<IReadOnlyList<T>, TError>> CollectAsync<T, TError>(this IAsyncEnumerable<Result<T, TError>> values, CancellationToken cancellationToken) => throw null!;
        public static ValueTask<Result<IReadOnlyList<T>>> CollectAsync<T>(this IAsyncEnumerable<Result<T>> values, CancellationToken cancellationToken) => throw null!;

        // Reads every item: the successes and the failures, each in order.
        public static ValueTask<(IReadOnlyList<T> Values, IReadOnlyList<TError> Errors)> PartitionAsync<T, TError>(this IAsyncEnumerable<Result<T, TError>> values, CancellationToken cancellationToken) => throw null!;
        public static ValueTask<(IReadOnlyList<T> Values, IReadOnlyList<Error> Errors)> PartitionAsync<T>(this IAsyncEnumerable<Result<T>> values, CancellationToken cancellationToken) => throw null!;
    }
}
#endif
