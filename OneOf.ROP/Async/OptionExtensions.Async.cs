using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OneOf.ROP.Utils;

namespace OneOf.ROP.Async
{
    public static class OptionExtensions
    {


        //Builder for Option<T> from Task<T> value
        public static Task<Option<T>> Some<T>(this Task<T> value)
            => value.WrapAsync(item => item.Some());

        //Unwrap on Option<Task<T>>
        public static Task<Option<T>> UnwrapAsync<T>(this Option<Task<T>> value)
            => value.Match(
                async item => (await item.ConfigureAwait(false)).Some(),
                none => Task.FromResult<Option<T>>(none));

        //WhenAll on IEnumerable<Task<Option<T>>>
        public static Task<Option<IEnumerable<T>>> WhenAll<T>(this IEnumerable<Option<Task<T>>> values)
            => Task.WhenAll(values.ThrowIfDefault(nameof(values)).Select(x => x.UnwrapAsync()))
                .WrapAsync(value => value.Unroll());

        //BindAsync on Option<T>
        public static Task<Option<TResult>> BindAsync<T, TResult>(this Option<T> value, Func<T, Task<Option<TResult>>> bindFunc)
            => value.Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), none => Task.FromResult<Option<TResult>>(none));

        public static Task<Option<TResult>> BindAsync<T, TResult>(this Task<Option<T>> value, Func<T, Option<TResult>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<Option<TResult>> BindAsync<T, TResult>(this Task<Option<T>> value, Func<T, Task<Option<TResult>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));


        //MapAsync on Option<T>
        public static Task<Option<TResult>> MapAsync<T, TResult>(this Option<T> value, Func<T, Task<TResult>> bindFunc)
            => value.Match(
                async item2 => (await bindFunc.ThrowIfDefault(nameof(bindFunc))(item2).ConfigureAwait(false)).Some(),
                none => Task.FromResult<Option<TResult>>(none));

        public static Task<Option<TResult>> MapAsync<T, TResult>(this Task<Option<T>> value, Func<T, TResult> bindFunc)
            => value.WrapAsync(item2 => item2.Map(bindFunc));

        public static Task<Option<TResult>> MapAsync<T, TResult>(this Task<Option<T>> value, Func<T, Task<TResult>> bindFunc)
            => value.WrapAsync(item2 => item2.MapAsync(bindFunc));

        //TeeAsync on Option<T>
        public static Task<Option<T>> TeeAsync<T>(this Option<T> value, Func<T, Task> asyncFunc)
            => value.MapAsync(async x =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(x).ConfigureAwait(false);
                return x;
            });

        public static Task<Option<T>> TeeAsync<T>(this Task<Option<T>> value, Func<T, Task> asyncFunc)
            => value.WrapAsync(item => item.TeeAsync(asyncFunc));

        public static Task<Option<T>> TeeAsync<T>(this Task<Option<T>> value, Action<T> teeAction)
            => value.WrapAsync(item => item.Tee(teeAction));
    }
}
