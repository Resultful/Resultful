using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OneOf.Types;
using Resultful.Utils;

namespace Resultful
{

    public static partial class Result
    {
        //These two may need to be moved, they are nothing to do with results
        internal static TResult Plus<TLeft, TRight, TResult>(this TLeft left, TRight right)
            where TLeft : IPlus<TRight, TResult>
            => left.Plus(right);

        internal static T Plus<T>(this T left, T right)
            where T : IPlus<T, T>
            => left.Plus(right);

        public static T Id<T>(this T value) => value;
        public static Task<T> IdAsync<T>(this T value) => Task.FromResult(value);

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
        public static Result<(TLeft, TRight), TError> Plus<TLeft, TRight, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right, Func<TError, TError, TError> mergeFunc)
            => left.Plus(right, mergeFunc, (leftValue, rightValue) => (leftValue, rightValue));

        public static Result<(TLeft, TRight), TError> Plus<TLeft, TRight, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right) where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError));

        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left, Result<TRight, TError> right, Func<TError, TError, TError> mergeFunc, Func<TLeft, TRight, TResult> plusFunc)
            => left.Match(
                leftValue => right.Match(
                    rightValue => Ok<TResult, TError>(plusFunc(leftValue, rightValue)),
                    Fail<TResult, TError>
                ),
                errors => right.Match(
                    rightValue => Fail<TResult, TError>(errors),
                    otherErrors => Fail<TResult, TError>(mergeFunc.ThrowIfDefault(nameof(mergeFunc))(errors, otherErrors))
                )
            );

        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right, Func<TError, TError, TError> mergeFunc) where TLeft : IPlus<TRight, TResult>
            => left.Plus(right, mergeFunc, (leftValue, rightValue) => leftValue.Plus(rightValue));

        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right, Func<TLeft, TRight, TResult> plusFunc) where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError), plusFunc);

        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right) where TLeft : IPlus<TRight, TResult> where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError), (leftValue, rightValue) => leftValue.Plus(rightValue));

        //Plus on Result<T>
        public static Result<(TLeft, TRight)> Plus<TLeft, TRight>(this Result<TLeft> left, Result<TRight> right)
            => left.Plus(right, (leftValue, rightValue) => (leftValue, rightValue));

        public static Result<TResult> Plus<TLeft, TRight, TResult>(this Result<TLeft> left, Result<TRight> right, Func<TLeft, TRight, TResult> plusFunc)
            => left.Match(
                leftValue => right.Match(rightValue => Ok(plusFunc.ThrowIfDefault(nameof(plusFunc))(leftValue, rightValue)), Fail<TResult>),
                errors => right.Match(rightValue => Fail<TResult>(errors), otherErrors => Fail<TResult>(errors.Concat(otherErrors)))
            );

        public static Result<TResult> Plus<TLeft, TRight, TResult>(this Result<TLeft> left, Result<TRight> right) where TLeft : IPlus<TRight, TResult>
            => left.Plus(right).Map(result =>
            {
                var (leftValue, rightValue) = result;
                return leftValue.Plus(rightValue);
            });

        //Fold on Result<T, TError>
        public static Result<T, TError> Fold<T, TError>(this IEnumerable<Result<T, TError>> values, Func<TError, TError, TError> mergeFunc, Func<T, T, T> plusFunc)
            => values.ThrowIfDefault(nameof(values)).Aggregate((seed, input) => seed.Plus(input, mergeFunc.ThrowIfDefault(nameof(mergeFunc)), plusFunc.ThrowIfDefault(nameof(plusFunc))));

        public static Result<T, TError> Fold<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<TError, TError, TError> mergeFunc) where T : IPlus<T, T>
            => values.Fold(mergeFunc, Plus);

        public static Result<T, TError> Fold<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<T, T, T> plusFunc) where TError : IPlus<TError, TError>
            => values.Fold(Plus, plusFunc);

        public static Result<T, TError> Fold<T, TError>(this IEnumerable<Result<T, TError>> values) where T : IPlus<T, T> where TError : IPlus<TError, TError>
            => values.Fold(Plus, Plus);

        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TError, TError, TError> mergeFunc, Func<TResult, T, TResult> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).Aggregate(seed, (acc, value) => acc.Plus(value, mergeFunc).Map(x =>
            {
                var (finalAcc, finalVal) = x;
                return aggrFunc.ThrowIfDefault(nameof(aggrFunc))(finalAcc, finalVal);
            }));

        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            TResult seed, Func<TError, TError, TError> mergeFunc, Func<TResult, T, TResult> aggrFunc)
            => values.Fold(seed.Ok<TResult, TError>(), mergeFunc, aggrFunc);

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

        //FoldUntil on Result<T, TError>
        public static Result<T, TError> ReduceUntil<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, T> plusFunc)
            => values.ThrowIfDefault(nameof(values)).ReduceUntil((acc, item) => acc.Match(x => item.Map(y => plusFunc(x, y)).Some(), err => new None()));

        public static Result<T, TError> ReduceUntil<T, TError>(this IEnumerable<Result<T, TError>> values) where T : IPlus<T, T>
            => values.ReduceUntil((acc, item) => Plus(acc, item));

        public static Result<TResult, TError> FoldUntil<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TResult, T, TResult> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).FoldUntil(seed, (acc, item) => acc.Match(x => item.Map(y => aggrFunc(x, y)).Some(), err => new None()));

        public static Result<TResult, TError> FoldUntil<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            TResult seed, Func<TResult, T, TResult> aggrFunc)
            => values.FoldUntil(seed.Ok<TResult, TError>(), aggrFunc);

        //Fold on Result<T>
        public static Result<T> Reduce<T>(this IEnumerable<Result<T>> values, Func<T, T, T> plusFunc)
            => values.ThrowIfDefault(nameof(values)).Aggregate((acc, item) => acc.Plus(item, plusFunc));

        public static Result<T> Reduce<T>(this IEnumerable<Result<T>> values) where T : IPlus<T, T>
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

        //FoldUntil on Result<T>
        public static Result<T> ReduceUtil<T>(this IEnumerable<Result<T>> values, Func<T, T, T> plusFunc)
            => values.ThrowIfDefault(nameof(values)).Aggregate((acc, value) =>
                acc.Bind(x => value.Map(y => plusFunc.ThrowIfDefault(nameof(plusFunc))(x, y))));

        public static Result<TResult> FoldUtil<TResult, T>(this IEnumerable<Result<T>> values,
            Result<TResult> seed, Func<TResult, T, TResult> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).Aggregate(seed, (acc, value) =>
                acc.Bind(x => value.Map(y => aggrFunc.ThrowIfDefault(nameof(aggrFunc))(x, y))));

        public static Result<TResult> FoldUtil<TResult, T>(this IEnumerable<Result<T>> values,
            TResult seed, Func<TResult, T, TResult> aggrFunc)
            => values.FoldUtil(seed.Ok(), aggrFunc);


        //Unroll on IEnumerable<Result<T>>
        public static Result<IEnumerable<T>> Unroll<T>(this IEnumerable<Result<T>> values)
            => values.Fold(EmptyArray<T>.Get.Ok<IEnumerable<T>>(), (acc, item) => acc.Concat(new[] { item }));

        //Unroll on IEnumerable<Result<T, TError>>
        public static Result<IEnumerable<T>, TError> Unroll<T, TError>(this IEnumerable<Result<T, TError>> values, Func<TError, TError, TError> mergeFunc)
            => values.Fold(EmptyArray<T>.Get.Ok<IEnumerable<T>, TError>(), mergeFunc, (acc, item) => acc.Concat(new[] { item }));

        //Flatten on Result<T, TError>
        public static Result<T, TError> Flatten<T, TError>(this Result<Result<T, TError>, TError> value)
            => value.Match(Id, errors => errors.Fail<T, TError>());

        public static VoidResult<TError> Flatten<TError>(this Result<VoidResult<TError>, TError> value)
            => value.Match(Id, errors => errors.Fail());

        //Flatten on Result<T>
        public static Result<T> Flatten<T>(this Result<Result<T>> value)
            => value.Match(Id, Fail<T>);

        public static VoidResult Flatten(this Result<VoidResult> value)
            => value.Match(Id, Fail);

    }
}
