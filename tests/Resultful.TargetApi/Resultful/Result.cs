#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resultful
{
    public readonly partial struct Result<T, TError> : IEquatable<Result<T, TError>>
    {
        //Implicit Converters
        public static implicit operator Result<T, TError>(TError value) => throw null!;
        public static implicit operator Result<T, TError>(T value) => throw null!;
        //Local Methods
        public void Switch(Action<T> successfulFunc, Action<TError> errorFunc) => throw null!;
        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<TError, TResult> errorFunc) => throw null!;
        public Task SwitchAsync(Func<T, Task> successfulFunc, Func<TError, Task> errorFunc) => throw null!;
        public Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> successfulFunc, Func<TError, Task<TResult>> errorFunc) => throw null!;
        public Result<TResult, TError> Bind<TResult>(Func<T, Result<TResult, TError>> bindFunc) => throw null!;
        public Task<Result<TResult, TError>> BindAsync<TResult>(Func<T, Task<Result<TResult, TError>>> bindFunc) => throw null!;
        public Result<TResult, TError> Map<TResult>(Func<T, TResult> mapFunc) => throw null!;
        public Task<Result<TResult, TError>> MapAsync<TResult>(Func<T, Task<TResult>> mapFunc) => throw null!;
        public Result<T, TErrorResult> MapError<TErrorResult>(Func<TError, TErrorResult> errorMapFunc) => throw null!;
        public Task<Result<T, TErrorResult>> MapErrorAsync<TErrorResult>(Func<TError, Task<TErrorResult>> errorMapFunc) => throw null!;
        public Result<T> MapError(Func<TError, Error> errorMapFunc) => throw null!;
        public Task<Result<T>> MapErrorAsync(Func<TError, Task<Error>> errorMapFunc) => throw null!;
        public Result<TResult, TErrorResult> Map2<TResult, TErrorResult>(Func<TError, TErrorResult> errorMapFunc, Func<T, TResult> mapFunc) => throw null!;
        public Task<Result<TResult, TErrorResult>> Map2Async<TResult, TErrorResult>(Func<TError, Task<TErrorResult>> errorMapFunc, Func<T, Task<TResult>> mapFunc) => throw null!;
        public Result<T, TError> Tee(Action<T> teeAction) => throw null!;
        public Task<Result<T, TError>> TeeAsync(Func<T, Task> asyncFunc) => throw null!;
        public Result<T, TError> TeeError(Action<TError> teeAction) => throw null!;
        public Task<Result<T, TError>> TeeErrorAsync(Func<TError, Task> asyncFunc) => throw null!;
        public Option<T> DiscardError(Action<TError> errorAction) => throw null!;
        public Task<Option<T>> DiscardErrorAsync(Func<TError, Task> errorAction) => throw null!;
        public Option<T> DiscardError() => throw null!;
        public Result<Unit, TError> DiscardValue(Func<T, Result<Unit, TError>> bindFunc) => throw null!;
        public Task<Result<Unit, TError>> DiscardValueAsync(Func<T, Task<Result<Unit, TError>>> bindFunc) => throw null!;
        public Result<Unit, TError> DiscardValue() => throw null!;
        public T ReturnOrValue(Func<TError, T> func) => throw null!;
        public Task<T> ReturnOrValueAsync(Func<TError, Task<T>> func) => throw null!;
        public T ReturnOrFail<TExn>(Func<TError, TExn> failFunc)
            where TExn : Exception => throw null!;
        public Result<T, TCast> CastError<TCast>() => throw null!;
        public Result<TCast, TError> Cast<TCast>() => throw null!;
    }
}
