using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Resultful.Utils;

namespace Resultful
{
    public static partial class EnumerableExtensions
    {
        //ReduceAsync on List<T>
        /// <summary>Combines the items in order with <paramref name="aggrFunc"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The items.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static async Task<T> ReduceAsync<T>(this IEnumerable<T> values,
            Func<T, T, Task<T>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.ReduceInternalAsync(enumerator, aggrFunc).ConfigureAwait(false);
            }
        }

        //TryReduceAsync on List<T>
        /// <summary>Combines the items in order with <paramref name="aggrFunc"/>. Returns None if there are no items.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The items.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static async Task<Option<T>> TryReduceAsync<T>(this IEnumerable<T> values,
            Func<T, T, Task<T>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.TryReduceInternalAsync(enumerator, aggrFunc).ConfigureAwait(false);
            }
        }

        //ReduceUntil on List<T>
        /// <summary>Combines the items in order with <paramref name="aggrFunc"/>, stopping when it returns None, and returns the last combined value.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The items.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static async Task<T> ReduceUntilAsync<T>(this IEnumerable<T> values,
            Func<T, T, Task<Option<T>>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.ReduceUntilInternalAsync(enumerator, aggrFunc).ConfigureAwait(false);
            }
        }

        //TryReduceUntil on List<T>
        /// <summary>Combines the items in order with <paramref name="aggrFunc"/>, stopping when it returns None. Returns the last combined value, or None if there are no items.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The items.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static async Task<Option<T>> TryReduceUntilAsync<T>(this IEnumerable<T> values,
            Func<T, T, Task<Option<T>>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.TryReduceUntilInternalAsync(enumerator, aggrFunc).ConfigureAwait(false);
            }
        }

        //FoldAsync on List<T>
        /// <summary>Combines <paramref name="seed"/> and the items in order with <paramref name="aggrFunc"/>.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The items.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static async Task<TResult> FoldAsync<TResult, T>(this IEnumerable<T> values, TResult seed,
            Func<TResult, T, Task<TResult>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.FoldInternalAsync(enumerator, seed, aggrFunc).ConfigureAwait(false);
            }
        }

        //FoldUntil on List<T>
        /// <summary>Combines <paramref name="seed"/> and the items in order with <paramref name="aggrFunc"/>, stopping when it returns None, and returns the last combined value.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The items.</param>
        /// <param name="seed">The task that gives the starting value. It is awaited first.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static async Task<TResult> FoldUntilAsync<TResult, T>(this IEnumerable<T> values, Task<TResult> seed,
            Func<TResult, T, Task<Option<TResult>>> aggrFunc)
        {
            var finalSeed = await seed.ConfigureAwait(false);
            return await FoldUntilAsync(values.ThrowIfDefault(nameof(values)), finalSeed, aggrFunc)
                .ConfigureAwait(false);
        }

        /// <summary>Combines <paramref name="seed"/> and the items in order with <paramref name="aggrFunc"/>, stopping when it returns None, and returns the last combined value.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The items.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static async Task<TResult> FoldUntilAsync<TResult, T>(this IEnumerable<T> values, TResult seed,
            Func<TResult, T, Task<Option<TResult>>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.FoldUntilInternalAsync(enumerator, seed, aggrFunc)
                    .ConfigureAwait(false);
            }
        }


    }
}
