using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OneOf.ROP.Utils;

namespace OneOf.ROP
{
    public static partial class Result
    {
        //MapAsync on VoidResult<TError>
        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this Task<VoidResult<TError>> value, Func<Unit, TResult> mapFunc)
            => value.WrapAsync(item => item.Map(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, TErrorResult> errorMapFunc, Func<Unit, TResult> mapFunc)
            => value.WrapAsync(item => item.Map2(errorMapFunc, mapFunc));

        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this Task<VoidResult<TError>> value, Task<TResult> mapFunc)
            => value.WrapAsync(item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this Task<VoidResult<TError>> value, TResult mapFunc)
            => value.WrapAsync(item => item.Map(mapFunc));

        public static Task<Result<T, TError>> MapAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Task<T>> bindFunc)
            => value.WrapAsync(item => item.MapAsync(bindFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => value.WrapAsync(item => item.Map2Async(errorMapFunc, mapFunc));

        //MapAsync on VoidResult
        public static Task<Result<TResult>> MapAsync<TResult>(this Task<VoidResult> value, Func<Unit, TResult> mapFunc)
            => value.WrapAsync(item => item.Map(mapFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, TError>(this Task<VoidResult> value, Func<IEnumerable<string>, TError> errorMapFunc, Func<Unit, TResult> mapFunc)
            => value.WrapAsync(item => item.Map2(errorMapFunc, mapFunc));

        public static Task<Result<TResult>> MapAsync<TResult>(this Task<VoidResult> value, Task<TResult> mapFunc)
            => value.WrapAsync(item => item.MapAsync(mapFunc));

        public static Task<Result<T>> MapAsync<T>(this Task<VoidResult> value, T bindFunc)
            => value.WrapAsync(item => item.Map(bindFunc));

        public static Task<Result<T>> MapValueAsync<T>(this Task<VoidResult> value, Func<Unit, Task<T>> bindFunc)
            => value.WrapAsync(item => item.MapAsync(bindFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, TError>(this Task<VoidResult> value, Func<IEnumerable<string>, Task<TError>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => value.WrapAsync(item => item.Map2Async(errorMapFunc, mapFunc));

        //MapErrorAync on VoidResult
        public static async Task<VoidResult<TErrorResult>> MapError<TErrorResult>(this Task<VoidResult> value, Func<IEnumerable<string>, TErrorResult> errorMapFunc)
            => await value.WrapAsync(item => item.Map2(errorMapFunc, Id)).ConfigureAwait(false);

        public static async Task<VoidResult<TErrorResult>> MapError<TErrorResult>(this Task<VoidResult> value, Func<IEnumerable<string>, Task<TErrorResult>> errorMapFunc)
            => await value.WrapAsync(item => item.Map2Async(errorMapFunc, () => Task.FromResult(Unit.Value))).ConfigureAwait(false);

        //MapErrorAsync on VoidResult<Terror>
        public static async Task<VoidResult<TErrorResult>> MapErrorAsync<TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, TErrorResult> errorMapFunc)
            => await value.WrapAsync(item => item.MapError(errorMapFunc));

        public static Task<VoidResult<TErrorResult>> MapErrorAsync<TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc)
            => value.WrapAsync(item => item.MapErrorAsync(errorMapFunc));

        public static Task<VoidResult> MapErrorAsync(this Task<VoidResult> value, Func<IEnumerable<string>, IEnumerable<string>> errorMapFunc)
            => value.WrapAsync(item => item.MapError(errorMapFunc));

        public static Task<VoidResult> MapErrorAsync(this Task<VoidResult> value, Func<IEnumerable<string>, Task<IEnumerable<string>>> errorMapFunc)
            => value.WrapAsync(item => item.MapErrorAsync(errorMapFunc));

        //BindAsync on VoidResult<TError>
        public static Task<VoidResult<TError>> BindAsync<TError>(this Task<VoidResult<TError>> value, Func<Unit, VoidResult<TError>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<VoidResult<TError>> BindAsync<TError>(this Task<VoidResult<TError>> value, Func<Unit, Task<VoidResult<TError>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        //BindAsync on VoidResult
        public static Task<VoidResult> BindAsync(this Task<VoidResult> value, Func<Unit, VoidResult> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<VoidResult> BindAsync(this Task<VoidResult> value, Func<Task<VoidResult>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        //BindValueAsync on VoidResult<TError>
        public static Task<Result<T, TError>> BindValueAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Result<T, TError>> bindFunc)
            => value.WrapAsync(item => item.BindValue(bindFunc));

        public static Task<Result<T, TError>> BindValueAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Task<Result<T, TError>>> bindFunc)
            => value.WrapAsync(item => item.BindValueAsync(bindFunc));

        //BindValueAsync on VoidResult
        public static Task<Result<T>> BindValueAsync<T>(this Task<VoidResult> value, Func<Unit, Result<T>> bindFunc)
            => value.WrapAsync(item => item.BindValue(bindFunc));

        public static Task<Result<T>> BindValueAsync<T>(this Task<VoidResult> value, Func<Unit, Task<Result<T>>> bindFunc)
            => value.WrapAsync(item => item.BindValueAsync(bindFunc));

        //TeeAsync on VoidResult<TError>
        public static Task<VoidResult<TError>> TeeAsync<TError>(this Task<VoidResult<TError>> value, Action<Unit> action)
            => value.WrapAsync(item => item.Tee(action));

        public static Task<VoidResult<TError>> TeeAsync<TError>(this Task<VoidResult<TError>> value, Func<Unit ,Task> asyncFunc)
            => value.WrapAsync(item => item.TeeAsync(asyncFunc).AsTask());

        //TeeAsync on VoidResult
        public static Task<VoidResult> TeeAsync(this Task<VoidResult> value, Action<Unit> action)
            => value.WrapAsync(item => item.Tee(action));

        public static Task<VoidResult> TeeAsync(this Task<VoidResult> value, Func<Unit, Task> asyncFunc)
            => value.WrapAsync(item => item.TeeAsync(asyncFunc).AsTask());

        //TeeErrorAsync on VoidResult<TError>
        public static Task<VoidResult<TError>> TeeErrorAsync<TError>(this Task<VoidResult<TError>> value, Action<TError> action)
            => value.WrapAsync(item => item.TeeError(action));

        public static Task<VoidResult<TError>> TeeErrorAsync<TError>(this Task<VoidResult<TError>> value, Func<TError, Task> asyncFunc)
            => value.WrapAsync(item => item.TeeErrorAsync(asyncFunc).AsTask());

        //TeeErrorAsync on VoidResult
        public static Task<VoidResult> TeeErrorAsync(this Task<VoidResult> value, Action<IEnumerable<string>> action)
            => value.WrapAsync(item => item.TeeError(action));

        public static Task<VoidResult> TeeErrorAsync(this Task<VoidResult> value, Func<IEnumerable<string>, Task> asyncFunc)
            => value.WrapAsync(item => item.TeeErrorAsync(asyncFunc).AsTask());


        //FlattenAsync on VoidResult<T>
        public static Task<VoidResult<TError>> FlattenAsync<TError>(this Task<VoidResult<VoidResult<TError>>> value)
            => value.WrapAsync(item => item.Flatten());
    }
}
