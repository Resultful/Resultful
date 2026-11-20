using System;
using System.Collections.Generic;
using System.Linq;
using OneOf.Types;
using Resultful.Utils;

namespace Resultful
{
    public static partial class Result
    {
        //Builder for VoidResult<TError>
        /// <summary>Returns a failure holding <paramref name="value"/>.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The error.</param>
        public static VoidResult<TError> Fail<TError>(this TError value)
            => new VoidResult<TError>(value);

        /// <summary>Returns a failure holding <paramref name="errors"/>.</summary>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="errors">The errors. <c>null</c> is treated as no errors.</param>
        public static VoidResult<IEnumerable<TError>> Fail<TError>(params TError[] errors)
            => new VoidResult<IEnumerable<TError>>(errors ?? EmptyArray<TError>.Get);

        /// <summary>Returns a success with no value.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        public static VoidResult<TError> Ok<TError>()
            => new VoidResult<TError>(Unit.Value);

        //Builder for VoidResult
        /// <summary>Returns a failure holding <paramref name="value"/>.</summary>
        /// <param name="value">The errors. <c>null</c> is treated as no errors.</param>
        public static VoidResult Fail(this IEnumerable<string> value)
            => new VoidResult(value ?? EmptyArray<string>.Get);

        /// <summary>Returns a failure holding <paramref name="errors"/>.</summary>
        /// <param name="errors">The errors. <c>null</c> is treated as no errors.</param>
        public static VoidResult Fail(params string[] errors)
            => new VoidResult(errors ?? EmptyArray<string>.Get);

        /// <summary>Returns a success with no value.</summary>
        public static VoidResult Ok()
            => new VoidResult(Unit.Value);

        //Plus on VoidResult<TError>
        /// <summary>Returns a success if both succeed. If both fail, returns a failure with the errors merged with <paramref name="mergeFunc"/>; if one fails, returns its error.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
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

        /// <summary>Returns a success if both succeed. If both fail, returns a failure with the errors merged with <typeparamref name="TError"/>'s <c>Plus</c>; if one fails, returns its error.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        public static VoidResult<TError> Plus<TError>(this VoidResult<TError> left, VoidResult<TError> right) where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError));

        //Plus on VoidResult
        /// <summary>Returns a success if both succeed. If both fail, returns a failure with every error of both, left first; if one fails, returns its errors.</summary>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        public static VoidResult Plus(this VoidResult left, VoidResult right)
            => left.Match(
                leftValue => right.Match(_ => Ok(), Fail),
                error => right.Match(rightValue => Fail(error), otherError => Fail(error.Concat(otherError)))
            );

        //Fold on VoidResult<TError>
        /// <summary>Returns a success if every result succeeds, otherwise a failure with the errors of every failed result, merged with <paramref name="mergeFunc"/>.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        public static VoidResult<TError> Reduce<TError>(this IEnumerable<VoidResult<TError>> values, Func<TError, TError, TError> mergeFunc)
            => values.Fold(Ok<TError>(), (acc, item) => acc.Plus(item, mergeFunc));

        /// <summary>Returns a success if every result succeeds, otherwise a failure with the errors of every failed result, merged with <typeparamref name="TError"/>'s <c>Plus</c>.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        public static VoidResult<TError> Reduce<TError>(this IEnumerable<VoidResult<TError>> values) where TError : IPlus<TError, TError>
            => values.Fold(Ok<TError>(), (acc, item) => acc.Plus(item));

        /// <summary>Returns a success if every result succeeds, otherwise a failure with the errors of every failed result, merged with <typeparamref name="TError"/>'s <c>Plus</c>.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        public static VoidResult<TError> Reduce<TError>(params VoidResult<TError>[] values) where TError : IPlus<TError, TError>
            => values.Fold(Ok<TError>(), (acc, item) => acc.Plus(item));

        //FoldUntil on VoidResult<TError>
        /// <summary>Returns a success if every result succeeds, otherwise stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static VoidResult<TError> ReduceUntil<TError>(this IEnumerable<VoidResult<TError>> values)
            => values.ReduceUntil((acc, next) => acc.Match(_ => next.Some(), err => new None()));

        /// <summary>Returns a success if every result succeeds, otherwise stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static VoidResult<TError> ReduceUntil<TError>(params VoidResult<TError>[] values)
            => values.ReduceUntil((acc, next) => acc.Match(_ => next.Some(), err => new None()));

        //Fold on VoidResult
        /// <summary>Returns a success if every result succeeds, otherwise a failure with the errors of every failed result, in order.</summary>
        /// <param name="values">The results.</param>
        public static VoidResult Reduce(this IEnumerable<VoidResult> values)
            => values.ThrowIfDefault(nameof(values))
                .Fold(Ok(), (acc, item) => acc.Plus(item));

        //FoldUntil on VoidResult
        /// <summary>Returns a success if every result succeeds, otherwise stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <param name="values">The results.</param>
        public static VoidResult ReduceUntil(this IEnumerable<VoidResult> values)
            => values.ThrowIfDefault(nameof(values))
                .FoldUntil(Ok(), (acc, next) => acc.Match(_ => next.Some(), err => new None()));

        //Flatten on VoidResult<TError>
        /// <summary>Returns the inner result if the outer result is a success, or the outer error if it is a failure.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        public static VoidResult<TError> Flatten<TError>(this VoidResult<VoidResult<TError>> value)
            => value.Match(_ => Ok<TError>(), Id);
    }
}
