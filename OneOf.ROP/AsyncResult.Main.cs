using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static OneOf.ROP.Result;

namespace OneOf.ROP
{
    public static partial class AsyncResult
    {
        //Flatten on Result<T, TError>
        public static Task<Result<T, TError>> UnwrapAsync<T, TError>(this Result<Task<T>, TError> value)
            => value.Match(
                async item => (await item.ConfigureAwait(false)).Ok<T, TError>(),
                errors => Task.FromResult(errors.Fail<T, TError>()));

        //Flatten on Result<T>
        public static Task<Result<T>> UnwrapAsync<T>(this Result<Task<T>> value)
            => value.Match(
                async item => (await item.ConfigureAwait(false)).Ok(),
                errors => Task.FromResult(errors.Fail<T>()));


        //Bind on Result<T, TErrror>
        public static Task<Result<TResult, TError>> BindAsync<TResult, T, TError>(this Result<T, TError> value, Func<T, Task<Result<TResult, TError>>> bindFunc)
            => value.Match(bindFunc, errors => Task.FromResult(errors.Fail<TResult, TError>()));

        public static Task<Result<TResult>> BindAsync<TResult, T>(this Result<T, IEnumerable<string>> value, Func<T, Task<Result<TResult>>> bindFunc)
            => value.Match(bindFunc, error => Task.FromResult(error.Fail<TResult>()));

        public static Task<VoidResult<TError>> BindAsync<T, TError>(this Result<T, TError> value, Func<T, Task<VoidResult<TError>>> bindFunc)
            => value.Match(bindFunc, error => Task.FromResult(error.Fail()));

        public static Task<VoidResult> BindAsync<T>(this Result<T, IEnumerable<string>> value, Func<T, Task<VoidResult>> bindFunc)
            => value.Match(bindFunc, error => Task.FromResult(error.Fail()));

        //Bind on Result<T>
        public static Task<Result<TResult>> BindAsync<TResult, T>(this Result<T> value, Func<T, Task<Result<TResult>>> bindFunc)
            => value.Match(bindFunc, error => Task.FromResult(error.Fail<TResult>()));

        public static Task<VoidResult> BindAsync<T>(this Result<T> value, Func<T, Task<VoidResult>> bindFunc)
            => value.Match(bindFunc, error => Task.FromResult(error.Fail()));

        //Bind on VoidResult<TError>
        public static Task<VoidResult<TError>> BindAsync<TError>(this VoidResult<TError> value, Func<Task<VoidResult<TError>>> bindFunc)
            => value.Match(x => bindFunc(), error => Task.FromResult(error.Fail()));

        public static Task<VoidResult> BindAsync(this VoidResult<IEnumerable<string>> value, Func<Task<VoidResult>> bindFunc)
            => value.Match(x => bindFunc(), error => Task.FromResult(error.Fail()));

        public static Task<Result<T, TError>> BindAsync<T, TError>(this VoidResult<TError> value, Func<Task<Result<T, TError>>> bindFunc)
            => value.Match(x => bindFunc(), error => Task.FromResult(error.Fail<T, TError>()));

        public static Task<Result<T>> BindAsync<T>(this VoidResult<IEnumerable<string>> value, Func<Task<Result<T>>> bindFunc)
            => value.Match(x => bindFunc(), error => Task.FromResult(error.Fail<T>()));

        //Bind on VoidResult
        public static Task<VoidResult> BindAsync(this VoidResult value, Func<Task<VoidResult>> bindFunc)
            => value.Match(x => bindFunc(), error => Task.FromResult(error.Fail()));

        public static Task<Result<T>> BindAsync<T>(this VoidResult value, Func<Task<Result<T>>> bindFunc)
            => value.Match(x => bindFunc(), error => Task.FromResult(error.Fail<T>()));


        //Map on Result<T, TError>
        public static Task<Result<TResult, TError>> MapAsync<TResult, T, TError>(this Result<T, TError> value, Func<T, Task<TResult>> mapFunc)
            => value.Map2Async(mapFunc, Task.FromResult);

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, T, TError, TErrorResult>(
            this Result<T, TError> value, Func<T, Task<TResult>> mapFunc, Func<TError, Task<TErrorResult>> errorMapFunc)
            => value.Match(
                async success => (await mapFunc(success).ConfigureAwait(false)).Ok<TResult, TErrorResult>(),
                async error => (await errorMapFunc(error).ConfigureAwait(false)).Fail<TResult, TErrorResult>());

        //Map on Result<T>
        public static async Task<Result<TResult>> MapAsync<T, TResult>(this Result<T> value, Func<T, Task<TResult>> mapFunc)
            => await value.Map2Async(mapFunc, Task.FromResult).ConfigureAwait(false);

        public static Task<Result<TResult, TError>> Map2Async<TResult, T, TError>(this Result<T> value, Func<T, Task<TResult>> mapFunc, Func<IEnumerable<string>, Task<TError>> errorMapFunc)
            => value.Match(
                async success => (await mapFunc(success).ConfigureAwait(false)).Ok<TResult, TError>(),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail<TResult, TError>());

        //Map on VoidResult<TError>
        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this VoidResult<TError> value, Func<Task<TResult>> mapFunc)
            => value.Map2Async(mapFunc, Task.FromResult);

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, TError, TErrorResult>(this VoidResult<TError> value, Func<Task<TResult>> mapFunc, Func<TError, Task<TErrorResult>> errorMapFunc)
            => value.Match(
                async success => (await mapFunc().ConfigureAwait(false)).Ok<TResult, TErrorResult>(),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail<TResult, TErrorResult>());

        //Map on VoidResult
        public static async Task<Result<TResult>> MapAsync<TResult>(this VoidResult value, Func<Task<TResult>> mapFunc)
            => await value.Map2Async(mapFunc, Task.FromResult).ConfigureAwait(false);

        public static Task<Result<TResult, TError>> Map2Async<TResult, TError>(this VoidResult value, Func<Task<TResult>> mapFunc, Func<IEnumerable<string>, Task<TError>> errorMapFunc)
            => value.Match(
                async success => (await mapFunc().ConfigureAwait(false)).Ok<TResult, TError>(),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail<TResult, TError>());

        //Tee on Result<T, TError>
        public static async Task<Result<T, TError>> TeeAsync<T, TError>(this Result<T, TError> value, Func<T, Task> asyncFunc)
            => await value.MapAsync(async x =>
            {
                await asyncFunc(x).ConfigureAwait(false);
                return x;
            }).ConfigureAwait(false);

        //Tee on Result<T>
        public static async Task<Result<T>> TeeAsync<T>(this Result<T> value, Func<T, Task> asyncFunc)
            => await value.MapAsync(async x =>
            {
                await asyncFunc(x).ConfigureAwait(false);
                return x;
            }).ConfigureAwait(false);

        //Tee on VoidResult<TError>
        public static async Task<VoidResult<TError>> TeeAsync<TError>(this VoidResult<TError> value, Func<Task> asyncFunc)
            => await value.MapAsync(async () =>
            {
                await asyncFunc().ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);

        //Tee on VoidResult
        public static async Task<VoidResult> TeeAsync(this VoidResult value, Func<Task> asyncFunc)
            => await value.MapAsync(async () =>
            {
                await asyncFunc().ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);

    }
}
