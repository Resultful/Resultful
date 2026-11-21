#if NET
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Resultful.Utils;

namespace Resultful
{
    // Async streams (decision 22, item 5; stack PR 24d), net10.0 and later: IAsyncEnumerable is in-box there and the
    // core references nothing (decision 1). Only result-aware operators live here; System.Linq.AsyncEnumerable (in-box
    // on net10.0) does the rest. CollectAsync and PartitionAsync are in Result.Async.cs.
    //
    // Rules for every operator: the token given to GetAsyncEnumerator (WithCancellation, or the eager operators'
    // cancellationToken) is passed to the source and checked before each item. Nothing catches: an exception from the
    // source or a selector stays an exception. A lazy operator pulls one item per item it yields. Stopping early
    // (break, a fail-fast collect, a cancelled token) disposes the source's enumerator, so the producer's finally runs.
    public static partial class Result
    {
        /// <summary>Maps each success's value with <paramref name="selector"/>. A failure passes through as an item, and <paramref name="selector"/> isn't called for it.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TResult">The type of the new values.</typeparam>
        /// <param name="source">The results to read.</param>
        /// <param name="selector">Maps a value.</param>
        /// <returns>A lazy stream with one item per item of <paramref name="source"/>, in order.</returns>
        /// <remarks>The token given to the returned stream's enumerator is passed to <paramref name="source"/>. An exception <paramref name="selector"/> throws ends the stream with that exception.</remarks>
        public static IAsyncEnumerable<Result<TResult>> SelectResultAsync<T, TResult>(this IAsyncEnumerable<Result<T>> source, Func<T, TResult> selector)
        {
            source.ThrowIfDefault(nameof(source));
            selector.ThrowIfDefault(nameof(selector));
            return Core(source, selector, default);

            static async IAsyncEnumerable<Result<TResult>> Core(IAsyncEnumerable<Result<T>> source, Func<T, TResult> selector, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return item.TryGetValue(out var value) ? selector(value).Ok() : item.Error.Fail<TResult>();
                }
            }
        }

        /// <summary>Replaces each success with the result <paramref name="selector"/> returns for its value. A failure passes through as an item, and <paramref name="selector"/> isn't called for it.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TResult">The type of the new values.</typeparam>
        /// <param name="source">The results to read.</param>
        /// <param name="selector">Returns the next result from a value.</param>
        /// <returns>A lazy stream with one item per item of <paramref name="source"/>, in order.</returns>
        /// <remarks>The token given to the returned stream's enumerator is passed to <paramref name="source"/>. An exception <paramref name="selector"/> throws ends the stream with that exception.</remarks>
        public static IAsyncEnumerable<Result<TResult>> SelectResultAsync<T, TResult>(this IAsyncEnumerable<Result<T>> source, Func<T, Result<TResult>> selector)
        {
            source.ThrowIfDefault(nameof(source));
            selector.ThrowIfDefault(nameof(selector));
            return Core(source, selector, default);

            static async IAsyncEnumerable<Result<TResult>> Core(IAsyncEnumerable<Result<T>> source, Func<T, Result<TResult>> selector, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return item.TryGetValue(out var value) ? selector(value) : item.Error.Fail<TResult>();
                }
            }
        }

        /// <summary>Maps each success's value with <paramref name="selector"/>, awaited one item at a time. A failure passes through as an item, and <paramref name="selector"/> isn't called for it.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TResult">The type of the new values.</typeparam>
        /// <param name="source">The results to read.</param>
        /// <param name="selector">Maps a value; it receives the token given to the returned stream's enumerator.</param>
        /// <returns>A lazy stream with one item per item of <paramref name="source"/>, in order.</returns>
        /// <remarks>The token given to the returned stream's enumerator (<c>WithCancellation</c>) is passed to <paramref name="source"/> and to <paramref name="selector"/>, as <c>System.Linq.AsyncEnumerable.Select</c> does. An exception <paramref name="selector"/> throws ends the stream with that exception.</remarks>
        public static IAsyncEnumerable<Result<TResult>> SelectResultAsync<T, TResult>(this IAsyncEnumerable<Result<T>> source, Func<T, CancellationToken, ValueTask<TResult>> selector)
        {
            source.ThrowIfDefault(nameof(source));
            selector.ThrowIfDefault(nameof(selector));
            return Core(source, selector, default);

            static async IAsyncEnumerable<Result<TResult>> Core(IAsyncEnumerable<Result<T>> source, Func<T, CancellationToken, ValueTask<TResult>> selector, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return item.TryGetValue(out var value)
                        ? (await selector(value, cancellationToken).ConfigureAwait(false)).Ok()
                        : item.Error.Fail<TResult>();
                }
            }
        }

        /// <summary>Replaces each success with the result <paramref name="selector"/> returns for its value, awaited one item at a time. A failure passes through as an item, and <paramref name="selector"/> isn't called for it.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TResult">The type of the new values.</typeparam>
        /// <param name="source">The results to read.</param>
        /// <param name="selector">Returns the next result from a value; it receives the token given to the returned stream's enumerator.</param>
        /// <returns>A lazy stream with one item per item of <paramref name="source"/>, in order.</returns>
        /// <remarks>The token given to the returned stream's enumerator (<c>WithCancellation</c>) is passed to <paramref name="source"/> and to <paramref name="selector"/>, as <c>System.Linq.AsyncEnumerable.Select</c> does. An exception <paramref name="selector"/> throws ends the stream with that exception.</remarks>
        public static IAsyncEnumerable<Result<TResult>> SelectResultAsync<T, TResult>(this IAsyncEnumerable<Result<T>> source, Func<T, CancellationToken, ValueTask<Result<TResult>>> selector)
        {
            source.ThrowIfDefault(nameof(source));
            selector.ThrowIfDefault(nameof(selector));
            return Core(source, selector, default);

            static async IAsyncEnumerable<Result<TResult>> Core(IAsyncEnumerable<Result<T>> source, Func<T, CancellationToken, ValueTask<Result<TResult>>> selector, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return item.TryGetValue(out var value)
                        ? await selector(value, cancellationToken).ConfigureAwait(false)
                        : item.Error.Fail<TResult>();
                }
            }
        }

        /// <summary>Maps each success's value with <paramref name="selector"/>. A failure passes through as an item, and <paramref name="selector"/> isn't called for it.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new values.</typeparam>
        /// <param name="source">The results to read.</param>
        /// <param name="selector">Maps a value.</param>
        /// <returns>A lazy stream with one item per item of <paramref name="source"/>, in order.</returns>
        /// <remarks>The token given to the returned stream's enumerator is passed to <paramref name="source"/>. An exception <paramref name="selector"/> throws ends the stream with that exception.</remarks>
        public static IAsyncEnumerable<Result<TResult, TError>> SelectResultAsync<T, TError, TResult>(this IAsyncEnumerable<Result<T, TError>> source, Func<T, TResult> selector)
        {
            source.ThrowIfDefault(nameof(source));
            selector.ThrowIfDefault(nameof(selector));
            return Core(source, selector, default);

            static async IAsyncEnumerable<Result<TResult, TError>> Core(IAsyncEnumerable<Result<T, TError>> source, Func<T, TResult> selector, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return item.TryGetValue(out var value) ? selector(value).Ok<TResult, TError>() : item.Error.Fail<TResult, TError>();
                }
            }
        }

        /// <summary>Replaces each success with the result <paramref name="selector"/> returns for its value. A failure passes through as an item, and <paramref name="selector"/> isn't called for it.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new values.</typeparam>
        /// <param name="source">The results to read.</param>
        /// <param name="selector">Returns the next result from a value.</param>
        /// <returns>A lazy stream with one item per item of <paramref name="source"/>, in order.</returns>
        /// <remarks>The token given to the returned stream's enumerator is passed to <paramref name="source"/>. An exception <paramref name="selector"/> throws ends the stream with that exception.</remarks>
        public static IAsyncEnumerable<Result<TResult, TError>> SelectResultAsync<T, TError, TResult>(this IAsyncEnumerable<Result<T, TError>> source, Func<T, Result<TResult, TError>> selector)
        {
            source.ThrowIfDefault(nameof(source));
            selector.ThrowIfDefault(nameof(selector));
            return Core(source, selector, default);

            static async IAsyncEnumerable<Result<TResult, TError>> Core(IAsyncEnumerable<Result<T, TError>> source, Func<T, Result<TResult, TError>> selector, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return item.TryGetValue(out var value) ? selector(value) : item.Error.Fail<TResult, TError>();
                }
            }
        }

        /// <summary>Maps each success's value with <paramref name="selector"/>, awaited one item at a time. A failure passes through as an item, and <paramref name="selector"/> isn't called for it.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new values.</typeparam>
        /// <param name="source">The results to read.</param>
        /// <param name="selector">Maps a value; it receives the token given to the returned stream's enumerator.</param>
        /// <returns>A lazy stream with one item per item of <paramref name="source"/>, in order.</returns>
        /// <remarks>The token given to the returned stream's enumerator (<c>WithCancellation</c>) is passed to <paramref name="source"/> and to <paramref name="selector"/>, as <c>System.Linq.AsyncEnumerable.Select</c> does. An exception <paramref name="selector"/> throws ends the stream with that exception.</remarks>
        public static IAsyncEnumerable<Result<TResult, TError>> SelectResultAsync<T, TError, TResult>(this IAsyncEnumerable<Result<T, TError>> source, Func<T, CancellationToken, ValueTask<TResult>> selector)
        {
            source.ThrowIfDefault(nameof(source));
            selector.ThrowIfDefault(nameof(selector));
            return Core(source, selector, default);

            static async IAsyncEnumerable<Result<TResult, TError>> Core(IAsyncEnumerable<Result<T, TError>> source, Func<T, CancellationToken, ValueTask<TResult>> selector, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return item.TryGetValue(out var value)
                        ? (await selector(value, cancellationToken).ConfigureAwait(false)).Ok<TResult, TError>()
                        : item.Error.Fail<TResult, TError>();
                }
            }
        }

        /// <summary>Replaces each success with the result <paramref name="selector"/> returns for its value, awaited one item at a time. A failure passes through as an item, and <paramref name="selector"/> isn't called for it.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new values.</typeparam>
        /// <param name="source">The results to read.</param>
        /// <param name="selector">Returns the next result from a value; it receives the token given to the returned stream's enumerator.</param>
        /// <returns>A lazy stream with one item per item of <paramref name="source"/>, in order.</returns>
        /// <remarks>The token given to the returned stream's enumerator (<c>WithCancellation</c>) is passed to <paramref name="source"/> and to <paramref name="selector"/>, as <c>System.Linq.AsyncEnumerable.Select</c> does. An exception <paramref name="selector"/> throws ends the stream with that exception.</remarks>
        public static IAsyncEnumerable<Result<TResult, TError>> SelectResultAsync<T, TError, TResult>(this IAsyncEnumerable<Result<T, TError>> source, Func<T, CancellationToken, ValueTask<Result<TResult, TError>>> selector)
        {
            source.ThrowIfDefault(nameof(source));
            selector.ThrowIfDefault(nameof(selector));
            return Core(source, selector, default);

            static async IAsyncEnumerable<Result<TResult, TError>> Core(IAsyncEnumerable<Result<T, TError>> source, Func<T, CancellationToken, ValueTask<Result<TResult, TError>>> selector, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return item.TryGetValue(out var value)
                        ? await selector(value, cancellationToken).ConfigureAwait(false)
                        : item.Error.Fail<TResult, TError>();
                }
            }
        }

        /// <summary>The values of the successes, in order; failures are skipped.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <param name="values">The results to read.</param>
        /// <returns>A lazy stream. The token given to its enumerator is passed to <paramref name="values"/>.</returns>
        public static IAsyncEnumerable<T> Successes<T>(this IAsyncEnumerable<Result<T>> values)
        {
            values.ThrowIfDefault(nameof(values));
            return Core(values, default);

            static async IAsyncEnumerable<T> Core(IAsyncEnumerable<Result<T>> values, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var item in values.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.TryGetValue(out var value)) yield return value;
                }
            }
        }

        /// <summary>The values of the successes, in order; failures are skipped.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results to read.</param>
        /// <returns>A lazy stream. The token given to its enumerator is passed to <paramref name="values"/>.</returns>
        public static IAsyncEnumerable<T> Successes<T, TError>(this IAsyncEnumerable<Result<T, TError>> values)
        {
            values.ThrowIfDefault(nameof(values));
            return Core(values, default);

            static async IAsyncEnumerable<T> Core(IAsyncEnumerable<Result<T, TError>> values, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var item in values.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.TryGetValue(out var value)) yield return value;
                }
            }
        }

        /// <summary>The errors of the failures, in order; successes are skipped.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <param name="values">The results to read.</param>
        /// <returns>A lazy stream. The token given to its enumerator is passed to <paramref name="values"/>.</returns>
        public static IAsyncEnumerable<Error> Failures<T>(this IAsyncEnumerable<Result<T>> values)
        {
            values.ThrowIfDefault(nameof(values));
            return Core(values, default);

            static async IAsyncEnumerable<Error> Core(IAsyncEnumerable<Result<T>> values, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var item in values.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!item.TryGetValue(out _)) yield return item.Error;
                }
            }
        }

        /// <summary>The errors of the failures, in order; successes are skipped.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results to read.</param>
        /// <returns>A lazy stream. The token given to its enumerator is passed to <paramref name="values"/>.</returns>
        public static IAsyncEnumerable<TError> Failures<T, TError>(this IAsyncEnumerable<Result<T, TError>> values)
        {
            values.ThrowIfDefault(nameof(values));
            return Core(values, default);

            static async IAsyncEnumerable<TError> Core(IAsyncEnumerable<Result<T, TError>> values, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var item in values.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!item.TryGetValue(out _)) yield return item.Error;
                }
            }
        }

        /// <summary>Reads every item of <paramref name="values"/>, and returns a success with the values in order, or a failure with every error in order.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <param name="values">The results to read.</param>
        /// <param name="cancellationToken">Passed to the enumerator and checked before each item: a cancelled token cancels the task.</param>
        /// <remarks>Each failed item adds its one <see cref="Error"/> to the validation's errors. Use <see cref="CollectAsync{T}(IAsyncEnumerable{Result{T}}, CancellationToken)"/> to stop at the first failure instead.</remarks>
        public static ValueTask<Validation<IReadOnlyList<T>>> CollectAllAsync<T>(this IAsyncEnumerable<Result<T>> values, CancellationToken cancellationToken)
        {
            values.ThrowIfDefault(nameof(values));
            return Core(values, cancellationToken);

            static async ValueTask<Validation<IReadOnlyList<T>>> Core(IAsyncEnumerable<Result<T>> values, CancellationToken cancellationToken)
            {
                var (successes, errors) = await values.PartitionAsync(cancellationToken).ConfigureAwait(false);
                return errors.Count == 0 ? Validation.Ok(successes) : Validation.Fail<IReadOnlyList<T>>(errors);
            }
        }

        /// <summary>Reads every item of <paramref name="values"/>, and returns a success with the values in order, or a failure with every error in order.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results to read.</param>
        /// <param name="cancellationToken">Passed to the enumerator and checked before each item: a cancelled token cancels the task.</param>
        /// <remarks>Use <see cref="CollectAsync{T, TError}(IAsyncEnumerable{Result{T, TError}}, CancellationToken)"/> to stop at the first failure instead.</remarks>
        public static ValueTask<Validation<IReadOnlyList<T>, TError>> CollectAllAsync<T, TError>(this IAsyncEnumerable<Result<T, TError>> values, CancellationToken cancellationToken)
        {
            values.ThrowIfDefault(nameof(values));
            return Core(values, cancellationToken);

            static async ValueTask<Validation<IReadOnlyList<T>, TError>> Core(IAsyncEnumerable<Result<T, TError>> values, CancellationToken cancellationToken)
            {
                var (successes, errors) = await values.PartitionAsync(cancellationToken).ConfigureAwait(false);
                return errors.Count == 0 ? Validation.Ok<IReadOnlyList<T>, TError>(successes) : Validation.Fail<IReadOnlyList<T>, TError>(errors);
            }
        }

        /// <summary>
        /// Runs <paramref name="selector"/> on each item of <paramref name="source"/>, at most <paramref name="maxConcurrency"/>
        /// at a time, and returns a success with every value, or the failure of the earliest item that failed. Once a call
        /// fails it stops reading <paramref name="source"/>, waits for the calls of earlier items, then cancels the rest.
        /// </summary>
        /// <typeparam name="T">The type of the items.</typeparam>
        /// <typeparam name="TResult">The type of the new values.</typeparam>
        /// <param name="source">The items to read. It is read only while fewer than <paramref name="maxConcurrency"/> calls are running.</param>
        /// <param name="selector">Returns a result for an item; it receives a token linked to <paramref name="cancellationToken"/>, cancelled once no earlier item can fail.</param>
        /// <param name="maxConcurrency">How many calls of <paramref name="selector"/> may run at once; 1 or more.</param>
        /// <param name="preserveOrder"><c>true</c> for the values in the order of <paramref name="source"/>; <c>false</c> for the order the calls completed. It doesn't change which failure is returned.</param>
        /// <param name="cancellationToken">Cancels the task. The linked token reaches <paramref name="source"/> and every call.</param>
        /// <returns>The values, or the failure of the earliest item (in the order of <paramref name="source"/>) that failed: the same answer as running the calls one at a time.</returns>
        /// <remarks>
        /// <para>The failure returned is the earliest item's, whatever <paramref name="preserveOrder"/> says: the calls of earlier items are never cancelled for a later failure, so they finish and may fail first. Only calls of later items are cancelled.</para>
        /// <para>The calls run as async work started from the reading loop, not on new threads: a <paramref name="selector"/> that blocks before its first <c>await</c> blocks the loop. A call that completes synchronously is handled at once, so a synchronous source and selector stop at the first failure without starting more calls.</para>
        /// <para>Nothing catches: the first exception from <paramref name="source"/> or a call faults the task. Before the task completes, early or not, the calls still running are cancelled and awaited (a call that ignores its token is waited for; what they then throw is discarded) and the source's enumerator is disposed. If <paramref name="cancellationToken"/> was cancelled, the <see cref="OperationCanceledException"/> carries it.</para>
        /// <para>Each completion scans the calls in flight, so the cost per item grows with <paramref name="maxConcurrency"/>. For a list in memory, use .NET's <c>ToAsyncEnumerable()</c>; there is no overload over <see cref="IEnumerable{T}"/> (it would be ambiguous for types that are both, such as an EF Core <c>DbSet</c>), and none on <c>netstandard2.0</c>.</para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrency"/> is less than 1.</exception>
        public static ValueTask<Result<IReadOnlyList<TResult>>> TraverseAsync<T, TResult>(this IAsyncEnumerable<T> source, Func<T, CancellationToken, ValueTask<Result<TResult>>> selector, int maxConcurrency, bool preserveOrder, CancellationToken cancellationToken)
        {
            source.ThrowIfDefault(nameof(source));
            selector.ThrowIfDefault(nameof(selector));
            ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
            return Core(source, selector, maxConcurrency, preserveOrder, cancellationToken);

            static async ValueTask<Result<IReadOnlyList<TResult>>> Core(IAsyncEnumerable<T> source, Func<T, CancellationToken, ValueTask<Result<TResult>>> selector, int maxConcurrency, bool preserveOrder, CancellationToken cancellationToken)
            {
                var (values, errors) = await StreamTraversal.Run(source, selector, Split, maxConcurrency, preserveOrder, failFast: true, cancellationToken).ConfigureAwait(false);
                return errors.Count == 0 ? values.Ok() : errors[0].Fail<IReadOnlyList<TResult>>();
            }

            static (bool, TResult, Error) Split(Result<TResult> r) => r.TryGetValue(out var v) ? (true, v, null!) : (false, default!, r.Error);
        }

        /// <summary>
        /// Runs <paramref name="selector"/> on each item of <paramref name="source"/>, at most <paramref name="maxConcurrency"/>
        /// at a time, and returns a success with every value, or the failure of the earliest item that failed. Once a call
        /// fails it stops reading <paramref name="source"/>, waits for the calls of earlier items, then cancels the rest.
        /// </summary>
        /// <typeparam name="T">The type of the items.</typeparam>
        /// <typeparam name="TResult">The type of the new values.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="source">The items to read. It is read only while fewer than <paramref name="maxConcurrency"/> calls are running.</param>
        /// <param name="selector">Returns a result for an item; it receives a token linked to <paramref name="cancellationToken"/>, cancelled once no earlier item can fail.</param>
        /// <param name="maxConcurrency">How many calls of <paramref name="selector"/> may run at once; 1 or more.</param>
        /// <param name="preserveOrder"><c>true</c> for the values in the order of <paramref name="source"/>; <c>false</c> for the order the calls completed. It doesn't change which failure is returned.</param>
        /// <param name="cancellationToken">Cancels the task. The linked token reaches <paramref name="source"/> and every call.</param>
        /// <returns>The values, or the failure of the earliest item (in the order of <paramref name="source"/>) that failed: the same answer as running the calls one at a time.</returns>
        /// <remarks>As <see cref="TraverseAsync{T, TResult}(IAsyncEnumerable{T}, Func{T, CancellationToken, ValueTask{Result{TResult}}}, int, bool, CancellationToken)"/>.</remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrency"/> is less than 1.</exception>
        public static ValueTask<Result<IReadOnlyList<TResult>, TError>> TraverseAsync<T, TResult, TError>(this IAsyncEnumerable<T> source, Func<T, CancellationToken, ValueTask<Result<TResult, TError>>> selector, int maxConcurrency, bool preserveOrder, CancellationToken cancellationToken)
        {
            source.ThrowIfDefault(nameof(source));
            selector.ThrowIfDefault(nameof(selector));
            ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
            return Core(source, selector, maxConcurrency, preserveOrder, cancellationToken);

            static async ValueTask<Result<IReadOnlyList<TResult>, TError>> Core(IAsyncEnumerable<T> source, Func<T, CancellationToken, ValueTask<Result<TResult, TError>>> selector, int maxConcurrency, bool preserveOrder, CancellationToken cancellationToken)
            {
                var (values, errors) = await StreamTraversal.Run(source, selector, Split, maxConcurrency, preserveOrder, failFast: true, cancellationToken).ConfigureAwait(false);
                return errors.Count == 0 ? values.Ok<IReadOnlyList<TResult>, TError>() : errors[0].Fail<IReadOnlyList<TResult>, TError>();
            }

            static (bool, TResult, TError) Split(Result<TResult, TError> r) => r.TryGetValue(out var v) ? (true, v, default!) : (false, default!, r.Error);
        }

        /// <summary>
        /// Runs <paramref name="selector"/> on each item of <paramref name="source"/>, at most <paramref name="maxConcurrency"/>
        /// at a time, and returns a success with every value, or a failure with every error.
        /// </summary>
        /// <typeparam name="T">The type of the items.</typeparam>
        /// <typeparam name="TResult">The type of the new values.</typeparam>
        /// <param name="source">The items to read. It is read only while fewer than <paramref name="maxConcurrency"/> calls are running.</param>
        /// <param name="selector">Returns a result for an item; it receives a token linked to <paramref name="cancellationToken"/>.</param>
        /// <param name="maxConcurrency">How many calls of <paramref name="selector"/> may run at once; 1 or more.</param>
        /// <param name="preserveOrder"><c>true</c> for the values and the errors in the order of <paramref name="source"/>; <c>false</c> for the order the calls completed.</param>
        /// <param name="cancellationToken">Cancels the task. The linked token reaches <paramref name="source"/> and every call.</param>
        /// <remarks>A failure doesn't stop the traversal: every item is read and every call runs. For a list in memory, use .NET's <c>ToAsyncEnumerable()</c>. Exceptions and cancellation behave as in <see cref="TraverseAsync{T, TResult}(IAsyncEnumerable{T}, Func{T, CancellationToken, ValueTask{Result{TResult}}}, int, bool, CancellationToken)"/>.</remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrency"/> is less than 1.</exception>
        public static ValueTask<Validation<IReadOnlyList<TResult>>> TraverseAllAsync<T, TResult>(this IAsyncEnumerable<T> source, Func<T, CancellationToken, ValueTask<Result<TResult>>> selector, int maxConcurrency, bool preserveOrder, CancellationToken cancellationToken)
        {
            source.ThrowIfDefault(nameof(source));
            selector.ThrowIfDefault(nameof(selector));
            ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
            return Core(source, selector, maxConcurrency, preserveOrder, cancellationToken);

            static async ValueTask<Validation<IReadOnlyList<TResult>>> Core(IAsyncEnumerable<T> source, Func<T, CancellationToken, ValueTask<Result<TResult>>> selector, int maxConcurrency, bool preserveOrder, CancellationToken cancellationToken)
            {
                var (values, errors) = await StreamTraversal.Run(source, selector, Split, maxConcurrency, preserveOrder, failFast: false, cancellationToken).ConfigureAwait(false);
                return errors.Count == 0 ? Validation.Ok(values) : Validation.Fail<IReadOnlyList<TResult>>(errors);
            }

            static (bool, TResult, Error) Split(Result<TResult> r) => r.TryGetValue(out var v) ? (true, v, null!) : (false, default!, r.Error);
        }

        /// <summary>
        /// Runs <paramref name="selector"/> on each item of <paramref name="source"/>, at most <paramref name="maxConcurrency"/>
        /// at a time, and returns a success with every value, or a failure with every error.
        /// </summary>
        /// <typeparam name="T">The type of the items.</typeparam>
        /// <typeparam name="TResult">The type of the new values.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="source">The items to read. It is read only while fewer than <paramref name="maxConcurrency"/> calls are running.</param>
        /// <param name="selector">Returns a result for an item; it receives a token linked to <paramref name="cancellationToken"/>.</param>
        /// <param name="maxConcurrency">How many calls of <paramref name="selector"/> may run at once; 1 or more.</param>
        /// <param name="preserveOrder"><c>true</c> for the values and the errors in the order of <paramref name="source"/>; <c>false</c> for the order the calls completed.</param>
        /// <param name="cancellationToken">Cancels the task. The linked token reaches <paramref name="source"/> and every call.</param>
        /// <remarks>As <see cref="TraverseAllAsync{T, TResult}(IAsyncEnumerable{T}, Func{T, CancellationToken, ValueTask{Result{TResult}}}, int, bool, CancellationToken)"/>.</remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrency"/> is less than 1.</exception>
        public static ValueTask<Validation<IReadOnlyList<TResult>, TError>> TraverseAllAsync<T, TResult, TError>(this IAsyncEnumerable<T> source, Func<T, CancellationToken, ValueTask<Result<TResult, TError>>> selector, int maxConcurrency, bool preserveOrder, CancellationToken cancellationToken)
        {
            source.ThrowIfDefault(nameof(source));
            selector.ThrowIfDefault(nameof(selector));
            ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
            return Core(source, selector, maxConcurrency, preserveOrder, cancellationToken);

            static async ValueTask<Validation<IReadOnlyList<TResult>, TError>> Core(IAsyncEnumerable<T> source, Func<T, CancellationToken, ValueTask<Result<TResult, TError>>> selector, int maxConcurrency, bool preserveOrder, CancellationToken cancellationToken)
            {
                var (values, errors) = await StreamTraversal.Run(source, selector, Split, maxConcurrency, preserveOrder, failFast: false, cancellationToken).ConfigureAwait(false);
                return errors.Count == 0 ? Validation.Ok<IReadOnlyList<TResult>, TError>(values) : Validation.Fail<IReadOnlyList<TResult>, TError>(errors);
            }

            static (bool, TResult, TError) Split(Result<TResult, TError> r) => r.TryGetValue(out var v) ? (true, v, default!) : (false, default!, r.Error);
        }
    }

    // The engine under TraverseAsync and TraverseAllAsync. One loop reads the source and starts calls while fewer than
    // maxConcurrency run. Completed calls are handled before anything else (a synchronous call at once), then a
    // completed read; otherwise it waits on whichever finishes first. In fail-fast mode the earliest failing index wins:
    // after a failure it stops reading, waits for every call of an earlier item (one may fail earlier still), then ends.
    // Leaving the loop, by any path, cancels the linked token, awaits what is still running (discarding what it throws)
    // and disposes the enumerator. Finding a completed call scans the calls in flight: O(maxConcurrency) per completion.
    internal static class StreamTraversal
    {
        internal static async ValueTask<(IReadOnlyList<TResult> Values, IReadOnlyList<TError> Errors)> Run<T, TOut, TResult, TError>(
            IAsyncEnumerable<T> source,
            Func<T, CancellationToken, ValueTask<TOut>> selector,
            Func<TOut, (bool Ok, TResult Value, TError Error)> split,
            int maxConcurrency,
            bool preserveOrder,
            bool failFast,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = linked.Token;
            var values = new List<(int Index, TResult Value)>();
            var errors = new List<(int Index, TError Error)>();
            var running = new List<(int Index, Task<TOut> Call)>(maxConcurrency);
            var enumerator = source.GetAsyncEnumerator(token);
            Task<bool>? pull = null;
            var sourceDone = false;
            var index = 0;
            var firstFailure = int.MaxValue;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var done = running.FindIndex(static r => r.Call.IsCompleted);
                    if (done >= 0)
                    {
                        var (at, call) = running[done];
                        running.RemoveAt(done);
                        var (ok, value, error) = split(await call.ConfigureAwait(false));
                        if (ok)
                        {
                            values.Add((at, value));
                        }
                        else
                        {
                            errors.Add((at, error));
                            if (failFast) firstFailure = Math.Min(firstFailure, at);
                        }

                        continue;
                    }

                    var stopped = failFast && firstFailure != int.MaxValue;
                    if (stopped && running.TrueForAll(r => r.Index > firstFailure)) break;

                    if (pull is { IsCompleted: true })
                    {
                        var hasItem = await pull.ConfigureAwait(false);
                        pull = null;
                        if (!hasItem) sourceDone = true;
                        else if (!stopped) running.Add((index++, Call(selector, enumerator.Current, token)));
                        continue;
                    }

                    if (pull is null && !sourceDone && !stopped && running.Count < maxConcurrency)
                    {
                        var next = enumerator.MoveNextAsync();
                        if (!next.IsCompleted)
                        {
                            pull = next.AsTask();
                        }
                        else if (await next.ConfigureAwait(false))
                        {
                            running.Add((index++, Call(selector, enumerator.Current, token)));
                            continue;
                        }
                        else
                        {
                            sourceDone = true;
                            continue;
                        }
                    }

                    if (pull is null && running.Count == 0) break;

                    var waits = new Task[running.Count + (pull is null ? 0 : 1)];
                    for (var i = 0; i < running.Count; i++) waits[i] = running[i].Call;
                    if (pull is not null) waits[^1] = pull;
                    await Task.WhenAny(waits).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException e) when (cancellationToken.IsCancellationRequested && e.CancellationToken != cancellationToken)
            {
                // The caller cancelled: answer with the caller's token, not the linked one a call or the source saw.
                throw new OperationCanceledException(e.Message, e, cancellationToken);
            }
            finally
            {
                if (running.Count > 0 || pull is not null)
                {
                    await linked.CancelAsync().ConfigureAwait(false);
                    foreach (var (_, call) in running) await Discard(call).ConfigureAwait(false);
                    if (pull is not null) await Discard(pull).ConfigureAwait(false);
                }

                await enumerator.DisposeAsync().ConfigureAwait(false);
            }

            // Fail-fast answers with the earliest failing index; otherwise preserveOrder picks the order.
            if (preserveOrder || failFast) errors.Sort(static (a, b) => a.Index.CompareTo(b.Index));
            if (preserveOrder) values.Sort(static (a, b) => a.Index.CompareTo(b.Index));
            return (values.ConvertAll(static v => v.Value).AsReadOnly(), errors.ConvertAll(static e => e.Error).AsReadOnly());
        }

        // A synchronous throw from the selector faults this task instead of escaping the loop; a selector that completes
        // synchronously gives a completed task, which the loop handles at once.
        private static async Task<TOut> Call<T, TOut>(Func<T, CancellationToken, ValueTask<TOut>> selector, T item, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return await selector(item, token).ConfigureAwait(false);
        }

        // Awaits abandoned work so nothing runs on after the traversal ends; what it throws (usually the cancellation
        // the traversal caused) is discarded, as the traversal already has its answer or its exception.
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Abandoned work after the traversal has its answer; documented.")]
        private static async Task Discard(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }
}
#endif
