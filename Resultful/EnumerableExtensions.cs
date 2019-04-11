using System;
using System.Collections.Generic;
using System.Text;
using Resultful.Utils;

namespace Resultful
{
    public static partial class EnumerableExtensions
    {
        //TryReduce on List<T>
        public static T Reduce<T>(this IEnumerable<T> values,
            Func<T, T, T> aggrFunc)
        {
            using (var enumerator = values.GetEnumerator())
            {
                return InternalList.ReduceInternal(enumerator, aggrFunc);
            }
        }

        //TryReduce on List<T>
        public static Option<T> TryReduce<T>(this IEnumerable<T> values,
            Func<T, T, T> aggrFunc)
        {
            using (var enumerator = values.GetEnumerator())
            {
                return InternalList.TryReduceInternal(enumerator, aggrFunc);
            }
        }

        //Fold on List<T>
        public static TResult Fold<TResult, T>(this IEnumerable<T> values, TResult seed,
            Func<TResult, T, TResult> aggrFunc)
        {
            using (var enumerator = values.GetEnumerator())
            {
                return InternalList.FoldInternal(enumerator, seed, aggrFunc);
            }
        }

        //FoldUntil on List<T>
        public static TResult FoldUntil<TResult, T>(this IEnumerable<T> values, TResult seed,
            Func<TResult, T, Option<TResult>> aggrFunc)
        {
            using (var enumerator = values.GetEnumerator())
            {
                return InternalList.FoldUntilInternal(enumerator, seed, aggrFunc);
            }
        }

        //ReduceUntil on List<T>
        public static T ReduceUntil<T>(this IEnumerable<T> values,
            Func<T, T, Option<T>> aggrFunc)
        {
            using (var enumerator = values.GetEnumerator())
            {
                return InternalList.ReduceUntilInternal(enumerator, aggrFunc);
            }
        }

        //TryReduceUntil on List<T>
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
