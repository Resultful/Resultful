using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using OneOf.Types;
using Resultful.Utils;

namespace Resultful
{
    public static partial class Option
    {
        public static Option<T> Some<T>(this T value) => value;

        //Plus on Option<T>
        public static Option<(TLeft, TRight)> Plus<TLeft, TRight>(this Option<TLeft> left, Option<TRight> right)
            => left.Bind(leftValue => right.Map(rightValue => (leftValue, rightValue)));

        public static Option<TResult> Plus<TLeft, TRight, TResult>(this Option<TLeft> left, Option<TRight> right) where TLeft : IPlus<TRight, TResult>
            => left.Bind(leftValue => right.Map(leftValue.Plus));

        public static Option<TResult> Plus<TLeft, TRight, TResult>(this Option<TLeft> left, Option<TRight> right, Func<TLeft, TRight, TResult> plusFunc)
            => left.Bind(leftValue => right.Map(rightValue => plusFunc.ThrowIfDefault(nameof(plusFunc))(leftValue, rightValue)));

        //Flatten on Option<Option<T>>
        public static Option<T> Flatten<T>(this Option<Option<T>> value)
            => value.Match(Result.Id, _ => Option<T>.None);

        //Unroll on IEnumerable<Option<T>>
        public static Option<IEnumerable<T>> Unroll<T>(this IEnumerable<Option<T>> values)
            => values.Fold(EmptyArray<T>.Get.Some<IEnumerable<T>>(), (acc, item) => acc.Concat(new[] { item }));

        //ToOption
        public static Option<T> ToOption<T>(this T value)
            => value?.Some() ?? new None();

        //Reduce on List<Option<T>>
        public static Option<T> Reduce<T>(this IEnumerable<Option<T>> values, Func<T, T, T> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.ReduceInternal(enumerator, (acc, val) => AggrHelper.Func(acc, val, aggrFunc));
            }
        }

        public static Option<T> Reduce<T>(this IEnumerable<Option<T>> values) where T : IPlus<T, T>
            => values.ThrowIfDefault(nameof(values)).Reduce((x, y) => x.Plus(y));

        //ReduceUntil on List<Option<T>>
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
        public static TResult Fold<TResult, T>(this IEnumerable<Option<T>> values, TResult seed,
            Func<TResult, T, TResult> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.FoldInternal(enumerator, seed, (acc, val) => val.Fold(acc, aggrFunc));
            }
        }

        public static Option<TResult> Fold<TResult, T>(this IEnumerable<Option<T>> values, Option<TResult> seed,
            Func<TResult, T, TResult> aggrFunc)
            => seed.Map(x => Fold(values, x, aggrFunc));

        //FoldUntil on List<Option<T>>
        public static TResult FoldUntil<TResult, T>(this IEnumerable<Option<T>> values, TResult seed,
            Func<TResult, T, Option<TResult>> aggrFunc)
        {
            using (var enumerator = values.ThrowIfDefault(nameof(values)).GetEnumerator())
            {
                return InternalList.FoldUntilInternal(enumerator, seed, (acc, val) => val.FoldUntil(acc, aggrFunc));
            }
        }

        public static Option<TResult> FoldUntil<TResult, T>(this IEnumerable<Option<T>> values, Option<TResult> seed,
            Func<TResult, T, Option<TResult>> aggrFunc)
            => seed.Map(x => FoldUntil(values, x, aggrFunc));


        //TryReduceUntil on List<Option<T>>
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
