#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resultful
{
    [Obsolete(ObsoleteMessages.VoidResult)]
    public readonly partial struct VoidResult
    {
        //Implicit Converters
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static implicit operator VoidResult(Result<Unit> value) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static implicit operator VoidResult(Unit? value) => throw null!;
        //Local Methods
        public TResult Match<TResult>(Func<Unit, TResult> successfulFunc, Func<Error, TResult> errorFunc) => throw null!;
        public void Switch(Action<Unit> successfulFunc, Action<Error> errorFunc) => throw null!;
        public Task SwitchAsync(Func<Unit, Task> successfulFunc, Func<Error, Task> errorFunc) => throw null!;
        public Task<TResult> MatchAsync<TResult>(Func<Unit, Task<TResult>> successfulFunc, Func<Error, Task<TResult>> errorFunc) => throw null!;
        public Result<TResult> Map<TResult>(Func<Unit, TResult> mapFunc) => throw null!;
        public Task<Result<TResult>> MapAsync<TResult>(Func<Unit, Task<TResult>> mapFunc) => throw null!;
        public Result<T> Map<T>(T value) => throw null!;
        public Task<Result<T>> MapAsync<T>(Task<T> value) => throw null!;
        public Result<TResult, TError> Map2<TResult, TError>(Func<Error, TError> errorMapFunc, Func<Unit, TResult> mapFunc) => throw null!;
        public Task<Result<TResult, TError>> Map2Async<TResult, TError>(Func<Error, Task<TError>> errorMapFunc, Func<Task<TResult>> mapFunc) => throw null!;
        public VoidResult<TErrorResult> MapError<TErrorResult>(Func<Error, TErrorResult> errorMapFunc) => throw null!;
        public Task<VoidResult<TErrorResult>> MapErrorAsync<TErrorResult>(Func<Error, Task<TErrorResult>> errorMapFunc) => throw null!;
        public VoidResult MapError(Func<Error, Error> errorMapFunc) => throw null!;
        public Task<VoidResult> MapErrorAsync(Func<Error, Task<Error>> errorMapFunc) => throw null!;
        public VoidResult Bind(Func<Unit, VoidResult> bindFunc) => throw null!;
        public Task<VoidResult> BindAsync(Func<Unit, Task<VoidResult>> bindFunc) => throw null!;
        public Result<T> BindValue<T>(Func<Unit, Result<T>> bindFunc) => throw null!;
        public Task<Result<T>> BindValueAsync<T>(Func<Unit, Task<Result<T>>> bindFunc) => throw null!;
        public VoidResult Tee(Action<Unit> action) => throw null!;
        public Task<VoidResult> TeeAsync(Func<Unit, Task> asyncFunc) => throw null!;
        public VoidResult TeeError(Action<Error> action) => throw null!;
        public Task<VoidResult> TeeErrorAsync(Func<Error, Task> asyncFunc) => throw null!;
    }
}
