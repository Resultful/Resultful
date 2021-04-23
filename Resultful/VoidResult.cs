using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resultful.Utils;
using OneOf;
using static Resultful.Result;

namespace Resultful
{
    public struct VoidResult<TError>
    {
        //Private Members
        private OneOf<Unit, TError> _value;

        //Constructors
        internal VoidResult(Ok item)
            => _value = item.Value;

        internal VoidResult(Fail<TError> item)
            => _value = item.Value;

        //Implicit Converters
        public static implicit operator VoidResult<TError>(Result<Unit, TError> value)
            => value.Match(
                result => Ok().Result<TError>(),
                error => error.Err().Result());

        public static implicit operator VoidResult<TError>(Fail<TError> value)
            => new VoidResult<TError>(value);

        public static implicit operator VoidResult<TError>(Ok value)
            => new VoidResult<TError>(value);

        //Local Methods
        public TResult Match<TResult>(Func<Unit, TResult> successfulFunc, Func<TError, TResult> errorFunc)
            => _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public void Switch(Action<Unit> successfulFunc, Action<TError> errorFunc)
            => _value.Switch(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public void SwitchAsync(Func<Unit, Task> successfulFunc, Func<TError, Task> errorFunc)
            => _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public Task<TResult> MatchAsync<TResult>(Func<Unit, Task<TResult>> successfulFunc, Func<TError, Task<TResult>> errorFunc) =>
            _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public OneOf<Unit, TError> ToOneOf() => _value;

        public Result<TResult, TError> Map<TResult>(Func<Unit, TResult> mapFunc)
            => Map2(Result.Id, mapFunc);

        public Task<Result<TResult, TError>> MapAsync<TResult>(Func<Unit, Task<TResult>> mapFunc)
            => BindValueAsync(async _ => (await mapFunc.ThrowIfDefault(nameof(mapFunc))(_).ConfigureAwait(false)).Ok().Result<TError>());

        public Result<T, TError> Map<T>(T value)
            => Map(_ => value);

        public Task<Result<T, TError>> MapAsync<T>(Task<T> value)
            => MapAsync(_ => value);

        public Result<TResult, TErrorResult> Map2<TResult, TErrorResult>(Func<TError, TErrorResult> errorMapFunc, Func<Unit, TResult> mapFunc)
            => Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok().Result<TErrorResult>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Err().Result<TResult>());

        public Task<Result<TResult, TErrorResult>> Map2Async<TResult, TErrorResult>(Func<TError, Task<TErrorResult>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => Match(
                async success => (await mapFunc.ThrowIfDefault(nameof(mapFunc))().ConfigureAwait(false)).Ok().Result<TErrorResult>(),
                async errors => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).ConfigureAwait(false)).Err().Result<TResult>());


        public VoidResult<TErrorResult> MapError<TErrorResult>(Func<TError, TErrorResult> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public Task<VoidResult<TErrorResult>> MapErrorAsync<TErrorResult>(Func<TError, Task<TErrorResult>> errorMapFunc)
            => Match(
                success => Task.FromResult(Ok().Result<TErrorResult>()),
                async errors => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).ConfigureAwait(false)).Err().Result());

        public VoidResult MapError(Func<TError, IEnumerable<string>> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public Task<VoidResult> MapErrorAsync(Func<TError, Task<IEnumerable<string>>> errorMapFunc)
            => Match(
                success => Task.FromResult(Ok().Result()),
                async errors => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).ConfigureAwait(false)).Fail().Result());

        public VoidResult<TError> Bind(Func<Unit, VoidResult<TError>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                err => err.Err().Result());

        public Task<VoidResult<TError>> BindAsync(Func<Unit, Task<VoidResult<TError>>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Err().Result()));

        public Result<T, TError> BindValue<T>(Func<Unit, Result<T, TError>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                err => err.Err().Result<T>());

        public Task<Result<T, TError>> BindValueAsync<T>(Func<Unit, Task<Result<T, TError>>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Err().Result<T>()));

        public VoidResult<TError> Tee(Action<Unit> action)
            => Map(_ =>
            {
                action.ThrowIfDefault(nameof(action))(_);
                return _;
            });

        public async Task<VoidResult<TError>> TeeAsync(Func<Unit, Task> asyncFunc)
            => await MapAsync(async _ =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(_).ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);


        public VoidResult<TError> TeeError(Action<TError> action)
            => MapError(error =>
            {
                action.ThrowIfDefault(nameof(action))(error);
                return error;
            });

        public async Task<VoidResult<TError>> TeeErrorAsync(Func<TError,Task> asyncFunc)
            => await MapErrorAsync(async error =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(error).ConfigureAwait(false);
                return error;
            }).ConfigureAwait(false);

        public VoidResult<TCast> Cast<TCast>() => MapError(x => (TCast)(object)x);

    }

}
