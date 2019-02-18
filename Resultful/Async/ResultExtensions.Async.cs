using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resultful.Utils;
using TaskExt;

namespace Resultful
{

    public static partial class Result
    {
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
            => value.Tee(item => item.TeeAsync(asyncFunc).AsTask());

        //TeeErrorAsync on Result<T, TError>
        public static Task<Result<T, TError>> TeeError<T, TError>(this Task<Result<T, TError>> value, Action<TError> action)
            => value.Map(item => item.TeeError(action));

        public static Task<Result<T, TError>> TeeErrorAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, Task> asyncFunc)
            => value.Tee(item => item.TeeErrorAsync(asyncFunc).AsTask());

        //TeeErrorAsync on Result<T>
        public static Task<Result<T>> TeeError<T>(this Task<Result<T>> value, Action<IEnumerable<string>> action)
            => value.Map(item => item.TeeError(action));

        public static Task<Result<T>> TeeErrorAsync<T>(this Task<Result<T>> value, Func<IEnumerable<string>, Task> asyncFunc)
            => value.Tee(item => item.TeeErrorAsync(asyncFunc).AsTask());


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
                async item => (await item.ConfigureAwait(false)).Ok<T, TError>(),
                errors => Task.FromResult(errors.Fail<T, TError>()));

        public static Task<Result<T, TError>> UnwrapAsync<T, TError>(this Result<T, Task<TError>> value)
            => value.Match(
                item => Task.FromResult(item.Ok<T, TError>()),
                async errors => (await errors.ConfigureAwait(false)).Fail<T, TError>());

        public static Task<VoidResult<TError>> UnwrapAsync<TError>(this Result<Task, TError> value)
            => value.Match(
                async item =>
                {
                    await item.ConfigureAwait(false);
                    return Ok<TError>();
                },
                errors => Task.FromResult(errors.Fail()));

        //UnwrapAsync on Result<T>
        public static Task<Result<T>> Unwrap<T>(this Result<Task<T>> value)
            => value.Match(
                async item => (await item.ConfigureAwait(false)).Ok(),
                errors => Task.FromResult(errors.Fail<T>()));

        public static Task<VoidResult> Unwrap<T>(this Result<Task> value)
            => value.Match(
                async item =>
                {
                    await item.ConfigureAwait(false);
                    return Ok();
                },
                errors => Task.FromResult(errors.Fail()));

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

    }
}
