using System;
using System.Collections.Generic;
using System.Linq;
using OneOf.ROP.Utils;

namespace OneOf.ROP
{
    public static partial class Result
    {
        //Builder for VoidResult<TError>
        public static VoidResult<TError> Fail<TError>(this TError value)
            => new VoidResult<TError>(value);

        public static VoidResult<IEnumerable<TError>> Fail<TError>(params TError[] errors)
            => new VoidResult<IEnumerable<TError>>(errors ?? EmptyArray<TError>.Get);

        public static VoidResult<TError> Ok<TError>()
            => new VoidResult<TError>(Unit.Value);

        //Builder for VoidResult
        public static VoidResult Fail(this IEnumerable<string> value)
            => new VoidResult(value ?? EmptyArray<string>.Get);

        public static VoidResult Fail(params string[] errors)
            => new VoidResult(errors ?? EmptyArray<string>.Get);

        public static VoidResult Ok()
            => new VoidResult(Unit.Value);

        //Plus on VoidResult<TError>
        public static VoidResult<TError> Plus<TError>(this VoidResult<TError> left, VoidResult<TError> right, Func<TError, TError, TError> mergeFunc)
            => left.Match(
                leftValue => right.Match(
                    rightValue => Ok<TError>(), Fail
                ),
                error => right.Match(
                    rightValue => Fail(error),
                    otherError => Fail(mergeFunc.ThrowIfDefault(nameof(mergeFunc))(error, otherError))
                )
            );

        public static VoidResult<TError> Plus<TError>(this VoidResult<TError> left, VoidResult<TError> right) where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError));

        //Plus on VoidResult
        public static VoidResult Plus(this VoidResult left, VoidResult right)
            => left.Match(
                leftValue => right.Match(_ => Ok(), Fail),
                error => right.Match(rightValue => Fail(error), otherError => Fail(error.Concat(otherError)))
            );

        //Fold on VoidResult<TError>
        public static VoidResult<TError> Fold<TError>(this IEnumerable<VoidResult<TError>> values, Func<TError, TError, TError> mergeFunc)
            => values.Aggregate(Ok<TError>(), (acc, item) => acc.Plus(item, mergeFunc));

        public static VoidResult<TError> Fold<TError>(this IEnumerable<VoidResult<TError>> values) where TError : IPlus<TError, TError>
            => values.Aggregate(Ok<TError>(), (acc, item) => acc.Plus(item));

        public static VoidResult<TError> Fold<TError>(params VoidResult<TError>[] values) where TError : IPlus<TError, TError>
            => values.Aggregate(Ok<TError>(), (acc, item) => acc.Plus(item));

        //Fold on VoidResult
        public static VoidResult Fold(this IEnumerable<VoidResult> values)
            => values.Aggregate(Ok(), (acc, item) => acc.Plus(item));

        //Flatten on VoidResult<TError>
        public static VoidResult<TError> Flatten<TError>(this VoidResult<VoidResult<TError>> value)
            => value.Match(_ => Ok<TError>(), Id);
    }
}
