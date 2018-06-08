using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OneOf.ROP.Utils;

namespace OneOf.ROP
{
    public struct VoidResult
    {
        //Private Members
        private OneOf<Unit, IEnumerable<string>> _value;

        //Constructors
        internal VoidResult(Unit value)
            => _value = value;

        internal VoidResult(IEnumerable<string> value)
            => _value = OneOf<Unit, IEnumerable<string>>.FromT1(value);

        //Implicit Converters
        public static implicit operator VoidResult(Result<Unit, IEnumerable<string>> value)
            => value.Match(_ => Result.Ok(), Result.Fail);

        public static implicit operator VoidResult(Result<Unit> value)
            => value.Match(_ => Result.Ok(), Result.Fail);

        public static implicit operator VoidResult(VoidResult<IEnumerable<string>> value)
            => value.Match(_ => Result.Ok(), Result.Fail);

        public static implicit operator VoidResult(string[] value)
            => Result.Fail(value);

        public static implicit operator VoidResult(List<string> value)
            => Result.Fail(value.ToArray());

        public static implicit operator VoidResult(string value)
            => Result.Fail(value);

        public static implicit operator VoidResult(Unit value)
            => value.Ok();

        //Local Methods
        public TResult Match<TResult>(Func<Unit, TResult> successfulFunc, Func<IEnumerable<string>, TResult> errorFunc)
            => _value.Match(successfulFunc.ThrowIfDefault(nameof(successfulFunc)), errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public void Switch(Action<Unit> successfulFunc, Action<IEnumerable<string>> errorFunc)
            => _value.Switch(successfulFunc.ThrowIfDefault(nameof(successfulFunc)), errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public OneOf<Unit, IEnumerable<string>> ToOneOf() => _value;

        public Result<TResult> Map<TResult>(Func<Unit, TResult> mapFunc)
            => Map2(Result.Id, mapFunc);

        public async Task<Result<TResult>> MapAsync<TResult>(Func<Task<TResult>> mapFunc)
            => await Map2Async(Task.FromResult, mapFunc).ConfigureAwait(false);

        public Result<TResult, TError> Map2<TResult, TError>(Func<IEnumerable<string>, TError> errorMapFunc, Func<Unit, TResult> mapFunc)
            => Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TError>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TError>());

        public Task<Result<TResult, TError>> Map2Async<TResult, TError>(Func<IEnumerable<string>, Task<TError>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => Match(
                async success => (await mapFunc().ConfigureAwait(false)).Ok<TResult, TError>(),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail<TResult, TError>());

        public VoidResult<TErrorResult> MapError<TErrorResult>(Func<IEnumerable<string>, TErrorResult> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public Task<VoidResult<TErrorResult>> MapErrorAsync<TErrorResult>(Func<IEnumerable<string>, Task<TErrorResult>> errorMapFunc)
            => Match(
                success => Task.FromResult(Result.Ok<TErrorResult>()),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail());

        public VoidResult MapError(Func<IEnumerable<string>, IEnumerable<string>> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public Task<VoidResult> MapErrorAsync(Func<IEnumerable<string>, Task<IEnumerable<string>>> errorMapFunc)
            => Match(
                success => Task.FromResult(Result.Ok()),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail());

        public VoidResult Bind(Func<Unit, VoidResult> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail);

        public Task<VoidResult> BindAsync(Func<Task<VoidResult>> bindFunc)
            => Match(x => bindFunc.ThrowIfDefault(nameof(bindFunc))(), error => Task.FromResult(error.Fail()));

        public Result<T> BindValue<T>(Func<Unit, Result<T>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail<T>);

        public Task<Result<T>> BindValueAsync<T>(Func<Unit, Task<Result<T>>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Fail<T>()));

        public VoidResult Tee(Action action)
            => Map(unit =>
            {
                action.ThrowIfDefault(nameof(action))();
                return unit;
            });

        public async Task<VoidResult> TeeAsync(Func<Task> asyncFunc)
            => await MapAsync(async () =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))().ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);
    }
}
