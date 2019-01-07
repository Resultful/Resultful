using System;
using System.Collections.Generic;
using System.Linq;
using OneOf.Types;
using Resultful.Utils;

namespace Resultful
{
    public static partial class Result
    {


        //Plus on VoidResult<TError>
        public static VoidResult<TError> Plus<TError>(this VoidResult<TError> left, VoidResult<TError> right, Func<TError, TError, TError> mergeFunc)
            => left.Match(
                leftValue => right.Match(
                    rightValue => Ok().Result<TError>(), error => error.Err()
                ),
                error => right.Match(
                    rightValue => Err(error),
                    otherError => Err(mergeFunc.ThrowIfDefault(nameof(mergeFunc))(error, otherError))
                )
            );

        public static VoidResult<TError> Plus<TError>(this VoidResult<TError> left, VoidResult<TError> right) where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError));

        //Plus on VoidResult
        public static VoidResult Plus(this VoidResult left, VoidResult right)
            => left.Match(
                leftValue => right.Match(_ => Ok().Result(), error => error.Fail()),
                error => right.Match(rightValue => error.Fail(), otherError => error.Concat(otherError).Fail())
            );

        //Fold on VoidResult<TError>
        public static VoidResult<TError> Reduce<TError>(this IEnumerable<VoidResult<TError>> values, Func<TError, TError, TError> mergeFunc)
            => values.Fold(Ok<TError>(), (acc, item) => acc.Plus(item, mergeFunc));

        public static VoidResult<TError> Reduce<TError>(this IEnumerable<VoidResult<TError>> values) where TError : IPlus<TError, TError>
            => values.Fold(Ok<TError>(), (acc, item) => acc.Plus(item));

        public static VoidResult<TError> Reduce<TError>(params VoidResult<TError>[] values) where TError : IPlus<TError, TError>
            => values.Fold(Ok<TError>(), (acc, item) => acc.Plus(item));

        //FoldUntil on VoidResult<TError>
        public static VoidResult<TError> ReduceUntil<TError>(this IEnumerable<VoidResult<TError>> values)
            => values.ReduceUntil((acc, next) => acc.Match(_ => next.Some(), err => new None()));

        public static VoidResult<TError> ReduceUntil<TError>(params VoidResult<TError>[] values)
            => values.ReduceUntil((acc, next) => acc.Match(_ => next.Some(), err => new None()));

        //Fold on VoidResult
        public static VoidResult Reduce(this IEnumerable<VoidResult> values)
            => values.ThrowIfDefault(nameof(values))
                .Fold(Ok(), (acc, item) => acc.Plus(item));

        //FoldUntil on VoidResult
        public static VoidResult ReduceUntil(this IEnumerable<VoidResult> values)
            => values.ThrowIfDefault(nameof(values))
                .FoldUntil(Ok(), (acc, next) => acc.Match(_ => next.Some(), err => new None()));

        //Flatten on VoidResult<TError>
        public static VoidResult<TError> Flatten<TError>(this VoidResult<VoidResult<TError>> value)
            => value.Match(_ => Ok().Result<TError>(), Id);
    }
}
