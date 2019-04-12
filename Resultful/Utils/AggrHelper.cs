using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Resultful.Utils
{
    internal static class AggrHelper
    {
        //Normal for Option<T>
        internal static Option<TResult> Func<TResult, T>(
            Option<TResult> acc,
            Option<T> value,
            Func<TResult, T, TResult> aggrFunc) =>
            acc.Plus(value)
                .Map(x =>
                {
                    var (finalAcc, finalVal) = x;
                    return aggrFunc.ThrowIfDefault(nameof(aggrFunc))(finalAcc, finalVal);
                });

        internal static Option<Option<TResult>> FuncUntil<TResult, T>(
            Option<TResult> acc,
            Option<T> value,
            Func<TResult, T, Option<TResult>> aggrFunc) =>
            acc.Match(
                x => value.Map(y => aggrFunc.ThrowIfDefault(nameof(aggrFunc))(x, y)),
                _ => Option<Option<TResult>>.None);

        //Async for Option<T>

        internal static Task<Option<TResult>> FuncAsync<TResult, T>(
            Option<TResult> acc,
            Option<T> value,
            Func<TResult, T, Task<TResult>> aggrFunc) =>
            acc.Plus(value)
                .MapAsync(async x =>
                {
                    var (finalAcc, finalVal) = x;
                    return await aggrFunc.ThrowIfDefault(nameof(aggrFunc))(finalAcc, finalVal).ConfigureAwait(false);
                });

        internal static Task<Option<Option<TResult>>> FuncUntilAsync<TResult, T>(
            Option<TResult> acc,
            Option<T> value,
            Func<TResult, T, Task<Option<TResult>>> aggrFunc) =>
            acc.Match(
                x => value.MapAsync(async y => await aggrFunc.ThrowIfDefault(nameof(aggrFunc))(x, y).ConfigureAwait(false)),
                _ => Task.FromResult(Option<Option<TResult>>.None));

        //Normal for Result<T>
        internal static Result<TResult> Func<TResult, T>(
            TResult acc,
            Result<T> value,
            Func<TResult, T, TResult> aggrFunc) =>
            value.Map(x => aggrFunc.ThrowIfDefault(nameof(aggrFunc))(acc, x));

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

        internal static Option<Result<TResult>> FuncUntil<TResult, T>(
            Result<TResult> acc,
            Result<T> value,
            Func<TResult, T, TResult> aggrFunc)
            => acc.Match(v =>
            {
                var result = value.Map(y => aggrFunc.ThrowIfDefault(nameof(aggrFunc))(v, y));
                return result.Some();
            }, _ => Option<Result<TResult>>.None);



        //Async for Result<T>
        internal static Task<Result<TResult>> FuncAsync<TResult, T>(
            TResult acc,
            Result<T> value,
            Func<TResult, T, Task<TResult>> aggrFunc) =>
            value
                .MapAsync(async x => await aggrFunc.ThrowIfDefault(nameof(aggrFunc))(acc, x)
                    .ConfigureAwait(false));

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

        //Normal for Result<T, TError>
        internal static Result<TResult, TError> Func<TResult, T, TError>(
            TResult acc,
            Result<T, TError> value,
            Func<TResult, T, TResult> aggrFunc) =>
            value.Map(x => aggrFunc.ThrowIfDefault(nameof(aggrFunc))(acc, x));


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

        internal static Option<Result<TResult, TError>> FuncUntil<TResult, T, TError>(
            Result<TResult, TError> acc,
            Result<T, TError> value,
            Func<TResult, T, TResult> aggrFunc)
            => acc.Match(v =>
            {
                var result = value.Map(y => aggrFunc.ThrowIfDefault(nameof(aggrFunc))(v, y));
                return result.Some();
            }, _ => Option<Result<TResult, TError>>.None);

        //Async for Result<T, TError>
        internal static Task<Result<TResult, TError>> FuncAsync<TResult, T, TError>(
            TResult acc,
            Result<T, TError> value,
            Func<TResult, T, Task<TResult>> aggrFunc) =>
            value.MapAsync(async x => await aggrFunc.ThrowIfDefault(nameof(aggrFunc))(acc, x)
                    .ConfigureAwait(false));

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
    }
}
