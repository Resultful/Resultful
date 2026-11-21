using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resultful.Utils;

namespace Resultful
{

    /// <summary>Builders for <c>Result&lt;T&gt;</c> and <c>Result&lt;T, TError&gt;</c> (a step with no value is a <c>Result&lt;Unit&gt;</c>), and extension methods on them, on tasks of them and on sequences of them.</summary>
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
        /// <param name="errors">The errors.</param>
        /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <c>null</c>.</exception>
        public static Result<T, IEnumerable<TError>> Fail<T, TError>(this IEnumerable<TError> errors)
            => new Result<T, IEnumerable<TError>>(errors.ThrowIfNoError(nameof(errors)));

        /// <summary>Returns a failure holding <paramref name="errors"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="errors">The errors.</param>
        /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <c>null</c>.</exception>
        public static Result<T, IEnumerable<TError>> Fail<T, TError>(params TError[] errors)
            => new Result<T, IEnumerable<TError>>(errors.ThrowIfNoError(nameof(errors)));

        /// <summary>Returns a failure holding <paramref name="value"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The error.</param>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">On <c>net11.0</c>, <paramref name="value"/> is an error union with no value (its zero value).</exception>
        public static Result<T, TError> Fail<T, TError>(this TError value)
            => new Result<T, TError>(value.ThrowIfNoError(nameof(value)));

        /// <summary>Returns a success holding <paramref name="value"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The success value. <c>null</c> is allowed: it gives a success holding <c>null</c>.</param>
        public static Result<T, TError> Ok<T, TError>(this T value)
            => new Result<T, TError>(value);

        //Builder for Result<T>
        /// <summary>Returns a failure holding <paramref name="error"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="error">The error.</param>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <c>null</c>.</exception>
        public static Result<T> Fail<T>(this Error error)
            => new Result<T>(error.ThrowIfNoError(nameof(error)));

        /// <summary>Returns a failure holding a <see cref="MessageError"/> with <paramref name="message"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="message">The error's message.</param>
        /// <remarks>Explicit, so a <see cref="string"/> is never an error by accident (decision 14). Same as <c>Error.From(message).Fail&lt;T&gt;()</c>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
        public static Result<T> Fail<T>(string message)
            => new Result<T>(Error.From(message.ThrowIfDefault(nameof(message))));

        /// <summary>Widens a result whose error derives from <see cref="Error"/> to a <c>Result&lt;T&gt;</c>. The value or error is kept, and so is the error's type at run time.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="result">The result to widen.</param>
        /// <remarks>Named, because C# can't write a conversion that is generic over every <typeparamref name="TError"/> deriving from <see cref="Error"/> (decision 14).</remarks>
        /// <exception cref="InvalidOperationException"><paramref name="result"/> is the zero value (<c>default</c>).</exception>
        public static Result<T> AsResult<T, TError>(this Result<T, TError> result) where TError : Error
            => result.MapError(e => (Error)e);

        /// <summary>On failure, adds <paramref name="message"/> to the error's <see cref="Error.Context"/>, keeping the error's type. A success is unchanged.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="result">The result.</param>
        /// <param name="message">The note, such as what the caller was doing.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="result"/> is the zero value (<c>default</c>).</exception>
        public static Result<T, TError> WithContext<T, TError>(this Result<T, TError> result, string message) where TError : Error
        {
            message.ThrowIfDefault(nameof(message));
            return result.MapError(e => e.WithContext(message));
        }

        /// <summary>Returns a success holding <paramref name="value"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The success value. <c>null</c> is allowed: it gives a success holding <c>null</c>.</param>
        public static Result<T> Ok<T>(this T value)
            => new Result<T>(value);

        //Builder for Result<Unit, TError>: a step with no value (decision 14)
        /// <summary>Returns a failure holding <paramref name="value"/>, as a result with no value.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The error.</param>
        /// <remarks>Returned <c>VoidResult&lt;TError&gt;</c> before 0.3.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">On <c>net11.0</c>, <paramref name="value"/> is an error union with no value (its zero value).</exception>
        public static Result<Unit, TError> Fail<TError>(this TError value)
            => new Result<Unit, TError>(value.ThrowIfNoError(nameof(value)));

        /// <summary>Returns a failure holding <paramref name="errors"/>, as a result with no value.</summary>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="errors">The errors.</param>
        /// <remarks>Returned <c>VoidResult&lt;IEnumerable&lt;TError&gt;&gt;</c> before 0.3.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <c>null</c>.</exception>
        public static Result<Unit, IEnumerable<TError>> Fail<TError>(params TError[] errors)
            => new Result<Unit, IEnumerable<TError>>(errors.ThrowIfNoError(nameof(errors)));

        /// <summary>Returns a success holding <see cref="Unit.Value"/>: a step with no value that succeeded.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <remarks>Returned <c>VoidResult&lt;TError&gt;</c> before 0.3. <typeparamref name="TError"/> can't be inferred, so the caller names it, or writes <c>return Unit.Value;</c>.</remarks>
        public static Result<Unit, TError> Ok<TError>()
            => new Result<Unit, TError>(Unit.Value);

        //Builder for Result<Unit>
        /// <summary>Returns a failure holding <paramref name="error"/>, as a result with no value.</summary>
        /// <param name="error">The error.</param>
        /// <remarks>Returned <c>VoidResult</c> before 0.3.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <c>null</c>.</exception>
        public static Result<Unit> Fail(Error error)
            => new Result<Unit>(error.ThrowIfNoError(nameof(error)));

        /// <summary>Returns a failure holding a <see cref="MessageError"/> with <paramref name="message"/>, as a result with no value.</summary>
        /// <param name="message">The error's message.</param>
        /// <remarks>Explicit, so a <see cref="string"/> is never an error by accident (decision 14). Same as <c>Result.Fail(Error.From(message))</c>. Returned <c>VoidResult</c> before 0.3.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
        public static Result<Unit> Fail(string message)
            => new Result<Unit>(Error.From(message.ThrowIfDefault(nameof(message))));

        /// <summary>Returns a success holding <see cref="Unit.Value"/>: a step with no value that succeeded.</summary>
        /// <remarks>Returned <c>VoidResult</c> before 0.3 (decision 14).</remarks>
        public static Result<Unit> Ok()
            => new Result<Unit>(Unit.Value);


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
        /// <param name="seed">The starting value. If it is a failure, its error is kept.</param>
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
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static VoidResult<TError> Flatten<TError>(this Result<VoidResult<TError>, TError> value)
            => value.Match(Id, VoidResult<TError>.Failure);

        //Flatten on Result<T>
        /// <summary>Returns the inner result if the outer result is a success, or the outer error if it is a failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The result.</param>
        public static Result<T> Flatten<T>(this Result<Result<T>> value)
            => value.Match(Id, Fail<T>);

        /// <summary>Returns the inner result if the outer result is a success, or the outer error if it is a failure.</summary>
        /// <param name="value">The result.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static VoidResult Flatten(this Result<VoidResult> value)
            => value.Match(Id, VoidResult.Failure);

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
