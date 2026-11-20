#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Resultful
{
    public static partial class Option
    {
        //Builder for Option<T> from Task<T> value
        public static Task<Option<T>> Some<T>(this Task<T> value) => throw null!;
        //Switch on Task<Option<T>>
        public static Task Switch<T>(this Task<Option<T>> value, Action<T> someFunc, Action<None> noneFunc) => throw null!;
        public static Task SwitchAsync<T>(this Task<Option<T>> value, Func<T, Task> someFunc, Func<None, Task> noneFunc) => throw null!;
        //Match on Task<Option<T>>
        public static Task<TResult> MatchAsync<T, TResult>(this Task<Option<T>> value, Func<T, Task<TResult>> someFunc, Func<None, Task<TResult>> noneFunc) => throw null!;
        public static Task<TResult> Match<T, TResult>(this Task<Option<T>> value, Func<T, TResult> someFunc, Func<None, TResult> noneFunc) => throw null!;
        //Unwrap on Option<Task<T>>
        public static Task<Option<T>> UnwrapAsync<T>(this Option<Task<T>> value) => throw null!;
        //WhenAll on IEnumerable<Task<Option<T>>>
        public static Task<Option<IEnumerable<T>>> WhenAll<T>(this IEnumerable<Option<Task<T>>> values) => throw null!;
        //BindAsync on Option<T>
        public static Task<Option<TResult>> Bind<T, TResult>(this Task<Option<T>> value, Func<T, Option<TResult>> bindFunc) => throw null!;
        public static Task<Option<TResult>> BindAsync<T, TResult>(this Task<Option<T>> value, Func<T, Task<Option<TResult>>> bindFunc) => throw null!;
        //MapAsync on Option<T>
        public static Task<Option<TResult>> Map<T, TResult>(this Task<Option<T>> value, Func<T, TResult> bindFunc) => throw null!;
        public static Task<Option<TResult>> MapAsync<T, TResult>(this Task<Option<T>> value, Func<T, Task<TResult>> bindFunc) => throw null!;
        //TeeAsync on Option<T>
        public static Task<Option<T>> TeeAsync<T>(this Task<Option<T>> value, Func<T, Task> asyncFunc) => throw null!;
        public static Task<Option<T>> Tee<T>(this Task<Option<T>> value, Action<T> teeAction) => throw null!;
        //FoldAsync on Option<T>
        public static Task<TResult> Fold<T, TResult>(this Task<Option<T>> value, TResult seed, Func<TResult, T, TResult> func) => throw null!;
        public static Task<TResult> FoldAsync<T, TResult>(this Task<Option<T>> value, TResult seed, Func<TResult, T, Task<TResult>> func) => throw null!;
        //FoldUntilAsync on Option<T>
        public static Task<Option<TResult>> FoldUntil<T, TResult>(this Task<Option<T>> value, TResult seed, Func<TResult, T, Option<TResult>> func) => throw null!;
        public static Task<Option<TResult>> FoldUntilAsync<T, TResult>(this Task<Option<T>> value, TResult seed, Func<TResult, T, Task<Option<TResult>>> func) => throw null!;
        //OrAsync on Option<T>
        public static Task<T> Or<T>(this Task<Option<T>> value, T otherValue) => throw null!;
        public static Task<T> OrAsync<T>(this Task<Option<T>> value, Task<T> otherValue) => throw null!;
        public static Task<T> Or<T>(this Task<Option<T>> value, Func<T> otherFunc) => throw null!;
        public static Task<T> OrAsync<T>(this Task<Option<T>> value, Func<Task<T>> otherFunc) => throw null!;
        public static Task<Option<T>> Or<T>(this Task<Option<T>> value, Option<T> other) => throw null!;
        public static Task<Option<T>> OrAsync<T>(this Task<Option<T>> value, Task<Option<T>> other) => throw null!;
        public static Task<Option<T>> Or<T>(this Task<Option<T>> value, Func<Option<T>> otherFunc) => throw null!;
        public static Task<Option<T>> OrAsync<T>(this Task<Option<T>> value, Func<Task<Option<T>>> otherFunc) => throw null!;
        //Flatten
        public static Task<Option<T>> Flatten<T>(this Task<Option<Option<T>>> value) => throw null!;
        //ToOptionAsync
        public static Task<Option<T>> ToOption<T>(this Task<T> value) => throw null!;
        //Reduce on List<Option<T>>
        public static Task<Option<T>> ReduceAsync<T>(this IEnumerable<Option<T>> values, Func<T, T, Task<T>> aggrFunc) => throw null!;
        //ReduceUntil on List<Option<T>>
        public static Task<Option<T>> ReduceUntil<T>(this IEnumerable<Option<T>> values, Func<T, T, Task<Option<T>>> aggrFunc) => throw null!;
        //Fold on List<Option<T>>
        public static Task<TResult> FoldAsync<TResult, T>(this IEnumerable<Option<T>> values, TResult seed, Func<TResult, T, Task<TResult>> aggrFunc) => throw null!;
        public static Task<Option<TResult>> FoldAsync<TResult, T>(this IEnumerable<Option<T>> values, Option<TResult> seed, Func<TResult, T, Task<TResult>> aggrFunc) => throw null!;
        //FoldUntil on List<Option<T>>
        public static Task<TResult> FoldUntilAsync<TResult, T>(this IEnumerable<Option<T>> values, TResult seed, Func<TResult, T, Task<Option<TResult>>> aggrFunc) => throw null!;
        public static Task<Option<TResult>> FoldUntilAsync<TResult, T>(this IEnumerable<Option<T>> values, Option<TResult> seed, Func<TResult, T, Task<Option<TResult>>> aggrFunc) => throw null!;
        //TryReduceUntil on List<Option<T>>
        public static Task<Option<T>> TryReduceAsync<T>(this IEnumerable<Option<T>> values, Func<T, T, Task<T>> aggrFunc) => throw null!;
        //TryReduceUntil on List<Option<T>>
        public static Task<Option<T>> TryReduceUntilAsync<T>(this IEnumerable<Option<T>> values, Func<T, T, Task<Option<T>>> aggrFunc) => throw null!;
    }
}
