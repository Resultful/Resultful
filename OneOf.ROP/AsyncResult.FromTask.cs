﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OneOf.ROP
{

    public static partial class AsyncResult
    {
        private static async Task<TResult> WrapAsync<T, TResult>(Task<T> value, Func<T, TResult> helperFunc)
            => helperFunc(await value.ConfigureAwait(false));

        private static async Task<T> WrapAsync<T>(Task<T> value, Action<T> helperFunc)
        {
            var result = await value.ConfigureAwait(false);
            helperFunc(result);
            return result;
        }


        //Bind on Result<T, TErrror>
        public static Task<Result<TResult, TError>> BindAsync<TResult, T, TError>(this Task<Result<T, TError>> value,
            Func<T, Result<TResult, TError>> bindFunc)
            => WrapAsync(value, item => item.Bind(bindFunc));

        public static Task<Result<TResult>> BindAsync<TResult, T>(this Task<Result<T, IEnumerable<string>>> value, Func<T, Result<TResult>> bindFunc)
            => WrapAsync(value, item => item.Bind(bindFunc));

        public static Task<VoidResult<TError>> BindAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, VoidResult<TError>> bindFunc)
            => WrapAsync(value, item => item.Bind(bindFunc));

        public static Task<VoidResult> BindAsync<T>(this Task<Result<T, IEnumerable<string>>> value,Func<T, VoidResult> bindFunc)
            => WrapAsync(value, item => item.Bind(bindFunc));

        //Bind on Result<T>
        public static Task<Result<TResult>> BindAsync<TResult, T>(this Task<Result<T>> value, Func<T, Result<TResult>> bindFunc)
            => WrapAsync(value, item => item.Bind(bindFunc));

        public static Task<VoidResult> BindAsync<T>(this Task<Result<T>> value, Func<T, VoidResult> bindFunc)
            => WrapAsync(value, item => item.Bind(bindFunc));

        //Bind on VoidResult<TError>
        public static Task<VoidResult<TError>> BindAsync<TError>(this Task<VoidResult<TError>> value, Func<VoidResult<TError>> bindFunc)
            => WrapAsync(value, item => item.Bind(bindFunc));

        public static Task<VoidResult> BindAsync(this Task<VoidResult<IEnumerable<string>>> value, Func<VoidResult> bindFunc)
            => WrapAsync(value, item => item.Bind(bindFunc));

        public static Task<Result<T, TError>> BindAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Result<T, TError>> bindFunc)
            => WrapAsync(value, item => item.Bind(bindFunc));

        public static Task<Result<T>> BindAsync<T>(this Task<VoidResult<IEnumerable<string>>> value, Func<Result<T>> bindFunc)
            => WrapAsync(value, item => item.Bind(bindFunc));

        //Bind on VoidResult
        public static Task<VoidResult> BindAsync(this Task<VoidResult> value, Func<VoidResult> bindFunc)
            => WrapAsync(value, item => item.Bind(bindFunc));

        public static Task<Result<T>> Bind<T>(this Task<VoidResult> value, Func<Result<T>> bindFunc)
            => WrapAsync(value, item => item.Bind(bindFunc));

        //Map on Result<T, TError>
        public static Task<Result<TResult, TError>> MapAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, TResult> mapFunc)
            => WrapAsync(value, item => item.Map(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<T, TResult> mapFunc, Func<TError, TErrorResult> errorMapFunc)
            => WrapAsync(value, item => item.Map2(mapFunc, errorMapFunc));

        //Map on Result<T>
        public static Task<Result<TResult>> MapAsync<T, TResult>(this Task<Result<T>> value, Func<T, TResult> mapFunc)
            => WrapAsync(value, item => item.Map(mapFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, T, TError>(this Task<Result<T>> value, Func<T, TResult> mapFunc, Func<IEnumerable<string>, TError> errorMapFunc)
            => WrapAsync(value, item => item.Map2(mapFunc, errorMapFunc));

        //Map on VoidResult<TError>
        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this Task<VoidResult<TError>> value, Func<TResult> mapFunc)
            => WrapAsync(value, item => item.Map(mapFunc));

        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TResult> mapFunc, Func<TError, TErrorResult> errorMapFunc)
            => WrapAsync(value, item => item.Map2(mapFunc, errorMapFunc));

        //Map on VoidResult
        public static Task<Result<TResult>> MapAsync<TResult>(this Task<VoidResult> value, Func<TResult> mapFunc)
            => WrapAsync(value, item => item.Map(mapFunc));

        public static Task<Result<TResult, TError>> Map2Async<TResult, TError>(this Task<VoidResult> value, Func<TResult> mapFunc, Func<IEnumerable<string>, TError> errorMapFunc)
            => WrapAsync(value, item => item.Map2(mapFunc, errorMapFunc));

        //Flatten on Result<T, TError>
        public static Task<Result<T, TError>> FlattenAsync<T, TError>(this Task<Result<Result<T, TError>, TError>> value)
            => WrapAsync(value, item => item.Flatten());

        public static Task<VoidResult<TError>> FlattenAsync<TError>(this Task<Result<VoidResult<TError>, TError>> value)
            => WrapAsync(value, item => item.Flatten());

        //Flatten on Result<T>
        public static Task<Result<T>> FlattenAsync<T>(this Task<Result<Result<T>>> value)
            => WrapAsync(value, item => item.Flatten());

        public static Task<VoidResult> FlattenAsync(this Task<Result<VoidResult>> value)
            => WrapAsync(value, item => item.Flatten());

        //Flatten on VoidResult<T>
        public static Task<VoidResult<TError>> FlattenAsync<TError>(this Task<VoidResult<VoidResult<TError>>> value)
            => WrapAsync(value, item => item.Flatten());

        //Tee on Result<T, TError>
        public static Task<Result<T, TError>> TeeAsync<T, TError>(this Task<Result<T, TError>> value, Action<T> action)
            => WrapAsync(value, item => item.Tee(action));

        //Tee on Result<T>
        public static Task<Result<T>> TeeAsync<T>(this Task<Result<T>> value, Action<T> action)
            => WrapAsync(value, item => item.Tee(action));

        //Tee on VoidResult<TError>
        public static Task<VoidResult<TError>> TeeAsync<TError>(this Task<VoidResult<TError>> value, Action action)
            => WrapAsync(value, item => item.Tee(action));

        //Tee on VoidResult
        public static Task<VoidResult> TeeAsync(this Task<VoidResult> value, Action action)
            => WrapAsync(value, item => item.Tee(action));

    }
}
