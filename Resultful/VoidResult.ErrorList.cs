using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resultful.Utils;
using OneOf;

namespace Resultful
{
    public struct VoidResult
    {
        //Private Members
        private OneOf<Unit, IEnumerable<string>> _value;

        //Constructors
        internal VoidResult(Ok item)
            => _value = item.Value;

        internal VoidResult(Fail item)
            => _value = OneOf<Unit, IEnumerable<string>>.FromT1(item.Value);

        //Implicit Converters
        public static implicit operator VoidResult(Result<Unit, IEnumerable<string>> value)
            => value.Match<VoidResult>(_ => Result.Ok(), err => err.Fail());

        public static implicit operator VoidResult(Result<Unit> value)
            => value.Match<VoidResult>(_ => Result.Ok(), err => err.Fail());

        public static implicit operator VoidResult(VoidResult<IEnumerable<string>> value)
            => value.Match<VoidResult>(_ => Result.Ok(), err => err.Fail());

        //Implicit Converters
        public static implicit operator VoidResult(Fail value)
            => new VoidResult(value);

        public static implicit operator VoidResult(Ok value)
            => new VoidResult(value);

        //Local Methods
        public TResult Match<TResult>(Func<Unit, TResult> successfulFunc, Func<IEnumerable<string>, TResult> errorFunc)
            => _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public void Switch(Action<Unit> successfulFunc, Action<IEnumerable<string>> errorFunc)
            => _value.Switch(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public void SwitchAsync(Func<Unit, Task> successfulFunc, Func<IEnumerable<string>, Task> errorFunc)
            => _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public Task<TResult> MatchAsync<TResult>(Func<Unit, Task<TResult>> successfulFunc, Func<IEnumerable<string>, Task<TResult>> errorFunc) =>
            _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public OneOf<Unit, IEnumerable<string>> ToOneOf() => _value;

        public Result<TResult> Map<TResult>(Func<Unit, TResult> mapFunc)
            => Map2(Result.Id, mapFunc);

        public Task<Result<TResult>> MapAsync<TResult>(Func<Unit, Task<TResult>> mapFunc)
            => BindValueAsync(async _ => (await mapFunc.ThrowIfDefault(nameof(mapFunc))(_).ConfigureAwait(false)).Ok().Result());

        public Result<T> Map<T>(T value)
            => Map(_ => value);

        public Task<Result<T>> MapAsync<T>(Task<T> value)
            => MapAsync(_ => value);

        public Result<TResult, TError> Map2<TResult, TError>(Func<IEnumerable<string>, TError> errorMapFunc, Func<Unit, TResult> mapFunc)
            => Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok().Result<TError>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Err().Result<TResult>());

        public Task<Result<TResult, TError>> Map2Async<TResult, TError>(Func<IEnumerable<string>, Task<TError>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => Match(
                async success => (await mapFunc().ConfigureAwait(false)).Ok().Result<TError>(),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Err().Result<TResult>());

        public VoidResult<TErrorResult> MapError<TErrorResult>(Func<IEnumerable<string>, TErrorResult> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public Task<VoidResult<TErrorResult>> MapErrorAsync<TErrorResult>(Func<IEnumerable<string>, Task<TErrorResult>> errorMapFunc)
            => Match(
                success => Task.FromResult(Result.Ok().Result<TErrorResult>()),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Err().Result());

        public VoidResult MapError(Func<IEnumerable<string>, IEnumerable<string>> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public Task<VoidResult> MapErrorAsync(Func<IEnumerable<string>, Task<IEnumerable<string>>> errorMapFunc)
            => Match(
                success => Task.FromResult(Result.Ok().Result()),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail().Result());



        public VoidResult Bind(Func<Unit, VoidResult> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), err => err.Fail());

        public Task<VoidResult> BindAsync(Func<Unit, Task<VoidResult>> bindFunc)
            => Match(
                x => bindFunc.ThrowIfDefault(nameof(bindFunc))(Unit.Value),
                error => Task.FromResult(error.Fail().Result()));

        public Result<T> BindValue<T>(Func<Unit, Result<T>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), err => err.Fail().Result<T>());

        public Task<Result<T>> BindValueAsync<T>(Func<Unit, Task<Result<T>>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Fail().Result<T>()));

        public VoidResult Tee(Action<Unit> action)
            => Map(_ =>
            {
                action.ThrowIfDefault(nameof(action))(_);
                return _;
            });

        public async Task<VoidResult> TeeAsync(Func<Unit, Task> asyncFunc)
            => await MapAsync(async _ =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(_).ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);

        public VoidResult TeeError(Action<IEnumerable<string>> action)
            => MapError(error =>
            {
                action.ThrowIfDefault(nameof(action))(error);
                return error;
            });

        public async Task<VoidResult> TeeErrorAsync(Func<IEnumerable<string>, Task> asyncFunc)
            => await MapErrorAsync(async error =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(error).ConfigureAwait(false);
                return error;
            }).ConfigureAwait(false);
    }
}
