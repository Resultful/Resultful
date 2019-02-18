using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resultful.Utils;
using OneOf.Types;
using OneOf;

namespace Resultful
{
    public struct Result<T>
    {
        //Private Members
        private OneOf<T, IEnumerable<string>> _value;

        //Constructors
        internal Result(T value)
            => _value = value;

        internal Result(IEnumerable<string> value)
            => _value = OneOf<T, IEnumerable<string>>.FromT1(value);

        //Implicit Converters
        public static implicit operator Result<T>(Result<T, IEnumerable<string>> value)
            => value.Match(
                result => result.Ok(),
                errors => errors.Fail<T>());

        public static implicit operator Result<T>(string[] value)
            => value.Fail<T>();

        public static implicit operator Result<T>(List<string> value)
            => value.Fail<T>();

        public static implicit operator Result<T>(string value)
            => Result.Fail<T>(value);

        //Local Methods
        public void Switch(Action<T> successfulFunc, Action<IEnumerable<string>> errorFunc)
            => _value.Switch(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<IEnumerable<string>, TResult> errorFunc) =>
            _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public void SwitchAsync(Func<T, Task> successfulFunc, Func<IEnumerable<string>, Task> errorFunc)
            => _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> successfulFunc, Func<IEnumerable<string>, Task<TResult>> errorFunc) =>
            _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public OneOf<T, IEnumerable<string>> ToOneOf() => _value;

        public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                errors => errors.Fail<TResult>());

        public Task<Result<TResult>> BindAsync<TResult>(Func<T, Task<Result<TResult>>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Fail<TResult>()));

        public Result<TResult> Map<TResult>(Func<T, TResult> mapFunc)
            => Map2(Result.Id, mapFunc);

        public async Task<Result<TResult>> MapAsync<TResult>(Func<T, Task<TResult>> mapFunc)
            => await Map2Async(Task.FromResult, mapFunc).ConfigureAwait(false);

        public Result<T, TError> MapError<TError>(Func<IEnumerable<string>, TError> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public Task<Result<T, TError>> MapErrorAsync<TError>(Func<IEnumerable<string>, Task<TError>> errorMapFunc)
            => Match(
                item => Task.FromResult(item.Ok<T, TError>()),
                async error => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(error).ConfigureAwait(false)).Fail<T, TError>());


        public Result<TResult, TError> Map2<TResult, TError>(Func<IEnumerable<string>, TError> errorMapFunc, Func<T, TResult> mapFunc)
            => Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TError>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TError>());

        public Task<Result<TResult, TError>> Map2Async<TResult, TError>(Func<IEnumerable<string>, Task<TError>> errorMapFunc, Func<T, Task<TResult>> mapFunc)
            => Match(
                async success => (await mapFunc(success).ConfigureAwait(false)).Ok<TResult, TError>(),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail<TResult, TError>());

        public Result<T> Tee(Action<T> teeAction)
            => Map(x =>
            {
                teeAction.ThrowIfDefault(nameof(teeAction))(x);
                return x;
            });

        public async Task<Result<T>> TeeAsync(Func<T, Task> asyncFunc)
            => await MapAsync(async item =>
            {
                await asyncFunc(item).ConfigureAwait(false);
                return item;
            }).ConfigureAwait(false);

        public Result<T> TeeError(Action<IEnumerable<string>> teeAction)
            => MapError(x =>
            {
                teeAction.ThrowIfDefault(nameof(teeAction))(x);
                return x;
            });

        public async Task<Result<T>> TeeErrorAsync(Func<IEnumerable<string>, Task> asyncFunc)
            => await MapErrorAsync(async item =>
            {
                await asyncFunc(item).ConfigureAwait(false);
                return item;
            }).ConfigureAwait(false);

        public Option<T> DiscardError(Action<IEnumerable<string>> errorAction)
            => Match(Option.Some, errors =>
            {
                errorAction.ThrowIfDefault(nameof(errorAction))(errors);
                return new None();
            });

        public Task<Option<T>> DiscardErrorAsync(Func<IEnumerable<string>, Task> errorAction)
            => Match(
                item => Task.FromResult(item.Some()),
                async errors =>
                {
                    await errorAction.ThrowIfDefault(nameof(errorAction))(errors);
                    return Option<T>.None;
                });

        public Option<T> DiscardError()
            => Match(Option.Some, _ => Option<T>.None);

        public VoidResult DiscardValue(Func<T, VoidResult> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail);

        public Task<VoidResult> DiscardValueAsync(Func<T, Task<VoidResult>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Fail()));

        public VoidResult DiscardValue()
            => Match(_ => Result.Ok(), Result.Fail);

        public T ReturnOrValue(Func<IEnumerable<string>, T> func)
            => Match(Result.Id, func);

        public Task<T> ReturnOrValueAsync(Func<IEnumerable<string>, Task<T>> func)
            => Match(Result.IdAsync, func);

        public T ReturnOrFail<TExn>(Func<IEnumerable<string>, TExn> failFunc) where TExn: Exception
            => Match(Result.Id, err => throw failFunc.ThrowIfDefault(nameof(failFunc))(err));

    }
}
