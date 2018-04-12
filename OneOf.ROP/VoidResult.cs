using System;
using System.Collections.Generic;
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

        public Result<TResult, TErrorResult> Map2<TResult, TErrorResult>(Func<TError, TErrorResult> errorMapFunc, Func<Unit, TResult> mapFunc)
            => Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TErrorResult>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TErrorResult>());

        public VoidResult<TErrorResult> MapError<TErrorResult>(Func<TError, TErrorResult> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public VoidResult MapError(Func<TError, IEnumerable<string>> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public VoidResult<TError> Bind(Func<Unit, VoidResult<TError>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail);

        public Result<T, TError> BindValue<T>(Func<Unit, Result<T, TError>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail<T, TError>);

        public VoidResult<TError> Tee(Action action)
            => Map(unit =>
            {
                action.ThrowIfDefault(nameof(action))();
                return unit;
            });


    }

}
