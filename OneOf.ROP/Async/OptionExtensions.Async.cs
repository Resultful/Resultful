using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OneOf.ROP.Utils;

namespace OneOf.ROP
{
    public static partial class Option
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
        public static Task<Option<TResult>> BindAsync<T, TResult>(this Task<Option<T>> value, Func<T, Option<TResult>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<Option<TResult>> BindAsync<T, TResult>(this Task<Option<T>> value, Func<T, Task<Option<TResult>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));

        //MapAsync on Option<T>
        public static Task<Option<TResult>> MapAsync<T, TResult>(this Task<Option<T>> value, Func<T, TResult> bindFunc)
            => value.WrapAsync(item2 => item2.Map(bindFunc));

        public static Task<Option<TResult>> MapAsync<T, TResult>(this Task<Option<T>> value, Func<T, Task<TResult>> bindFunc)
            => value.WrapAsync(item => item.MapAsync(bindFunc));

        //TeeAsync on Option<T>
        public static Task<Option<T>> TeeAsync<T>(this Task<Option<T>> value, Func<T, Task> asyncFunc)
            => value.WrapAsync(item => item.TeeAsync(asyncFunc));

        public static Task<Option<T>> TeeAsync<T>(this Task<Option<T>> value, Action<T> teeAction)
            => value.WrapAsync(item => item.Tee(teeAction));

        //OrAsync on Option<T>

        public static Task<T> OrAsync<T>(this Task<Option<T>> value, T otherValue)
            => value.WrapAsync(item => item.Or(otherValue));

        public static Task<T> OrAsync<T>(this Task<Option<T>> value, Task<T> otherValue)
            => value.WrapAsync(item => item.OrAsync(otherValue));

        public static Task<T> OrAsync<T>(this Task<Option<T>> value, Func<T> otherFunc)
            => value.WrapAsync(item => item.Or(otherFunc));

        public static Task<T> OrAsync<T>(this Task<Option<T>> value, Func<Task<T>> otherFunc)
            => value.WrapAsync(item => item.OrAsync(otherFunc));

        public static Task<Option<T>> OrAsync<T>(this Task<Option<T>> value, Option<T> other)
            => value.WrapAsync(item => item.Or(other));

        public static Task<Option<T>> OrAsync<T>(this Task<Option<T>> value, Task<Option<T>> other)
            => value.WrapAsync(item => item.OrAsync(other));

        public static Task<Option<T>> OrAsync<T>(this Task<Option<T>> value, Func<Option<T>> otherFunc)
            => value.WrapAsync(item => item.Or(otherFunc));

        public static Task<Option<T>> OrAsync<T>(this Task<Option<T>> value, Func<Task<Option<T>>> otherFunc)
            => value.WrapAsync(item => item.OrAsync(otherFunc));
    }
}
