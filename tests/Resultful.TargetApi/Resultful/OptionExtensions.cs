#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;

namespace Resultful
{
    public static partial class Option
    {
        public static Option<T> Some<T>(this T value) => throw null!;
        //Plus on Option<T>
        public static Option<(TLeft, TRight)> Plus<TLeft, TRight>(this Option<TLeft> left, Option<TRight> right) => throw null!;
        public static Option<TResult> Plus<TLeft, TRight, TResult>(this Option<TLeft> left, Option<TRight> right)
            where TLeft : IPlus<TRight, TResult> => throw null!;
        public static Option<TResult> Plus<TLeft, TRight, TResult>(this Option<TLeft> left, Option<TRight> right, Func<TLeft, TRight, TResult> plusFunc) => throw null!;
        //Flatten on Option<Option<T>>
        public static Option<T> Flatten<T>(this Option<Option<T>> value) => throw null!;
        //Unroll on IEnumerable<Option<T>>
        public static Option<IEnumerable<T>> Unroll<T>(this IEnumerable<Option<T>> values) => throw null!;
        //ToOption
        public static Option<T> ToOption<T>(this T value) => throw null!;
        //Reduce on List<Option<T>>
        public static Option<T> Reduce<T>(this IEnumerable<Option<T>> values, Func<T, T, T> aggrFunc) => throw null!;
        public static Option<T> Reduce<T>(this IEnumerable<Option<T>> values)
            where T : IPlus<T, T> => throw null!;
        //ReduceUntil on List<Option<T>>
        public static Option<T> ReduceUntil<T>(this IEnumerable<Option<T>> values, Func<T, T, Option<T>> aggrFunc) => throw null!;
        //Fold on List<Option<T>>
        public static TResult Fold<TResult, T>(this IEnumerable<Option<T>> values, TResult seed, Func<TResult, T, TResult> aggrFunc) => throw null!;
        public static Option<TResult> Fold<TResult, T>(this IEnumerable<Option<T>> values, Option<TResult> seed, Func<TResult, T, TResult> aggrFunc) => throw null!;
        //FoldUntil on List<Option<T>>
        public static TResult FoldUntil<TResult, T>(this IEnumerable<Option<T>> values, TResult seed, Func<TResult, T, Option<TResult>> aggrFunc) => throw null!;
        public static Option<TResult> FoldUntil<TResult, T>(this IEnumerable<Option<T>> values, Option<TResult> seed, Func<TResult, T, Option<TResult>> aggrFunc) => throw null!;
        //TryReduceUntil on List<Option<T>>
        public static Option<T> TryReduce<T>(this IEnumerable<Option<T>> values, Func<T, T, T> aggrFunc) => throw null!;
        //TryReduceUntil on List<Option<T>>
        public static Option<T> TryReduceUntil<T>(this IEnumerable<Option<T>> values, Func<T, T, Option<T>> aggrFunc) => throw null!;
    }
}
