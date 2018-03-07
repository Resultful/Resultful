using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OneOf.ROP
{
    public static partial class AsyncResult
    {
        private static async Task<TResult> WrapAsync<T, TResult>(Task<T> value, Func<T, Task<TResult>> helperFunc)
            => await helperFunc(await value.ConfigureAwait(false)).ConfigureAwait(false);

        private static async Task<T> WrapAsync<T>(Task<T> value, Func<T, Task> helperFunc)
        {
            var result = await value.ConfigureAwait(false);
            await helperFunc(result).ConfigureAwait(false);
            return result;
        }

        private static async Task AsTask<T>(Task<T> item)
        {
            await item.ConfigureAwait(false);
        }

        //Bind on Result<T, TErrror>
        public static Task<Result<TResult, TError>> BindAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Task<Result<TResult, TError>>> bindFunc)
            => WrapAsync(value, item => item.BindAsync(bindFunc));

        public static Task<Result<TResult>> BindAsync<TResult, T>(this Task<Result<T, IEnumerable<string>>> value, Func<T, Task<Result<TResult>>> bindFunc)
            => WrapAsync(value, item => item.BindAsync(bindFunc));

        public static Task<VoidResult<TError>> BindAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, Task<VoidResult<TError>>> bindFunc)
            => WrapAsync(value, item => item.BindAsync(bindFunc));

        public static Task<VoidResult> BindAsync<T>(this Task<Result<T, IEnumerable<string>>> value, Func<T, Task<VoidResult>> bindFunc)
            => WrapAsync(value, item => item.BindAsync(bindFunc));

        //Bind on Result<T>
        public static Task<Result<TResult>> BindAsync<TResult, T>(this Task<Result<T>> value, Func<T, Task<Result<TResult>>> bindFunc)
            => WrapAsync(value, item => item.BindAsync(bindFunc));

        public static Task<VoidResult> BindAsync<T>(this Task<Result<T>> value, Func<T, Task<VoidResult>> bindFunc)
            => WrapAsync(value, item => item.BindAsync(bindFunc));

        //Bind on VoidResult<TError>
        public static Task<VoidResult<TError>> BindAsync<TError>(this Task<VoidResult<TError>> value, Func<Task<VoidResult<TError>>> bindFunc)
            => WrapAsync(value, item => item.BindAsync(bindFunc));

        public static Task<VoidResult> BindAsync(this Task<VoidResult<IEnumerable<string>>> value, Func<Task<VoidResult>> bindFunc)
            => WrapAsync(value, item => item.BindAsync(bindFunc));

        public static Task<Result<T, TError>> BindAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Task<Result<T, TError>>> bindFunc)
            => WrapAsync(value, item => item.BindAsync(bindFunc));

        public static Task<Result<T>> BindAsync<T>(this Task<VoidResult<IEnumerable<string>>> value, Func<Task<Result<T>>> bindFunc)
            => WrapAsync(value, item => item.BindAsync(bindFunc));

        //Bind on VoidResult
        public static Task<VoidResult> BindAsync(this Task<VoidResult> value, Func<Task<VoidResult>> bindFunc)
            => WrapAsync(value, item => item.BindAsync(bindFunc));

        public static Task<Result<T>> BindAync<T>(this Task<VoidResult> value, Func<Task<Result<T>>> bindFunc)
            => WrapAsync(value, item => item.BindAsync(bindFunc));

        //Map on Result<T, TError>
        public static Task<Result<TResult, TError>> MapAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Task<TResult>> mapFunc)
            => WrapAsync(value, item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, T, TError, TErrorResult>(
            this Task<Result<T, TError>> value, Func<T, Task<TResult>> mapFunc, Func<TError, Task<TErrorResult>> errorMapFunc)
                => WrapAsync(value, item => item.Map2Async(mapFunc, errorMapFunc));

        //Map on Result<T>
        public static Task<Result<TResult>> MapAsync<T, TResult>(this Task<Result<T>> value, Func<T, Task<TResult>> mapFunc)
            => WrapAsync(value, item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, T, TError>(this Task<Result<T>> value, Func<T, Task<TResult>> mapFunc, Func<IEnumerable<string>, Task<TError>> errorMapFunc)
            => WrapAsync(value, item => item.Map2Async(mapFunc, errorMapFunc));

        //Map on VoidResult<TError>
        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this Task<VoidResult<TError>> value, Func<Task<TResult>> mapFunc)
            => WrapAsync(value, item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<Task<TResult>> mapFunc, Func<TError, Task<TErrorResult>> errorMapFunc)
            => WrapAsync(value, item => item.Map2Async(mapFunc, errorMapFunc));

        //Map on VoidResult
        public static Task<Result<TResult>> MapAsync<TResult>(this Task<VoidResult> value, Func<Task<TResult>> mapFunc)
            => WrapAsync(value, item => item.MapAsync(mapFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, TError>(this Task<VoidResult> value, Func<Task<TResult>> mapFunc, Func<IEnumerable<string>, Task<TError>> errorMapFunc)
            => WrapAsync(value, item => item.Map2Async(mapFunc, errorMapFunc));

        //Tee on Result<T, TError>
        public static Task<Result<T, TError>> TeeAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, Task> asyncFunc)
            => WrapAsync(value, item => item.TeeAsync(asyncFunc));

        //Tee on Result<T>
        public static Task<Result<T>> TeeAsync<T>(this Task<Result<T>> value, Func<T, Task> asyncFunc)
            => WrapAsync(value, item => AsTask(item.TeeAsync(asyncFunc)));

        //Tee on VoidResult<TError>
        public static Task<VoidResult<TError>> TeeAsync<TError>(this Task<VoidResult<TError>> value, Func<Task> asyncFunc)
            => WrapAsync(value, item => AsTask(item.TeeAsync(asyncFunc)));

        //Tee on VoidResult
        public static Task<VoidResult> TeeAsync(this Task<VoidResult> value, Func<Task> asyncFunc)
            => WrapAsync(value, item => AsTask(item.TeeAsync(asyncFunc)));
    }
}
