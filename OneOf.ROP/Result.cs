using System;
using System.Collections.Generic;
using System.Linq;

namespace OneOf.ROP
{
    public class Unit
    {
        public static Unit Value { get; } = new Unit();

        private Unit()
        {
        }
    }

    public static class Result
    {
        public static IResult<T, IEnumerable<TError>> Fail<T, TError>(this IEnumerable<TError> errors)
            => new Result<T, IEnumerable<TError>>(errors);

        public static IResult<T, IEnumerable<TError>> Fail<T, TError>(params TError[] errors)
            => new Result<T, IEnumerable<TError>>(errors);

        public static IResult<T, TError> Fail<T, TError>(this TError value)
            => new Result<T, TError>(value);

        public static IResult<T, TError> Ok<T, TError>(this T value)
            => new Result<T, TError>(value);

        public static IResult<T> Fail<T>(this IEnumerable<string> errors)
            => new Result<T>(errors);

        public static IResult<T> Fail<T>(params string[] errors)
            => new Result<T>(errors);

        public static IResult<T> Ok<T>(this T value)
            => new Result<T>(value);

        public static IVoidResult<TError> Fail<TError>(this TError value)
            => new VoidResult<TError>(value);

        public static IVoidResult<IEnumerable<TError>> Fail<TError>(params TError[] errors)
            => new VoidResult<IEnumerable<TError>>(errors);

        public static IVoidResult<TError> Ok<TError>()
            => new VoidResult<TError>(Unit.Value);

        public static IVoidResult Fail(this IEnumerable<string> value)
            => new VoidResult(value);

        public static IVoidResult Fail(params string[] errors)
            => new VoidResult(errors);

        public static IVoidResult Ok()
            => new VoidResult(Unit.Value);

        public static IResult<(TLeft, TRight), TError> Plus<TLeft, TRight, TError>(this IResult<TLeft, TError> left, IResult<TRight, TError> right, Func<TError, TError, TError> mergeFunc)
            => left.Match(
                leftValue => right.Match(rightValue => Ok<(TLeft, TRight), TError>((leftValue, rightValue)), Fail<(TLeft, TRight), TError>),
                errors => right.Match(rightValue => Fail<(TLeft, TRight), TError>(errors), otherErrors => Fail<(TLeft, TRight), TError>(mergeFunc(errors, otherErrors)))
            );

        public static IResult<T, TError> Plus<T, TError>(this IResult<T, TError> left, IResult<T, TError> right, Func<T, T, T> plusFunc, Func<TError, TError, TError> mergeFunc)
            => left.Match(
                leftValue => right.Match(rightValue => Ok<T, TError>(plusFunc(leftValue, rightValue)), Fail<T, TError>),
                errors => right.Match(rightValue => Fail<T, TError>(errors), otherErrors => Fail<T, TError>(mergeFunc(errors, otherErrors)))
            );

        public static IResult<(TLeft, TRight)> Plus<TLeft, TRight>(this IResult<TLeft> left, IResult<TRight> right)
            => left.Match(
                leftValue => right.Match(rightValue => Ok((leftValue, rightValue)), Fail<(TLeft, TRight)>),
                errors => right.Match(rightValue => Fail<(TLeft, TRight)>(errors), otherErrors => Fail<(TLeft, TRight)>(errors.Concat(otherErrors)))
            );

        public static IVoidResult<TError> Plus<TError>(this IVoidResult<TError> left, IVoidResult<TError> right, Func<TError, TError, TError> mergeFunc)
            => left.Match(
                leftValue => right.Match(rightValue => Ok<TError>(), Fail),
                errors => right.Match(rightValue => Fail(errors), otherErrors => Fail(mergeFunc(errors, otherErrors)))
            );

        public static IResult<T, TError> Collect<T, TError>(this IEnumerable<IResult<T, TError>> outcomes, Func<T, T, T> plusFunc, Func<TError, TError, TError> mergeFunc)
            => outcomes.Aggregate((seed, input) => seed.Plus(input, mergeFunc).Map(tuple =>
            {
                var (item1, item2) = tuple;
                return plusFunc(item1, item2);
            }));

        public static IResult<T> Plus<T>(this IResult<T> left, IResult<T> right, Func<T, T, T> plusFunc)
            => left.Match(
                leftValue => right.Match(rightValue => Ok(plusFunc(leftValue, rightValue)), Fail<T>),
                errors => right.Match(rightValue => Fail<T>(errors), otherErrors => Fail<T>(errors.Concat(otherErrors)))
            );

    }


    public struct Result<T, TError> : IResult<T, TError>
    {
        private OneOf<T,TError> _value;

        internal Result(T value)
        {
            _value = value;
        }

        internal Result(TError value)
        {
            _value = value;
        }

        public IResult<TResult, TError> Bind<TResult>(Func<T, IResult<TResult, TError>> bindFunc)
            => Match(bindFunc,
                errors => errors.Fail<TResult, TError>());

        public void Switch(Action<T> successfulFunc, Action<TError> errorFunc)
            => _value.Switch(successfulFunc, errorFunc);

        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<TError, TResult> errorFunc) =>
            _value.Match(successfulFunc, errorFunc);

        public IResult<TResult, TError> Map<TResult>(Func<T, TResult> mapFunc) =>
            Map2(mapFunc, errors => errors);


        public IResult<TResult, TError> Map2<TResult>(Func<T, TResult> mapFunc,
            Func<TError, TError> errorMapFunc)
            => Match(success => mapFunc(success).Ok<TResult, TError>(),
                errors => errorMapFunc(errors).Fail<TResult, TError>());

    }

    public struct Result<T> : IResult<T>
    {
        private OneOf<T, IEnumerable<string>> _value;

        internal Result(T value)
        {
            _value = value;
        }

        internal Result(IEnumerable<string> value)
        {
            _value = OneOf<T, IEnumerable<string>>.FromT1(value);
        }

        public IResult<TResult, IEnumerable<string>> Bind<TResult>(Func<T, IResult<TResult, IEnumerable<string>>> bindFunc)
            => Match(bindFunc,
                errors => errors.Fail<TResult>());

        public IResult<TResult> Bind<TResult>(Func<T, IResult<TResult>> bindFunc)
            => Match(bindFunc,
                errors => errors.Fail<TResult>());


        public void Switch(Action<T> successfulFunc, Action<IEnumerable<string>> errorFunc)
            => _value.Switch(successfulFunc, errorFunc);

        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<IEnumerable<string>, TResult> errorFunc) =>
            _value.Match(successfulFunc, errorFunc);

        public IResult<TResult, IEnumerable<string>> Map<TResult>(Func<T, TResult> mapFunc) =>
            Bind(success => mapFunc(success).Ok<TResult, IEnumerable<string>>());

        IResult<TResult> IResult<T>.Map<TResult>(Func<T, TResult> mapFunc) => Bind(success => mapFunc(success).Ok());
    }

    public struct VoidResult<TError> : IVoidResult<TError>
    {
        private OneOf<Unit, TError> _value;

        internal VoidResult(Unit value)
        {
            _value = value;
        }

        internal VoidResult(TError value)
        {
            _value = value;
        }

        public IResult<TResult, TError> Bind<TResult>(Func<Unit, IResult<TResult, TError>> bindFunc)
            => Match(bindFunc, error => error.Fail<TResult, TError>());

        public IVoidResult<TError> Bind(Func<Unit, IVoidResult<TError>> bindFunc)
            => Match(bindFunc, error => error.Fail());

        IResult<TResult, TError> IResult<Unit, TError>.Map<TResult>(Func<Unit, TResult> mapFunc)
            => Bind(input => mapFunc(input).Ok<TResult, TError>());

        public TResult Match<TResult>(Func<Unit, TResult> successfulFunc, Func<TError, TResult> errorFunc)
            => _value.Match(successfulFunc, errorFunc);

        public void Switch(Action<Unit> successfulFunc, Action<TError> errorFunc)
            => _value.Switch(successfulFunc, errorFunc);

        
    }

    public struct VoidResult: IVoidResult
    {
        private OneOf<Unit, IEnumerable<string>> _value;

        internal VoidResult(Unit value)
        {
            _value = value;
        }

        internal VoidResult(IEnumerable<string> value)
        {
            _value = OneOf<Unit, IEnumerable<string>>.FromT1(value);
        }

        public IResult<TResult, IEnumerable<string>> Bind<TResult>(Func<Unit, IResult<TResult, IEnumerable<string>>> bindFunc)
            => Match(bindFunc, error => error.Fail<TResult, IEnumerable<string>>());

        public IVoidResult<IEnumerable<string>> Bind(Func<Unit, IVoidResult<IEnumerable<string>>> bindFunc)
            => Match(bindFunc, error => error.Fail());

        public IVoidResult Bind(Func<Unit, IVoidResult> bindFunc)
            => Match(bindFunc, error => error.Fail());

        public IResult<TResult, IEnumerable<string>> Map<TResult>(Func<Unit, TResult> mapFunc)
            => Match(
                success => mapFunc(success).Ok<TResult, IEnumerable<string>>(),
                errors => errors.Fail<TResult, IEnumerable<string>>()); 

        public TResult Match<TResult>(Func<Unit, TResult> successfulFunc, Func<IEnumerable<string>, TResult> errorFunc)
            => _value.Match(successfulFunc, errorFunc);

        public void Switch(Action<Unit> successfulFunc, Action<IEnumerable<string>> errorFunc)
            => _value.Switch(successfulFunc, errorFunc);
    }

    public interface IResult<out T, TError>
    {
        IResult<TResult, TError> Map<TResult>(Func<T, TResult> mapFunc);
        TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<TError, TResult> errorFunc);
        void Switch(Action<T> successfulFunc, Action<TError> errorFunc);
        IResult<TResult, TError> Bind<TResult>(Func<T, IResult<TResult, TError>> bindFunc);
    }

    public interface IResult<out T>: IResult<T, IEnumerable<string>>
    { 
        new IResult<TResult> Map<TResult>(Func<T, TResult> mapFunc);
        IResult<TResult> Bind<TResult>(Func<T, IResult<TResult>> bindFunc);
        
    }

    public interface IVoidResult<TError>: IResult<Unit, TError>
    {
        IVoidResult<TError> Bind(Func<Unit, IVoidResult<TError>> bindFunc);
        
    }

    public interface IVoidResult : IVoidResult<IEnumerable<string>>
    {
        IVoidResult Bind(Func<Unit, IVoidResult> bindFunc);
    }
}
