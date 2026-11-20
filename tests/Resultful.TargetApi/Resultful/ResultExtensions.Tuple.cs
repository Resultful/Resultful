#nullable enable
using System;

namespace Resultful
{
    public static partial class Result
    {
        public static Result<(T0, T1, T2), TError> Plus<T0, T1, T2, TError>(this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Func<TError, TError, TError> mergeFunc) => throw null!;
        public static Result<(T0, T1, T2), TError> Plus<T0, T1, T2, TError>(this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2)
            where TError : IPlus<TError, TError> => throw null!;
        public static Result<(T0, T1, T2, T3), TError> Plus<T0, T1, T2, T3, TError>(this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Func<TError, TError, TError> mergeFunc) => throw null!;
        public static Result<(T0, T1, T2, T3), TError> Plus<T0, T1, T2, T3, TError>(this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3)
            where TError : IPlus<TError, TError> => throw null!;
        public static Result<(T0, T1, T2, T3, T4), TError> Plus<T0, T1, T2, T3, T4, TError>(this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Func<TError, TError, TError> mergeFunc) => throw null!;
        public static Result<(T0, T1, T2, T3, T4), TError> Plus<T0, T1, T2, T3, T4, TError>(this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4)
            where TError : IPlus<TError, TError> => throw null!;
        public static Result<(T0, T1, T2, T3, T4, T5), TError> Plus<T0, T1, T2, T3, T4, T5, TError>(this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5, Func<TError, TError, TError> mergeFunc) => throw null!;
        public static Result<(T0, T1, T2, T3, T4, T5), TError> Plus<T0, T1, T2, T3, T4, T5, TError>(this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5)
            where TError : IPlus<TError, TError> => throw null!;
        public static Result<(T0, T1, T2, T3, T4, T5, T6), TError> Plus<T0, T1, T2, T3, T4, T5, T6, TError>(this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5, Result<T6, TError> result6, Func<TError, TError, TError> mergeFunc) => throw null!;
        public static Result<(T0, T1, T2, T3, T4, T5, T6), TError> Plus<T0, T1, T2, T3, T4, T5, T6, TError>(this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5, Result<T6, TError> result6)
            where TError : IPlus<TError, TError> => throw null!;
        public static Result<(T0, T1, T2, T3, T4, T5, T6, T7), TError> Plus<T0, T1, T2, T3, T4, T5, T6, T7, TError>(this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5, Result<T6, TError> result6, Result<T7, TError> result7, Func<TError, TError, TError> mergeFunc) => throw null!;
        public static Result<(T0, T1, T2, T3, T4, T5, T6, T7), TError> Plus<T0, T1, T2, T3, T4, T5, T6, T7, TError>(this Result<T0, TError> result0, Result<T1, TError> result1, Result<T2, TError> result2, Result<T3, TError> result3, Result<T4, TError> result4, Result<T5, TError> result5, Result<T6, TError> result6, Result<T7, TError> result7)
            where TError : IPlus<TError, TError> => throw null!;
    }
}
