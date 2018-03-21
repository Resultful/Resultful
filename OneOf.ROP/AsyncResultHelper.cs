using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace OneOf.ROP
{
    internal static class AsyncResultHelper
    {
        internal static async Task<TResult> WrapAsync<T, TResult>(this Task<T> value, Func<T, TResult> helperFunc)
            => helperFunc(await value.ConfigureAwait(false));

        internal static async Task<TResult> WrapAsync<T, TResult>(this Task<T> value, Func<T, Task<TResult>> helperFunc)
            => await helperFunc(await value.ConfigureAwait(false)).ConfigureAwait(false);

        internal static async Task<T> WrapAsync<T>(this Task<T> value, Func<T, Task> helperFunc)
        {
            var result = await value.ConfigureAwait(false);
            await helperFunc(result).ConfigureAwait(false);
            return result;
        }

        internal static async Task AsTask<T>(this Task<T> item)
        {
            await item.ConfigureAwait(false);
        }
    }
}
