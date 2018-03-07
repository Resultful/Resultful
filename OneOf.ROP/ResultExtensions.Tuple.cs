using System;
using System.Collections.Generic;
using System.Linq;

namespace OneOf.ROP
{
    public static partial class AssertionExtensions
    {
        private static(T0, T1, T2)
            Unfold<T0, T1, T2>
                (this
                    ((T0, T1), T2)
                    item)
        {
            var (tuple, item2) = item;
            var (item0, item1) = tuple;
            return (item0, item1, item2);
        }

        public static Result<(T0, T1, T2), TError>
            Plus<T0, T1, T2, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Func<TError, TError, TError> mergeFunc)
                    => result0.Plus(result1, mergeFunc).Plus(result2, mergeFunc).Map(Unfold);

        public static Result<(T0, T1, T2)>
            Plus<T0, T1, T2>
                (this Result<T0> result0, Result<T1> result1, Result<T2> result2)
                    => result0.Plus(result1).Plus(result2).Map(Unfold);

        private static(T0, T1, T2, T3)
            Unfold<T0, T1, T2, T3>
                (this
                    ((T0, T1, T2), T3)
                    item)
        {
            var (tuple, item3) = item;
            var (item0, item1, item2) = tuple;
            return (item0, item1, item2, item3);
        }

        public static Result<(T0, T1, T2, T3), TError>
            Plus<T0, T1, T2, T3, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Func<TError, TError, TError> mergeFunc)
                    => result0.Plus(result1, result2, mergeFunc).Plus(result3, mergeFunc).Map(Unfold);

        public static Result<(T0, T1, T2, T3)>
            Plus<T0, T1, T2, T3>
                (this Result<T0> result0, Result<T1> result1, Result<T2> result2, Result<T3> result3)
                    => result0.Plus(result1, result2).Plus(result3).Map(Unfold);

        private static(T0, T1, T2, T3, T4)
            Unfold<T0, T1, T2, T3, T4>
                (this
                    ((T0, T1, T2, T3), T4)
                    item)
        {
            var (tuple, item4) = item;
            var (item0, item1, item2, item3) = tuple;
            return (item0, item1, item2, item3, item4);
        }

        public static Result<(T0, T1, T2, T3, T4), TError>
            Plus<T0, T1, T2, T3, T4, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Func<TError, TError, TError> mergeFunc)
                    => result0.Plus(result1, result2, result3, mergeFunc).Plus(result4, mergeFunc).Map(Unfold);

        public static Result<(T0, T1, T2, T3, T4)>
            Plus<T0, T1, T2, T3, T4>
                (this Result<T0> result0, Result<T1> result1, Result<T2> result2, Result<T3> result3, Result<T4> result4)
                    => result0.Plus(result1, result2, result3).Plus(result4).Map(Unfold);

        private static(T0, T1, T2, T3, T4, T5)
            Unfold<T0, T1, T2, T3, T4, T5>
                (this
                    ((T0, T1, T2, T3, T4), T5)
                    item)
        {
            var (tuple, item5) = item;
            var (item0, item1, item2, item3, item4) = tuple;
            return (item0, item1, item2, item3, item4, item5);
        }

        public static Result<(T0, T1, T2, T3, T4, T5), TError>
            Plus<T0, T1, T2, T3, T4, T5, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5, Func<TError, TError, TError> mergeFunc)
                    => result0.Plus(result1, result2, result3, result4, mergeFunc).Plus(result5, mergeFunc).Map(Unfold);

        public static Result<(T0, T1, T2, T3, T4, T5)>
            Plus<T0, T1, T2, T3, T4, T5>
                (this Result<T0> result0, Result<T1> result1, Result<T2> result2, Result<T3> result3, Result<T4> result4, Result<T5> result5)
                    => result0.Plus(result1, result2, result3, result4).Plus(result5).Map(Unfold);

        private static(T0, T1, T2, T3, T4, T5, T6)
            Unfold<T0, T1, T2, T3, T4, T5, T6>
                (this
                    ((T0, T1, T2, T3, T4, T5), T6)
                    item)
        {
            var (tuple, item6) = item;
            var (item0, item1, item2, item3, item4, item5) = tuple;
            return (item0, item1, item2, item3, item4, item5, item6);
        }

        public static Result<(T0, T1, T2, T3, T4, T5, T6), TError>
            Plus<T0, T1, T2, T3, T4, T5, T6, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5, Result<T6, TError> result6, Func<TError, TError, TError> mergeFunc)
                    => result0.Plus(result1, result2, result3, result4, result5, mergeFunc).Plus(result6, mergeFunc).Map(Unfold);

        public static Result<(T0, T1, T2, T3, T4, T5, T6)>
            Plus<T0, T1, T2, T3, T4, T5, T6>
                (this Result<T0> result0, Result<T1> result1, Result<T2> result2, Result<T3> result3, Result<T4> result4, Result<T5> result5, Result<T6> result6)
                    => result0.Plus(result1, result2, result3, result4, result5).Plus(result6).Map(Unfold);

        private static(T0, T1, T2, T3, T4, T5, T6, T7)
            Unfold<T0, T1, T2, T3, T4, T5, T6, T7>
                (this
                    ((T0, T1, T2, T3, T4, T5, T6), T7)
                    item)
        {
            var (tuple, item7) = item;
            var (item0, item1, item2, item3, item4, item5, item6) = tuple;
            return (item0, item1, item2, item3, item4, item5, item6, item7);
        }

        public static Result<(T0, T1, T2, T3, T4, T5, T6, T7), TError>
            Plus<T0, T1, T2, T3, T4, T5, T6, T7, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5, Result<T6, TError> result6, Result<T7, TError> result7, Func<TError, TError, TError> mergeFunc)
                    => result0.Plus(result1, result2, result3, result4, result5, result6, mergeFunc).Plus(result7, mergeFunc).Map(Unfold);

        public static Result<(T0, T1, T2, T3, T4, T5, T6, T7)>
            Plus<T0, T1, T2, T3, T4, T5, T6, T7>
                (this Result<T0> result0, Result<T1> result1, Result<T2> result2, Result<T3> result3, Result<T4> result4, Result<T5> result5, Result<T6> result6, Result<T7> result7)
                    => result0.Plus(result1, result2, result3, result4, result5, result6).Plus(result7).Map(Unfold);

    }
}
