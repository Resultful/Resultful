using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneOf.ROP
{
    public static class OptionExtensions
    {
        public static Option<T> Some<T>(this T value) => value;

        public static Option<TResult> Bind<T, TResult>(this Option<T> item, Func<T, Option<TResult>> bindFunc)
            => item.Match(bindFunc, none => none);

        public static Option<TResult> Map<T, TResult>(this Option<T> item, Func<T, TResult> bindFunc)
            => item.Match(value => bindFunc(value).Some(), none => none);

        public static Option<T> Tee<T>(this Option<T> item, Action<T> teeAction)
            => item.Map(value =>
            {
                teeAction(value);
                return value;
            });

        //Plus on Option<T>
        public static Option<(TLeft, TRight)> Plus<TLeft, TRight>(this Option<TLeft> left, Option<TRight> right)
            => left.Bind(leftValue => right.Map(rightValue => (leftValue, rightValue)));

        public static Option<T> Plus<T>(this Option<T> left, Option<T> right) where T: IPlus<T>
            => left.Bind(leftValue => right.Map(leftValue.Plus));

        public static Option<TResult> Plus<TLeft, TRight, TResult>(this Option<TLeft> left, Option<TRight> right, Func<TLeft, TRight, TResult> plusFunc)
            => left.Bind(leftValue => right.Map(rightValue => plusFunc(leftValue, rightValue)));

        //Fold on Option<T>
        public static Option<T> Fold<T>(this IEnumerable<Option<T>> values, Func<T, T, T> plusFunc)
            => values.Aggregate((seed, input) => seed.Plus(input, plusFunc));

        public static Option<T> Fold<T>(this IEnumerable<Option<T>> values) where T : IPlus<T>
            => values.Aggregate((seed, input) => seed.Plus<T>(input));

        public static Option<TResult> Fold<TResult, T>(this IEnumerable<Option<T>> values, Option<TResult> seed, Func<TResult, T, TResult> aggrFunc)
            => values.Aggregate(seed, (acc, value) => acc.Plus(value).Map(x =>
            {
                var (finalAcc, finalVal) = x;
                return aggrFunc(finalAcc, finalVal);
            }));

        public static Option<TResult> Fold<TResult, T>(this IEnumerable<Option<T>> values, TResult seed,
            Func<TResult, T, TResult> aggrFunc)
            => values.Fold(seed.Some(), aggrFunc);

        //Unroll on IEnumerable<Option<T>>
        public static Option<IEnumerable<T>> Unroll<T>(this IEnumerable<Option<T>> items)
            => items.Fold(new T[] { }.Some<IEnumerable<T>>(), (acc, item) => acc.Concat(new[] { item }));


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
            => Task.WhenAll(values.Select(x => x.UnwrapAsync()))
                .WrapAsync(value => value.Unroll());

        //BindAsync on Option<T>
        public static Task<Option<TResult>> BindAsync<T, TResult>(this Option<T> item, Func<T, Task<Option<TResult>>> bindFunc)
            => item.Match(bindFunc, none => Task.FromResult<Option<TResult>>(none));

        public static Task<Option<TResult>> BindAsync<T, TResult>(this Task<Option<T>> value, Func<T, Option<TResult>> bindFunc)
            => value.WrapAsync(item => item.Bind(bindFunc));

        public static Task<Option<TResult>> BindAsync<T, TResult>(this Task<Option<T>> value, Func<T, Task<Option<TResult>>> bindFunc)
            => value.WrapAsync(item => item.BindAsync(bindFunc));


        //MapAsync on Option<T>
        public static Task<Option<TResult>> MapAsync<T, TResult>(this Option<T> item, Func<T, Task<TResult>> bindFunc)
            => item.Match(
                async value => (await bindFunc(value).ConfigureAwait(false)).Some(),
                none => Task.FromResult<Option<TResult>>(none));

        public static Task<Option<TResult>> MapAsync<T, TResult>(this Task<Option<T>> item, Func<T, TResult> bindFunc)
            => item.WrapAsync(value => value.Map(bindFunc));

        public static Task<Option<TResult>> MapAsync<T, TResult>(this Task<Option<T>> item,
            Func<T, Task<TResult>> bindFunc)
            => item.WrapAsync(value => value.MapAsync(bindFunc));

        //TeeAsync on Option<T>
        public static Task<Option<T>> TeeAsync<T>(this Option<T> value, Func<T, Task> asyncFunc)
            => value.MapAsync(async x =>
            {
                await asyncFunc(x).ConfigureAwait(false);
                return x;
            });

        public static Task<Option<T>> TeeAsync<T>(this Task<Option<T>> item, Func<T, Task> asyncFunc)
            => item.WrapAsync(value => value.TeeAsync(asyncFunc));

        public static Task<Option<T>> TeeAsync<T>(this Task<Option<T>> item, Action<T> teeAction)
            => item.WrapAsync(value => value.Tee(teeAction));
    }
}
