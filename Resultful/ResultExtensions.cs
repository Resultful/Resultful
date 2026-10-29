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
       
        public static T Id<T>(this T value) => value;
        public static Task<T> IdAsync<T>(this T value) => Task.FromResult(value);

        //Plus on Result<T, TError>
        public static Result<(TLeft, TRight), TError> Plus<TLeft, TRight, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right, Func<TError, TError, TError> mergeFunc)
            => left.Plus(right, mergeFunc, (leftValue, rightValue) => (leftValue, rightValue));

        public static Result<(TLeft, TRight), TError> Plus<TLeft, TRight, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right) where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError));

        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left, Result<TRight, TError> right, Func<TError, TError, TError> mergeFunc, Func<TLeft, TRight, TResult> plusFunc)
            => left.Match(
                leftValue => right.Match<Result<TResult, TError>>(
                    rightValue => plusFunc(leftValue, rightValue).Ok(),
                    rightErr => rightErr.Err()
                ),
                errors => right.Match<Result<TResult, TError>>(
                    rightValue => errors.Err(),
                    otherErrors => mergeFunc.ThrowIfDefault(nameof(mergeFunc))(errors, otherErrors).Err()
                )
            );

        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right, Func<TError, TError, TError> mergeFunc) where TLeft : IPlus<TRight, TResult>
            => left.Plus(right, mergeFunc, (leftValue, rightValue) => leftValue.Plus(rightValue));

        public static Result<T, TError> Plus<T, TError>(this Result<T, TError> left,
            Result<T, TError> right, Func<TError, TError, TError> mergeFunc) where T : IPlus<T, T>
            => left.Plus(right, mergeFunc, (leftValue, rightValue) => leftValue.Plus(rightValue));


        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right, Func<TLeft, TRight, TResult> plusFunc) where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError), plusFunc);

        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right) where TLeft : IPlus<TRight, TResult> where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError), (leftValue, rightValue) => leftValue.Plus(rightValue));

        public static Result<T, TError> Plus<T, TError>(this Result<T, TError> left,
            Result<T, TError> right) where T : IPlus<T, T> where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError), (leftValue, rightValue) => leftValue.Plus(rightValue));

        //Plus on Result<T>
        public static Result<(TLeft, TRight)> Plus<TLeft, TRight>(this Result<TLeft> left, Result<TRight> right)
            => left.Plus(right, (leftValue, rightValue) => (leftValue, rightValue));

        public static Result<TResult> Plus<TLeft, TRight, TResult>(this Result<TLeft> left, Result<TRight> right, Func<TLeft, TRight, TResult> plusFunc)
            => left.Match(
                leftValue => right.Match<Result<TResult>>(rightValue => Ok(plusFunc.ThrowIfDefault(nameof(plusFunc))(leftValue, rightValue)), error => error.Fail()),
                errors => right.Match<Result<TResult>>(rightValue => errors.Fail(), otherErrors => errors.Concat(otherErrors).Fail())
            );

        public static Result<TResult> Plus<TLeft, TRight, TResult>(this Result<TLeft> left, Result<TRight> right) where TLeft : IPlus<TRight, TResult>
            => left.Plus(right).Map(result =>
            {
                var (leftValue, rightValue) = result;
                return leftValue.Plus(rightValue);
            });

        //Reduce on Result<T, TError>
        public static Result<T, TError> Reduce<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<TError, TError, TError> mergeFunc, Func<T, T, T> plusFunc)
        {
            using (var enumerable = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.ReduceInternal(enumerable, (acc, val) => AggrHelper.Func(acc, val, plusFunc, mergeFunc));
            }
        }

        public static Result<T, TError> Reduce<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<TError, TError, TError> mergeFunc) where T : IPlus<T, T>
            => values.Reduce(mergeFunc, PlusHelper.Plus);

        public static Result<T, TError> Reduce<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<T, T, T> plusFunc) where TError : IPlus<TError, TError>
            => values.Reduce(PlusHelper.Plus, plusFunc);

        public static Result<T, TError> Reduce<T, TError>(this IEnumerable<Result<T, TError>> values) where T : IPlus<T, T> where TError : IPlus<TError, TError>
            => values.Reduce(PlusHelper.Plus, PlusHelper.Plus);

        //Fold on Result<T, TError>
        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TError, TError, TError> mergeFunc, Func<TResult, T, TResult> aggrFunc)
        {
            using (var enumerable = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.FoldInternal(enumerable, seed, (acc, value) => AggrHelper.Func(acc, value, aggrFunc, mergeFunc));
            }
        }

        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            TResult seed, Func<TError, TError, TError> mergeFunc, Func<TResult, T, TResult> aggrFunc)
            => values.Fold(seed.Ok(), mergeFunc, aggrFunc);

        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TResult, T, TResult> aggrFunc) where TError : IPlus<TError, TError>
        {
            using (var enumerable = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.FoldInternal(enumerable, seed, (acc, value) => AggrHelper.Func(acc, value, aggrFunc, PlusHelper.Plus));
            }
        }

        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            TResult seed, Func<TResult, T, TResult> aggrFunc) where TError : IPlus<TError, TError>
            => values.Fold(seed.Ok(), aggrFunc);

        //ReduceUntil on Result<T, TError>
        public static Result<T, TError> ReduceUntil<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, T> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).ReduceUntil(
                (acc, value) => AggrHelper.FuncUntil(acc, value, aggrFunc));

        public static Result<T, TError> ReduceUntil<T, TError>(this IEnumerable<Result<T, TError>> values) where T : IPlus<T, T>
            => values.ReduceUntil((acc, item) => PlusHelper.Plus(acc, item));

        //FoldUntil on Result<T, TError>
        public static Result<TResult, TError> FoldUntil<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TResult, T, TResult> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).FoldUntil(seed,
                (acc, item) => AggrHelper.FuncUntil(acc, item, aggrFunc));

        public static Result<TResult, TError> FoldUntil<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            TResult seed, Func<TResult, T, TResult> aggrFunc)
            => values.FoldUntil(seed.Ok<TResult, TError>(), aggrFunc);

        //Reduce on Result<T>
        public static Result<T> Reduce<T>(this IEnumerable<Result<T>> values, Func<T, T, T> aggrFunc)
        {
            using (var enumerable = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.ReduceInternal(enumerable, (acc, val) => AggrHelper.Func(acc, val, aggrFunc));
            }
        }

        public static Result<T> Reduce<T>(this IEnumerable<Result<T>> values) where T : IPlus<T, T>
        {
            using (var enumerable = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.ReduceInternal(enumerable, (acc, val) => AggrHelper.Func(acc, val, PlusHelper.Plus));
            }
        }

        //Fold on Result<T>
        public static Result<TResult> Fold<TResult, T>(this IEnumerable<Result<T>> values,
            Result<TResult> seed, Func<TResult, T, TResult> aggrFunc)
        {
            using (var enumerable = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.FoldInternal(enumerable, seed, (acc, val) => AggrHelper.Func(acc, val, aggrFunc));
            }
        }

        public static Result<TResult> Fold<TResult, T>(this IEnumerable<Result<T>> values,
            TResult seed, Func<TResult, T, TResult> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).Fold(seed.Ok(), aggrFunc);

        //ReduceUntil on Result<T>
        public static Result<T> ReduceUntil<T>(this IEnumerable<Result<T>> values, Func<T, T, T> plusFunc)
            => values.ThrowIfDefault(nameof(values)).ReduceUntil(
                (acc, value) => AggrHelper.FuncUntil(acc, value, plusFunc));

        //FoldUntil on Result<T>
        public static Result<TResult> FoldUntil<TResult, T>(this IEnumerable<Result<T>> values,
            Result<TResult> seed, Func<TResult, T, TResult> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).FoldUntil(seed,
                (acc, val) => AggrHelper.FuncUntil(acc, val, aggrFunc));

        public static Result<TResult> FoldUntil<TResult, T>(this IEnumerable<Result<T>> values,
            TResult seed, Func<TResult, T, TResult> aggrFunc)
            => values.ThrowIfDefault(nameof(values))
                .FoldUntil(seed.Ok(), aggrFunc);


        //Unroll on IEnumerable<Result<T>>
        public static Result<IEnumerable<T>> Unroll<T>(this IEnumerable<Result<T>> values)
            => values.ThrowIfDefault(nameof(values))
                .Fold(EmptyArray<T>.Get.Ok<IEnumerable<T>>(), (acc, item) => acc.Concat(new[] { item }));

        //UnrollUntil on IEnumerable<Result<T>>
        public static Result<IEnumerable<T>> UnrollUntil<T>(this IEnumerable<Result<T>> values)
            => values.ThrowIfDefault(nameof(values))
                .FoldUntil(EmptyArray<T>.Get.Ok<IEnumerable<T>>(), (acc, item) => acc.Concat(new[] { item }));

        //Unroll on IEnumerable<Result<T, TError>>
        public static Result<IEnumerable<T>, TError> Unroll<T, TError>(this IEnumerable<Result<T, TError>> values, Func<TError, TError, TError> mergeFunc)
            => values.ThrowIfDefault(nameof(values))
                .Fold(EmptyArray<T>.Get.Ok<IEnumerable<T>, TError>(), mergeFunc, (acc, item) => acc.Concat(new[] { item }));

        //Unroll on IEnumerable<Result<T, TError>>
        public static Result<IEnumerable<T>, TError> UnrollUntil<T, TError>(this IEnumerable<Result<T, TError>> values)
            => values.ThrowIfDefault(nameof(values))
                .FoldUntil(EmptyArray<T>.Get.Ok<IEnumerable<T>, TError>(), (acc, item) => acc.Concat(new[] { item }));

        //Flatten on Result<T, TError>
        public static Result<T, TError> Flatten<T, TError>(this Result<Result<T, TError>, TError> value)
            => value.Match(Id, errors => errors.Err());

        public static VoidResult<TError> Flatten<TError>(this Result<VoidResult<TError>, TError> value)
            => value.Match(Id, errors => errors.Err().Result());

        //Flatten on Result<T>
        public static Result<T> Flatten<T>(this Result<Result<T>> value)
            => value.Match(Id, error => error.Fail());

        public static VoidResult Flatten(this Result<VoidResult> value)
            => value.Match(Id, error => error.Fail());

        //Unfold on Result<Option<T>>
        public static Option<Result<T>> Unfold<T>(this Result<Option<T>> value)
            => value.Match(
                vOpt => vOpt.Match(v => v.Ok().Some(), _ => Option<Result<T>>.None),
                err => err.Fail<T>().Some());

        //Unfold on Result<Option<T>, TError>
        public static Option<Result<T, TError>> Unfold<T, TError>(this Result<Option<T>, TError> value)
            => value.Match(
                vOpt => vOpt.Match(v => v.Ok<T, TError>().Some(), _ => Option<Result<T, TError>>.None),
                err => err.Fail<T, TError>().Some());

    }
}
