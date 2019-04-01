using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Resultful.Utils
{
    internal static class AggrHelper
    {
        //Normal for Result<T>
        internal static Option<Result<TResult>> FuncUntil<TResult, T>(
            Result<TResult> acc,
            Result<T> value,
            Func<TResult, T, Option<TResult>> aggrFunc)
            => acc.Match(v =>
            {
                var result = value.Map(y => aggrFunc.ThrowIfDefault(nameof(aggrFunc))(v, y));
                return result.Unfold();
            }, _ => Option<Result<TResult>>.None);

        internal static Result<TResult> Func<TResult, T>(
            Result<TResult> acc,
            Result<T> value,
            Func<TResult, T, TResult> aggrFunc) =>
            acc.Plus(value)
                .Map(x =>
                {
                    var (finalAcc, finalVal) = x;
                    return aggrFunc.ThrowIfDefault(nameof(aggrFunc))(finalAcc, finalVal);
                });

        //Async for Result<T>
        internal static Task<Option<Result<TResult>>> FuncUntilAsync<TResult, T>(
            Result<TResult> acc,
            Result<T> value,
            Func<TResult, T, Task<TResult>> aggrFunc)
            => acc.MatchAsync(async v =>
            {
                var result = await value.MapAsync(
                        async y => await aggrFunc.ThrowIfDefault(nameof(aggrFunc))(v, y)
                            .ConfigureAwait(false))
                    .ConfigureAwait(false);
                return result.Some();
            }, _ => Task.FromResult(Option<Result<TResult>>.None));

        internal static Task<Result<TResult>> FuncAsync<TResult, T>(
            Result<TResult> acc,
            Result<T> value,
            Func<TResult, T, Task<TResult>> aggrFunc) =>
            acc.Plus(value)
                .MapAsync(async x =>
                {
                    var (finalAcc, finalVal) = x;
                    return await aggrFunc.ThrowIfDefault(nameof(aggrFunc))(finalAcc, finalVal)
                        .ConfigureAwait(false);
                });

        //Normal for Result<T, TError>
        internal static Option<Result<TResult, TError>> FuncUntil<TResult, T, TError>(
            Result<TResult, TError> acc,
            Result<T, TError> value,
            Func<TResult, T, TResult> aggrFunc)
            => acc.Match( v =>
            {
                var result = value.Map( y => aggrFunc.ThrowIfDefault(nameof(aggrFunc))(v, y));
                return result.Some();
            }, _ => Option<Result<TResult, TError>>.None);

        internal static Result<TResult, TError> Func<TResult, T, TError>(
            Result<TResult, TError> acc,
            Result<T, TError> value,
            Func<TResult, T, TResult> aggrFunc, Func<TError, TError, TError> mergeError) =>
            acc.Plus(value, mergeError)
                .Map( x =>
                {
                    var (finalAcc, finalVal) = x;
                    return aggrFunc.ThrowIfDefault(nameof(aggrFunc))(finalAcc, finalVal);
                });

        //Async for Result<T, TError>
        internal static Task<Option<Result<TResult, TError>>> FuncUntilAsync<TResult, T, TError>(
            Result<TResult, TError> acc,
            Result<T, TError> value,
            Func<TResult, T, Task<TResult>> aggrFunc)
            => acc.MatchAsync(async v =>
            {
                var result = await value.MapAsync(
                        async y => await aggrFunc.ThrowIfDefault(nameof(aggrFunc))(v, y)
                            .ConfigureAwait(false))
                    .ConfigureAwait(false);
                return result.Some();
            }, _ => Task.FromResult(Option<Result<TResult, TError>>.None));

        internal static Task<Result<TResult, TError>> FuncAsync<TResult, T, TError>(
            Result<TResult, TError> acc,
            Result<T, TError> value,
            Func<TResult, T, Task<TResult>> aggrFunc,
            Func<TError, TError, TError> mergeError) =>
            acc.Plus(value, mergeError)
                .MapAsync(async x =>
                {
                    var (finalAcc, finalVal) = x;
                    return await aggrFunc.ThrowIfDefault(nameof(aggrFunc))(finalAcc, finalVal)
                        .ConfigureAwait(false);
                });
    }
}
