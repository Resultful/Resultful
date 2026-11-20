#nullable enable
using System;

namespace Resultful
{
    // Sequential composition: run a chain of steps where each step only runs if everything before it succeeded.
    // The first failure is returned unchanged (including any errors it already accumulated); later steps never run.
    public static partial class Result
    {
        //Sequence: start a chain of VoidResult steps
        public static Result<Unit> Sequence(params Func<Result<Unit>>[] steps) => throw null!;
        public static Result<Unit, TError> Sequence<TError>(params Func<Result<Unit, TError>>[] steps) => throw null!;
        //Then on VoidResult
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static VoidResult Then(this VoidResult value, params Func<VoidResult>[] steps) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Result<T> Then<T>(this VoidResult value, Func<Result<T>> next) => throw null!;
        //Then on VoidResult<TError>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static VoidResult<TError> Then<TError>(this VoidResult<TError> value, params Func<VoidResult<TError>>[] steps) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Result<T, TError> Then<T, TError>(this VoidResult<TError> value, Func<Result<T, TError>> next) => throw null!;
        //Then on Result<T>: steps that transform the value without changing its type
        public static Result<T> Then<T>(this Result<T> value, params Func<T, Result<T>>[] steps) => throw null!;
        //Then on Result<T, TError>
        public static Result<T, TError> Then<T, TError>(this Result<T, TError> value, params Func<T, Result<T, TError>>[] steps) => throw null!;
        //Check on Result<T>: steps that can fail but keep the value
        public static Result<T> Check<T>(this Result<T> value, params Func<T, Result<Unit>>[] checks) => throw null!;
        //Check on Result<T, TError>
        public static Result<T, TError> Check<T, TError>(this Result<T, TError> value, params Func<T, Result<Unit, TError>>[] checks) => throw null!;
    }
}
