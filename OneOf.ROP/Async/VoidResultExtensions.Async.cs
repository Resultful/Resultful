using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OneOf.ROP.Utils;

namespace OneOf.ROP.Async
{
    public static partial class Result
    {
        //MapAsync on VoidResult<TError>
        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this VoidResult<TError> value, Func<Task<TResult>> mapFunc)
            => value.Map2Async(Task.FromResult, mapFunc);

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, TError, TErrorResult>(this VoidResult<TError> value, Func<TError, Task<TErrorResult>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => value.Match(
                async success => (await mapFunc.ThrowIfDefault(nameof(mapFunc))().ConfigureAwait(false)).Ok<TResult, TErrorResult>(),
                async errors => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).ConfigureAwait(false)).Fail<TResult, TErrorResult>());

        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this Task<VoidResult<TError>> value, Func<Unit, TResult> mapFunc)
            => value.WrapAsync(item => item.Map(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, TErrorResult> errorMapFunc, Func<Unit, TResult> mapFunc)
            => value.WrapAsync(item => item.Map2(errorMapFunc, mapFunc));

        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this Task<VoidResult<TError>> value, Func<Task<TResult>> mapFunc)
            => value.WrapAsync(item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => value.WrapAsync(item => item.Map2Async(errorMapFunc, mapFunc));

        //MapAsync on VoidResult
        public static async Task<Result<TResult>> MapAsync<TResult>(this VoidResult value, Func<Task<TResult>> mapFunc)
            => await value.Map2Async(Task.FromResult, mapFunc).ConfigureAwait(false);

        public static Task<Result<TResult, TError>> Map2Async<TResult, TError>(this VoidResult value, Func<IEnumerable<string>, Task<TError>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => value.Match(
                async success => (await mapFunc().ConfigureAwait(false)).Ok<TResult, TError>(),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail<TResult, TError>());

        public static Task<Result<TResult>> MapAsync<TResult>(this Task<VoidResult> value, Func<Unit, TResult> mapFunc)
            => value.WrapAsync(item => item.Map(mapFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, TError>(this Task<VoidResult> value, Func<IEnumerable<string>, TError> errorMapFunc, Func<Unit, TResult> mapFunc)
            => value.WrapAsync(item => item.Map2(errorMapFunc, mapFunc));

        public static Task<Result<TResult>> MapAsync<TResult>(this Task<VoidResult> value, Func<Task<TResult>> mapFunc)
            => value.WrapAsync(item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, TError>(this Task<VoidResult> value, Func<IEnumerable<string>, Task<TError>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => value.WrapAsync(item => item.Map2Async(errorMapFunc, mapFunc));

        //BindAsync on VoidResult<TError>
        public static Task<VoidResult<TError>> BindAsync<TError>(this VoidResult<TError> value, Func<Unit, Task<VoidResult<TError>>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => Task.FromResult(error.Fail()));

        public static Task<VoidResult<TError>> BindAsync<TError>(this Task<VoidResult<TError>> value, Func<Unit, VoidResult<TError>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));


        public static Task<VoidResult<TError>> BindAsync<TError>(this Task<VoidResult<TError>> value, Func<Unit, Task<VoidResult<TError>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        //BindAsync on VoidResult
        public static Task<VoidResult> BindAsync(this VoidResult value, Func<Task<VoidResult>> bindFunc)
            => value.Match(x => bindFunc.ThrowIfDefault(nameof(bindFunc))(), error => Task.FromResult(error.Fail()));

        public static Task<VoidResult> BindAsync(this Task<VoidResult> value, Func<Unit, VoidResult> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<VoidResult> BindAsync(this Task<VoidResult> value, Func<Task<VoidResult>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        //BindValue on VoidResult<TError>
        public static Task<Result<T, TError>> BindValueAsync<T, TError>(this VoidResult<TError> value, Func<Unit, Task<Result<T, TError>>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => Task.FromResult(error.Fail<T, TError>()));

        public static Task<Result<T, TError>> BindValueAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Result<T, TError>> bindFunc)
            => value.WrapAsync(item => item.BindValue(bindFunc));

        public static Task<Result<T, TError>> BindValueAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Task<Result<T, TError>>> bindFunc)
            => value.WrapAsync(item => item.BindValueAsync(bindFunc));

        //BindValue on VoidResult
        public static Task<Result<T>> BindValueAsync<T>(this VoidResult value, Func<Unit, Task<Result<T>>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => Task.FromResult(error.Fail<T>()));

        public static Task<Result<T>> BindValueAsync<T>(this Task<VoidResult> value, Func<Unit, Result<T>> bindFunc)
            => value.WrapAsync(item => item.BindValue(bindFunc));

        public static Task<Result<T>> BindValueAsync<T>(this Task<VoidResult> value, Func<Unit, Task<Result<T>>> bindFunc)
            => value.WrapAsync(item => item.BindValueAsync(bindFunc));

        //TeeAsync on VoidResult<TError>
        public static async Task<VoidResult<TError>> TeeAsync<TError>(this VoidResult<TError> value, Func<Task> asyncFunc)
            => await value.MapAsync(async () =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))().ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);

        public static Task<VoidResult<TError>> TeeAsync<TError>(this Task<VoidResult<TError>> value, Action action)
            => value.WrapAsync(item => item.Tee(action));

        public static Task<VoidResult<TError>> TeeAsync<TError>(this Task<VoidResult<TError>> value, Func<Task> asyncFunc)
            => value.WrapAsync(item => item.TeeAsync(asyncFunc).AsTask());

        //TeeAsync on VoidResult
        public static async Task<VoidResult> TeeAsync(this VoidResult value, Func<Task> asyncFunc)
            => await value.MapAsync(async () =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))().ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);

        public static Task<VoidResult> TeeAsync(this Task<VoidResult> value, Action action)
            => value.WrapAsync(item => item.Tee(action));

        public static Task<VoidResult> TeeAsync(this Task<VoidResult> value, Func<Task> asyncFunc)
            => value.WrapAsync(item => item.TeeAsync(asyncFunc).AsTask());

        //FlattenAsync on VoidResult<T>
        public static Task<VoidResult<TError>> FlattenAsync<TError>(this Task<VoidResult<VoidResult<TError>>> value)
            => value.WrapAsync(item => item.Flatten());
    }
}
