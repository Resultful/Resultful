using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OneOf.ROP.Utils;

namespace OneOf.ROP
{

    public static partial class Result
    {
        public static T Id<T>(this T value) => value;

        //Builder for Result<T, TError>
        public static Result<T, IEnumerable<TError>> Fail<T, TError>(this IEnumerable<TError> errors)
            => new Result<T, IEnumerable<TError>>(errors ?? EmptyArray<TError>.Get);

        public static Result<T, IEnumerable<TError>> Fail<T, TError>(params TError[] errors)
            => new Result<T, IEnumerable<TError>>(errors ?? EmptyArray<TError>.Get);

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


        //Plus on Result<T, TError>
        public static Result<(TLeft, TRight), TError> Plus<TLeft, TRight, TError>(this Result<TLeft, TError> left, Result<TRight, TError> right, Func<TError, TError, TError> mergeFunc)
            => left.Match(
                leftValue => right.Match(
                    rightValue => Ok<(TLeft, TRight), TError>((leftValue, rightValue)),
                    Fail<(TLeft, TRight), TError>
                ),
                errors => right.Match(
                    rightValue => Fail<(TLeft, TRight), TError>(errors),
                    otherErrors => Fail<(TLeft, TRight), TError>(mergeFunc.ThrowIfDefault(nameof(mergeFunc))(errors, otherErrors))
                )
            );

        public static Result<(TLeft, TRight), TError> Plus<TLeft, TRight, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right) where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError));

        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left, Result<TRight, TError> right, Func<TLeft, TRight, TResult> plusFunc, Func<TError, TError, TError> mergeFunc)
            => left.Plus(right, mergeFunc).Map(result =>
            {
                var (leftValue, rightValue) = result;
                return plusFunc.ThrowIfDefault(nameof(plusFunc))(leftValue, rightValue);
            });

        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left, Result<TRight, TError> right, Func<TError, TError, TError> mergeFunc) where TLeft : IPlus<TRight, TResult>
            => left.Plus(right, mergeFunc).Map(result =>
            {
                var (leftValue, rightValue) = result;
                return leftValue.Plus(rightValue);
            });

        public static Result<T, TError> Plus<T, TError>(this Result<T, TError> left, Result<T, TError> right, Func<T, T, T> plusFunc) where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError)).Map(result =>
            {
                var (leftValue, rightValue) = result;
                return plusFunc.ThrowIfDefault(nameof(plusFunc))(leftValue, rightValue);
            });

        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left, Result<TRight, TError> right) where TLeft : IPlus<TRight, TResult> where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError)).Map(result =>
            {
                var (leftValue, rightValue) = result;
                return leftValue.Plus(rightValue);
            });

        //Plus on Result<T>
        public static Result<(TLeft, TRight)> Plus<TLeft, TRight>(this Result<TLeft> left, Result<TRight> right)
            => left.Match(
                leftValue => right.Match(rightValue => Ok((leftValue, rightValue)), Fail<(TLeft, TRight)>),
                errors => right.Match(rightValue => Fail<(TLeft, TRight)>(errors), otherErrors => Fail<(TLeft, TRight)>(errors.Concat(otherErrors)))
            );

        public static Result<TResult> Plus<TLeft, TRight, TResult>(this Result<TLeft> left, Result<TRight> right,Func<TLeft, TRight, TResult> plusFunc)
            => left.Plus(right).Map(result =>
            {
                var(leftValue, rightValue) = result;
                return plusFunc.ThrowIfDefault(nameof(plusFunc))(leftValue, rightValue);
            });

        public static Result<TResult> Plus<TLeft, TRight, TResult>(this Result<TLeft> left, Result<TRight> right) where TLeft : IPlus<TRight, TResult>
            => left.Plus(right).Map(result =>
            {
                var (leftValue, rightValue) = result;
                return leftValue.Plus(rightValue);
            });

        //Fold on Result<T, TError>
        public static Result<T, TError> Fold<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, T> plusFunc, Func<TError, TError, TError> mergeFunc)
            => values.ThrowIfDefault(nameof(values)).Aggregate((seed, input) => seed.Plus(input, plusFunc, mergeFunc.ThrowIfDefault(nameof(mergeFunc))));

        public static Result<T, TError> Fold<T, TError>(this IEnumerable<Result<T, TError>> values, Func<TError, TError, TError> mergeFunc) where T : IPlus<T, T>
            => values.ThrowIfDefault(nameof(values)).Aggregate((seed, input) => seed.Plus<T, T, T, TError>(input, mergeFunc.ThrowIfDefault(nameof(mergeFunc))));

        public static Result<T, TError> Fold<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, T> plusFunc) where TError : IPlus<TError, TError>
            => values.ThrowIfDefault(nameof(values)).Aggregate((seed, input) => seed.Plus(input, plusFunc.ThrowIfDefault(nameof(plusFunc))));

        public static Result<T, TError> Fold<T, TError>(this IEnumerable<Result<T, TError>> values) where T : IPlus<T, T> where TError : IPlus<TError, TError>
            => values.ThrowIfDefault(nameof(values)).Aggregate((seed, input) => seed.Plus<T, T, T, TError>(input));

        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TResult, T, TResult> aggrFunc, Func<TError, TError, TError> mergeFunc)
            => values.ThrowIfDefault(nameof(values)).Aggregate(seed, (acc, value) => acc.Plus(value, mergeFunc).Map(x =>
            {
                var (finalAcc, finalVal) = x;
                return aggrFunc.ThrowIfDefault(nameof(aggrFunc))(finalAcc, finalVal);
            }));

        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            TResult seed, Func<TResult, T, TResult> aggrFunc, Func<TError, TError, TError> mergeFunc)
            => values.Fold(seed.Ok<TResult, TError>(), aggrFunc, mergeFunc);

        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TResult, T, TResult> aggrFunc) where TError : IPlus<TError, TError>
            => values.ThrowIfDefault(nameof(values)).Aggregate(seed, (acc, value) => acc.Plus(value).Map(x =>
            {
                var (finalAcc, finalVal) = x;
                return aggrFunc.ThrowIfDefault(nameof(aggrFunc))(finalAcc, finalVal);
            }));

        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            TResult seed, Func<TResult, T, TResult> aggrFunc) where TError : IPlus<TError, TError>
            => values.Fold(seed.Ok<TResult, TError>(), aggrFunc);

        //Fold on Result<T>
        public static Result<T> Fold<T>(this IEnumerable<Result<T>> values, Func<T, T, T> plusFunc)
            => values.ThrowIfDefault(nameof(values)).Aggregate((acc, item) => acc.Plus(item, plusFunc));

        public static Result<T> Fold<T>(this IEnumerable<Result<T>> values) where T : IPlus<T, T>
            => values.ThrowIfDefault(nameof(values)).Aggregate((acc, item) => acc.Plus<T, T, T>(item));

        public static Result<TResult> Fold<TResult, T>(this IEnumerable<Result<T>> values,
            Result<TResult> seed, Func<TResult, T, TResult> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).Aggregate(seed, (acc, value) => acc.Plus(value).Map(x =>
            {
                var (finalAcc, finalVal) = x;
                return aggrFunc.ThrowIfDefault(nameof(aggrFunc))(finalAcc, finalVal);
            }));

        public static Result<TResult> Fold<TResult, T>(this IEnumerable<Result<T>> values,
            TResult seed, Func<TResult, T, TResult> aggrFunc)
            => values.Fold(seed.Ok(), aggrFunc);

        //Unroll on IEnumerable<Result<T>>
        public static Result<IEnumerable<T>> Unroll<T>(this IEnumerable<Result<T>> values)
            => values.Fold(EmptyArray<T>.Get.Ok<IEnumerable<T>>(), (acc, item) => acc.Concat(new[] { item }));

        //Unroll on IEnumerable<Result<T, TError>>
        public static Result<IEnumerable<T>, TError> Unroll<T, TError>(this IEnumerable<Result<T, TError>> values, Func<TError, TError, TError> mergeFunc)
            => values.Fold(EmptyArray<T>.Get.Ok<IEnumerable<T>, TError>(), (acc, item) => acc.Concat(new[] { item }), mergeFunc);

        //Bind on Result<T, TError>
        public static Result<TResult, TError> Bind<TResult, T, TError>(this Result<T, TError> value, Func<T, Result<TResult, TError>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), errors => errors.Fail<TResult, TError>());

        public static Result<TResult> Bind<TResult, T>(this Result<T, IEnumerable<string>> value, Func<T, Result<TResult>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => error.Fail<TResult>());

        public static VoidResult<TError> Bind<T, TError>(this Result<T, TError> value, Func<T, VoidResult<TError>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => error.Fail());

        public static VoidResult Bind<T>(this Result<T, IEnumerable<string>> value, Func<T, VoidResult> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => error.Fail());

        //Bind on Result<T>
        public static Result<TResult> Bind<TResult, T>(this Result<T> value, Func<T, Result<TResult>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => error.Fail<TResult>());

        public static VoidResult Bind<T>(this Result<T> value, Func<T, VoidResult> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), error => error.Fail());


        //Map on Result<T, TError>
        public static Result<TResult, TError> Map<TResult, T, TError>(this Result<T, TError> value, Func<T, TResult> mapFunc)
            => value.Map2(mapFunc, Id);

        public static Result<TResult, TErrorResult> Map2<TResult, T, TError, TErrorResult>(this Result<T, TError> value, Func<T, TResult> mapFunc, Func<TError, TErrorResult> errorMapFunc)
            => value.Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TErrorResult>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TErrorResult>());

        //Map on Result<T>
        public static Result<TResult> Map<T, TResult>(this Result<T> value, Func<T, TResult> mapFunc)
            => value.Map2(mapFunc, Id);

        public static Result<TResult, TError> Map2<TResult, T, TError>(this Result<T> value, Func<T, TResult> mapFunc, Func<IEnumerable<string>, TError> errorMapFunc)
            => value.Match(
                    success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TError>(),
                    errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TError>());

        //Flatten on Result<T, TError>
        public static Result<T, TError> Flatten<T, TError>(this Result<Result<T, TError>, TError> value)
            => value.Match(Id, errors => errors.Fail<T, TError>());

        public static VoidResult<TError> Flatten<TError>(this Result<VoidResult<TError>, TError> value)
            => value.Match(Id, errors => errors.Fail());

        //Flatten on Result<T>
        public static Result<T> Flatten<T>(this Result<Result<T>> value)
            => value.Match(Id, errors => errors.Fail<T>());

        public static VoidResult Flatten(this Result<VoidResult> value)
            => value.Match(Id, errors => errors.Fail());

        //Tee on Result<T, TError>
        public static Result<T, TError> Tee<T, TError>(this Result<T, TError> value, Action<T> teeAction)
            => value.Map(x =>
            {
                teeAction.ThrowIfDefault(nameof(teeAction))(x);
                return x;
            });

        //Tee on Result<T>
        public static Result<T> Tee<T>(this Result<T> value, Action<T> teeAction)
            => value.Map(x =>
            {
                teeAction.ThrowIfDefault(nameof(teeAction))(x);
                return x;
            });

    }
}
