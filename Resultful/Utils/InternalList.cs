using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TaskExt;

namespace Resultful.Utils
{
    internal static class InternalList
    {
        //Normal
        internal static TResult FoldInternal<TResult, T>(
            this IEnumerator<T> values, TResult seed,
            Func<TResult, T, TResult> aggrFunc)
        {
            while (values.MoveNext())
            {
                seed = aggrFunc(seed, values.Current);
            }
            return seed;
        }

        internal static T ReduceInternal<T>(
            this IEnumerator<T> values,
            Func<T, T, T> aggrFunc) =>
            !values.MoveNext()
                ? throw new ArgumentException("Must have at least one item present", nameof(values))
                : FoldInternal(values, values.Current, aggrFunc);

        internal static Option<T> TryReduceInternal<T>(
            this IEnumerator<T> values,
            Func<T, T, T> aggrFunc) =>
            !values.MoveNext()
                ? Option<T>.None
                : FoldInternal(values, values.Current, aggrFunc).Some();

        internal static TResult FoldUntilInternal<TResult, T>(
            this IEnumerator<T> values, TResult seed,
            Func<TResult, T, Option<TResult>> aggrFunc)
        {
            var exit = false;
            while (!exit && values.MoveNext())
            {
                aggrFunc(seed, values.Current).Switch(
                    x => { seed = x; },
                    _ => { exit = true; });
            }
            return seed;
        }

        internal static T ReduceUntilInternal<T>(
            this IEnumerator<T> values,
            Func<T, T, Option<T>> aggrFunc) =>
            !values.MoveNext()
                ? throw new ArgumentException("Must have at least one item present", nameof(values))
                : FoldUntilInternal(values, values.Current, aggrFunc);

        internal static Option<T> TryReduceUntilInternal<T>(
            this IEnumerator<T> values,
            Func<T, T, Option<T>> aggrFunc) =>
            !values.MoveNext()
                ? Option<T>.None
                : FoldUntilInternal(values, values.Current, aggrFunc).Some();

        //Async
        internal static async Task<TResult> FoldInternalAsync<TResult, T>(this IEnumerator<T> values, TResult seed,
            Func<TResult, T, Task<TResult>> aggrFunc)
        {
            while (values.MoveNext())
            {
                seed = await aggrFunc(seed, values.Current).ConfigureAwait(false);
            }
            return seed;
        }

        internal static Task<T> ReduceInternalAsync<T>(
            this IEnumerator<T> values,
            Func<T, T, Task<T>> aggrFunc) =>
            !values.MoveNext()
                ? throw new ArgumentException("Must have at least one item present", nameof(values))
                : FoldInternalAsync(values, values.Current, aggrFunc);

        internal static async Task<Option<T>> TryReduceInternalAsync<T>(
            this IEnumerator<T> values,
            Func<T, T, Task<T>> aggrFunc) =>
            !values.MoveNext()
                ? Option<T>.None
                : (await FoldInternalAsync(values, values.Current, aggrFunc).ConfigureAwait(false)).Some();

        internal static async Task<TResult> FoldUntilInternalAsync<TResult, T>(this IEnumerator<T> values, TResult seed,
            Func<TResult, T, Task<Option<TResult>>> aggrFunc)
        {
            var exit = false;
            while (!exit && values.MoveNext())
            {
                await aggrFunc(seed, values.Current).Switch(
                    x =>
                    {
                        seed = x;
                    },
                    _ =>
                    {
                        exit = true;
                    }).ConfigureAwait(false);
            }
            return seed;
        }

        internal static Task<T> ReduceUntilInternalAsync<T>(
            this IEnumerator<T> values,
            Func<T, T, Task<Option<T>>> aggrFunc) =>
            !values.MoveNext()
                ? throw new ArgumentException("Must have at least one item present", nameof(values))
                : FoldUntilInternalAsync(values, values.Current, aggrFunc);

        internal static async Task<Option<T>> TryReduceUntilInternalAsync<T>(
            this IEnumerator<T> values,
            Func<T, T, Task<Option<T>>> aggrFunc) =>
            !values.MoveNext()
                ? Option<T>.None
                : (await FoldUntilInternalAsync(values, values.Current, aggrFunc).ConfigureAwait(false)).Some();
    }
}
