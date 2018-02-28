using System;
using System.Collections.Generic;
using System.Linq;
using static OneOf.ROP.Result;

namespace OneOf.ROP
{

    public class Unit
    {
        public static Unit Value { get; } = new Unit();

        private Unit()
        {
        }
    }


    /*
     * Introduce a IPlus<T> interface which has T, T method which is a local method which can be used to define abstract plus operations on a type.
     * This will be useful for introducing a custom plus type without loosing any power in the implementation.
     * But the number of overloads grows at an unmanageable rate especially with Result<T ,TError>
     *
     * This can be used against the Plus Function, for Result<T, TError> this will mean 4 cases of the generic implementation but should make it more useful.
     * This will also affect the Fold Function.
     * Every other main type should have fewer overloads
     */

    public static class Result
    {
        public static T Id<T>(this T value) => value;

        //Builder for Result<T, TError>
        public static Result<T, IEnumerable<TError>> Fail<T, TError>(this IEnumerable<TError> errors)
            => new Result<T, IEnumerable<TError>>(errors);

        public static Result<T, IEnumerable<TError>> Fail<T, TError>(params TError[] errors)
            => new Result<T, IEnumerable<TError>>(errors);

        public static Result<T, TError> Fail<T, TError>(this TError value)
            => new Result<T, TError>(value);

        public static Result<T, TError> Ok<T, TError>(this T value)
            => new Result<T, TError>(value);

        //Builder for Result<T>
        public static Result<T> Fail<T>(this IEnumerable<string> errors)
            => new Result<T>(errors);

        public static Result<T> Fail<T>(params string[] errors)
            => new Result<T>(errors);

        public static Result<T> Ok<T>(this T value)
            => new Result<T>(value);

        //Builder for VoidResult<TError>
        public static VoidResult<TError> Fail<TError>(this TError value)
            => new VoidResult<TError>(value);

        public static VoidResult<IEnumerable<TError>> Fail<TError>(params TError[] errors)
            => new VoidResult<IEnumerable<TError>>(errors);

        public static VoidResult<TError> Ok<TError>()
            => new VoidResult<TError>(Unit.Value);

        //Builder for VoidResult
        public static VoidResult Fail(this IEnumerable<string> value)
            => new VoidResult(value);

        public static VoidResult Fail(params string[] errors)
            => new VoidResult(errors);

        public static VoidResult Ok()
            => new VoidResult(Unit.Value);


        //Plus on Result<T, TError>
        public static Result<(TLeft, TRight), TError> Plus<TLeft, TRight, TError>(this Result<TLeft, TError> left, Result<TRight, TError> right, Func<TError, TError, TError> mergeFunc)
            => left.Match(
                leftValue => right.Match(rightValue => Ok<(TLeft, TRight), TError>((leftValue, rightValue)), Fail<(TLeft, TRight), TError>),
                errors => right.Match(rightValue => Fail<(TLeft, TRight), TError>(errors), otherErrors => Fail<(TLeft, TRight), TError>(mergeFunc(errors, otherErrors)))
            );

        public static Result<T, TError> Plus<T, TError>(this Result<T, TError> left, Result<T, TError> right, Func<T, T, T> plusFunc, Func<TError, TError, TError> mergeFunc)
            => left.Match(
                leftValue => right.Match(rightValue => Ok<T, TError>(plusFunc(leftValue, rightValue)), Fail<T, TError>),
                error => right.Match(rightValue => Fail<T, TError>(error), otherError => Fail<T, TError>(mergeFunc(error, otherError)))
            );

        //Plus on Result<T>
        public static Result<(TLeft, TRight)> Plus<TLeft, TRight>(this Result<TLeft> left, Result<TRight> right)
            => left.Match(
                leftValue => right.Match(rightValue => Ok((leftValue, rightValue)), Fail<(TLeft, TRight)>),
                errors => right.Match(rightValue => Fail<(TLeft, TRight)>(errors), otherErrors => Fail<(TLeft, TRight)>(errors.Concat(otherErrors)))
            );

        public static Result<T> Plus<T>(this Result<T> left, Result<T> right, Func<T, T, T> plusFunc)
            => left.Match(
                leftValue => right.Match(rightValue => Ok(plusFunc(leftValue, rightValue)), Fail<T>),
                errors => right.Match(rightValue => Fail<T>(errors), otherErrors => Fail<T>(errors.Concat(otherErrors)))
            );

        //Plus on VoidResult<TError>
        public static VoidResult<TError> Plus<TError>(this VoidResult<TError> left, VoidResult<TError> right, Func<TError, TError, TError> mergeFunc)
            => left.Match(
                leftValue => right.Match(rightValue => Ok<TError>(), Fail),
                error => right.Match(rightValue => Fail(error), otherError => Fail(mergeFunc(error, otherError)))
            );

        //Plus on VoidResult
        public static VoidResult Plus(this VoidResult left, VoidResult right)
            => left.Match(
                leftValue => right.Match(unit => Ok(), Fail),
                error => right.Match(rightValue => Fail(error), otherError => Fail(error.Concat(otherError)))
            );

        //Fold on Result<T, TError>
        public static Result<T, TError> Fold<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, T> plusFunc, Func<TError, TError, TError> mergeFunc)
            => values.Aggregate((seed, input) => seed.Plus(input, plusFunc, mergeFunc));

        public static Result<T, TError> Fold<T, TError>(this IEnumerable<Result<T, TError>> values, Result<T, TError> seed, Func<T, T, T> plusFunc, Func<TError, TError, TError> mergeFunc)
            => values.Aggregate(seed, (acc, input) => acc.Plus(input, plusFunc, mergeFunc));

        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TResult, T, TResult> aggrFunc, Func<TError, TError, TError> mergeFunc)
            => values.Aggregate(seed, (acc, value) => acc.Plus(value, mergeFunc).Map(x =>
            {
                var (finalAcc, finalVal) = x;
                return aggrFunc(finalAcc, finalVal);
            }));

        //Fold on Result<T>
        public static Result<T> Fold<T>(this IEnumerable<Result<T>> values, Result<T> seed, Func<T, T, T> plusFunc)
            => values.Aggregate(seed, (acc, item) => acc.Plus(item, plusFunc));

        public static Result<T> Fold<T>(this IEnumerable<Result<T>> values, Func<T, T, T> plusFunc)
            => values.Aggregate((acc, item) => acc.Plus(item, plusFunc));

        public static Result<TResult> Fold<TResult, T>(this IEnumerable<Result<T>> values,
            Result<TResult> seed, Func<TResult, T, TResult> aggrFunc)
            => values.Aggregate(seed, (acc, value) => acc.Plus(value).Map(x =>
            {
                var (finalAcc, finalVal) = x;
                return aggrFunc(finalAcc, finalVal);
            }));

        //Fold on VoidResult<TError>
        public static VoidResult<TError> Fold<TError>(this IEnumerable<VoidResult<TError>> values, Func<TError, TError, TError> mergeFunc)
            => values.Aggregate(Ok<TError>(), (acc, item) => acc.Plus(item, mergeFunc));

        //Fold on VoidResult
        public static VoidResult Fold(this IEnumerable<VoidResult> values)
            => values.Aggregate(Ok(), (acc, item) => acc.Plus(item));

        //Fold on VoidResult
        public static Result<TResult, TError> Bind<TResult, T, TError>(this Result<T, TError> value, Func<T, Result<TResult, TError>> bindFunc)
            => value.Match(bindFunc, errors => errors.Fail<TResult, TError>());

        //Bind on Result<T, TErrror>
        public static Result<TResult> Bind<TResult, T>(this Result<T, IEnumerable<string>> value, Func<T, Result<TResult>> bindFunc)
            => value.Match(bindFunc, error => error.Fail<TResult>());

        public static VoidResult<TError> Bind<TError>(this Result<Unit, TError> value, Func<Unit, VoidResult<TError>> bindFunc)
            => value.Match(bindFunc, error => error.Fail());

        public static VoidResult Bind(this Result<Unit, IEnumerable<string>> value, Func<Unit, VoidResult> bindFunc)
            => value.Match(bindFunc, error => error.Fail());

        //Bind on Result<T>
        public static Result<TResult> Bind<TResult, T>(this Result<T> value, Func<T, Result<TResult>> bindFunc)
            => value.Match(bindFunc, error => error.Fail<TResult>());

        public static VoidResult Bind(this Result<Unit> value, Func<Unit, VoidResult> bindFunc)
            => value.Match(bindFunc, error => error.Fail());

        //Bind on VoidResult<TError>
        public static VoidResult<TError> Bind<TError>(this VoidResult<TError> value, Func<Unit, VoidResult<TError>> bindFunc)
            => value.Match(bindFunc, error => error.Fail());

        public static VoidResult Bind(this VoidResult<IEnumerable<string>> value, Func<Unit, VoidResult> bindFunc)
            => value.Match(bindFunc, error => error.Fail());

        //Bind on VoidResult
        public static VoidResult Bind(this VoidResult value, Func<Unit, VoidResult> bindFunc)
            => value.Match(bindFunc, error => error.Fail());


        //Map on Result<T, TError>
        public static Result<TResult, TError> Map<TResult, T, TError>(this Result<T, TError> value, Func<T, TResult> mapFunc)
            => value.Map2(mapFunc, Id);

        public static Result<TResult, TErrorResult> Map2<TResult, T, TError, TErrorResult>(this Result<T, TError> value, Func<T, TResult> mapFunc, Func<TError, TErrorResult> errorMapFunc)
            => value.Match(
                success => mapFunc(success).Ok<TResult, TErrorResult>(),
                errors => errorMapFunc(errors).Fail<TResult, TErrorResult>());

        //Map on Result<T>
        public static Result<TResult> Map<T, TResult>(this Result<T> value, Func<T, TResult> mapFunc)
            => value.Map2(mapFunc, Id);

        public static Result<TResult, TError> Map2<TResult, T, TError>(this Result<T> value, Func<T, TResult> mapFunc, Func<IEnumerable<string>, TError> errorMapFunc)
            => value.Match(
                    success => mapFunc(success).Ok<TResult, TError>(),
                    errors => errorMapFunc(errors).Fail<TResult, TError>());

        //Map on VoidResult<TError>
        public static Result<TResult, TError> Map<TResult, TError>(this VoidResult<TError> value, Func<Unit, TResult> mapFunc)
            => value.Map2(mapFunc, Id);

        public static Result<TResult, TErrorResult> Map2<TResult, TError, TErrorResult>(this VoidResult<TError> value, Func<Unit, TResult> mapFunc, Func<TError, TErrorResult> errorMapFunc)
            => value.Match(
                success => mapFunc(success).Ok<TResult, TErrorResult>(),
                errors => errorMapFunc(errors).Fail<TResult, TErrorResult>());

        //Map on VoidResult
        public static Result<TResult> Map<TResult>(this VoidResult value, Func<Unit, TResult> mapFunc)
            => value.Map2(mapFunc, Id);

        public static Result<TResult, TError> Map2<TResult, TError>(this VoidResult value, Func<Unit, TResult> mapFunc, Func<IEnumerable<string>, TError> errorMapFunc)
            => value.Match(
                success => mapFunc(success).Ok<TResult, TError>(),
                errors => errorMapFunc(errors).Fail<TResult, TError>());

        //Flatten on Result<T, TError>
        public static Result<T, TError> Flatten<T, TError>(this Result<Result<T, TError>, TError> value)
            => value.Match(Id, errors => errors.Fail<T, TError>());

        //Flatten on Result<T>
        public static Result<T> Flatten<T>(this Result<Result<T>> value)
            => value.Match(Id, errors => errors.Fail<T>());

        //Flatten on VoidResult<T>
        public static VoidResult<TError> Flatten<TError>(this VoidResult<VoidResult<TError>> value)
            => value.Match(x => Ok<TError>(), Id);

        //Tee on Result<T, TError>
        public static Result<T, TError> Tee<T, TError>(this Result<T, TError> value, Action<T> action)
            => value.Map(x =>
            {
                action(x);
                return x;
            });

        //Tee on Result<T>
        public static Result<T> Tee<T>(this Result<T> value, Action<T> action)
            => value.Map(x =>
            {
                action(x);
                return x;
            });

        //Tee on VoidResult<TError>
        public static VoidResult<TError> Tee<TError>(this VoidResult<TError> value, Action action)
            => value.Map(x =>
            {
                action();
                return x;
            });

        //Tee on VoidResult
        public static VoidResult Tee(this VoidResult value, Action action)
            => value.Map(x =>
            {
                action();
                return x;
            });

    }


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

        public OneOf<T, IEnumerable<string>> ToOneOf() => _value;

    }

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
                result => Ok<TError>(),
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
                result => Ok(),
                error => error.Fail());

        public static implicit operator Result<Unit>(VoidResult value)
            => value.Match(
                result => result.Ok(),
                error => error.Fail<Unit>());

        public static implicit operator VoidResult(Result<Unit> value)
            => value.Match(
                result => Ok(),
                error => error.Fail());

        public static implicit operator VoidResult<IEnumerable<string>>(VoidResult value)
            => value.Match<VoidResult<IEnumerable<string>>>(
                result => Ok<IEnumerable<string>>(),
                error => error.Fail());

        public static implicit operator VoidResult(VoidResult<IEnumerable<string>> value)
            => value.Match(
                result => Ok(),
                errors => errors.Fail());

        public OneOf<Unit, IEnumerable<string>> ToOneOf() => _value;
    }

}
