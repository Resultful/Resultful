using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OneOf.ROP.Utils;

namespace OneOf.ROP.Async
{

    public static partial class Result
    {
        //BindAsync on Result<T, TError>
        public static Task<Result<TResult, TError>> BindAsync<TResult, T, TError>(this Result<T, TError> value, Func<T, Task<Result<TResult, TError>>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), errors => Task.FromResult(errors.Fail<TResult, TError>()));

        public static Task<Result<TResult, TError>> BindAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Result<TResult, TError>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<Result<TResult, TError>> BindAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Task<Result<TResult, TError>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));


        //BindAsync on Result<T>
        public static Task<Result<TResult>> BindAsync<TResult, T>(this Result<T> value, Func<T, Task<Result<TResult>>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => Task.FromResult(error.Fail<TResult>()));

        public static Task<Result<TResult>> BindAsync<TResult, T>(this Task<Result<T>> value, Func<T, Result<TResult>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<Result<TResult>> BindAsync<TResult, T>(this Task<Result<T>> value, Func<T, Task<Result<TResult>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        //MapAsync on Result<T, TError>
        public static Task<Result<TResult, TError>> MapAsync<TResult, T, TError>(this Result<T, TError> value, Func<T, Task<TResult>> mapFunc)
            => value.Map2Async(Task.FromResult, mapFunc);

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, T, TError, TErrorResult>(
            this Result<T, TError> value, Func<TError, Task<TErrorResult>> errorMapFunc, Func<T, Task<TResult>> mapFunc)
            => value.Match(
                async success => (await mapFunc(success).ConfigureAwait(false)).Ok<TResult, TErrorResult>(),
                async error => (await errorMapFunc(error).ConfigureAwait(false)).Fail<TResult, TErrorResult>());

        public static Task<Result<TResult, TError>> MapAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, TResult> mapFunc)
            => value.WrapAsync(item => item.Map(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<TError, TErrorResult> errorMapFunc, Func<T, TResult> mapFunc)
            => value.WrapAsync(item => item.Map2(errorMapFunc, mapFunc));

        public static Task<Result<TResult, TError>> MapAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Task<TResult>> mapFunc)
            => value.WrapAsync(item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, T, TError, TErrorResult>(
            this Task<Result<T, TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc, Func<T, Task<TResult>> mapFunc)
            => value.WrapAsync(item => item.Map2Async(errorMapFunc, mapFunc));

        //MapAsync on Result<T>
        public static async Task<Result<TResult>> MapAsync<T, TResult>(this Result<T> value, Func<T, Task<TResult>> mapFunc)
            => await value.Map2Async(Task.FromResult, mapFunc).ConfigureAwait(false);

        public static Task<Result<TResult, TError>> Map2Async<TResult, T, TError>(this Result<T> value, Func<IEnumerable<string>, Task<TError>> errorMapFunc, Func<T, Task<TResult>> mapFunc)
            => value.Match(
                async success => (await mapFunc(success).ConfigureAwait(false)).Ok<TResult, TError>(),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail<TResult, TError>());

        public static Task<Result<TResult>> MapAsync<T, TResult>(this Task<Result<T>> value, Func<T, TResult> mapFunc)
            => value.WrapAsync(item => item.Map(mapFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, T, TError>(this Task<Result<T>> value, Func<IEnumerable<string>, TError> errorMapFunc, Func<T, TResult> mapFunc)
            => value.WrapAsync(item => item.Map2(errorMapFunc, mapFunc));

        public static Task<Result<TResult>> MapAsync<T, TResult>(this Task<Result<T>> value, Func<T, Task<TResult>> mapFunc)
            => value.WrapAsync(item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, T, TError>(this Task<Result<T>> value, Func<IEnumerable<string>, Task<TError>> errorMapFunc, Func<T, Task<TResult>> mapFunc)
            => value.WrapAsync(item => item.Map2Async(errorMapFunc, mapFunc));

        //MapErrorAsync on Result<T, TError>
        public static Task<Result<T, TErrorResult>> MapErrorAsync<T, TError, TErrorResult>(this Result<T, TError> value, Func<TError, Task<TErrorResult>> errorMapFunc)
            => value.Map2Async(errorMapFunc, Task.FromResult);

        public static Task<Result<T, TErrorResult>> MapErrorAsync<T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<TError, TErrorResult> errorMapFunc)
            => value.WrapAsync(item => item.MapError(errorMapFunc));

        public static Task<Result<T, TErrorResult>> MapErrorAsync<T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc)
            => value.WrapAsync(item => item.MapErrorAsync(errorMapFunc));

        public static Task<Result<T>> MapErrorAsync<T, TError>(this Result<T, TError> value, Func<TError, Task<IEnumerable<string>>> errorMapFunc)
            => value.Match(
                item => Task.FromResult(item.Ok()),
                async error => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(error).ConfigureAwait(false)).Fail<T>());

        public static Task<Result<T>> MapErrorAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, IEnumerable<string>> errorMapFunc)
            => value.WrapAsync(item => item.MapError(errorMapFunc));

        public static Task<Result<T>> MapErrorAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, Task<IEnumerable<string>>> errorMapFunc)
            => value.WrapAsync(item => item.MapErrorAsync(errorMapFunc));

        //MapError on Result<T>
        public static Task<Result<T, TError>> MapErrorAsync<T, TError>(this Result<T> value, Func<IEnumerable<string>, Task<TError>> errorMapFunc)
            => value.Match(
                item => Task.FromResult(item.Ok<T, TError>()),
                async error => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(error).ConfigureAwait(false)).Fail<T, TError>());

        public static Task<Result<T, TError>> MapErrorAsync<T, TError>(this Task<Result<T>> value, Func<IEnumerable<string>, TError> errorMapFunc)
            => value.WrapAsync(item => item.MapError(errorMapFunc));

        public static Task<Result<T, TError>> MapErrorAsync<T, TError>(this Task<Result<T>> value, Func<IEnumerable<string>, Task<TError>> errorMapFunc)
            => value.WrapAsync(item => item.MapErrorAsync(errorMapFunc));


        //TeeAsync on Result<T, TError>
        public static async Task<Result<T, TError>> TeeAsync<T, TError>(this Result<T, TError> value, Func<T, Task> asyncFunc)
            => await value.MapAsync(async item =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(item).ConfigureAwait(false);
                return item;
            }).ConfigureAwait(false);

        public static Task<Result<T, TError>> TeeAsync<T, TError>(this Task<Result<T, TError>> value, Action<T> action)
            => value.WrapAsync(item => item.Tee(action));

        public static Task<Result<T, TError>> TeeAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, Task> asyncFunc)
            => value.WrapAsync(item => item.TeeAsync(asyncFunc));

        //TeeAsync on Result<T>
        public static async Task<Result<T>> TeeAsync<T>(this Result<T> value, Func<T, Task> asyncFunc)
            => await value.MapAsync(async item =>
            {
                await asyncFunc(item).ConfigureAwait(false);
                return item;
            }).ConfigureAwait(false);

        public static Task<Result<T>> TeeAsync<T>(this Task<Result<T>> value, Action<T> action)
            => value.WrapAsync(item => item.Tee(action));

        public static Task<Result<T>> TeeAsync<T>(this Task<Result<T>> value, Func<T, Task> asyncFunc)
            => value.WrapAsync(item => item.TeeAsync(asyncFunc).AsTask());

        //FlattenAsync on Result<T, TError>
        public static Task<Result<T, TError>> FlattenAsync<T, TError>(this Task<Result<Result<T, TError>, TError>> value)
            => value.WrapAsync(item => item.Flatten());

        public static Task<VoidResult<TError>> FlattenAsync<TError>(this Task<Result<VoidResult<TError>, TError>> value)
            => value.WrapAsync(item => item.Flatten());

        //FlattenAsync on Result<T>
        public static Task<Result<T>> FlattenAsync<T>(this Task<Result<Result<T>>> value)
            => value.WrapAsync(item => item.Flatten());

        public static Task<VoidResult> FlattenAsync(this Task<Result<VoidResult>> value)
            => value.WrapAsync(item => item.Flatten());

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
                    return ROP.Result.Ok<TError>();
                },
                errors => Task.FromResult(errors.Fail()));

        //UnwrapAsync on Result<T>
        public static Task<Result<T>> UnwrapAsync<T>(this Result<Task<T>> value)
            => value.Match(
                async item => (await item.ConfigureAwait(false)).Ok(),
                errors => Task.FromResult(errors.Fail<T>()));

        public static Task<VoidResult> UnwrapAsync<T>(this Result<Task> value)
            => value.Match(
                async item =>
                {
                    await item.ConfigureAwait(false);
                    return ROP.Result.Ok();
                },
                errors => Task.FromResult(errors.Fail()));
    }
}
