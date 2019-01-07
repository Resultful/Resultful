using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resultful.Utils;
using OneOf;
using static Resultful.Result;

namespace Resultful
{
    public struct Result<T, TError>
    {
        //Private Members
        private OneOf<T, TError> _value;

        //Constructors
        private Result(Ok<T> item)
            => _value = item.Value;

        private Result(Fail<TError> item)
            => _value = item.Value;

        //Implicit Converters
        public static implicit operator Result<T, TError>(Fail<TError> value)
            => new Result<T, TError>(value);

        public static implicit operator Result<T, TError>(Ok<T> value)
            => new Result<T, TError>(value);

        //Local Methods
        public void Switch(Action<T> successfulFunc, Action<TError> errorFunc)
            => _value.Switch(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<TError, TResult> errorFunc) =>
            _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public void SwitchAsync(Func<T, Task> successfulFunc, Func<TError, Task> errorFunc)
            => _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> successfulFunc, Func<TError, Task<TResult>> errorFunc) =>
            _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public OneOf<T, TError> ToOneOf() => _value;

        public Result<TResult, TError> Bind<TResult>(Func<T, Result<TResult, TError>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => error.Err());

        public Task<Result<TResult, TError>> BindAsync<TResult>(Func<T, Task<Result<TResult, TError>>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), errors => Task.FromResult<Result<TResult, TError>>(errors.Err()));

        public Result<TResult, TError> Map<TResult>(Func<T, TResult> mapFunc)
            => Map2(Result.Id, mapFunc);

        public Task<Result<TResult, TError>> MapAsync<TResult>(Func<T, Task<TResult>> mapFunc)
            => Map2Async(Task.FromResult, mapFunc);

        public Result<T, TErrorResult> MapError<TErrorResult>(Func<TError, TErrorResult> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public Task<Result<T, TErrorResult>> MapErrorAsync<TErrorResult>(Func<TError, Task<TErrorResult>> errorMapFunc)
            => Map2Async(errorMapFunc, Task.FromResult);

        public Result<T> MapError(Func<TError, IEnumerable<string>> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public Task<Result<T>> MapErrorAsync(Func<TError, Task<IEnumerable<string>>> errorMapFunc)
            => Match<Task<Result<T>>>(
                item => Task.FromResult<Result<T>>(item.Ok()),
                async error => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(error).ConfigureAwait(false)).Fail());

        public Result<TResult, TErrorResult> Map2<TResult, TErrorResult>(Func<TError, TErrorResult> errorMapFunc, Func<T, TResult> mapFunc)
            => Match<Result<TResult, TErrorResult>>(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Err());

        public Task<Result<TResult, TErrorResult>> Map2Async<TResult, TErrorResult>(
            Func<TError, Task<TErrorResult>> errorMapFunc, Func<T, Task<TResult>> mapFunc)
            => Match<Task<Result<TResult, TErrorResult>>>(
                async success => (await mapFunc(success).ConfigureAwait(false)).Ok(),
                async error => (await errorMapFunc(error).ConfigureAwait(false)).Err());

        public Result<T, TError> Tee(Action<T> teeAction)
            => Map(x =>
            {
                teeAction.ThrowIfDefault(nameof(teeAction))(x);
                return x;
            });

        public async Task<Result<T, TError>> TeeAsync(Func<T, Task> asyncFunc)
            => await MapAsync(async item =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(item).ConfigureAwait(false);
                return item;
            }).ConfigureAwait(false);

        public Result<T, TError> TeeError(Action<TError> teeAction)
            => MapError(x =>
            {
                teeAction.ThrowIfDefault(nameof(teeAction))(x);
                return x;
            });

        public async Task<Result<T, TError>> TeeErrorAsync(Func<TError, Task> asyncFunc)
            => await MapErrorAsync(async item =>
            {
                await asyncFunc(item).ConfigureAwait(false);
                return item;
            }).ConfigureAwait(false);

        public Option<T> DiscardError(Action<TError> errorAction)
            => Match(Option.Some, errors =>
            {
                errorAction.ThrowIfDefault(nameof(errorAction))(errors);
                return Option<T>.None;
            });

        public Task<Option<T>> DiscardErrorAsync(Func<TError, Task> errorAction)
            => Match(
                item => Task.FromResult(item.Some()),
                async errors =>
                {
                    await errorAction.ThrowIfDefault(nameof(errorAction))(errors);
                    return Option<T>.None;
                });

        public Option<T> DiscardError()
            => Match(Option.Some, _ => Option<T>.None);

        public VoidResult<TError> DiscardValue(Func<T, VoidResult<TError>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), err => err.Err().Result());

        public Task<VoidResult<TError>> DiscardValueAsync(Func<T, Task<VoidResult<TError>>> bindFunc)
            => Match(
                item => bindFunc.ThrowIfDefault(nameof(bindFunc))(item),
                error => Task.FromResult(error.Err().Result()));


        public VoidResult<TError> DiscardValue()
            => DiscardValue(_ => Ok().Result<TError>());


        public T ReturnOrValue(Func<TError, T> func)
            => Match(Result.Id, func);

        public Task<T> ReturnOrValueAsync(Func<TError, Task<T>> func)
            => Match(Result.IdAsync, func);

        public T ReturnOrFail<TExn>(Func<TError, TExn> failFunc) where TExn : Exception
            => Match(Result.Id, err => throw failFunc.ThrowIfDefault(nameof(failFunc))(err));

        public Result<T, TCast> CastError<TCast>() => MapError(x => (TCast)(object)x);

        public Result<TCast, TError> Cast<TCast>() => Map(x => (TCast)(object)x);
    }
}
