using System;
using System.Collections.Generic;
using System.Text;
using Resultful.Utils;

namespace Resultful
{
    /// <summary>Reduce and fold over a sequence of plain values.</summary>
    /// <remarks>Exceptions thrown by a delegate are not caught.</remarks>
    public static partial class EnumerableExtensions
    {
        //TryReduce on List<T>
        /// <summary>Combines the items in order with <paramref name="aggrFunc"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The items.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static T Reduce<T>(this IEnumerable<T> values,
            Func<T, T, T> aggrFunc)
        {
            using (var enumerator = values.GetEnumerator())
            {
                return InternalList.ReduceInternal(enumerator, aggrFunc);
            }
        }

        //TryReduce on List<T>
        /// <summary>Combines the items in order with <paramref name="aggrFunc"/>. Returns None if there are no items.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The items.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Option<T> TryReduce<T>(this IEnumerable<T> values,
            Func<T, T, T> aggrFunc)
        {
            using (var enumerator = values.GetEnumerator())
            {
                return InternalList.TryReduceInternal(enumerator, aggrFunc);
            }
        }

        //Fold on List<T>
        /// <summary>Combines <paramref name="seed"/> and the items in order with <paramref name="aggrFunc"/>.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The items.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static TResult Fold<TResult, T>(this IEnumerable<T> values, TResult seed,
            Func<TResult, T, TResult> aggrFunc)
        {
            using (var enumerator = values.GetEnumerator())
            {
                return InternalList.FoldInternal(enumerator, seed, aggrFunc);
            }
        }

        //FoldUntil on List<T>
        /// <summary>Combines <paramref name="seed"/> and the items in order with <paramref name="aggrFunc"/>, stopping when it returns None, and returns the last combined value.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The items.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static TResult FoldUntil<TResult, T>(this IEnumerable<T> values, TResult seed,
            Func<TResult, T, Option<TResult>> aggrFunc)
        {
            using (var enumerator = values.GetEnumerator())
            {
                return InternalList.FoldUntilInternal(enumerator, seed, aggrFunc);
            }
        }

        //ReduceUntil on List<T>
        /// <summary>Combines the items in order with <paramref name="aggrFunc"/>, stopping when it returns None, and returns the last combined value.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The items.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static T ReduceUntil<T>(this IEnumerable<T> values,
            Func<T, T, Option<T>> aggrFunc)
        {
            using (var enumerator = values.GetEnumerator())
            {
                return InternalList.ReduceUntilInternal(enumerator, aggrFunc);
            }
        }

        //TryReduceUntil on List<T>
        /// <summary>Combines the items in order with <paramref name="aggrFunc"/>, stopping when it returns None. Returns the last combined value, or None if there are no items.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The items.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Option<T> TryReduceUntil<T>(this IEnumerable<T> values,
            Func<T, T, Option<T>> aggrFunc)
        {
            using (var enumerator = values.GetEnumerator())
            {
                return InternalList.TryReduceUntilInternal(enumerator, aggrFunc);
            }
        }

    }
}
