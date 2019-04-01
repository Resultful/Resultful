using System;
using System.Collections.Generic;
using System.Linq;
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

        //Reduce on Option<T>
        public static Option<T> Reduce<T>(this IEnumerable<Option<T>> values, Func<T, T, T> plusFunc)
            => values.ThrowIfDefault(nameof(values)).Aggregate((seed, input) => seed.Plus(input, plusFunc));

        public static Option<T> Reduce<T>(this IEnumerable<Option<T>> values) where T : IPlus<T, T>
            => values.ThrowIfDefault(nameof(values)).Aggregate((seed, input) => seed.Plus<T, T, T>(input));


        //Fold on Option<T>
        public static Option<TResult> Fold<TResult, T>(this IEnumerable<Option<T>> values, Option<TResult> seed, Func<TResult, T, TResult> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).Aggregate(seed, (acc, value) => acc.Plus(value).Map(x =>
            {
                var (finalAcc, finalVal) = x;
                return aggrFunc.ThrowIfDefault(nameof(aggrFunc))(finalAcc, finalVal);
            }));

        public static Option<TResult> Fold<TResult, T>(this IEnumerable<Option<T>> values, TResult seed, Func<TResult, T, TResult> aggrFunc)
            => values.Fold(seed.Some(), aggrFunc);


        //FoldUntil on Option<T>
        public static TResult FoldUntil<TResult, T>(this IEnumerable<T> values, TResult seed,
            Func<TResult, T, Option<TResult>> aggrFunc)
        {
            using (var enumerator = values.GetEnumerator())
            {
                return InternalList.FoldUntilInternal(enumerator, seed, aggrFunc);
            }
        }

        //ReduceUntil on Option<T>
        public static T ReduceUntil<T>(this IEnumerable<T> values,
            Func<T, T, Option<T>> aggrFunc)
        {
            using (var enumerator = values.GetEnumerator())
            {
                return !enumerator.MoveNext()
                    ? throw new ArgumentException("Must have at least one item present", nameof(values))
                    : InternalList.FoldUntilInternal(enumerator, enumerator.Current, aggrFunc);
            }
        }

        //Internal Methods
        public static Option<T> TryReduceUntil<T>(this IEnumerable<T> values,
            Func<T, T, Option<T>> aggrFunc)
        {
            using (var enumerator = values.GetEnumerator())
            {
                return !enumerator.MoveNext()
                    ? Option<T>.None
                    : InternalList.FoldUntilInternal(enumerator, enumerator.Current, aggrFunc).Some();
            }
        }

        //Unroll on IEnumerable<Option<T>>
        public static Option<IEnumerable<T>> Unroll<T>(this IEnumerable<Option<T>> values)
            => values.Fold(EmptyArray<T>.Get.Some<IEnumerable<T>>(), (acc, item) => acc.Concat(new[] { item }));




        //ToOption
        public static Option<T> ToOption<T>(this T value)
            => value?.Some() ?? new None();

    }
}
