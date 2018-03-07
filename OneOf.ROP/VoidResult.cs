using System;
using System.Collections.Generic;

namespace OneOf.ROP
{
    public struct VoidResult<TError>
    {
        private OneOf<Unit, TError> _value;

        internal VoidResult(Unit value)
            => _value = value;

        internal VoidResult(TError value)
            => _value = value;

        public TResult Match<TResult>(Func<Unit, TResult> successfulFunc, Func<TError, TResult> errorFunc)
            => _value.Match(successfulFunc, errorFunc);

        public void Switch(Action<Unit> successfulFunc, Action<TError> errorFunc)
            => _value.Switch(successfulFunc, errorFunc);

        public static implicit operator Result<Unit, TError>(VoidResult<TError> value)
            => value.Match(
                result => result.Ok<Unit, TError>(),
                error => error.Fail<Unit, TError>());

        public static implicit operator VoidResult<TError>(Result<Unit, TError>  value)
            => value.Match(
                result => Result.Ok<TError>(),
                error => error.Fail());

        public OneOf<Unit, TError> ToOneOf() => _value;

    }

    public struct VoidResult
    {
        private OneOf<Unit, IEnumerable<string>> _value;

        internal VoidResult(Unit value)
            => _value = value;

        internal VoidResult(IEnumerable<string> value)
            => _value = OneOf<Unit, IEnumerable<string>>.FromT1(value);

        public TResult Match<TResult>(Func<Unit, TResult> successfulFunc, Func<IEnumerable<string>, TResult> errorFunc)
            => _value.Match(successfulFunc, errorFunc);

        public void Switch(Action<Unit> successfulFunc, Action<IEnumerable<string>> errorFunc)
            => _value.Switch(successfulFunc, errorFunc);

        public static implicit operator Result<Unit, IEnumerable<string>>(VoidResult value)
            => value.Match(
                result => result.Ok<Unit, IEnumerable<string>>(),
                error => error.Fail<Unit, IEnumerable<string>>());

        public static implicit operator VoidResult(Result<Unit, IEnumerable<string>> value)
            => value.Match(
                result => Result.Ok(),
                error => error.Fail());

        public static implicit operator Result<Unit>(VoidResult value)
            => value.Match(
                result => result.Ok(),
                error => error.Fail<Unit>());

        public static implicit operator VoidResult(Result<Unit> value)
            => value.Match(
                result => Result.Ok(),
                error => error.Fail());

        public static implicit operator VoidResult<IEnumerable<string>>(VoidResult value)
            => value.Match<VoidResult<IEnumerable<string>>>(
                result => Result.Ok<IEnumerable<string>>(),
                error => error.Fail());

        public static implicit operator VoidResult(VoidResult<IEnumerable<string>> value)
            => value.Match(
                result => Result.Ok(),
                errors => errors.Fail());

        public OneOf<Unit, IEnumerable<string>> ToOneOf() => _value;
    }
}
