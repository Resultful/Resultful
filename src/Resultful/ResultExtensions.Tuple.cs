using System;
using Resultful.Utils;

namespace Resultful
{
    public static partial class Result
    {
        public static Result<(T0, T1, T2), TError>
            Plus<T0, T1, T2, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Func<TError, TError, TError> mergeFunc)
                    => Result.Plus(result0, Result.Plus(result1, result2, mergeFunc.ThrowIfDefault(nameof(mergeFunc))), mergeFunc.ThrowIfDefault(nameof(mergeFunc))).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2), TError>
            Plus<T0, T1, T2, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2) where TError : IPlus<TError, TError>
                    => Result.Plus(result0, Result.Plus(result1, result2)).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2)>
            Plus<T0, T1, T2>
                (this Result<T0> result0, Result<T1> result1, Result<T2> result2)
                    => Result.Plus(result0, Result.Plus(result1, result2)).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3), TError>
            Plus<T0, T1, T2, T3, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Func<TError, TError, TError> mergeFunc)
                    => Result.Plus(Result.Plus(result0, result1, mergeFunc.ThrowIfDefault(nameof(mergeFunc))), Result.Plus(result2, result3, mergeFunc.ThrowIfDefault(nameof(mergeFunc))), mergeFunc.ThrowIfDefault(nameof(mergeFunc))).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3), TError>
            Plus<T0, T1, T2, T3, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3) where TError : IPlus<TError, TError>
                    => Result.Plus(Result.Plus(result0, result1), Result.Plus(result2, result3)).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3)>
            Plus<T0, T1, T2, T3>
                (this Result<T0> result0, Result<T1> result1, Result<T2> result2, Result<T3> result3)
                    => Result.Plus(Result.Plus(result0, result1), Result.Plus(result2, result3)).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3, T4), TError>
            Plus<T0, T1, T2, T3, T4, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Func<TError, TError, TError> mergeFunc)
                    => Result.Plus(Result.Plus(result0, result1, mergeFunc.ThrowIfDefault(nameof(mergeFunc))), Result.Plus(result2, result3, result4, mergeFunc.ThrowIfDefault(nameof(mergeFunc))), mergeFunc.ThrowIfDefault(nameof(mergeFunc))).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3, T4), TError>
            Plus<T0, T1, T2, T3, T4, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4) where TError : IPlus<TError, TError>
                    => Result.Plus(Result.Plus(result0, result1), Result.Plus(result2, result3, result4)).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3, T4)>
            Plus<T0, T1, T2, T3, T4>
                (this Result<T0> result0, Result<T1> result1, Result<T2> result2, Result<T3> result3, Result<T4> result4)
                    => Result.Plus(Result.Plus(result0, result1), Result.Plus(result2, result3, result4)).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3, T4, T5), TError>
            Plus<T0, T1, T2, T3, T4, T5, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5, Func<TError, TError, TError> mergeFunc)
                    => Result.Plus(Result.Plus(result0, result1, result2, mergeFunc.ThrowIfDefault(nameof(mergeFunc))), Result.Plus(result3, result4, result5, mergeFunc.ThrowIfDefault(nameof(mergeFunc))), mergeFunc.ThrowIfDefault(nameof(mergeFunc))).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3, T4, T5), TError>
            Plus<T0, T1, T2, T3, T4, T5, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5) where TError : IPlus<TError, TError>
                    => Result.Plus(Result.Plus(result0, result1, result2), Result.Plus(result3, result4, result5)).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3, T4, T5)>
            Plus<T0, T1, T2, T3, T4, T5>
                (this Result<T0> result0, Result<T1> result1, Result<T2> result2, Result<T3> result3, Result<T4> result4, Result<T5> result5)
                    => Result.Plus(Result.Plus(result0, result1, result2), Result.Plus(result3, result4, result5)).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3, T4, T5, T6), TError>
            Plus<T0, T1, T2, T3, T4, T5, T6, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5, Result<T6, TError> result6, Func<TError, TError, TError> mergeFunc)
                    => Result.Plus(Result.Plus(result0, result1, result2, mergeFunc.ThrowIfDefault(nameof(mergeFunc))), Result.Plus(result3, result4, result5, result6, mergeFunc.ThrowIfDefault(nameof(mergeFunc))), mergeFunc.ThrowIfDefault(nameof(mergeFunc))).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3, T4, T5, T6), TError>
            Plus<T0, T1, T2, T3, T4, T5, T6, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5, Result<T6, TError> result6) where TError : IPlus<TError, TError>
                    => Result.Plus(Result.Plus(result0, result1, result2), Result.Plus(result3, result4, result5, result6)).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3, T4, T5, T6)>
            Plus<T0, T1, T2, T3, T4, T5, T6>
                (this Result<T0> result0, Result<T1> result1, Result<T2> result2, Result<T3> result3, Result<T4> result4, Result<T5> result5, Result<T6> result6)
                    => Result.Plus(Result.Plus(result0, result1, result2), Result.Plus(result3, result4, result5, result6)).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3, T4, T5, T6, T7), TError>
            Plus<T0, T1, T2, T3, T4, T5, T6, T7, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5, Result<T6, TError> result6, Result<T7, TError> result7, Func<TError, TError, TError> mergeFunc)
                    => Result.Plus(Result.Plus(result0, result1, result2, result3, mergeFunc.ThrowIfDefault(nameof(mergeFunc))), Result.Plus(result4, result5, result6, result7, mergeFunc.ThrowIfDefault(nameof(mergeFunc))), mergeFunc.ThrowIfDefault(nameof(mergeFunc))).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3, T4, T5, T6, T7), TError>
            Plus<T0, T1, T2, T3, T4, T5, T6, T7, TError>
                (this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5, Result<T6, TError> result6, Result<T7, TError> result7) where TError : IPlus<TError, TError>
                    => Result.Plus(Result.Plus(result0, result1, result2, result3), Result.Plus(result4, result5, result6, result7)).Map(TupleHelper.Unfold);

        public static Result<(T0, T1, T2, T3, T4, T5, T6, T7)>
            Plus<T0, T1, T2, T3, T4, T5, T6, T7>
                (this Result<T0> result0, Result<T1> result1, Result<T2> result2, Result<T3> result3, Result<T4> result4, Result<T5> result5, Result<T6> result6, Result<T7> result7)
                    => Result.Plus(Result.Plus(result0, result1, result2, result3), Result.Plus(result4, result5, result6, result7)).Map(TupleHelper.Unfold);

    }
}
