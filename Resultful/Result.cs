using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resultful.Utils;
using OneOf.Types;
using OneOf;

namespace Resultful
{
    public struct Result<T, TError>
    {
        //Private Members
        private OneOf<T, TError> _value;

        //Constructors
        internal Result(T value)
            => _value = value;

        internal Result(TError value)
            => _value = value;

        //Implicit Converters
        public static implicit operator Result<T, TError>(TError value)
            => value.Fail<T, TError>();

        public static implicit operator Result<T, TError>(T value)
            => value.Ok<T, TError>();

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
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail<TResult, TError>);

        public Task<Result<TResult, TError>> BindAsync<TResult>(Func<T, Task<Result<TResult, TError>>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), errors => Task.FromResult(errors.Fail<TResult, TError>()));

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
            => Match(
                item => Task.FromResult(item.Ok()),
                async error => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(error).ConfigureAwait(false)).Fail<T>());

        public Result<TResult, TErrorResult> Map2<TResult, TErrorResult>(Func<TError, TErrorResult> errorMapFunc, Func<T, TResult> mapFunc)
            => Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TErrorResult>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TErrorResult>());

        public Task<Result<TResult, TErrorResult>> Map2Async<TResult, TErrorResult>(
            Func<TError, Task<TErrorResult>> errorMapFunc, Func<T, Task<TResult>> mapFunc)
            => Match(
                async success => (await mapFunc(success).ConfigureAwait(false)).Ok<TResult, TErrorResult>(),
                async error => (await errorMapFunc(error).ConfigureAwait(false)).Fail<TResult, TErrorResult>());

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
                return new None();
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
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail);

        public Task<VoidResult<TError>> DiscardValueAsync(Func<T, Task<VoidResult<TError>>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Fail()));


        public VoidResult<TError> DiscardValue()
            => DiscardValue(_ => Result.Ok<TError>());


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
