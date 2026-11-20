#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Resultful
{
    public static partial class EnumerableExtensions
    {
        //TryReduce on List<T>
        public static T Reduce<T>(this IEnumerable<T> values, Func<T, T, T> aggrFunc) => throw null!;
        //TryReduce on List<T>
        public static Option<T> TryReduce<T>(this IEnumerable<T> values, Func<T, T, T> aggrFunc) => throw null!;
        //Fold on List<T>
        public static TResult Fold<TResult, T>(this IEnumerable<T> values, TResult seed, Func<TResult, T, TResult> aggrFunc) => throw null!;
        //FoldUntil on List<T>
        public static TResult FoldUntil<TResult, T>(this IEnumerable<T> values, TResult seed, Func<TResult, T, Option<TResult>> aggrFunc) => throw null!;
        //ReduceUntil on List<T>
        public static T ReduceUntil<T>(this IEnumerable<T> values, Func<T, T, Option<T>> aggrFunc) => throw null!;
        //TryReduceUntil on List<T>
        public static Option<T> TryReduceUntil<T>(this IEnumerable<T> values, Func<T, T, Option<T>> aggrFunc) => throw null!;
    }
}
