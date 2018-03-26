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

        public static Task<Result<TResult>> BindAsync<TResult, T>(this Result<T, IEnumerable<string>> value, Func<T, Task<Result<TResult>>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => Task.FromResult(error.Fail<TResult>()));

        public static Task<VoidResult<TError>> BindAsync<T, TError>(this Result<T, TError> value, Func<T, Task<VoidResult<TError>>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => Task.FromResult(error.Fail()));

        public static Task<VoidResult> BindAsync<T>(this Result<T, IEnumerable<string>> value, Func<T, Task<VoidResult>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => Task.FromResult(error.Fail()));

        public static Task<Result<TResult, TError>> BindAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Result<TResult, TError>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<Result<TResult>> BindAsync<TResult, T>(this Task<Result<T, IEnumerable<string>>> value, Func<T, Result<TResult>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<VoidResult<TError>> BindAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, VoidResult<TError>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<VoidResult> BindAsync<T>(this Task<Result<T, IEnumerable<string>>> value, Func<T, VoidResult> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<Result<TResult, TError>> BindAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Task<Result<TResult, TError>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        public static Task<Result<TResult>> BindAsync<TResult, T>(this Task<Result<T, IEnumerable<string>>> value, Func<T, Task<Result<TResult>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        public static Task<VoidResult<TError>> BindAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, Task<VoidResult<TError>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        public static Task<VoidResult> BindAsync<T>(this Task<Result<T, IEnumerable<string>>> value, Func<T, Task<VoidResult>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        //BindAsync on Result<T>
        public static Task<Result<TResult>> BindAsync<TResult, T>(this Result<T> value, Func<T, Task<Result<TResult>>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => Task.FromResult(error.Fail<TResult>()));

        public static Task<VoidResult> BindAsync<T>(this Result<T> value, Func<T, Task<VoidResult>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => Task.FromResult(error.Fail()));

        public static Task<Result<TResult>> BindAsync<TResult, T>(this Task<Result<T>> value, Func<T, Result<TResult>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<VoidResult> BindAsync<T>(this Task<Result<T>> value, Func<T, VoidResult> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<Result<TResult>> BindAsync<TResult, T>(this Task<Result<T>> value, Func<T, Task<Result<TResult>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        public static Task<VoidResult> BindAsync<T>(this Task<Result<T>> value, Func<T, Task<VoidResult>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        //MapAsync on Result<T, TError>
        public static Task<Result<TResult, TError>> MapAsync<TResult, T, TError>(this Result<T, TError> value, Func<T, Task<TResult>> mapFunc)
            => value.Map2Async(mapFunc, Task.FromResult);

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, T, TError, TErrorResult>(
            this Result<T, TError> value, Func<T, Task<TResult>> mapFunc, Func<TError, Task<TErrorResult>> errorMapFunc)
            => value.Match(
                async success => (await mapFunc(success).ConfigureAwait(false)).Ok<TResult, TErrorResult>(),
                async error => (await errorMapFunc(error).ConfigureAwait(false)).Fail<TResult, TErrorResult>());

        public static Task<Result<TResult, TError>> MapAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, TResult> mapFunc)
            => value.WrapAsync(item => item.Map(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<T, TResult> mapFunc, Func<TError, TErrorResult> errorMapFunc)
            => value.WrapAsync(item => item.Map2(mapFunc, errorMapFunc));

        public static Task<Result<TResult, TError>> MapAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Task<TResult>> mapFunc)
            => value.WrapAsync(item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, T, TError, TErrorResult>(
            this Task<Result<T, TError>> value, Func<T, Task<TResult>> mapFunc, Func<TError, Task<TErrorResult>> errorMapFunc)
            => value.WrapAsync(item => item.Map2Async(mapFunc, errorMapFunc));

        //MapAsync on Result<T>
        public static async Task<Result<TResult>> MapAsync<T, TResult>(this Result<T> value, Func<T, Task<TResult>> mapFunc)
            => await value.Map2Async(mapFunc, Task.FromResult).ConfigureAwait(false);

        public static Task<Result<TResult, TError>> Map2Async<TResult, T, TError>(this Result<T> value, Func<T, Task<TResult>> mapFunc, Func<IEnumerable<string>, Task<TError>> errorMapFunc)
            => value.Match(
                async success => (await mapFunc(success).ConfigureAwait(false)).Ok<TResult, TError>(),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail<TResult, TError>());

        public static Task<Result<TResult, TError>> Map2Async<TResult, TError>(this VoidResult value, Func<Task<TResult>> mapFunc, Func<IEnumerable<string>, Task<TError>> errorMapFunc)
            => value.Match(
                async success => (await mapFunc().ConfigureAwait(false)).Ok<TResult, TError>(),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail<TResult, TError>());

        public static Task<Result<TResult>> MapAsync<T, TResult>(this Task<Result<T>> value, Func<T, TResult> mapFunc)
            => value.WrapAsync(item => item.Map(mapFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, T, TError>(this Task<Result<T>> value, Func<T, TResult> mapFunc, Func<IEnumerable<string>, TError> errorMapFunc)
            => value.WrapAsync(item => item.Map2(mapFunc, errorMapFunc));

        public static Task<Result<TResult>> MapAsync<T, TResult>(this Task<Result<T>> value, Func<T, Task<TResult>> mapFunc)
            => value.WrapAsync(item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, T, TError>(this Task<Result<T>> value, Func<T, Task<TResult>> mapFunc, Func<IEnumerable<string>, Task<TError>> errorMapFunc)
            => value.WrapAsync(item => item.Map2Async(mapFunc, errorMapFunc));

        //TeeAsync on Result<T, TError>
        public static async Task<Result<T, TError>> TeeAsync<T, TError>(this Result<T, TError> value, Func<T, Task> asyncFunc)
            => await value.MapAsync(async x =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(x).ConfigureAwait(false);
                return x;
            }).ConfigureAwait(false);

        public static Task<Result<T, TError>> TeeAsync<T, TError>(this Task<Result<T, TError>> value, Action<T> action)
            => value.WrapAsync(item => item.Tee(action));

        public static Task<Result<T, TError>> TeeAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, Task> asyncFunc)
            => value.WrapAsync(item => item.TeeAsync(asyncFunc));

        //TeeAsync on Result<T>
        public static async Task<Result<T>> TeeAsync<T>(this Result<T> value, Func<T, Task> asyncFunc)
            => await value.MapAsync(async x =>
            {
                await asyncFunc(x).ConfigureAwait(false);
                return x;
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


        //UnwrapAsync on Result<T>
        public static Task<Result<T>> UnwrapAsync<T>(this Result<Task<T>> value)
            => value.Match(
                async item => (await item.ConfigureAwait(false)).Ok(),
                errors => Task.FromResult(errors.Fail<T>()));
    }
}
