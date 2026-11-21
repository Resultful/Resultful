using System;
using System.Collections.Generic;
using System.Linq;
using Resultful.Utils;

namespace Resultful
{
    public static partial class Result
    {
        //Plus on VoidResult<TError>
        /// <summary>Returns a success if both succeed. If both fail, returns a failure with the errors merged with <paramref name="mergeFunc"/>; if one fails, returns its error.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static VoidResult<TError> Plus<TError>(this VoidResult<TError> left, VoidResult<TError> right, Func<TError, TError, TError> mergeFunc)
            => left.Match(
                leftValue => right.Match(
                    rightValue => VoidResult<TError>.Success, VoidResult<TError>.Failure
                ),
                error => right.Match(
                    rightValue => VoidResult<TError>.Failure(error),
                    otherError => VoidResult<TError>.Failure(mergeFunc.ThrowIfDefault(nameof(mergeFunc))(error, otherError))
                )
            );

        /// <summary>Returns a success if both succeed. If both fail, returns a failure with the errors merged with <typeparamref name="TError"/>'s <c>Plus</c>; if one fails, returns its error.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static VoidResult<TError> Plus<TError>(this VoidResult<TError> left, VoidResult<TError> right) where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError));

        //Fold on VoidResult<TError>
        /// <summary>Returns a success if every result succeeds, otherwise a failure with the errors of every failed result, merged with <paramref name="mergeFunc"/>.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static VoidResult<TError> Reduce<TError>(this IEnumerable<VoidResult<TError>> values, Func<TError, TError, TError> mergeFunc)
            => values.Fold(VoidResult<TError>.Success, (acc, item) => acc.Plus(item, mergeFunc));

        /// <summary>Returns a success if every result succeeds, otherwise a failure with the errors of every failed result, merged with <typeparamref name="TError"/>'s <c>Plus</c>.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static VoidResult<TError> Reduce<TError>(this IEnumerable<VoidResult<TError>> values) where TError : IPlus<TError, TError>
            => values.Fold(VoidResult<TError>.Success, (acc, item) => acc.Plus(item));

        /// <summary>Returns a success if every result succeeds, otherwise a failure with the errors of every failed result, merged with <typeparamref name="TError"/>'s <c>Plus</c>.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static VoidResult<TError> Reduce<TError>(params VoidResult<TError>[] values) where TError : IPlus<TError, TError>
            => values.Fold(VoidResult<TError>.Success, (acc, item) => acc.Plus(item));

        //FoldUntil on VoidResult<TError>
        /// <summary>Returns a success if every result succeeds, otherwise stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static VoidResult<TError> ReduceUntil<TError>(this IEnumerable<VoidResult<TError>> values)
            => values.ReduceUntil((acc, next) => acc.Match(_ => next.Some(), err => new None()));

        /// <summary>Returns a success if every result succeeds, otherwise stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static VoidResult<TError> ReduceUntil<TError>(params VoidResult<TError>[] values)
            => values.ReduceUntil((acc, next) => acc.Match(_ => next.Some(), err => new None()));

        //FoldUntil on VoidResult
        /// <summary>Returns a success if every result succeeds, otherwise stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <param name="values">The results.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static VoidResult ReduceUntil(this IEnumerable<VoidResult> values)
            => values.ThrowIfDefault(nameof(values))
                .FoldUntil(VoidResult.Success, (acc, next) => acc.Match(_ => next.Some(), err => new None()));

        //Flatten on VoidResult<TError>
        /// <summary>Returns the inner result if the outer result is a success, or the outer error if it is a failure.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static VoidResult<TError> Flatten<TError>(this VoidResult<VoidResult<TError>> value)
            => value.Match(_ => VoidResult<TError>.Success, Id);
    }
}
