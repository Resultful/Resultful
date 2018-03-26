using System;
using OneOf.Types;
using OneOf.ROP.Utils;

namespace OneOf.ROP
{
    public struct Option<T>
    {
        private OneOf<T, None> _value;

        private Option(T value)
            => _value = value;

        private Option(None value)
            => _value = value;

        public void Switch(Action<T> successfulFunc, Action<None> errorFunc)
            => _value.Switch(successfulFunc.ThrowIfDefault(nameof(successfulFunc)), errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<None, TResult> errorFunc) =>
            _value.Match(successfulFunc.ThrowIfDefault(nameof(successfulFunc)), errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public static implicit operator Option<T>(T value)
            => new Option<T>(value);

        public static implicit operator Option<T>(None value)
            => new Option<T>(value);

        public OneOf<T, None> ToOneOf() => _value;
    }
}
