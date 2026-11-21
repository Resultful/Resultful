using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resultful.Utils;
using TaskExt;

namespace Resultful
{
    public static partial class Option
    {
        //Builder for Option<T> from Task<T> value
        /// <summary>Awaits <paramref name="value"/>, then returns its value as an option with a value.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the value.</param>
        public static Task<Option<T>> Some<T>(this Task<T> value)
            => value.Map(item => item.Some());

        //Switch on Task<Option<T>>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="someFunc"/> with the value if there is one, or <paramref name="noneFunc"/> if the option is None.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="someFunc">Called with the value.</param>
        /// <param name="noneFunc">Called if the option is None.</param>
        public static Task Switch<T>(this Task<Option<T>> value, Action<T> someFunc, Action<None> noneFunc)
            => value.Discard(item => item.Switch(
                someFunc.ThrowIfDefault(nameof(someFunc)),
                noneFunc.ThrowIfDefault(nameof(noneFunc))));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="someFunc"/> with the value if there is one, or <paramref name="noneFunc"/> if the option is None, and returns a task that completes when the call's task does.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="someFunc">Called with the value.</param>
        /// <param name="noneFunc">Called if the option is None.</param>
        public static Task SwitchAsync<T>(this Task<Option<T>> value, Func<T, Task> someFunc, Func<None, Task> noneFunc)
            => value.DiscardAsync(item => item.Match(
                someFunc.ThrowIfDefault(nameof(someFunc)),
                noneFunc.ThrowIfDefault(nameof(noneFunc))));

        //Match on Task<Option<T>>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="someFunc"/> with the value if there is one, or <paramref name="noneFunc"/> if the option is None, and returns its task.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="someFunc">Called with the value.</param>
        /// <param name="noneFunc">Called if the option is None.</param>
        public static Task<TResult> MatchAsync<T, TResult>(this Task<Option<T>> value, Func<T, Task<TResult>> someFunc, Func<None, Task<TResult>> noneFunc)
            => value.Bind(item => item.Match(
                someFunc.ThrowIfDefault(nameof(someFunc)),
                noneFunc.ThrowIfDefault(nameof(noneFunc))));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="someFunc"/> with the value if there is one, or <paramref name="noneFunc"/> if the option is None, and returns what it returns.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="someFunc">Called with the value.</param>
        /// <param name="noneFunc">Called if the option is None.</param>
        public static Task<TResult> Match<T, TResult>(this Task<Option<T>> value, Func<T, TResult> someFunc, Func<None, TResult> noneFunc)
            => value.Map(item => item.Match(
                someFunc.ThrowIfDefault(nameof(someFunc)),
                noneFunc.ThrowIfDefault(nameof(noneFunc))));

        //Unwrap on Option<Task<T>>
        /// <summary>If the option has a value, awaits the task it holds and returns the task's value as an option. None stays None.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The option.</param>
        public static Task<Option<T>> UnwrapAsync<T>(this Option<Task<T>> value)
            => value.Match(
                async item => (await item.ConfigureAwait(false)).Some(),
                none => Task.FromResult<Option<T>>(none));

        //WhenAll on IEnumerable<Task<Option<T>>>
        /// <summary>Awaits the tasks held by the options that have a value, together, and returns their values in order as an option with a value. None items are skipped, so the result always has a value (empty if no option has one).</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        public static Task<Option<IEnumerable<T>>> WhenAll<T>(this IEnumerable<Option<Task<T>>> values)
            => Task.WhenAll(values.ThrowIfDefault(nameof(values)).Select(x => x.UnwrapAsync()))
                .Map(value => value.Unroll());

        //BindAsync on Option<T>
        /// <summary>Awaits <paramref name="value"/>, then: if the option has a value, returns what <paramref name="bindFunc"/> returns for it. None stays None and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="bindFunc">Returns the next option from the value.</param>
        public static Task<Option<TResult>> Bind<T, TResult>(this Task<Option<T>> value, Func<T, Option<TResult>> bindFunc)
            => value.Map(item => item.Bind(bindFunc));

        /// <summary>Awaits <paramref name="value"/>, then: if the option has a value, returns what <paramref name="bindFunc"/> returns for it. None stays None and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="bindFunc">Returns the next option from the value.</param>
        public static Task<Option<TResult>> BindAsync<T, TResult>(this Task<Option<T>> value, Func<T, Task<Option<TResult>>> bindFunc)
            => value.Bind(item => item.BindAsync(bindFunc));

        //MapAsync on Option<T>
        /// <summary>Awaits <paramref name="value"/>, then: if the option has a value, returns an option with the value <paramref name="bindFunc"/> gives for it. None stays None.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="bindFunc">Maps the value.</param>
        public static Task<Option<TResult>> Map<T, TResult>(this Task<Option<T>> value, Func<T, TResult> bindFunc)
            => value.Map(item2 => item2.Map(bindFunc));

        /// <summary>Awaits <paramref name="value"/>, then: if the option has a value, returns an option with the value <paramref name="bindFunc"/> gives for it. None stays None.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="bindFunc">Maps the value.</param>
        public static Task<Option<TResult>> MapAsync<T, TResult>(this Task<Option<T>> value, Func<T, Task<TResult>> bindFunc)
            => value.Bind(item => item.MapAsync(bindFunc));

        //TeeAsync on Option<T>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="asyncFunc"/> with the value if there is one, awaits it, and returns the option unchanged.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="asyncFunc">Called with the value.</param>
        public static Task<Option<T>> TeeAsync<T>(this Task<Option<T>> value, Func<T, Task> asyncFunc)
            => value.Bind(item => item.TeeAsync(asyncFunc));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="teeAction"/> with the value if there is one, and returns the option unchanged.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="teeAction">Called with the value.</param>
        public static Task<Option<T>> Tee<T>(this Task<Option<T>> value, Action<T> teeAction)
            => value.Map(item => item.Tee(teeAction));

        //FoldAsync on Option<T>
        /// <summary>Awaits <paramref name="value"/>, then: if the option has a value, returns what <paramref name="func"/> returns for <paramref name="seed"/> and the value. Otherwise returns <paramref name="seed"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="func">Combines the seed with the value.</param>
        public static Task<TResult> Fold<T, TResult>(this Task<Option<T>> value, TResult seed, Func<TResult, T, TResult> func)
            => value.Map(x => x.Fold(seed, func));

        /// <summary>Awaits <paramref name="value"/>, then: if the option has a value, returns what <paramref name="func"/> returns for <paramref name="seed"/> and the value. Otherwise returns <paramref name="seed"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="func">Combines the seed with the value.</param>
        public static Task<TResult> FoldAsync<T, TResult>(this Task<Option<T>> value, TResult seed, Func<TResult, T, Task<TResult>> func)
            => value.Bind(x => x.FoldAsync(seed, func));

        //FoldUntilAsync on Option<T>
        /// <summary>Awaits <paramref name="value"/>, then: if the option has a value, returns what <paramref name="func"/> returns for <paramref name="seed"/> and the value. Otherwise returns <paramref name="seed"/> as an option with a value.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="func">Combines the seed with the value.</param>
        public static Task<Option<TResult>> FoldUntil<T, TResult>(this Task<Option<T>> value, TResult seed, Func<TResult, T, Option<TResult>> func)
            => value.Map(x => x.FoldUntil(seed, func));

        /// <summary>Awaits <paramref name="value"/>, then: if the option has a value, returns what <paramref name="func"/> returns for <paramref name="seed"/> and the value. Otherwise returns <paramref name="seed"/> as an option with a value.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="func">Combines the seed with the value.</param>
        public static Task<Option<TResult>> FoldUntilAsync<T, TResult>(this Task<Option<T>> value, TResult seed, Func<TResult, T, Task<Option<TResult>>> func)
            => value.Bind(x => x.FoldUntilAsync(seed, func));


        //OrAsync on Option<T>

        /// <summary>Awaits <paramref name="value"/>, then returns the value if there is one, otherwise <paramref name="otherValue"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="otherValue">Used if the option is None.</param>
        public static Task<T> Or<T>(this Task<Option<T>> value, T otherValue)
            => value.Map(item => item.Or(otherValue));

        /// <summary>Awaits <paramref name="value"/>, then returns the value if there is one, otherwise the value <paramref name="otherValue"/> gives.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="otherValue">Awaited only if the option is None. It runs either way.</param>
        public static Task<T> OrAsync<T>(this Task<Option<T>> value, Task<T> otherValue)
            => value.Bind(item => item.OrAsync(otherValue));

        /// <summary>Awaits <paramref name="value"/>, then returns the value if there is one, otherwise what <paramref name="otherFunc"/> returns.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="otherFunc">Called only if the option is None.</param>
        public static Task<T> Or<T>(this Task<Option<T>> value, Func<T> otherFunc)
            => value.Map(item => item.Or(otherFunc));

        /// <summary>Awaits <paramref name="value"/>, then returns the value if there is one, otherwise what <paramref name="otherFunc"/> returns.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="otherFunc">Called only if the option is None.</param>
        public static Task<T> OrAsync<T>(this Task<Option<T>> value, Func<Task<T>> otherFunc)
            => value.Bind(item => item.OrAsync(otherFunc));

        /// <summary>Awaits <paramref name="value"/>, then returns the option if it has a value, otherwise <paramref name="other"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="other">Used if the option is None.</param>
        public static Task<Option<T>> Or<T>(this Task<Option<T>> value, Option<T> other)
            => value.Map(item => item.Or(other));

        /// <summary>Awaits <paramref name="value"/>, then returns the option if it has a value, otherwise the option <paramref name="other"/> gives.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="other">Awaited only if the option is None. It runs either way.</param>
        public static Task<Option<T>> OrAsync<T>(this Task<Option<T>> value, Task<Option<T>> other)
            => value.Bind(item => item.OrAsync(other));

        /// <summary>Awaits <paramref name="value"/>, then returns the option if it has a value, otherwise what <paramref name="otherFunc"/> returns.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="otherFunc">Called only if the option is None.</param>
        public static Task<Option<T>> Or<T>(this Task<Option<T>> value, Func<Option<T>> otherFunc)
            => value.Map(item => item.Or(otherFunc));

        /// <summary>Awaits <paramref name="value"/>, then returns the option if it has a value, otherwise what <paramref name="otherFunc"/> returns.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        /// <param name="otherFunc">Called only if the option is None.</param>
        public static Task<Option<T>> OrAsync<T>(this Task<Option<T>> value, Func<Task<Option<T>>> otherFunc)
            => value.Bind(item => item.OrAsync(otherFunc));

        //Flatten
        /// <summary>Awaits <paramref name="value"/>, then returns the inner option, or None if the outer option is None.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        public static Task<Option<T>> Flatten<T>(this Task<Option<Option<T>>> value)
            => value.Map(x => x.Flatten());

        //ToOptionAsync
        /// <summary>Awaits <paramref name="value"/>, then returns None if its value is <c>null</c>, or an option with the value otherwise.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the value.</param>
        public static Task<Option<T>> ToOption<T>(this Task<T> value)
            => value.Map(item => item.ToOption());

        //Reduce on List<Option<T>>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. Returns None if any item is None.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static async Task<Option<T>> ReduceAsync<T>(this IEnumerable<Option<T>> values, Func<T, T, Task<T>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.ReduceInternalAsync(enumerator,
                    (acc, val) => AggrHelper.FuncAsync(acc, val, aggrFunc))
                    .ConfigureAwait(false);
            }
        }

        //ReduceUntil on List<Option<T>>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. Stops at the first None item, keeping what was combined so far; if <paramref name="aggrFunc"/> returns None, the result is None.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static async Task<Option<T>> ReduceUntil<T>(this IEnumerable<Option<T>> values,
            Func<T, T, Task<Option<T>>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.ReduceUntilInternalAsync(enumerator,
                    (acc, val) => AggrHelper.FuncUntilAsync(acc, val, aggrFunc))
                    .ConfigureAwait(false);
            }
        }

        //Fold on List<Option<T>>
        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. None items are skipped.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static async Task<TResult> FoldAsync<TResult, T>(this IEnumerable<Option<T>> values, TResult seed,
            Func<TResult, T, Task<TResult>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.FoldInternalAsync(enumerator, seed,
                    (acc, val) => val.FoldAsync(acc, aggrFunc))
                    .ConfigureAwait(false);
            }
        }

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. None items are skipped.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="seed">The starting value. If it is None, the items are not read.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Task<Option<TResult>> FoldAsync<TResult, T>(this IEnumerable<Option<T>> values, Option<TResult> seed,
            Func<TResult, T, Task<TResult>> aggrFunc)
            => seed.MapAsync(x => FoldAsync(values.ThrowIfDefault(nameof(values)), x, aggrFunc));

        //FoldUntil on List<Option<T>>
        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>, skipping None items, and stopping when it returns None. Returns the last combined value.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static async Task<TResult> FoldUntilAsync<TResult, T>(this IEnumerable<Option<T>> values, TResult seed,
            Func<TResult, T, Task<Option<TResult>>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.FoldUntilInternalAsync(enumerator, seed,
                    (acc, val) => val.FoldUntilAsync(acc, aggrFunc))
                    .ConfigureAwait(false);
            }
        }

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>, skipping None items, and stopping when it returns None. Returns the last combined value.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="seed">The starting value. If it is None, the items are not read.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Task<Option<TResult>> FoldUntilAsync<TResult, T>(this IEnumerable<Option<T>> values, Option<TResult> seed,
            Func<TResult, T, Task<Option<TResult>>> aggrFunc)
            => seed.MapAsync(x => FoldUntilAsync(values.ThrowIfDefault(nameof(values)), x, aggrFunc));


        //TryReduceUntil on List<Option<T>>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. Returns None if any item is None, or if there are no items.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static async Task<Option<T>> TryReduceAsync<T>(this IEnumerable<Option<T>> values,
            Func<T, T, Task<T>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.TryReduceInternalAsync(enumerator,
                    (acc, val) => AggrHelper.FuncAsync(acc, val, aggrFunc))
                    .Flatten().ConfigureAwait(false);
            }
        }

        //TryReduceUntil on List<Option<T>>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. Stops at the first None item, keeping what was combined so far; if <paramref name="aggrFunc"/> returns None, the result is None. Returns None if there are no items.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static async Task<Option<T>> TryReduceUntilAsync<T>(this IEnumerable<Option<T>> values,
            Func<T, T, Task<Option<T>>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return await InternalList.TryReduceUntilInternalAsync(enumerator,
                    (acc, val) => AggrHelper.FuncUntilAsync(acc, val, aggrFunc))
                    .Flatten().ConfigureAwait(false);
            }
        }


    }
}
