using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using OneOf.Types;
using Resultful.Utils;

namespace Resultful
{
    /// <summary>Builders and extension methods for <c>Option&lt;T&gt;</c>, for tasks of options and for sequences of options.</summary>
    /// <remarks>Arguments must not be null unless their documentation says otherwise. A null delegate, task or sequence throws <see cref="System.ArgumentNullException"/> or <see cref="System.NullReferenceException"/>, depending on the member, and some members only fail when they would use it. Exceptions thrown by a delegate are not caught.</remarks>
    public static partial class Option
    {
        /// <summary>Returns <paramref name="value"/> as an option with a value.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value.</param>
        /// <remarks>A <c>null</c> value still gives an option with a value. Use <c>ToOption</c> to turn <c>null</c> into None.</remarks>
        public static Option<T> Some<T>(this T value) => value;

        //Plus on Option<T>
        /// <summary>Returns an option with both values as a tuple if both have a value, otherwise None.</summary>
        /// <typeparam name="TLeft">The type of the left value.</typeparam>
        /// <typeparam name="TRight">The type of the right value.</typeparam>
        /// <param name="left">The first option.</param>
        /// <param name="right">The second option.</param>
        public static Option<(TLeft, TRight)> Plus<TLeft, TRight>(this Option<TLeft> left, Option<TRight> right)
            => left.Bind(leftValue => right.Map(rightValue => (leftValue, rightValue)));

        /// <summary>Returns an option with the left value's <c>Plus</c> of the right value if both have a value, otherwise None.</summary>
        /// <typeparam name="TLeft">The type of the left value.</typeparam>
        /// <typeparam name="TRight">The type of the right value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="left">The first option.</param>
        /// <param name="right">The second option.</param>
        public static Option<TResult> Plus<TLeft, TRight, TResult>(this Option<TLeft> left, Option<TRight> right) where TLeft : IPlus<TRight, TResult>
            => left.Bind(leftValue => right.Map(leftValue.Plus));

        /// <summary>Returns an option with the two values combined by <paramref name="plusFunc"/> if both have a value, otherwise None.</summary>
        /// <typeparam name="TLeft">The type of the left value.</typeparam>
        /// <typeparam name="TRight">The type of the right value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="left">The first option.</param>
        /// <param name="right">The second option.</param>
        /// <param name="plusFunc">Combines the two values.</param>
        public static Option<TResult> Plus<TLeft, TRight, TResult>(this Option<TLeft> left, Option<TRight> right, Func<TLeft, TRight, TResult> plusFunc)
            => left.Bind(leftValue => right.Map(rightValue => plusFunc.ThrowIfDefault(nameof(plusFunc))(leftValue, rightValue)));

        //Flatten on Option<Option<T>>
        /// <summary>Returns the inner option, or None if the outer option is None.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The option.</param>
        public static Option<T> Flatten<T>(this Option<Option<T>> value)
            => value.Match(Result.Id, _ => Option<T>.None);

        //Unroll on IEnumerable<Option<T>>
        /// <summary>Returns the values of the options that have one, in order, as an option with a value. None items are skipped, so the result always has a value (empty if no option has one).</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        public static Option<IEnumerable<T>> Unroll<T>(this IEnumerable<Option<T>> values)
            => values.Fold(EmptyArray<T>.Get.Some<IEnumerable<T>>(), (acc, item) => acc.Concat(new[] { item }));

        //ToOption
        /// <summary>Returns None if <paramref name="value"/> is <c>null</c>, or an option with the value otherwise.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value.</param>
        public static Option<T> ToOption<T>(this T value)
            => value?.Some() ?? new None();

        //Reduce on List<Option<T>>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. Returns None if any item is None.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Option<T> Reduce<T>(this IEnumerable<Option<T>> values, Func<T, T, T> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.ReduceInternal(enumerator, (acc, val) => AggrHelper.Func(acc, val, aggrFunc));
            }
        }

        /// <summary>Combines the values in order with <typeparamref name="T"/>'s <c>Plus</c>. Returns None if any item is None.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Option<T> Reduce<T>(this IEnumerable<Option<T>> values) where T : IPlus<T, T>
            => values.ThrowIfDefault(nameof(values)).Reduce((x, y) => x.Plus(y));

        //ReduceUntil on List<Option<T>>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. Stops at the first None item, keeping what was combined so far; if <paramref name="aggrFunc"/> returns None, the result is None.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Option<T> ReduceUntil<T>(this IEnumerable<Option<T>> values,
            Func<T, T, Option<T>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.ReduceUntilInternal(enumerator,
                    (acc, val) => AggrHelper.FuncUntil(acc, val, aggrFunc));
            }
        }

        //Fold on List<Option<T>>
        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. None items are skipped.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static TResult Fold<TResult, T>(this IEnumerable<Option<T>> values, TResult seed,
            Func<TResult, T, TResult> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.FoldInternal(enumerator, seed, (acc, val) => val.Fold(acc, aggrFunc));
            }
        }

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. None items are skipped.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="seed">The starting value. If it is None, the items are not read.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Option<TResult> Fold<TResult, T>(this IEnumerable<Option<T>> values, Option<TResult> seed,
            Func<TResult, T, TResult> aggrFunc)
            => seed.Map(x => Fold(values, x, aggrFunc));

        //FoldUntil on List<Option<T>>
        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>, skipping None items, and stopping when it returns None. Returns the last combined value.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static TResult FoldUntil<TResult, T>(this IEnumerable<Option<T>> values, TResult seed,
            Func<TResult, T, Option<TResult>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.FoldUntilInternal(enumerator, seed, (acc, val) => val.FoldUntil(acc, aggrFunc));
            }
        }

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>, skipping None items, and stopping when it returns None. Returns the last combined value.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="seed">The starting value. If it is None, the items are not read.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Option<TResult> FoldUntil<TResult, T>(this IEnumerable<Option<T>> values, Option<TResult> seed,
            Func<TResult, T, Option<TResult>> aggrFunc)
            => seed.Map(x => FoldUntil(values, x, aggrFunc));


        //TryReduceUntil on List<Option<T>>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. Returns None if any item is None, or if there are no items.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Option<T> TryReduce<T>(this IEnumerable<Option<T>> values,
            Func<T, T, T> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.TryReduceInternal(enumerator,
                    (acc, val) => AggrHelper.Func(acc, val, aggrFunc)).Flatten();
            }
        }

        //TryReduceUntil on List<Option<T>>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. Stops at the first None item, keeping what was combined so far; if <paramref name="aggrFunc"/> returns None, the result is None. Returns None if there are no items.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The options.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Option<T> TryReduceUntil<T>(this IEnumerable<Option<T>> values,
            Func<T, T, Option<T>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.TryReduceUntilInternal(enumerator,
                    (acc, val) => AggrHelper.FuncUntil(acc, val, aggrFunc)).Flatten();
            }
        }

    }
}
