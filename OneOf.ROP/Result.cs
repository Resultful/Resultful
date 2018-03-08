using System;
using System.Collections.Generic;

namespace OneOf.ROP
{
    public struct Result<T, TError>
    {
        private OneOf<T,TError> _value;

        internal Result(T value)
            => _value = value;

        internal Result(TError value)
            => _value = value;

        public void Switch(Action<T> successfulFunc, Action<TError> errorFunc)
            => _value.Switch(successfulFunc, errorFunc);

        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<TError, TResult> errorFunc) =>
            _value.Match(successfulFunc, errorFunc);

        public static implicit operator Result<T, TError>(TError value)
            => value.Fail<T, TError>();

        public static implicit operator Result<T, TError>(T value)
            => value.Ok<T, TError>();

        public OneOf<T, TError> ToOneOf() => _value;
    }

    public struct Result<T>
    {
        private OneOf<T, IEnumerable<string>> _value;

        internal Result(T value)
            => _value = value;

        internal Result(IEnumerable<string> value)
            => _value = OneOf<T, IEnumerable<string>>.FromT1(value);

        public void Switch(Action<T> successfulFunc, Action<IEnumerable<string>> errorFunc)
            => _value.Switch(successfulFunc, errorFunc);

        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<IEnumerable<string>, TResult> errorFunc) =>
            _value.Match(successfulFunc, errorFunc);

        public static implicit operator Result<T, IEnumerable<string>>(Result<T> value)
            => value.Match(
                result => result.Ok<T, IEnumerable<string>>(),
                errors => errors.Fail<T, IEnumerable<string>>());

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

        public static implicit operator Result<T>(T value)
            => value.Ok();

        public OneOf<T, IEnumerable<string>> ToOneOf() => _value;

    }
}
