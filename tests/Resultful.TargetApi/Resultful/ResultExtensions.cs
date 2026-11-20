#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Resultful
{
    public static partial class Result
    {
        public static T Id<T>(this T value) => throw null!;
        public static Task<T> IdAsync<T>(this T value) => throw null!;
        //Builder for Result<T, TError>
        public static Result<T, TError> Fail<T, TError>(this TError value) => throw null!;
        public static Result<T, TError> Ok<T, TError>(this T value) => throw null!;
        //Builder for Result<T>
        public static Result<T> Fail<T>(this Error error) => throw null!;
        public static Result<T> Ok<T>(this T value) => throw null!;
        //Plus on Result<T, TError>
        public static Result<(TLeft, TRight), TError> Plus<TLeft, TRight, TError>(this Result<TLeft, TError> left, Result<TRight, TError> right, Func<TError, TError, TError> mergeFunc) => throw null!;
        public static Result<(TLeft, TRight), TError> Plus<TLeft, TRight, TError>(this Result<TLeft, TError> left, Result<TRight, TError> right)
            where TError : IPlus<TError, TError> => throw null!;
        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left, Result<TRight, TError> right, Func<TError, TError, TError> mergeFunc, Func<TLeft, TRight, TResult> plusFunc) => throw null!;
        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left, Result<TRight, TError> right, Func<TError, TError, TError> mergeFunc)
            where TLeft : IPlus<TRight, TResult> => throw null!;
        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left, Result<TRight, TError> right, Func<TLeft, TRight, TResult> plusFunc)
            where TError : IPlus<TError, TError> => throw null!;
        public static Result<TResult, TError> Plus<TLeft, TRight, TResult, TError>(this Result<TLeft, TError> left, Result<TRight, TError> right)
            where TLeft : IPlus<TRight, TResult> where TError : IPlus<TError, TError> => throw null!;
        //Plus on Result<T>
        //Reduce on Result<T, TError>
        public static Result<T, TError> Reduce<T, TError>(this IEnumerable<Result<T, TError>> values, Func<TError, TError, TError> mergeFunc, Func<T, T, T> plusFunc) => throw null!;
        public static Result<T, TError> Reduce<T, TError>(this IEnumerable<Result<T, TError>> values, Func<TError, TError, TError> mergeFunc)
            where T : IPlus<T, T> => throw null!;
        public static Result<T, TError> Reduce<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, T> plusFunc)
            where TError : IPlus<TError, TError> => throw null!;
        public static Result<T, TError> Reduce<T, TError>(this IEnumerable<Result<T, TError>> values)
            where T : IPlus<T, T> where TError : IPlus<TError, TError> => throw null!;
        //Fold on Result<T, TError>
        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, Result<TResult, TError> seed, Func<TError, TError, TError> mergeFunc, Func<TResult, T, TResult> aggrFunc) => throw null!;
        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, TResult seed, Func<TError, TError, TError> mergeFunc, Func<TResult, T, TResult> aggrFunc) => throw null!;
        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, Result<TResult, TError> seed, Func<TResult, T, TResult> aggrFunc)
            where TError : IPlus<TError, TError> => throw null!;
        public static Result<TResult, TError> Fold<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, TResult seed, Func<TResult, T, TResult> aggrFunc)
            where TError : IPlus<TError, TError> => throw null!;
        //ReduceUntil on Result<T, TError>
        public static Result<T, TError> ReduceUntil<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, T> aggrFunc) => throw null!;
        public static Result<T, TError> ReduceUntil<T, TError>(this IEnumerable<Result<T, TError>> values)
            where T : IPlus<T, T> => throw null!;
        //FoldUntil on Result<T, TError>
        public static Result<TResult, TError> FoldUntil<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, Result<TResult, TError> seed, Func<TResult, T, TResult> aggrFunc) => throw null!;
        public static Result<TResult, TError> FoldUntil<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, TResult seed, Func<TResult, T, TResult> aggrFunc) => throw null!;
        //Reduce on Result<T>
        //Fold on Result<T>
        //ReduceUntil on Result<T>
        public static Result<T> ReduceUntil<T>(this IEnumerable<Result<T>> values, Func<T, T, T> plusFunc) => throw null!;
        //FoldUntil on Result<T>
        public static Result<TResult> FoldUntil<TResult, T>(this IEnumerable<Result<T>> values, Result<TResult> seed, Func<TResult, T, TResult> aggrFunc) => throw null!;
        public static Result<TResult> FoldUntil<TResult, T>(this IEnumerable<Result<T>> values, TResult seed, Func<TResult, T, TResult> aggrFunc) => throw null!;
        //Unroll on IEnumerable<Result<T>>
        //UnrollUntil on IEnumerable<Result<T>>
        public static Result<IEnumerable<T>> UnrollUntil<T>(this IEnumerable<Result<T>> values) => throw null!;
        //Unroll on IEnumerable<Result<T, TError>>
        public static Result<IEnumerable<T>, TError> Unroll<T, TError>(this IEnumerable<Result<T, TError>> values, Func<TError, TError, TError> mergeFunc) => throw null!;
        //Unroll on IEnumerable<Result<T, TError>>
        public static Result<IEnumerable<T>, TError> UnrollUntil<T, TError>(this IEnumerable<Result<T, TError>> values) => throw null!;
        //Flatten on Result<T, TError>
        public static Result<T, TError> Flatten<T, TError>(this Result<Result<T, TError>, TError> value) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static VoidResult<TError> Flatten<TError>(this Result<VoidResult<TError>, TError> value) => throw null!;
        //Flatten on Result<T>
        public static Result<T> Flatten<T>(this Result<Result<T>> value) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static VoidResult Flatten(this Result<VoidResult> value) => throw null!;
        //Unfold on Result<Option<T>>
        public static Option<Result<T>> Unfold<T>(this Result<Option<T>> value) => throw null!;
        //Unfold on Result<Option<T>, TError>
        public static Option<Result<T, TError>> Unfold<T, TError>(this Result<Option<T>, TError> value) => throw null!;
    }
}
