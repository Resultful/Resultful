#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Resultful
{
    public static partial class Result
    {
        //Switch on Task<Result<T>>
        public static Task Switch<T>(this Task<Result<T>> value, Action<T> successFunc, Action<Error> errorFunc) => throw null!;
        public static Task SwitchAsync<T>(this Task<Result<T>> value, Func<T, Task> successFunc, Func<Error, Task> errorFunc) => throw null!;
        //Match on Task<Option<T>>
        public static Task<TResult> MatchAsync<T, TResult>(this Task<Result<T>> value, Func<T, Task<TResult>> successFunc, Func<Error, Task<TResult>> errorFunc) => throw null!;
        public static Task<TResult> Match<T, TResult>(this Task<Result<T>> value, Func<T, TResult> successFunc, Func<Error, TResult> errorFunc) => throw null!;
        //Switch on Task<Result<T, TError>>
        public static Task Switch<T, TError>(this Task<Result<T, TError>> value, Action<T> successFunc, Action<TError> errorFunc) => throw null!;
        public static Task SwitchAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, Task> successFunc, Func<TError, Task> errorFunc) => throw null!;
        //Match on Task<Result<T, TError>>
        public static Task<TResult> MatchAsync<T, TError, TResult>(this Task<Result<T, TError>> value, Func<T, Task<TResult>> successFunc, Func<TError, Task<TResult>> errorFunc) => throw null!;
        public static Task<TResult> Match<T, TError, TResult>(this Task<Result<T, TError>> value, Func<T, TResult> successFunc, Func<TError, TResult> errorFunc) => throw null!;
        //BindAsync on Result<T, TError>
        public static Task<Result<TResult, TError>> Bind<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Result<TResult, TError>> bindFunc) => throw null!;
        public static Task<Result<TResult, TError>> BindAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Task<Result<TResult, TError>>> bindFunc) => throw null!;
        //BindAsync on Result<T>
        public static Task<Result<TResult>> Bind<TResult, T>(this Task<Result<T>> value, Func<T, Result<TResult>> bindFunc) => throw null!;
        public static Task<Result<TResult>> BindAsync<TResult, T>(this Task<Result<T>> value, Func<T, Task<Result<TResult>>> bindFunc) => throw null!;
        //MapAsync on Result<T, TError>
        public static Task<Result<TResult, TError>> Map<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, TResult> mapFunc) => throw null!;
        public static Task<Result<TResult, TErrorResult>> Map2<TResult, T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<TError, TErrorResult> errorMapFunc, Func<T, TResult> mapFunc) => throw null!;
        public static Task<Result<TResult, TError>> MapAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Task<TResult>> mapFunc) => throw null!;
        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc, Func<T, Task<TResult>> mapFunc) => throw null!;
        //MapAsync on Result<T>
        public static Task<Result<TResult>> Map<T, TResult>(this Task<Result<T>> value, Func<T, TResult> mapFunc) => throw null!;
        public static Task<Result<TResult, TError>> Map2<TResult, T, TError>(this Task<Result<T>> value, Func<Error, TError> errorMapFunc, Func<T, TResult> mapFunc) => throw null!;
        public static Task<Result<TResult>> MapAsync<T, TResult>(this Task<Result<T>> value, Func<T, Task<TResult>> mapFunc) => throw null!;
        public static Task<Result<TResult, TError>> Map2Async<TResult, T, TError>(this Task<Result<T>> value, Func<Error, Task<TError>> errorMapFunc, Func<T, Task<TResult>> mapFunc) => throw null!;
        //MapErrorAsync on Result<T, TError>
        public static Task<Result<T, TErrorResult>> MapError<T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<TError, TErrorResult> errorMapFunc) => throw null!;
        public static Task<Result<T, TErrorResult>> MapErrorAsync<T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc) => throw null!;
        public static Task<Result<T>> MapError<T, TError>(this Task<Result<T, TError>> value, Func<TError, Error> errorMapFunc) => throw null!;
        public static Task<Result<T>> MapErrorAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, Task<Error>> errorMapFunc) => throw null!;
        //MapError on Result<T>
        public static Task<Result<T, TError>> MapError<T, TError>(this Task<Result<T>> value, Func<Error, TError> errorMapFunc) => throw null!;
        public static Task<Result<T, TError>> MapErrorAsync<T, TError>(this Task<Result<T>> value, Func<Error, Task<TError>> errorMapFunc) => throw null!;
        //TeeAsync on Result<T, TError>
        public static Task<Result<T, TError>> Tee<T, TError>(this Task<Result<T, TError>> value, Action<T> action) => throw null!;
        public static Task<Result<T, TError>> TeeAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, Task> asyncFunc) => throw null!;
        //TeeAsync on Result<T>
        public static Task<Result<T>> Tee<T>(this Task<Result<T>> value, Action<T> action) => throw null!;
        public static Task<Result<T>> TeeAsync<T>(this Task<Result<T>> value, Func<T, Task> asyncFunc) => throw null!;
        //TeeErrorAsync on Result<T, TError>
        public static Task<Result<T, TError>> TeeError<T, TError>(this Task<Result<T, TError>> value, Action<TError> action) => throw null!;
        public static Task<Result<T, TError>> TeeErrorAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, Task> asyncFunc) => throw null!;
        //TeeErrorAsync on Result<T>
        public static Task<Result<T>> TeeError<T>(this Task<Result<T>> value, Action<Error> action) => throw null!;
        public static Task<Result<T>> TeeErrorAsync<T>(this Task<Result<T>> value, Func<Error, Task> asyncFunc) => throw null!;
        //FlattenAsync on Result<T, TError>
        public static Task<Result<T, TError>> Flatten<T, TError>(this Task<Result<Result<T, TError>, TError>> value) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TError>> Flatten<TError>(this Task<Result<VoidResult<TError>, TError>> value) => throw null!;
        //FlattenAsync on Result<T>
        public static Task<Result<T>> Flatten<T>(this Task<Result<Result<T>>> value) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> Flatten(this Task<Result<VoidResult>> value) => throw null!;
        //UnwrapAsync on Result<T, TError>
        public static Task<Result<T, TError>> UnwrapAsync<T, TError>(this Result<Task<T>, TError> value) => throw null!;
        public static Task<Result<T, TError>> UnwrapAsync<T, TError>(this Result<T, Task<TError>> value) => throw null!;
        public static Task<Result<Unit, TError>> UnwrapAsync<TError>(this Result<Task, TError> value) => throw null!;
        //UnwrapAsync on Result<T>
        public static Task<Result<T>> Unwrap<T>(this Result<Task<T>> value) => throw null!;
        public static Task<Result<Unit>> Unwrap(this Result<Task> value) => throw null!;
        //DiscardErrorAsync on Result<T, TError>
        public static Task<Option<T>> DiscardErrorAsync<T, TError>(this Result<T, TError> value, Func<TError, Task> errorAction) => throw null!;
        public static Task<Option<T>> DiscardError<T, TError>(this Task<Result<T, TError>> value, Action<TError> errorAction) => throw null!;
        public static Task<Option<T>> DiscardErrorAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, Task> errorAction) => throw null!;
        public static Task<Option<T>> DiscardError<T, TError>(this Task<Result<T, TError>> value) => throw null!;
        //DiscardErrorAsync on Result<T>
        public static Task<Option<T>> DiscardError<T>(this Task<Result<T>> value, Action<Error> errorAction) => throw null!;
        public static Task<Option<T>> DiscardErrorAsync<T>(this Task<Result<T>> value, Func<Error, Task> errorAction) => throw null!;
        public static Task<Option<T>> DiscardError<T>(this Task<Result<T>> value) => throw null!;
        //DiscardValueAsync on Result<T, TError>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TError>> DiscardValue<T, TError>(this Task<Result<T, TError>> value) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TError>> DiscardValue<T, TError>(this Task<Result<T, TError>> value, Func<T, VoidResult<TError>> bindFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult<TError>> DiscardValueAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, Task<VoidResult<TError>>> bindFunc) => throw null!;
        //DiscardValueAsync on Result<T>
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> DiscardValue<T>(this Task<Result<T>> value) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> DiscardValue<T>(this Task<Result<T>> value, Func<T, VoidResult> bindFunc) => throw null!;
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static Task<VoidResult> DiscardValueAsync<T>(this Task<Result<T>> value, Func<T, Task<VoidResult>> bindFunc) => throw null!;
        //ReturnOrValue on Result<T>
        public static Task<T> ReturnOrValue<T>(this Task<Result<T>> value, Func<Error, T> func) => throw null!;
        public static Task<T> ReturnOrValueAsync<T>(this Task<Result<T>> value, Func<Error, Task<T>> func) => throw null!;
        //ReturnOrFail on Result<T>
        public static Task<T> ReturnOrFail<T, TExn>(this Task<Result<T>> value, Func<Error, TExn> func)
            where TExn : Exception => throw null!;
        //ReturnOrValue on Result<T, TError>
        public static Task<T> ReturnOrValue<T, TError>(this Task<Result<T, TError>> value, Func<TError, T> func) => throw null!;
        public static Task<T> ReturnOrValueAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, Task<T>> func) => throw null!;
        //ReturnOrFail on Result<T, TError>
        public static Task<T> ReturnOrFail<T, TError, TExn>(this Task<Result<T, TError>> value, Func<TError, TExn> func)
            where TExn : Exception => throw null!;
        //Fold on Result<T>
        //FoldUntilAsync on Result<T>
        public static Task<Result<TResult>> FoldUntilAsync<TResult, T>(this IEnumerable<Result<T>> values, TResult seed, Func<TResult, T, Task<TResult>> aggrFunc) => throw null!;
        public static Task<Result<TResult>> FoldUntilAsync<TResult, T>(this IEnumerable<Result<T>> values, Result<TResult> seed, Func<TResult, T, Task<TResult>> aggrFunc) => throw null!;
        public static Task<Result<TResult>> FoldUntilAsync<TResult, T>(this IEnumerable<Result<T>> values, Task<Result<TResult>> seed, Func<TResult, T, Task<TResult>> aggrFunc) => throw null!;
        //ReduceAsync on Result<T>
        //ReduceUntil on Result<T>
        public static Task<Result<T>> ReduceUntilAsync<T>(this IEnumerable<Result<T>> values, Func<T, T, Task<T>> aggrFunc) => throw null!;
        //TryReduce on Result<T>
        //TryReduceUntilAsync on Result<T>
        public static Task<Option<Result<T>>> TryReduceUntilAsync<T>(this IEnumerable<Result<T>> values, Func<T, T, Task<T>> aggrFunc) => throw null!;
        //FoldAsync on Result<T, TError>
        public static Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, Task<Result<TResult, TError>> seed, Func<TError, TError, TError> mergeFunc, Func<TResult, T, Task<TResult>> aggrFunc) => throw null!;
        public static Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, Result<TResult, TError> seed, Func<TError, TError, TError> mergeFunc, Func<TResult, T, Task<TResult>> aggrFunc) => throw null!;
        public static Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, TResult seed, Func<TError, TError, TError> mergeFunc, Func<TResult, T, Task<TResult>> aggrFunc) => throw null!;
        public static Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, TResult seed, Func<TResult, T, Task<TResult>> aggrFunc)
            where TError : IPlus<TError, TError> => throw null!;
        public static Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, Result<TResult, TError> seed, Func<TResult, T, Task<TResult>> aggrFunc)
            where TError : IPlus<TError, TError> => throw null!;
        public static Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, Task<Result<TResult, TError>> seed, Func<TResult, T, Task<TResult>> aggrFunc)
            where TError : IPlus<TError, TError> => throw null!;
        //FoldUntil on Result<T, TError>
        public static Task<Result<TResult, TError>> FoldUntilAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, TResult seed, Func<TResult, T, Task<TResult>> aggrFunc) => throw null!;
        public static Task<Result<TResult, TError>> FoldUntilAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, Result<TResult, TError> seed, Func<TResult, T, Task<TResult>> aggrFunc) => throw null!;
        public static Task<Result<TResult, TError>> FoldUntilAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values, Task<Result<TResult, TError>> seed, Func<TResult, T, Task<TResult>> aggrFunc) => throw null!;
        //Reduce on Result<T, TError>
        public static Task<Result<T, TError>> ReduceAsync<T, TError>(this IEnumerable<Result<T, TError>> values, Func<TError, TError, TError> mergeFunc, Func<T, T, Task<T>> aggrFunc) => throw null!;
        public static Task<Result<T, TError>> ReduceAsync<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, Task<T>> aggrFunc)
            where TError : IPlus<TError, TError> => throw null!;
        //ReduceUntil on Result<T, TError>
        public static Task<Result<T, TError>> ReduceUntilAsync<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, Task<T>> aggrFunc) => throw null!;
        //TryReduce on Result<T, TError>
        public static Task<Option<Result<T, TError>>> TryReduceAsync<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, Task<T>> aggrFunc, Func<TError, TError, TError> mergeFunc) => throw null!;
        public static Task<Option<Result<T, TError>>> TryReduceAsync<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, Task<T>> aggrFunc)
            where TError : IPlus<TError, TError> => throw null!;
        //TryReduceUntil on Result<T, TError>
        public static Task<Option<Result<T, TError>>> TryReduceUntilAsync<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, Task<T>> aggrFunc) => throw null!;
    }
}
