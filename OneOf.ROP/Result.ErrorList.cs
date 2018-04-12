using System;
using System.Collections.Generic;
using OneOf.ROP.Utils;
using OneOf.Types;

namespace OneOf.ROP
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
            => _value.Switch(successfulFunc.ThrowIfDefault(nameof(successfulFunc)), errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<IEnumerable<string>, TResult> errorFunc) =>
            _value.Match(successfulFunc.ThrowIfDefault(nameof(successfulFunc)), errorFunc.ThrowIfDefault(nameof(errorFunc)));

        public OneOf<T, IEnumerable<string>> ToOneOf() => _value;

        public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), errors => errors.Fail<TResult>());

        public Result<TResult> Map<TResult>(Func<T, TResult> mapFunc)
            => Map2(Result.Id, mapFunc);

        public Result<T, TError> MapError<TError>(Func<IEnumerable<string>, TError> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        public Result<TResult, TError> Map2<TResult, TError>(Func<IEnumerable<string>, TError> errorMapFunc, Func<T, TResult> mapFunc)
            => Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TError>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TError>());

        public Result<T> Tee(Action<T> teeAction)
            => Map(x =>
            {
                teeAction.ThrowIfDefault(nameof(teeAction))(x);
                return x;
            });

        public Option<T> DiscardError(Action<IEnumerable<string>> errorAction)
            => Match(OptionExtensions.Some, errors =>
            {
                errorAction.ThrowIfDefault(nameof(errorAction))(errors);
                return new None();
            });

        public Option<T> DiscardError()
            => Match(OptionExtensions.Some, _ => Option<T>.None);

        public  VoidResult DiscardValue(Func<T, VoidResult> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail);

        public VoidResult DiscardValue()
            => Match(_ => Result.Ok(), Result.Fail);
    }
}
