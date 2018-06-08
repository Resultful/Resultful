using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OneOf.ROP.Utils;

namespace OneOf.ROP
{
    public struct VoidResult<TError>
    {
        //Private Members
        private OneOf<Unit, TError> _value;

        //Constructors
        internal VoidResult(Unit value)
            => _value = value;

        internal VoidResult(TError value)
            => _value = value;

        //Implicit Converters
        public static implicit operator VoidResult<TError>(Result<Unit, TError> value)
            => value.Match(
                result => Result.Ok<TError>(),
                error => error.Fail());

        public static implicit operator VoidResult<TError>(TError value)
            => value.Fail();

        public static implicit operator VoidResult<TError>(Unit value)
            => Result.Ok<TError>();

        //Local Methods
        public TResult Match<TResult>(Func<Unit, TResult> successfulFunc, Func<TError, TResult> errorFunc)
            => _value.Match(successfulFunc.ThrowIfDefault(nameof(successfulFunc)), errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public void Switch(Action<Unit> successfulFunc, Action<TError> errorFunc)
            => _value.Switch(successfulFunc.ThrowIfDefault(nameof(successfulFunc)), errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public OneOf<Unit, TError> ToOneOf() => _value;

        public Result<TResult, TError> Map<TResult>(Func<Unit, TResult> mapFunc)
            => Map2(Result.Id, mapFunc);

        public Task<Result<TResult, TError>> MapAsync<TResult>(Func<Task<TResult>> mapFunc)
            => Map2Async(Task.FromResult, mapFunc);

        public Result<TResult, TErrorResult> Map2<TResult, TErrorResult>(Func<TError, TErrorResult> errorMapFunc, Func<Unit, TResult> mapFunc)
            => Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TErrorResult>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TErrorResult>());

        public Task<Result<TResult, TErrorResult>> Map2Async<TResult, TErrorResult>(Func<TError, Task<TErrorResult>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => Match(
                async success => (await mapFunc.ThrowIfDefault(nameof(mapFunc))().ConfigureAwait(false)).Ok<TResult, TErrorResult>(),
                async errors => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).ConfigureAwait(false)).Fail<TResult, TErrorResult>());


        public VoidResult<TErrorResult> MapError<TErrorResult>(Func<TError, TErrorResult> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public Task<VoidResult<TErrorResult>> MapErrorAsync<TErrorResult>(Func<TError, Task<TErrorResult>> errorMapFunc)
            => Match(
                success => Task.FromResult(Result.Ok<TErrorResult>()),
                async errors => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).ConfigureAwait(false)).Fail());

        public VoidResult MapError(Func<TError, IEnumerable<string>> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public Task<VoidResult> MapErrorAsync(Func<TError, Task<IEnumerable<string>>> errorMapFunc)
            => Match(
                success => Task.FromResult(Result.Ok()),
                async errors => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).ConfigureAwait(false)).Fail());

        public VoidResult<TError> Bind(Func<Unit, VoidResult<TError>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail);

        public Task<VoidResult<TError>> BindAsync(Func<Unit, Task<VoidResult<TError>>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => Task.FromResult(error.Fail()));

        public Result<T, TError> BindValue<T>(Func<Unit, Result<T, TError>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail<T, TError>);

        public Task<Result<T, TError>> BindValueAsync<T>(Func<Unit, Task<Result<T, TError>>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Fail<T, TError>()));

        public VoidResult<TError> Tee(Action action)
            => Map(unit =>
            {
                action.ThrowIfDefault(nameof(action))();
                return unit;
            });

        public async Task<VoidResult<TError>> TeeAsync(Func<Task> asyncFunc)
            => await MapAsync(async () =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))().ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);

    }

}
