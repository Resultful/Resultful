using System;
using System.Threading.Tasks;

namespace TaskExt
{
    internal static class TaskExtensions
    {
        private static T ThrowIfDefault<T>(this T value, string argName) where T : class
            => value ?? throw new ArgumentNullException(argName);

        internal static async Task<TResult> Map<T, TResult>(this Task<T> value, Func<T, TResult> helperFunc)
            => helperFunc.ThrowIfDefault(nameof(helperFunc))
                (await value.ThrowIfDefault(nameof(value)).ConfigureAwait(false));

        internal static async Task<TResult> Bind<T, TResult>(this Task<T> value, Func<T, Task<TResult>> helperFunc)
            => await helperFunc.ThrowIfDefault(nameof(helperFunc))
                (await value.ThrowIfDefault(nameof(value)).ConfigureAwait(false)).ConfigureAwait(false);

        internal static async Task<T> Tee<T>(this Task<T> value, Func<T, Task> helperFunc)
        {
            var result = await value.ThrowIfDefault(nameof(value)).ConfigureAwait(false);
            await helperFunc.ThrowIfDefault(nameof(helperFunc))(result).ConfigureAwait(false);
            return result;
        }

        internal static async Task DiscardAsync<T>(this Task<T> value, Func<T, Task> helperFunc)
        {
            var result = await value.ThrowIfDefault(nameof(value)).ConfigureAwait(false);
            await helperFunc.ThrowIfDefault(nameof(helperFunc))(result).ConfigureAwait(false);
        }

        internal static async Task Discard<T>(this Task<T> value, Action<T> helperFunc)
        {
            var result = await value.ThrowIfDefault(nameof(value)).ConfigureAwait(false);
            helperFunc.ThrowIfDefault(nameof(helperFunc))(result);
        }

        internal static async Task AsTask<T>(this Task<T> value)
        {
            await value.ThrowIfDefault(nameof(value)).ConfigureAwait(false);
        }
    }
}
