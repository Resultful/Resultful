#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Resultful
{
    public static partial class Result
    {
        //Builder for VoidResult<TError>
        public static Result<Unit, TError> Fail<TError>(this TError value) => throw null!;
        public static Result<Unit, TError> Ok<TError>() => throw null!;
        //Builder for VoidResult
        public static Result<Unit> Fail(Error error) => throw null!;
        public static Result<Unit> Ok() => throw null!;
        //Plus on VoidResult<TError>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static VoidResult<TError> Plus<TError>(this VoidResult<TError> left, VoidResult<TError> right, Func<TError, TError, TError> mergeFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static VoidResult<TError> Plus<TError>(this VoidResult<TError> left, VoidResult<TError> right)
            where TError : IPlus<TError, TError> => throw null!;
        //Plus on VoidResult
        //Fold on VoidResult<TError>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static VoidResult<TError> Reduce<TError>(this IEnumerable<VoidResult<TError>> values, Func<TError, TError, TError> mergeFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static VoidResult<TError> Reduce<TError>(this IEnumerable<VoidResult<TError>> values)
            where TError : IPlus<TError, TError> => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static VoidResult<TError> Reduce<TError>(params VoidResult<TError>[] values)
            where TError : IPlus<TError, TError> => throw null!;
        //FoldUntil on VoidResult<TError>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static VoidResult<TError> ReduceUntil<TError>(this IEnumerable<VoidResult<TError>> values) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static VoidResult<TError> ReduceUntil<TError>(params VoidResult<TError>[] values) => throw null!;
        //Fold on VoidResult
        //FoldUntil on VoidResult
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static VoidResult ReduceUntil(this IEnumerable<VoidResult> values) => throw null!;
        //Flatten on VoidResult<TError>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static VoidResult<TError> Flatten<TError>(this VoidResult<VoidResult<TError>> value) => throw null!;
    }
}
