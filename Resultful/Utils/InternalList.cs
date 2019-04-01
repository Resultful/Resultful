using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Resultful.Utils
{
    internal static class InternalList
    {
        //Normal
        internal static TResult FoldUntilInternal<TResult, T>(this IEnumerator<T> values, TResult seed,
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

        internal static Option<TResult> FoldUntilInternal<TResult, T>(this IEnumerator<T> values, Option<TResult> seed,
            Func<TResult, T, Option<TResult>> aggrFunc)
        {
            var exit = false;

            do
            {
                seed.Switch(
                    x =>
                    {
                        if (values.MoveNext())
                        {
                            seed = aggrFunc(x, values.Current);
                        }
                        else
                        {
                            exit = true;
                        }
                    },
                    _ => { exit = true; });
            } while (!exit);
            return seed;
        }


        //Async
        internal static async Task<TResult> ReduceAsyncInternal<TResult, T>(this IEnumerator<T> values, TResult seed,
            Func<TResult, T, Task<TResult>> aggrFunc)
        {
            while (values.MoveNext())
            {
                seed = await aggrFunc(seed, values.Current).ConfigureAwait(false);
            }
            return seed;
        }

        internal static async Task<Option<TResult>> FoldUntilInternalAsync<TResult, T>(this IEnumerator<T> values, Option<TResult> seed,
            Func<TResult, T, Task<Option<TResult>>> aggrFunc)
        {
            var exit = false;

            do
            {
                await seed.SwitchAsync(async x =>
                {
                    if (values.MoveNext())
                    {
                        seed = await aggrFunc(x, values.Current).ConfigureAwait(false);
                    }
                    else
                    {
                        exit = true;
                    }
                },
                     _ => { exit = true; return Task.CompletedTask; }).ConfigureAwait(false);
            } while (!exit);
            return seed;
        }

        internal static async Task<TResult> FoldInternalAsync<TResult, T>(this IEnumerator<T> values, TResult seed,
            Func<TResult, T, Task<TResult>> aggrFunc)
        {
            while (values.MoveNext())
            {
                seed = await aggrFunc(seed, values.Current).ConfigureAwait(false);
            }

            return seed;
        }

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
    }
}
