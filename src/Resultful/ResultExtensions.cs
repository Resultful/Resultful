using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OneOf.Types;
using Resultful.Utils;

namespace Resultful
{

    /// <summary>Builders for <c>Result&lt;T&gt;</c>, <c>Result&lt;T, TError&gt;</c>, <c>VoidResult</c> and <c>VoidResult&lt;TError&gt;</c>, and extension methods on them, on tasks of them and on sequences of them.</summary>
    /// <remarks>Arguments must not be null unless their documentation says otherwise. A null delegate, task or sequence throws <see cref="System.ArgumentNullException"/> or <see cref="System.NullReferenceException"/>, depending on the member, and some members only fail when they would use it. Exceptions thrown by a delegate are not caught.</remarks>
    public static partial class Result
    {

        /// <summary>Returns <paramref name="value"/> unchanged.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value.</param>
        public static T Id<T>(this T value) => value;
        /// <summary>Returns a completed task holding <paramref name="value"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value.</param>
        public static Task<T> IdAsync<T>(this T value) => Task.FromResult(value);

        //Builder for Result<T, TError>
        /// <summary>Returns a failure holding <paramref name="errors"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="errors">The errors. <c>null</c> is treated as no errors.</param>
        public static Result<T, IEnumerable<TError>> Fail<T, TError>(this IEnumerable<TError> errors)
            => new Result<T, IEnumerable<TError>>(errors ?? EmptyArray<TError>.Get);

        /// <summary>Returns a failure holding <paramref name="errors"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="errors">The errors. <c>null</c> is treated as no errors.</param>
        public static Result<T, IEnumerable<TError>> Fail<T, TError>(params TError[] errors)
            => new Result<T, IEnumerable<TError>>(errors ?? EmptyArray<TError>.Get);

        /// <summary>Returns a failure holding <paramref name="value"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The error.</param>
        public static Result<T, TError> Fail<T, TError>(this TError value)
            => new Result<T, TError>(value);

        /// <summary>Returns a success holding <paramref name="value"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The success value.</param>
        public static Result<T, TError> Ok<T, TError>(this T value)
            => new Result<T, TError>(value);

        //Builder for Result<T>
        /// <summary>Returns a failure holding <paramref name="errors"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="errors">The errors.</param>
        public static Result<T> Fail<T>(this IEnumerable<string> errors)
            => new Result<T>(errors);

        /// <summary>Returns a failure holding <paramref name="errors"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="errors">The errors.</param>
        public static Result<T> Fail<T>(params string[] errors)
            => new Result<T>(errors);


        /// <summary>Returns a success holding <paramref name="value"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The success value.</param>
        public static Result<T> Ok<T>(this T value)
            => new Result<T>(value);


        //Plus on Result<T, TError>
        /// <summary>Returns a success holding both values as a tuple if both succeed. If both fail, returns a failure with the errors merged with <paramref name="mergeFunc"/>; if one fails, returns its error.</summary>
        /// <typeparam name="TLeft">The type of the left value.</typeparam>
        /// <typeparam name="TRight">The type of the right value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        public static Result<(TLeft, TRight), TError> Plus<TLeft, TRight, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right, Func<TError, TError, TError> mergeFunc)
            => left.Plus(right, mergeFunc, (leftValue, rightValue) => (leftValue, rightValue));

        /// <summary>Returns a success holding both values as a tuple if both succeed. If both fail, returns a failure with the errors merged with <typeparamref name="TError"/>'s <c>Plus</c>; if one fails, returns its error.</summary>
        /// <typeparam name="TLeft">The type of the left value.</typeparam>
        /// <typeparam name="TRight">The type of the right value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        public static Result<(TLeft, TRight), TError> Plus<TLeft, TRight, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right) where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError));

        /// <summary>Returns a success holding the two values combined by <paramref name="plusFunc"/> if both succeed. If both fail, returns a failure with the errors merged with <paramref name="mergeFunc"/>; if one fails, returns its error.</summary>
        /// <typeparam name="TLeft">The type of the left value.</typeparam>
        /// <typeparam name="TRight">The type of the right value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        /// <param name="plusFunc">Combines the two values.</param>
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

        /// <summary>Returns a success holding the left value's <c>Plus</c> of the right value if both succeed. If both fail, returns a failure with the errors merged with <paramref name="mergeFunc"/>; if one fails, returns its error.</summary>
        /// <typeparam name="TLeft">The type of the left value.</typeparam>
        /// <typeparam name="TRight">The type of the right value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right, Func<TError, TError, TError> mergeFunc) where TLeft : IPlus<TRight, TResult>
            => left.Plus(right, mergeFunc, (leftValue, rightValue) => leftValue.Plus(rightValue));

        /// <summary>Returns a success holding the two values combined by <paramref name="plusFunc"/> if both succeed. If both fail, returns a failure with the errors merged with <typeparamref name="TError"/>'s <c>Plus</c>; if one fails, returns its error.</summary>
        /// <typeparam name="TLeft">The type of the left value.</typeparam>
        /// <typeparam name="TRight">The type of the right value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        /// <param name="plusFunc">Combines the two values.</param>
        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right, Func<TLeft, TRight, TResult> plusFunc) where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError), plusFunc);

        /// <summary>Returns a success holding the left value's <c>Plus</c> of the right value if both succeed. If both fail, returns a failure with the errors merged with <typeparamref name="TError"/>'s <c>Plus</c>; if one fails, returns its error.</summary>
        /// <typeparam name="TLeft">The type of the left value.</typeparam>
        /// <typeparam name="TRight">The type of the right value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left,
            Result<TRight, TError> right) where TLeft : IPlus<TRight, TResult> where TError : IPlus<TError, TError>
            => left.Plus(right, (leftError, rightError) => leftError.Plus(rightError), (leftValue, rightValue) => leftValue.Plus(rightValue));

        //Plus on Result<T>
        /// <summary>Returns a success holding both values as a tuple if both succeed. Otherwise returns a failure with every error of both, left first.</summary>
        /// <typeparam name="TLeft">The type of the left value.</typeparam>
        /// <typeparam name="TRight">The type of the right value.</typeparam>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        public static Result<(TLeft, TRight)> Plus<TLeft, TRight>(this Result<TLeft> left, Result<TRight> right)
            => left.Plus(right, (leftValue, rightValue) => (leftValue, rightValue));

        /// <summary>Returns a success holding the two values combined by <paramref name="plusFunc"/> if both succeed. Otherwise returns a failure with every error of both, left first.</summary>
        /// <typeparam name="TLeft">The type of the left value.</typeparam>
        /// <typeparam name="TRight">The type of the right value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        /// <param name="plusFunc">Combines the two values.</param>
        public static Result<TResult> Plus<TLeft, TRight, TResult>(this Result<TLeft> left, Result<TRight> right, Func<TLeft, TRight, TResult> plusFunc)
            => left.Match(
                leftValue => right.Match(rightValue => Ok(plusFunc.ThrowIfDefault(nameof(plusFunc))(leftValue, rightValue)), Fail<TResult>),
                errors => right.Match(rightValue => Fail<TResult>(errors), otherErrors => Fail<TResult>(errors.Concat(otherErrors)))
            );

        /// <summary>Returns a success holding the left value's <c>Plus</c> of the right value if both succeed. Otherwise returns a failure with every error of both, left first.</summary>
        /// <typeparam name="TLeft">The type of the left value.</typeparam>
        /// <typeparam name="TRight">The type of the right value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        public static Result<TResult> Plus<TLeft, TRight, TResult>(this Result<TLeft> left, Result<TRight> right) where TLeft : IPlus<TRight, TResult>
            => left.Plus(right).Map(result =>
            {
                var (leftValue, rightValue) = result;
                return leftValue.Plus(rightValue);
            });

        //Reduce on Result<T, TError>
        /// <summary>Combines the values in order with <paramref name="plusFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <paramref name="mergeFunc"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        /// <param name="plusFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Result<T, TError> Reduce<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<TError, TError, TError> mergeFunc, Func<T, T, T> plusFunc)
        {
            using (var enumerable = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.ReduceInternal(enumerable, (acc, val) => AggrHelper.Func(acc, val, plusFunc, mergeFunc));
            }
        }

        /// <summary>Combines the values in order with <typeparamref name="T"/>'s <c>Plus</c>. If any result fails, returns a failure with the errors of every failed result, merged with <paramref name="mergeFunc"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Result<T, TError> Reduce<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<TError, TError, TError> mergeFunc) where T : IPlus<T, T>
            => values.Reduce(mergeFunc, PlusHelper.Plus);

        /// <summary>Combines the values in order with <paramref name="plusFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <typeparamref name="TError"/>'s <c>Plus</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="plusFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Result<T, TError> Reduce<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<T, T, T> plusFunc) where TError : IPlus<TError, TError>
            => values.Reduce(PlusHelper.Plus, plusFunc);

        /// <summary>Combines the values in order with <typeparamref name="T"/>'s <c>Plus</c>. If any result fails, returns a failure with the errors of every failed result, merged with <typeparamref name="TError"/>'s <c>Plus</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Result<T, TError> Reduce<T, TError>(this IEnumerable<Result<T, TError>> values) where T : IPlus<T, T> where TError : IPlus<TError, TError>
            => values.Reduce(PlusHelper.Plus, PlusHelper.Plus);

        //Fold on Result<T, TError>
        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <paramref name="mergeFunc"/>.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value. If it is a failure, its errors are kept.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TError, TError, TError> mergeFunc, Func<TResult, T, TResult> aggrFunc)
        {
            using (var enumerable = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.FoldInternal(enumerable, seed, (acc, value) => AggrHelper.Func(acc, value, aggrFunc, mergeFunc));
            }
        }

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <paramref name="mergeFunc"/>.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            TResult seed, Func<TError, TError, TError> mergeFunc, Func<TResult, T, TResult> aggrFunc)
            => values.Fold(seed.Ok<TResult, TError>(), mergeFunc, aggrFunc);

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <typeparamref name="TError"/>'s <c>Plus</c>.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value. If it is a failure, its errors are kept.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TResult, T, TResult> aggrFunc) where TError : IPlus<TError, TError>
        {
            using (var enumerable = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.FoldInternal(enumerable, seed, (acc, value) => AggrHelper.Func(acc, value, aggrFunc, PlusHelper.Plus));
            }
        }

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <typeparamref name="TError"/>'s <c>Plus</c>.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            TResult seed, Func<TResult, T, TResult> aggrFunc) where TError : IPlus<TError, TError>
            => values.Fold(seed.Ok<TResult, TError>(), aggrFunc);

        //ReduceUntil on Result<T, TError>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Result<T, TError> ReduceUntil<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, T> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).ReduceUntil(
                (acc, value) => AggrHelper.FuncUntil(acc, value, aggrFunc));

        /// <summary>Combines the values in order with <typeparamref name="T"/>'s <c>Plus</c>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Result<T, TError> ReduceUntil<T, TError>(this IEnumerable<Result<T, TError>> values) where T : IPlus<T, T>
            => values.ReduceUntil((acc, item) => PlusHelper.Plus(acc, item));

        //FoldUntil on Result<T, TError>
        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value. If it is a failure, its errors are kept.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Result<TResult, TError> FoldUntil<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TResult, T, TResult> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).FoldUntil(seed,
                (acc, item) => AggrHelper.FuncUntil(acc, item, aggrFunc));

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Result<TResult, TError> FoldUntil<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            TResult seed, Func<TResult, T, TResult> aggrFunc)
            => values.FoldUntil(seed.Ok<TResult, TError>(), aggrFunc);

        //Reduce on Result<T>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, in order.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Result<T> Reduce<T>(this IEnumerable<Result<T>> values, Func<T, T, T> aggrFunc)
        {
            using (var enumerable = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.ReduceInternal(enumerable, (acc, val) => AggrHelper.Func(acc, val, aggrFunc));
            }
        }

        /// <summary>Combines the values in order with <typeparamref name="T"/>'s <c>Plus</c>. If any result fails, returns a failure with the errors of every failed result, in order.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The results.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Result<T> Reduce<T>(this IEnumerable<Result<T>> values) where T : IPlus<T, T>
        {
            using (var enumerable = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.ReduceInternal(enumerable, (acc, val) => AggrHelper.Func(acc, val, PlusHelper.Plus));
            }
        }

        //Fold on Result<T>
        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, in order.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value. If it is a failure, its errors are kept.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Result<TResult> Fold<TResult, T>(this IEnumerable<Result<T>> values,
            Result<TResult> seed, Func<TResult, T, TResult> aggrFunc)
        {
            using (var enumerable = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.FoldInternal(enumerable, seed, (acc, val) => AggrHelper.Func(acc, val, aggrFunc));
            }
        }

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, in order.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Result<TResult> Fold<TResult, T>(this IEnumerable<Result<T>> values,
            TResult seed, Func<TResult, T, TResult> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).Fold(seed.Ok(), aggrFunc);

        //ReduceUntil on Result<T>
        /// <summary>Combines the values in order with <paramref name="plusFunc"/>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="plusFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Result<T> ReduceUntil<T>(this IEnumerable<Result<T>> values, Func<T, T, T> plusFunc)
            => values.ThrowIfDefault(nameof(values)).ReduceUntil(
                (acc, value) => AggrHelper.FuncUntil(acc, value, plusFunc));

        //FoldUntil on Result<T>
        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value. If it is a failure, its errors are kept.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Result<TResult> FoldUntil<TResult, T>(this IEnumerable<Result<T>> values,
            Result<TResult> seed, Func<TResult, T, TResult> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).FoldUntil(seed,
                (acc, val) => AggrHelper.FuncUntil(acc, val, aggrFunc));

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Result<TResult> FoldUntil<TResult, T>(this IEnumerable<Result<T>> values,
            TResult seed, Func<TResult, T, TResult> aggrFunc)
            => values.ThrowIfDefault(nameof(values))
                .FoldUntil(seed.Ok(), aggrFunc);


        //Unroll on IEnumerable<Result<T>>
        /// <summary>Returns a success holding every value, in order, if every result succeeds. Otherwise returns a failure with the errors of every failed result, in order.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The results.</param>
        public static Result<IEnumerable<T>> Unroll<T>(this IEnumerable<Result<T>> values)
            => values.ThrowIfDefault(nameof(values))
                .Fold(EmptyArray<T>.Get.Ok<IEnumerable<T>>(), (acc, item) => acc.Concat(new[] { item }));

        //UnrollUntil on IEnumerable<Result<T>>
        /// <summary>Returns a success holding every value, in order, if every result succeeds. Otherwise stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The results.</param>
        public static Result<IEnumerable<T>> UnrollUntil<T>(this IEnumerable<Result<T>> values)
            => values.ThrowIfDefault(nameof(values))
                .FoldUntil(EmptyArray<T>.Get.Ok<IEnumerable<T>>(), (acc, item) => acc.Concat(new[] { item }));

        //Unroll on IEnumerable<Result<T, TError>>
        /// <summary>Returns a success holding every value, in order, if every result succeeds. Otherwise returns a failure whose errors are merged with <paramref name="mergeFunc"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        public static Result<IEnumerable<T>, TError> Unroll<T, TError>(this IEnumerable<Result<T, TError>> values, Func<TError, TError, TError> mergeFunc)
            => values.ThrowIfDefault(nameof(values))
                .Fold(EmptyArray<T>.Get.Ok<IEnumerable<T>, TError>(), mergeFunc, (acc, item) => acc.Concat(new[] { item }));

        //Unroll on IEnumerable<Result<T, TError>>
        /// <summary>Returns a success holding every value, in order, if every result succeeds. Otherwise stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        public static Result<IEnumerable<T>, TError> UnrollUntil<T, TError>(this IEnumerable<Result<T, TError>> values)
            => values.ThrowIfDefault(nameof(values))
                .FoldUntil(EmptyArray<T>.Get.Ok<IEnumerable<T>, TError>(), (acc, item) => acc.Concat(new[] { item }));

        //Flatten on Result<T, TError>
        /// <summary>Returns the inner result if the outer result is a success, or the outer error if it is a failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        public static Result<T, TError> Flatten<T, TError>(this Result<Result<T, TError>, TError> value)
            => value.Match(Id, errors => errors.Fail<T, TError>());

        /// <summary>Returns the inner result if the outer result is a success, or the outer error if it is a failure.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        public static VoidResult<TError> Flatten<TError>(this Result<VoidResult<TError>, TError> value)
            => value.Match(Id, errors => errors.Fail());

        //Flatten on Result<T>
        /// <summary>Returns the inner result if the outer result is a success, or the outer errors if it is a failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The result.</param>
        public static Result<T> Flatten<T>(this Result<Result<T>> value)
            => value.Match(Id, Fail<T>);

        /// <summary>Returns the inner result if the outer result is a success, or the outer errors if it is a failure.</summary>
        /// <param name="value">The result.</param>
        public static VoidResult Flatten(this Result<VoidResult> value)
            => value.Match(Id, Fail);

        //Unfold on Result<Option<T>>
        /// <summary>Swaps the layers: a success holding None gives None, a success holding a value gives that value as a success, and a failure gives the failure as an option with a value.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The result.</param>
        public static Option<Result<T>> Unfold<T>(this Result<Option<T>> value)
            => value.Match(
                vOpt => vOpt.Match(v => v.Ok().Some(), _ => Option<Result<T>>.None),
                err => err.Fail<T>().Some());

        //Unfold on Result<Option<T>, TError>
        /// <summary>Swaps the layers: a success holding None gives None, a success holding a value gives that value as a success, and a failure gives the failure as an option with a value.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        public static Option<Result<T, TError>> Unfold<T, TError>(this Result<Option<T>, TError> value)
            => value.Match(
                vOpt => vOpt.Match(v => v.Ok<T, TError>().Some(), _ => Option<Result<T, TError>>.None),
                err => err.Fail<T, TError>().Some());

    }
}
