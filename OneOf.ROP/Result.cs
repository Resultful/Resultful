using System;
using System.Collections.Generic;
using OneOf.ROP.Utils;
using OneOf.Types;

namespace OneOf.ROP
{
    public struct Result<T, TError>
    {
        //Private Members
        private OneOf<T,TError> _value;

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
            => _value.Switch(successfulFunc.ThrowIfDefault(nameof(successfulFunc)), errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<TError, TResult> errorFunc) =>
            _value.Match(successfulFunc.ThrowIfDefault(nameof(successfulFunc)), errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public OneOf<T, TError> ToOneOf() => _value;

        public Result<TResult, TError> Bind<TResult>(Func<T, Result<TResult, TError>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail<TResult, TError>);

        public Result<TResult, TError> Map<TResult>(Func<T, TResult> mapFunc)
            => Map2(Result.Id, mapFunc);

        public Result<T, TErrorResult> MapError<TErrorResult>(Func<TError, TErrorResult> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public Result<T> MapError(Func<TError, IEnumerable<string>> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public Result<TResult, TErrorResult> Map2<TResult, TErrorResult>(Func<TError, TErrorResult> errorMapFunc, Func<T, TResult> mapFunc)
            => Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TErrorResult>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TErrorResult>());

        public Result<T, TError> Tee(Action<T> teeAction)
            => Map(x =>
            {
                teeAction.ThrowIfDefault(nameof(teeAction))(x);
                return x;
            });

        public Option<T> DiscardError(Action<TError> errorAction)
            => Match(OptionExtensions.Some, errors =>
            {
                errorAction.ThrowIfDefault(nameof(errorAction))(errors);
                return new None();
            });

        public Option<T> DiscardError()
            => Match(OptionExtensions.Some, _ => Option<T>.None);

        public VoidResult<TError> DiscardValue(Func<T, VoidResult<TError>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail);

        public VoidResult<TError> DiscardValue()
            => DiscardValue(_ => Result.Ok<TError>());
    }
}
