#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Resultful
{
    public static partial class EnumerableExtensions
    {
        //ReduceAsync on List<T>
        public static Task<T> ReduceAsync<T>(this IEnumerable<T> values, Func<T, T, Task<T>> aggrFunc) => throw null!;
        //TryReduceAsync on List<T>
        public static Task<Option<T>> TryReduceAsync<T>(this IEnumerable<T> values, Func<T, T, Task<T>> aggrFunc) => throw null!;
        //ReduceUntil on List<T>
        public static Task<T> ReduceUntilAsync<T>(this IEnumerable<T> values, Func<T, T, Task<Option<T>>> aggrFunc) => throw null!;
        //TryReduceUntil on List<T>
        public static Task<Option<T>> TryReduceUntilAsync<T>(this IEnumerable<T> values, Func<T, T, Task<Option<T>>> aggrFunc) => throw null!;
        //FoldAsync on List<T>
        public static Task<TResult> FoldAsync<TResult, T>(this IEnumerable<T> values, TResult seed, Func<TResult, T, Task<TResult>> aggrFunc) => throw null!;
        //FoldUntil on List<T>
        public static Task<TResult> FoldUntilAsync<TResult, T>(this IEnumerable<T> values, Task<TResult> seed, Func<TResult, T, Task<Option<TResult>>> aggrFunc) => throw null!;
        public static Task<TResult> FoldUntilAsync<TResult, T>(this IEnumerable<T> values, TResult seed, Func<TResult, T, Task<Option<TResult>>> aggrFunc) => throw null!;
    }
}
