using System;
using System.Collections.Generic;
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

        public VoidResult Bind(Func<Unit, VoidResult> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail);

        public Result<T> BindValue<T>(Func<Unit, Result<T>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail<T>);

        public Result<TResult> Map<TResult>(Func<Unit, TResult> mapFunc)
            => Map2(Result.Id, mapFunc);

        public Result<TResult, TError> Map2<TResult, TError>(Func<IEnumerable<string>, TError> errorMapFunc, Func<Unit, TResult> mapFunc)
            => Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TError>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TError>());

        public VoidResult<TErrorResult> MapError<TErrorResult>(Func<IEnumerable<string>, TErrorResult> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public VoidResult Tee(Action action)
            => Map(unit =>
            {
                action.ThrowIfDefault(nameof(action))();
                return unit;
            });
    }
}
