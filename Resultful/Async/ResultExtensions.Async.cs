using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OneOf;
using Resultful.Utils;
using TaskExt;

namespace Resultful
{

    public static partial class Result
    {
        //ToOneOf
        public static Task<OneOf<T, IEnumerable<string>>> ToOneOf<T>(this Task<Result<T>> value)
            => value.Map(x => x.ToOneOf());

        public static Task<OneOf<T, TError>> ToOneOf<T, TError>(this Task<Result<T, TError>> value)
            => value.Map(x => x.ToOneOf());

        //Switch on Task<Result<T>>
        public static Task Switch<T>(this Task<Result<T>> value, Action<T> successFunc, Action<IEnumerable<string>> errorFunc)
            => value.Discard(item => item.Switch(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        public static Task SwitchAsync<T>(this Task<Result<T>> value, Func<T, Task> successFunc, Func<IEnumerable<string>, Task> errorFunc)
            => value.DiscardAsync(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //Match on Task<Option<T>>
        public static Task<TResult> MatchAsync<T, TResult>(this Task<Result<T>> value, Func<T, Task<TResult>> successFunc, Func<IEnumerable<string>, Task<TResult>> errorFunc)
            => value.Bind(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        public static Task<TResult> Match<T, TResult>(this Task<Result<T>> value, Func<T, TResult> successFunc, Func<IEnumerable<string>, TResult> errorFunc)
            => value.Map(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //Switch on Task<Result<T, TError>>
        public static Task Switch<T, TError>(this Task<Result<T, TError>> value, Action<T> successFunc, Action<TError> errorFunc)
            => value.Discard(item => item.Switch(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        public static Task SwitchAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, Task> successFunc, Func<TError, Task> errorFunc)
            => value.DiscardAsync(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //Match on Task<Result<T, TError>>
        public static Task<TResult> MatchAsync<T, TError, TResult>(this Task<Result<T, TError>> value, Func<T, Task<TResult>> successFunc, Func<TError, Task<TResult>> errorFunc)
            => value.Bind(item => item.MatchAsync(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        public static Task<TResult> Match<T, TError, TResult>(this Task<Result<T, TError>> value, Func<T, TResult> successFunc, Func<TError, TResult> errorFunc)
            => value.Map(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //BindAsync on Result<T, TError>
        public static Task<Result<TResult, TError>> Bind<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Result<TResult, TError>> bindFunc)
            => value.Map(item => item.Bind(bindFunc));

        public static Task<Result<TResult, TError>> BindAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Task<Result<TResult, TError>>> bindFunc)
            => value.Bind(item => item.BindAsync(bindFunc));

        //BindAsync on Result<T>
        public static Task<Result<TResult>> Bind<TResult, T>(this Task<Result<T>> value, Func<T, Result<TResult>> bindFunc)
            => value.Map(item => item.Bind(bindFunc));

        public static Task<Result<TResult>> BindAsync<TResult, T>(this Task<Result<T>> value, Func<T, Task<Result<TResult>>> bindFunc)
            => value.Bind(item => item.BindAsync(bindFunc));

        //MapAsync on Result<T, TError>
        public static Task<Result<TResult, TError>> Map<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, TResult> mapFunc)
            => value.Map(item => item.Map(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2<TResult, T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<TError, TErrorResult> errorMapFunc, Func<T, TResult> mapFunc)
            => value.Map(item => item.Map2(errorMapFunc, mapFunc));

        public static Task<Result<TResult, TError>> MapAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Task<TResult>> mapFunc)
            => value.Bind(item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, T, TError, TErrorResult>(
            this Task<Result<T, TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc, Func<T, Task<TResult>> mapFunc)
            => value.Bind(item => item.Map2Async(errorMapFunc, mapFunc));

        //MapAsync on Result<T>
        public static Task<Result<TResult>> Map<T, TResult>(this Task<Result<T>> value, Func<T, TResult> mapFunc)
            => value.Map(item => item.Map(mapFunc));

        public static Task<Result<TResult, TError>> Map2<TResult, T, TError>(this Task<Result<T>> value, Func<IEnumerable<string>, TError> errorMapFunc, Func<T, TResult> mapFunc)
            => value.Map(item => item.Map2(errorMapFunc, mapFunc));

        public static Task<Result<TResult>> MapAsync<T, TResult>(this Task<Result<T>> value, Func<T, Task<TResult>> mapFunc)
            => value.Bind(item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, T, TError>(this Task<Result<T>> value, Func<IEnumerable<string>, Task<TError>> errorMapFunc, Func<T, Task<TResult>> mapFunc)
            => value.Bind(item => item.Map2Async(errorMapFunc, mapFunc));

        //MapErrorAsync on Result<T, TError>
        public static Task<Result<T, TErrorResult>> MapError<T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<TError, TErrorResult> errorMapFunc)
            => value.Map(item => item.MapError(errorMapFunc));

        public static Task<Result<T, TErrorResult>> MapErrorAsync<T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc)
            => value.Bind(item => item.MapErrorAsync(errorMapFunc));

        public static Task<Result<T>> MapError<T, TError>(this Task<Result<T, TError>> value, Func<TError, IEnumerable<string>> errorMapFunc)
            => value.Map(item => item.MapError(errorMapFunc));

        public static Task<Result<T>> MapErrorAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, Task<IEnumerable<string>>> errorMapFunc)
            => value.Bind(item => item.MapErrorAsync(errorMapFunc));

        //MapError on Result<T>
        public static Task<Result<T, TError>> MapError<T, TError>(this Task<Result<T>> value, Func<IEnumerable<string>, TError> errorMapFunc)
            => value.Map(item => item.MapError(errorMapFunc));

        public static Task<Result<T, TError>> MapErrorAsync<T, TError>(this Task<Result<T>> value, Func<IEnumerable<string>, Task<TError>> errorMapFunc)
            => value.Bind(item => item.MapErrorAsync(errorMapFunc));

        //TeeAsync on Result<T, TError>
        public static Task<Result<T, TError>> Tee<T, TError>(this Task<Result<T, TError>> value, Action<T> action)
            => value.Map(item => item.Tee(action));

        public static Task<Result<T, TError>> TeeAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, Task> asyncFunc)
            => value.Bind(item => item.TeeAsync(asyncFunc));

        //TeeAsync on Result<T>
        public static Task<Result<T>> Tee<T>(this Task<Result<T>> value, Action<T> action)
            => value.Map(item => item.Tee(action));

        public static Task<Result<T>> TeeAsync<T>(this Task<Result<T>> value, Func<T, Task> asyncFunc)
            => value.Tee(item => item.TeeAsync(asyncFunc).Discard());

        //TeeErrorAsync on Result<T, TError>
        public static Task<Result<T, TError>> TeeError<T, TError>(this Task<Result<T, TError>> value, Action<TError> action)
            => value.Map(item => item.TeeError(action));

        public static Task<Result<T, TError>> TeeErrorAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, Task> asyncFunc)
            => value.Tee(item => item.TeeErrorAsync(asyncFunc).Discard());

        //TeeErrorAsync on Result<T>
        public static Task<Result<T>> TeeError<T>(this Task<Result<T>> value, Action<IEnumerable<string>> action)
            => value.Map(item => item.TeeError(action));

        public static Task<Result<T>> TeeErrorAsync<T>(this Task<Result<T>> value, Func<IEnumerable<string>, Task> asyncFunc)
            => value.Tee(item => item.TeeErrorAsync(asyncFunc).Discard());


        //FlattenAsync on Result<T, TError>
        public static Task<Result<T, TError>> Flatten<T, TError>(this Task<Result<Result<T, TError>, TError>> value)
            => value.Map(item => item.Flatten());

        public static Task<VoidResult<TError>> Flatten<TError>(this Task<Result<VoidResult<TError>, TError>> value)
            => value.Map(item => item.Flatten());

        //FlattenAsync on Result<T>
        public static Task<Result<T>> Flatten<T>(this Task<Result<Result<T>>> value)
            => value.Map(item => item.Flatten());

        public static Task<VoidResult> Flatten(this Task<Result<VoidResult>> value)
            => value.Map(item => item.Flatten());

        //UnwrapAsync on Result<T, TError>
        public static Task<Result<T, TError>> UnwrapAsync<T, TError>(this Result<Task<T>, TError> value)
            => value.Match(
                async item => (await item.ConfigureAwait(false)).Ok().Result<TError>(),
                errors => Task.FromResult(errors.Err().Result<T>()));

        public static Task<Result<T, TError>> UnwrapAsync<T, TError>(this Result<T, Task<TError>> value)
            => value.Match(
                item => Task.FromResult(item.Ok().Result<TError>()),
                async errors => (await errors.ConfigureAwait(false)).Err().Result<T>());

        public static Task<VoidResult<TError>> UnwrapAsync<TError>(this Result<Task, TError> value)
            => value.Match(
                async item =>
                {
                    await item.ConfigureAwait(false);
                    return Ok().Result<TError>();
                },
                errors => Task.FromResult(errors.Err().Result()));

        //UnwrapAsync on Result<T>
        public static Task<Result<T>> Unwrap<T>(this Result<Task<T>> value)
            => value.Match(
                async item => (await item.ConfigureAwait(false)).Ok().Result(),
                errors => Task.FromResult(errors.Fail().Result<T>()));

        public static Task<VoidResult> Unwrap<T>(this Result<Task> value)
            => value.Match(
                async item =>
                {
                    await item.ConfigureAwait(false);
                    return Ok().Result();
                },
                errors => Task.FromResult(errors.Fail().Result()));

        //DiscardErrorAsync on Result<T, TError>
        public static Task<Option<T>> DiscardErrorAsync<T, TError>(this Result<T, TError> value, Func<TError, Task> errorAction)
            => value.Match(
                item => Task.FromResult(item.Some()),
                async errors =>
                {
                    await errorAction.ThrowIfDefault(nameof(errorAction))(errors);
                    return Option<T>.None;
                });

        public static Task<Option<T>> DiscardError<T, TError>(this Task<Result<T, TError>> value, Action<TError> errorAction)
            => value.Map(item => item.DiscardError(errorAction));

        public static Task<Option<T>> DiscardErrorAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, Task> errorAction)
            => value.Bind(item => item.DiscardErrorAsync(errorAction));

        public static Task<Option<T>> DiscardError<T, TError>(this Task<Result<T, TError>> value)
            => value.Map(x => x.DiscardError());

        //DiscardErrorAsync on Result<T>
        public static Task<Option<T>> DiscardError<T>(this Task<Result<T>> value, Action<IEnumerable<string>> errorAction)
            => value.Map(item => item.DiscardError(errorAction));

        public static Task<Option<T>> DiscardErrorAsync<T>(this Task<Result<T>> value, Func<IEnumerable<string>, Task> errorAction)
            => value.Bind(item => item.DiscardErrorAsync(errorAction));

        public static Task<Option<T>> DiscardError<T>(this Task<Result<T>> value)
            => value.Map(item => item.DiscardError());

        //DiscardValueAsync on Result<T, TError>
        public static Task<VoidResult<TError>> DiscardValue<T, TError>(this Task<Result<T, TError>> value)
            => value.Map(item => item.DiscardValue());

        public static Task<VoidResult<TError>> DiscardValue<T, TError>(this Task<Result<T, TError>> value, Func<T, VoidResult<TError>> bindFunc)
            => value.Map(x => x.DiscardValue(bindFunc));

        public static Task<VoidResult<TError>> DiscardValueAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, Task<VoidResult<TError>>> bindFunc)
            => value.Bind(x => x.DiscardValueAsync(bindFunc));

        //DiscardValueAsync on Result<T>
        public static Task<VoidResult> DiscardValue<T>(this Task<Result<T>> value)
            => value.Map(item => item.DiscardValue());

        public static Task<VoidResult> DiscardValue<T>(this Task<Result<T>> value, Func<T, VoidResult> bindFunc)
            => value.Map(x => x.DiscardValue(bindFunc));

        public static Task<VoidResult> DiscardValueAsync<T>(this Task<Result<T>> value, Func<T, Task<VoidResult>> bindFunc)
            => value.Bind(x => x.DiscardValueAsync(bindFunc));


        //ReturnOrValue on Result<T>
        public static Task<T> ReturnOrValue<T>(this Task<Result<T>> value, Func<IEnumerable<string>, T> func)
            => value.Map(x => x.ReturnOrValue(func));

        public static Task<T> ReturnOrValueAsync<T>(this Task<Result<T>> value, Func<IEnumerable<string>, Task<T>> func)
            => value.Bind(x => x.ReturnOrValueAsync(func));

        //ReturnOrFail on Result<T>
        public static Task<T> ReturnOrFail<T, TExn>(this Task<Result<T>> value, Func<IEnumerable<string>, TExn> func) where TExn : Exception
            => value.Map(x => x.ReturnOrFail(func));

        //ReturnOrValue on Result<T, TError>
        public static Task<T> ReturnOrValue<T, TError>(this Task<Result<T, TError>> value, Func<TError, T> func)
            => value.Map(x => x.ReturnOrValue(func));

        public static Task<T> ReturnOrValueAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, Task<T>> func)
            => value.Bind(x => x.ReturnOrValueAsync(func));

        //ReturnOrFail on Result<T, TError>
        public static Task<T> ReturnOrFail<T, TError, TExn>(this Task<Result<T, TError>> value, Func<TError, TExn> func) where TExn : Exception
            => value.Map(x => x.ReturnOrFail(func));

        //Fold on Result<T>
        public static async Task<Result<TResult>> FoldAsync<TResult, T>(this IEnumerable<Result<T>> values,
            Task<Result<TResult>> seed, Func<TResult, T, Task<TResult>> aggrFunc)
        {
            var seedVal = await seed.ConfigureAwait(false);
            return await FoldAsync(values, seedVal, aggrFunc).ConfigureAwait(false);
        }

        public static async Task<Result<TResult>> FoldAsync<TResult, T>(this IEnumerable<Result<T>> values,
            Result<TResult> seed, Func<TResult, T, Task<TResult>> aggrFunc) =>
            await values.FoldAsync(seed,
                    (acc, value) => AggrHelper.FuncAsync(acc, value, aggrFunc))
                .ConfigureAwait(false);

        public static Task<Result<TResult>> FoldAsync<TResult, T>(this IEnumerable<Result<T>> values,
            TResult seed, Func<TResult, T, Task<TResult>> aggrFunc)
            => values.FoldAsync(Task.FromResult(seed.Ok()), aggrFunc);

        //FoldUntilAsync on Result<T>
        public static Task<Result<TResult>> FoldUntilAsync<TResult, T>(this IEnumerable<Result<T>> values,
            TResult seed, Func<TResult, T, Task<TResult>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).FoldUntilAsync(seed.Ok(), aggrFunc);

        public static async Task<Result<TResult>> FoldUntilAsync<TResult, T>(this IEnumerable<Result<T>> values,
            Result<TResult> seed, Func<TResult, T, Task<TResult>> aggrFunc) =>
            await values.FoldUntilAsync(seed,
                    (acc, value) => AggrHelper.FuncUntilAsync(acc, value, aggrFunc))
                .ConfigureAwait(false);

        public static async Task<Result<TResult>> FoldUntilAsync<TResult, T>(this IEnumerable<Result<T>> values,
            Task<Result<TResult>> seed, Func<TResult, T, Task<TResult>> aggrFunc)
        {
            var seedValue = await seed.ConfigureAwait(false);
            return await values.FoldUntilAsync(seedValue, aggrFunc)
                .ConfigureAwait(false);
        }

        //ReduceAsync on Result<T>
        public static Task<Result<T>> ReduceAsync<T>(this IEnumerable<Result<T>> values,
            Func<T, T, Task<T>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).ReduceAsync(
                (acc, val) => AggrHelper.FuncAsync(acc, val, aggrFunc));

        //ReduceUntil on Result<T>
        public static Task<Result<T>> ReduceUntilAsync<T>(this IEnumerable<Result<T>> values,
            Func<T, T, Task<T>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).ReduceUntilAsync(
                (acc, val) => AggrHelper.FuncUntilAsync(acc, val, aggrFunc));

        //TryReduce on Result<T>
        public static Task<Option<Result<T>>> TryReduceAsync<T>(this IEnumerable<Result<T>> values,
            Func<T, T, Task<T>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).TryReduceAsync(
                (acc, val) => AggrHelper.FuncAsync(acc, val, aggrFunc));

        //TryReduceUntilAsync on Result<T>
        public static Task<Option<Result<T>>> TryReduceUntilAsync<T>(this IEnumerable<Result<T>> values,
            Func<T, T, Task<T>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).TryReduceUntilAsync(
                (acc, val) => AggrHelper.FuncUntilAsync(acc, val, aggrFunc));

        //FoldAsync on Result<T, TError>
        public static async Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(
            this IEnumerable<Result<T, TError>> values,
            Task<Result<TResult, TError>> seed,
            Func<TError, TError, TError> mergeFunc,
            Func<TResult, T, Task<TResult>> aggrFunc)
        {
            var seedVal = await seed.ConfigureAwait(false);
            return await values.FoldAsync(seedVal, mergeFunc, aggrFunc);
        }

        public static async Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(
            this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TError, TError, TError> mergeFunc,
            Func<TResult, T, Task<TResult>> aggrFunc)
            => await EnumerableExtensions.FoldAsync(values, seed,
                    (acc, value) => AggrHelper.FuncAsync(acc, value, aggrFunc, mergeFunc))
                .ConfigureAwait(false);

        public static Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(
            this IEnumerable<Result<T, TError>> values,
            TResult seed, Func<TError, TError, TError> mergeFunc,
            Func<TResult, T, Task<TResult>> aggrFunc)
            => values.FoldAsync(seed.Ok<TResult, TError>(), mergeFunc, aggrFunc);

        public static Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(
            this IEnumerable<Result<T, TError>> values,
            TResult seed,
            Func<TResult, T, Task<TResult>> aggrFunc) where TError : IPlus<TError, TError>
            => values.FoldAsync(seed, PlusHelper.Plus, aggrFunc);

        public static Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(
            this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed,
            Func<TResult, T, Task<TResult>> aggrFunc) where TError : IPlus<TError, TError>
            => values.FoldAsync(seed, PlusHelper.Plus, aggrFunc);

        public static Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(
            this IEnumerable<Result<T, TError>> values,
            Task<Result<TResult, TError>> seed,
            Func<TResult, T, Task<TResult>> aggrFunc) where TError : IPlus<TError, TError>
            => values.FoldAsync(seed, PlusHelper.Plus, aggrFunc);


        //FoldUntil on Result<T, TError>
        public static Task<Result<TResult, TError>> FoldUntilAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            TResult seed, Func<TResult, T, Task<TResult>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).FoldUntilAsync(seed.Ok<TResult, TError>(), aggrFunc);

        public static Task<Result<TResult, TError>> FoldUntilAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TResult, T, Task<TResult>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).FoldUntilAsync(seed,
                (acc, value) =>  AggrHelper.FuncUntilAsync(acc, value, aggrFunc));

        public static async Task<Result<TResult, TError>> FoldUntilAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Task<Result<TResult, TError>> seed, Func<TResult, T, Task<TResult>> aggrFunc)
        {
            var seedValue = await seed.ConfigureAwait(false);
            return await FoldUntilAsync(values.ThrowIfDefault(nameof(values)), seedValue, aggrFunc).ConfigureAwait(false);
        }

        //Reduce on Result<T, TError>
        public static Task<Result<T, TError>> ReduceAsync<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<TError, TError, TError> mergeFunc, Func<T, T, Task<T>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).ReduceAsync(
                (acc, value) => AggrHelper.FuncAsync(acc, value, aggrFunc, mergeFunc));

        public static Task<Result<T, TError>> ReduceAsync<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<T, T, Task<T>> aggrFunc) where TError : IPlus<TError, TError>
            => values.ThrowIfDefault(nameof(values)).ReduceAsync(
                (acc, value) => AggrHelper.FuncAsync(acc, value, aggrFunc, PlusHelper.Plus));

        //ReduceUntil on Result<T, TError>
        public static Task<Result<T, TError>> ReduceUntilAsync<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, Task<T>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).ReduceUntilAsync(
                (acc, value) =>  AggrHelper.FuncUntilAsync(acc, value, aggrFunc));

        //TryReduce on Result<T, TError>
        public static Task<Option<Result<T, TError>>> TryReduceAsync<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<T, T, Task<T>> aggrFunc, Func<TError, TError, TError> mergeFunc)
            => values.ThrowIfDefault(nameof(values)).TryReduceAsync(
                (acc, value) => AggrHelper.FuncAsync(acc, value, aggrFunc, mergeFunc));

        public static Task<Option<Result<T, TError>>> TryReduceAsync<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<T, T, Task<T>> aggrFunc) where TError : IPlus<TError, TError>
            => values.ThrowIfDefault(nameof(values)).TryReduceAsync(
                (acc, value) => AggrHelper.FuncAsync(acc, value, aggrFunc, PlusHelper.Plus));

        //TryReduceUntil on Result<T, TError>
        public static Task<Option<Result<T, TError>>> TryReduceUntilAsync<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<T, T, Task<T>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).TryReduceUntilAsync(
                (acc, val) => AggrHelper.FuncUntilAsync(acc, val, aggrFunc));

    }
}
