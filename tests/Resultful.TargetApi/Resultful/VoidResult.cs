#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resultful
{
    [Obsolete(ObsoleteMessages.VoidResult)]
    public readonly partial struct VoidResult<TError>
    {
        //Implicit Converters
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static implicit operator VoidResult<TError>(Result<Unit, TError> value) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static implicit operator VoidResult<TError>(TError value) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static implicit operator VoidResult<TError>(Unit value) => throw null!;
        //Local Methods
        public TResult Match<TResult>(Func<Unit, TResult> successfulFunc, Func<TError, TResult> errorFunc) => throw null!;
        public void Switch(Action<Unit> successfulFunc, Action<TError> errorFunc) => throw null!;
        public Task SwitchAsync(Func<Unit, Task> successfulFunc, Func<TError, Task> errorFunc) => throw null!;
        public Task<TResult> MatchAsync<TResult>(Func<Unit, Task<TResult>> successfulFunc, Func<TError, Task<TResult>> errorFunc) => throw null!;
        public Result<TResult, TError> Map<TResult>(Func<Unit, TResult> mapFunc) => throw null!;
        public Task<Result<TResult, TError>> MapAsync<TResult>(Func<Unit, Task<TResult>> mapFunc) => throw null!;
        public Result<T, TError> Map<T>(T value) => throw null!;
        public Task<Result<T, TError>> MapAsync<T>(Task<T> value) => throw null!;
        public Result<TResult, TErrorResult> Map2<TResult, TErrorResult>(Func<TError, TErrorResult> errorMapFunc, Func<Unit, TResult> mapFunc) => throw null!;
        public Task<Result<TResult, TErrorResult>> Map2Async<TResult, TErrorResult>(Func<TError, Task<TErrorResult>> errorMapFunc, Func<Task<TResult>> mapFunc) => throw null!;
        public VoidResult<TErrorResult> MapError<TErrorResult>(Func<TError, TErrorResult> errorMapFunc) => throw null!;
        public Task<VoidResult<TErrorResult>> MapErrorAsync<TErrorResult>(Func<TError, Task<TErrorResult>> errorMapFunc) => throw null!;
        public VoidResult MapError(Func<TError, Error> errorMapFunc) => throw null!;
        public Task<VoidResult> MapErrorAsync(Func<TError, Task<Error>> errorMapFunc) => throw null!;
        public VoidResult<TError> Bind(Func<Unit, VoidResult<TError>> bindFunc) => throw null!;
        public Task<VoidResult<TError>> BindAsync(Func<Unit, Task<VoidResult<TError>>> bindFunc) => throw null!;
        public Result<T, TError> BindValue<T>(Func<Unit, Result<T, TError>> bindFunc) => throw null!;
        public Task<Result<T, TError>> BindValueAsync<T>(Func<Unit, Task<Result<T, TError>>> bindFunc) => throw null!;
        public VoidResult<TError> Tee(Action<Unit> action) => throw null!;
        public Task<VoidResult<TError>> TeeAsync(Func<Unit, Task> asyncFunc) => throw null!;
        public VoidResult<TError> TeeError(Action<TError> action) => throw null!;
        public Task<VoidResult<TError>> TeeErrorAsync(Func<TError, Task> asyncFunc) => throw null!;
        public VoidResult<TCast> Cast<TCast>() => throw null!;
    }
}
