#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resultful
{
    public readonly partial struct Result<T> : IEquatable<Result<T>>
    {
        //Implicit Converters
        //Local Methods
        public void Switch(Action<T> successfulFunc, Action<Error> errorFunc) => throw null!;
        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<Error, TResult> errorFunc) => throw null!;
        public Task SwitchAsync(Func<T, Task> successfulFunc, Func<Error, Task> errorFunc) => throw null!;
        public Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> successfulFunc, Func<Error, Task<TResult>> errorFunc) => throw null!;
        public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> bindFunc) => throw null!;
        public Task<Result<TResult>> BindAsync<TResult>(Func<T, Task<Result<TResult>>> bindFunc) => throw null!;
        public Result<TResult> Map<TResult>(Func<T, TResult> mapFunc) => throw null!;
        public Task<Result<TResult>> MapAsync<TResult>(Func<T, Task<TResult>> mapFunc) => throw null!;
        public Result<T, TError> MapError<TError>(Func<Error, TError> errorMapFunc) => throw null!;
        public Task<Result<T, TError>> MapErrorAsync<TError>(Func<Error, Task<TError>> errorMapFunc) => throw null!;
        public Result<TResult, TError> Map2<TResult, TError>(Func<Error, TError> errorMapFunc, Func<T, TResult> mapFunc) => throw null!;
        public Task<Result<TResult, TError>> Map2Async<TResult, TError>(Func<Error, Task<TError>> errorMapFunc, Func<T, Task<TResult>> mapFunc) => throw null!;
        public Result<T> Tee(Action<T> teeAction) => throw null!;
        public Task<Result<T>> TeeAsync(Func<T, Task> asyncFunc) => throw null!;
        public Result<T> TeeError(Action<Error> teeAction) => throw null!;
        public Task<Result<T>> TeeErrorAsync(Func<Error, Task> asyncFunc) => throw null!;
        public Option<T> DiscardError(Action<Error> errorAction) => throw null!;
        public Task<Option<T>> DiscardErrorAsync(Func<Error, Task> errorAction) => throw null!;
        public Option<T> DiscardError() => throw null!;
        public Result<Unit> DiscardValue(Func<T, Result<Unit>> bindFunc) => throw null!;
        public Task<Result<Unit>> DiscardValueAsync(Func<T, Task<Result<Unit>>> bindFunc) => throw null!;
        public Result<Unit> DiscardValue() => throw null!;
        public T ReturnOrValue(Func<Error, T> func) => throw null!;
        public Task<T> ReturnOrValueAsync(Func<Error, Task<T>> func) => throw null!;
        public T ReturnOrFail<TExn>(Func<Error, TExn> failFunc)
            where TExn : Exception => throw null!;
        public Result<TCast> Cast<TCast>() => throw null!;
    }
}
