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
        public static async Task<T> ReduceAsync<T>(this IEnumerable<T> values,
            Func<T, T, Task<T>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.ReduceInternalAsync(enumerator, aggrFunc).ConfigureAwait(false);
            }
        }

        //TryReduceAsync on List<T>
        public static async Task<Option<T>> TryReduceAsync<T>(this IEnumerable<T> values,
            Func<T, T, Task<T>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.TryReduceInternalAsync(enumerator, aggrFunc).ConfigureAwait(false);
            }
        }

        //ReduceUntil on List<T>
        public static async Task<T> ReduceUntilAsync<T>(this IEnumerable<T> values,
            Func<T, T, Task<Option<T>>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.ReduceUntilInternalAsync(enumerator, aggrFunc).ConfigureAwait(false);
            }
        }

        //TryReduceUntil on List<T>
        public static async Task<Option<T>> TryReduceUntilAsync<T>(this IEnumerable<T> values,
            Func<T, T, Task<Option<T>>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.TryReduceUntilInternalAsync(enumerator, aggrFunc).ConfigureAwait(false);
            }
        }

        //FoldAsync on List<T>
        public static async Task<TResult> FoldAsync<TResult, T>(this IEnumerable<T> values, TResult seed,
            Func<TResult, T, Task<TResult>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.FoldInternalAsync(enumerator, seed, aggrFunc);
            }
        }

        //FoldUntil on List<T>
        public static async Task<TResult> FoldUntilAsync<TResult, T>(this IEnumerable<T> values, Task<TResult> seed,
            Func<TResult, T, Task<Option<TResult>>> aggrFunc)
        {
            var finalSeed = await seed.ConfigureAwait(false);
            return await FoldUntilAsync(values.ThrowIfDefault(nameof(values)), finalSeed, aggrFunc)
                .ConfigureAwait(false);
        }

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
