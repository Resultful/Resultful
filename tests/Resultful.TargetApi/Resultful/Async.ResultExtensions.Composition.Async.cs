#nullable enable
using System;
using System.Threading.Tasks;

namespace Resultful
{
    // Async counterparts of ResultExtensions.Composition.cs, following the library convention:
    // an `Async` suffix means the steps return a Task; extensions on Task<...> with synchronous steps keep the plain name.
    public static partial class Result
    {
        //SequenceAsync: start a chain of VoidResult steps
        public static Task<Result<Unit>> SequenceAsync(params Func<Task<Result<Unit>>>[] steps) => throw null!;
        public static Task<Result<Unit, TError>> SequenceAsync<TError>(params Func<Task<Result<Unit, TError>>>[] steps) => throw null!;
        //ThenAsync on VoidResult
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> ThenAsync(this VoidResult value, params Func<Task<VoidResult>>[] steps) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<T>> ThenAsync<T>(this VoidResult value, Func<Task<Result<T>>> next) => throw null!;
        //ThenAsync on VoidResult<TError>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TError>> ThenAsync<TError>(this VoidResult<TError> value, params Func<Task<VoidResult<TError>>>[] steps) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<T, TError>> ThenAsync<T, TError>(this VoidResult<TError> value, Func<Task<Result<T, TError>>> next) => throw null!;
        //ThenAsync on Result<T>
        public static Task<Result<T>> ThenAsync<T>(this Result<T> value, params Func<T, Task<Result<T>>>[] steps) => throw null!;
        //ThenAsync on Result<T, TError>
        public static Task<Result<T, TError>> ThenAsync<T, TError>(this Result<T, TError> value, params Func<T, Task<Result<T, TError>>>[] steps) => throw null!;
        //CheckAsync on Result<T>
        public static Task<Result<T>> CheckAsync<T>(this Result<T> value, params Func<T, Task<Result<Unit>>>[] checks) => throw null!;
        //CheckAsync on Result<T, TError>
        public static Task<Result<T, TError>> CheckAsync<T, TError>(this Result<T, TError> value, params Func<T, Task<Result<Unit, TError>>>[] checks) => throw null!;
        //Then/ThenAsync on Task<VoidResult>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> Then(this Task<VoidResult> value, params Func<VoidResult>[] steps) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<T>> Then<T>(this Task<VoidResult> value, Func<Result<T>> next) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> ThenAsync(this Task<VoidResult> value, params Func<Task<VoidResult>>[] steps) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<T>> ThenAsync<T>(this Task<VoidResult> value, Func<Task<Result<T>>> next) => throw null!;
        //Then/ThenAsync on Task<VoidResult<TError>>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TError>> Then<TError>(this Task<VoidResult<TError>> value, params Func<VoidResult<TError>>[] steps) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<T, TError>> Then<T, TError>(this Task<VoidResult<TError>> value, Func<Result<T, TError>> next) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TError>> ThenAsync<TError>(this Task<VoidResult<TError>> value, params Func<Task<VoidResult<TError>>>[] steps) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<Result<T, TError>> ThenAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Task<Result<T, TError>>> next) => throw null!;
        //Then/ThenAsync/Check/CheckAsync on Task<Result<T>>
        public static Task<Result<T>> Then<T>(this Task<Result<T>> value, params Func<T, Result<T>>[] steps) => throw null!;
        public static Task<Result<T>> ThenAsync<T>(this Task<Result<T>> value, params Func<T, Task<Result<T>>>[] steps) => throw null!;
        public static Task<Result<T>> Check<T>(this Task<Result<T>> value, params Func<T, Result<Unit>>[] checks) => throw null!;
        public static Task<Result<T>> CheckAsync<T>(this Task<Result<T>> value, params Func<T, Task<Result<Unit>>>[] checks) => throw null!;
        //Then/ThenAsync/Check/CheckAsync on Task<Result<T, TError>>
        public static Task<Result<T, TError>> Then<T, TError>(this Task<Result<T, TError>> value, params Func<T, Result<T, TError>>[] steps) => throw null!;
        public static Task<Result<T, TError>> ThenAsync<T, TError>(this Task<Result<T, TError>> value, params Func<T, Task<Result<T, TError>>>[] steps) => throw null!;
        public static Task<Result<T, TError>> Check<T, TError>(this Task<Result<T, TError>> value, params Func<T, Result<Unit, TError>>[] checks) => throw null!;
        public static Task<Result<T, TError>> CheckAsync<T, TError>(this Task<Result<T, TError>> value, params Func<T, Task<Result<Unit, TError>>>[] checks) => throw null!;
    }
}
