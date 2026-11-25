using System;
using System.Collections.Generic;
using Resultful.Utils;

namespace Resultful
{
    // Decision 22 item 4: a list of inputs through a step that can fail. Traverse and Collect stop at the first failure in
    // input order and call nothing after it; TraverseAll and CollectAll run every item and keep every error, in input
    // order, as Validation.Combine does. Sync and over IEnumerable only: the async forms are over IAsyncEnumerable (24d),
    // which a list reaches through ToAsyncEnumerable(). Collect, not Sequence: Result.Sequence(steps) runs a chain.
    public static partial class Result
    {
        //Traverse and Collect: first failure
        /// <summary>Calls <paramref name="func"/> on each item in order, and returns a success with every value, in order, or the first failure in input order. Items after a failure aren't read and <paramref name="func"/> isn't called for them.</summary>
        /// <typeparam name="T">The type of the items.</typeparam>
        /// <typeparam name="TResult">The type of each new value.</typeparam>
        /// <param name="source">The items.</param>
        /// <param name="func">The step for one item.</param>
        /// <remarks>Equal to folding <c>Bind</c> over the items. A zero value <paramref name="func"/> returns throws <see cref="InvalidOperationException"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="func"/> is <c>null</c>.</exception>
        public static Result<IReadOnlyList<TResult>> Traverse<T, TResult>(this IEnumerable<T> source, Func<T, Result<TResult>> func)
        {
            source.ThrowIfDefault(nameof(source));
            func.ThrowIfDefault(nameof(func));
            var list = new List<TResult>();
            foreach (var item in source)
            {
                var next = func(item);
                if (!next.TryGetValue(out var value))
                    return next.Error.Fail<IReadOnlyList<TResult>>();
                list.Add(value);
            }

            return list.AsReadOnly().Ok<IReadOnlyList<TResult>>();
        }

        /// <summary>Calls <paramref name="func"/> on each item in order, and returns a success with every value, in order, or the first failure in input order. Items after a failure aren't read and <paramref name="func"/> isn't called for them.</summary>
        /// <typeparam name="T">The type of the items.</typeparam>
        /// <typeparam name="TResult">The type of each new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="source">The items.</param>
        /// <param name="func">The step for one item.</param>
        /// <remarks>Equal to folding <c>Bind</c> over the items. A zero value <paramref name="func"/> returns throws <see cref="InvalidOperationException"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="func"/> is <c>null</c>.</exception>
        public static Result<IReadOnlyList<TResult>, TError> Traverse<T, TResult, TError>(this IEnumerable<T> source, Func<T, Result<TResult, TError>> func)
        {
            source.ThrowIfDefault(nameof(source));
            func.ThrowIfDefault(nameof(func));
            var list = new List<TResult>();
            foreach (var item in source)
            {
                var next = func(item);
                if (!next.TryGetValue(out var value))
                    return next.Error.Fail<IReadOnlyList<TResult>, TError>();
                list.Add(value);
            }

            return list.AsReadOnly().Ok<IReadOnlyList<TResult>, TError>();
        }

        /// <summary>Returns a success with every value, in order, or the first failure in input order. Results after a failure aren't read.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <param name="results">The results.</param>
        /// <remarks>A zero value throws <see cref="InvalidOperationException"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="results"/> is <c>null</c>.</exception>
        public static Result<IReadOnlyList<T>> Collect<T>(this IEnumerable<Result<T>> results)
            => results.ThrowIfDefault(nameof(results)).Traverse(static r => r);

        /// <summary>Returns a success with every value, in order, or the first failure in input order. Results after a failure aren't read.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="results">The results.</param>
        /// <remarks>A zero value throws <see cref="InvalidOperationException"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="results"/> is <c>null</c>.</exception>
        public static Result<IReadOnlyList<T>, TError> Collect<T, TError>(this IEnumerable<Result<T, TError>> results)
            => results.ThrowIfDefault(nameof(results)).Traverse(static r => r);

        //TraverseAll and CollectAll: every error
        /// <summary>Calls <paramref name="func"/> on every item, and returns a success with every value, in order, or a failure with every error, in input order.</summary>
        /// <typeparam name="T">The type of the items.</typeparam>
        /// <typeparam name="TResult">The type of each new value.</typeparam>
        /// <param name="source">The items.</param>
        /// <param name="func">The check for one item.</param>
        /// <remarks>Gathers as <c>Validation.Combine</c> does: a failure doesn't stop the others. A zero value <paramref name="func"/> returns throws <see cref="InvalidOperationException"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="func"/> is <c>null</c>.</exception>
        public static Validation<IReadOnlyList<TResult>> TraverseAll<T, TResult>(this IEnumerable<T> source, Func<T, Result<TResult>> func)
        {
            func.ThrowIfDefault(nameof(func));
            return source.TraverseAll(item => (Validation<TResult>)func(item));
        }

        /// <summary>Calls <paramref name="func"/> on every item, and returns a success with every value, in order, or a failure with every error, in input order.</summary>
        /// <typeparam name="T">The type of the items.</typeparam>
        /// <typeparam name="TResult">The type of each new value.</typeparam>
        /// <param name="source">The items.</param>
        /// <param name="func">The checks for one item.</param>
        /// <remarks>Gathers as <c>Validation.Combine</c> does: a failure doesn't stop the others. A zero value <paramref name="func"/> returns throws <see cref="InvalidOperationException"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="func"/> is <c>null</c>.</exception>
        public static Validation<IReadOnlyList<TResult>> TraverseAll<T, TResult>(this IEnumerable<T> source, Func<T, Validation<TResult>> func)
        {
            func.ThrowIfDefault(nameof(func));
            return new(source.TraverseAll(item => func(item).Inner));
        }

        /// <summary>Calls <paramref name="func"/> on every item, and returns a success with every value, in order, or a failure with every error, in input order.</summary>
        /// <typeparam name="T">The type of the items.</typeparam>
        /// <typeparam name="TResult">The type of each new value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="source">The items.</param>
        /// <param name="func">The check for one item.</param>
        /// <remarks>Gathers as <c>Validation.Combine</c> does: a failure doesn't stop the others. A zero value <paramref name="func"/> returns throws <see cref="InvalidOperationException"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="func"/> is <c>null</c>.</exception>
        public static Validation<IReadOnlyList<TResult>, TError> TraverseAll<T, TResult, TError>(this IEnumerable<T> source, Func<T, Result<TResult, TError>> func)
        {
            func.ThrowIfDefault(nameof(func));
            return source.TraverseAll(item => (Validation<TResult, TError>)func(item));
        }

        /// <summary>Calls <paramref name="func"/> on every item, and returns a success with every value, in order, or a failure with every error, in input order.</summary>
        /// <typeparam name="T">The type of the items.</typeparam>
        /// <typeparam name="TResult">The type of each new value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="source">The items.</param>
        /// <param name="func">The checks for one item.</param>
        /// <remarks>Gathers as <c>Validation.Combine</c> does: a failure doesn't stop the others. A zero value <paramref name="func"/> returns throws <see cref="InvalidOperationException"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="func"/> is <c>null</c>.</exception>
        public static Validation<IReadOnlyList<TResult>, TError> TraverseAll<T, TResult, TError>(this IEnumerable<T> source, Func<T, Validation<TResult, TError>> func)
        {
            source.ThrowIfDefault(nameof(source));
            func.ThrowIfDefault(nameof(func));
            var values = new List<TResult>();
            List<TError>? errors = null;
            foreach (var item in source)
            {
                var next = func(item);
                if (next.Gather(ref errors))
                    values.Add(next.SuccessValue);
            }

            return errors is null
                ? Validation.Ok<IReadOnlyList<TResult>, TError>(values.AsReadOnly())
                : Validation<IReadOnlyList<TResult>, TError>.Gathered(errors);
        }

        /// <summary>Returns a success with every value, in order, or a failure with every error, in input order.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <param name="results">The results.</param>
        /// <remarks>A zero value throws <see cref="InvalidOperationException"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="results"/> is <c>null</c>.</exception>
        public static Validation<IReadOnlyList<T>> CollectAll<T>(this IEnumerable<Result<T>> results)
            => results.ThrowIfDefault(nameof(results)).TraverseAll(static r => r);

        /// <summary>Returns a success with every value, in order, or a failure with every error, in input order.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="results">The results.</param>
        /// <remarks>A zero value throws <see cref="InvalidOperationException"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="results"/> is <c>null</c>.</exception>
        public static Validation<IReadOnlyList<T>, TError> CollectAll<T, TError>(this IEnumerable<Result<T, TError>> results)
            => results.ThrowIfDefault(nameof(results)).TraverseAll(static r => r);
    }
}
