#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resultful
{
    public static partial class Result
    {
        //Switch on Task<Result<T>>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task Switch<T>(this Task<VoidResult> value, Action<Unit> successFunc, Action<Error> errorFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task SwitchAsync<T>(this Task<VoidResult> value, Func<Unit, Task> successFunc, Func<Error, Task> errorFunc) => throw null!;
        //Match on Task<Option<T>>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<TResult> MatchAsync<T, TResult>(this Task<VoidResult> value, Func<Unit, Task<TResult>> successFunc, Func<Error, Task<TResult>> errorFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<TResult> Match<T, TResult>(this Task<VoidResult> value, Func<Unit, TResult> successFunc, Func<Error, TResult> errorFunc) => throw null!;
        //Switch on Task<Result<T, TError>>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task Switch<T, TError>(this Task<VoidResult<TError>> value, Action<Unit> successFunc, Action<TError> errorFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task SwitchAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Task> successFunc, Func<TError, Task> errorFunc) => throw null!;
        //Match on Task<Result<T, TError>>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<TResult> MatchAsync<T, TError, TResult>(this Task<VoidResult<TError>> value, Func<Unit, Task<TResult>> successFunc, Func<TError, Task<TResult>> errorFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<TResult> Match<T, TError, TResult>(this Task<VoidResult<TError>> value, Func<Unit, TResult> successFunc, Func<TError, TResult> errorFunc) => throw null!;
        //MapAsync on VoidResult<TError>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<TResult, TError>> Map<TResult, TError>(this Task<VoidResult<TError>> value, Func<Unit, TResult> mapFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<TResult, TErrorResult>> Map2<TResult, TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, TErrorResult> errorMapFunc, Func<Unit, TResult> mapFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this Task<VoidResult<TError>> value, Task<TResult> mapFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<TResult, TError>> Map<TResult, TError>(this Task<VoidResult<TError>> value, TResult mapFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<T, TError>> MapAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Task<T>> bindFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc, Func<Task<TResult>> mapFunc) => throw null!;
        //MapAsync on VoidResult
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<TResult>> Map<TResult>(this Task<VoidResult> value, Func<Unit, TResult> mapFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<TResult, TError>> Map2<TResult, TError>(this Task<VoidResult> value, Func<Error, TError> errorMapFunc, Func<Unit, TResult> mapFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<TResult>> MapAsync<TResult>(this Task<VoidResult> value, Task<TResult> mapFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<T>> Map<T>(this Task<VoidResult> value, T bindFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<T>> MapAsync<T>(this Task<VoidResult> value, Func<Unit, Task<T>> bindFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<TResult, TError>> Map2Async<TResult, TError>(this Task<VoidResult> value, Func<Error, Task<TError>> errorMapFunc, Func<Task<TResult>> mapFunc) => throw null!;
        //MapErrorAync on VoidResult
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TErrorResult>> MapError<TErrorResult>(this Task<VoidResult> value, Func<Error, TErrorResult> errorMapFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TErrorResult>> MapErrorAsync<TErrorResult>(this Task<VoidResult> value, Func<Error, Task<TErrorResult>> errorMapFunc) => throw null!;
        //MapErrorAsync on VoidResult<Terror>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TErrorResult>> MapError<TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, TErrorResult> errorMapFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TErrorResult>> MapErrorAsync<TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> MapError(this Task<VoidResult> value, Func<Error, Error> errorMapFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> MapErrorAsync(this Task<VoidResult> value, Func<Error, Task<Error>> errorMapFunc) => throw null!;
        //BindAsync on VoidResult<TError>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TError>> Bind<TError>(this Task<VoidResult<TError>> value, Func<Unit, VoidResult<TError>> bindFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TError>> BindAsync<TError>(this Task<VoidResult<TError>> value, Func<Unit, Task<VoidResult<TError>>> bindFunc) => throw null!;
        //BindAsync on VoidResult
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> Bind(this Task<VoidResult> value, Func<Unit, VoidResult> bindFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> BindAsync(this Task<VoidResult> value, Func<Unit, Task<VoidResult>> bindFunc) => throw null!;
        //BindValueAsync on VoidResult<TError>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<T, TError>> BindValue<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Result<T, TError>> bindFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<T, TError>> BindValueAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Task<Result<T, TError>>> bindFunc) => throw null!;
        //BindValueAsync on VoidResult
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<T>> BindValue<T>(this Task<VoidResult> value, Func<Unit, Result<T>> bindFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<T>> BindValueAsync<T>(this Task<VoidResult> value, Func<Unit, Task<Result<T>>> bindFunc) => throw null!;
        //TeeAsync on VoidResult<TError>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TError>> Tee<TError>(this Task<VoidResult<TError>> value, Action<Unit> action) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TError>> TeeAsync<TError>(this Task<VoidResult<TError>> value, Func<Unit, Task> asyncFunc) => throw null!;
        //TeeAsync on VoidResult
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> Tee(this Task<VoidResult> value, Action<Unit> action) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> TeeAsync(this Task<VoidResult> value, Func<Unit, Task> asyncFunc) => throw null!;
        //TeeErrorAsync on VoidResult<TError>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TError>> TeeError<TError>(this Task<VoidResult<TError>> value, Action<TError> action) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TError>> TeeErrorAsync<TError>(this Task<VoidResult<TError>> value, Func<TError, Task> asyncFunc) => throw null!;
        //TeeErrorAsync on VoidResult
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> TeeError(this Task<VoidResult> value, Action<Error> action) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> TeeErrorAsync(this Task<VoidResult> value, Func<Error, Task> asyncFunc) => throw null!;
        //FlattenAsync on VoidResult<T>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TError>> Flatten<TError>(this Task<VoidResult<VoidResult<TError>>> value) => throw null!;
    }
}
