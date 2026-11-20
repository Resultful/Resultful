using System;
using System.Threading.Tasks;
using Resultful.Utils;

namespace Resultful
{
    // Async counterparts of ResultExtensions.Composition.cs, following the library convention:
    // an `Async` suffix means the steps return a Task; extensions on Task<...> with synchronous steps keep the plain name.
    public static partial class Result
    {
        //SequenceAsync: start a chain of async VoidResult steps
        public static Task<VoidResult> SequenceAsync(params Func<Task<VoidResult>>[] steps)
            => Ok().ThenAsync(steps);

        public static Task<VoidResult<TError>> SequenceAsync<TError>(params Func<Task<VoidResult<TError>>>[] steps)
            => Ok<TError>().ThenAsync(steps);

        //ThenAsync on VoidResult
        public static async Task<VoidResult> ThenAsync(this VoidResult value, params Func<Task<VoidResult>>[] steps)
        {
            foreach (var step in Steps(steps))
            {
                value = await value.BindAsync(_ => step()).ConfigureAwait(false);
            }
            return value;
        }

        public static Task<Result<T>> ThenAsync<T>(this VoidResult value, Func<Task<Result<T>>> next)
        {
            next.ThrowIfDefault(nameof(next));
            return value.BindValueAsync(_ => next());
        }

        //ThenAsync on VoidResult<TError>
        public static async Task<VoidResult<TError>> ThenAsync<TError>(this VoidResult<TError> value, params Func<Task<VoidResult<TError>>>[] steps)
        {
            foreach (var step in Steps(steps))
            {
                value = await value.BindAsync(_ => step()).ConfigureAwait(false);
            }
            return value;
        }

        public static Task<Result<T, TError>> ThenAsync<T, TError>(this VoidResult<TError> value, Func<Task<Result<T, TError>>> next)
        {
            next.ThrowIfDefault(nameof(next));
            return value.BindValueAsync(_ => next());
        }

        //ThenAsync on Result<T>
        public static async Task<Result<T>> ThenAsync<T>(this Result<T> value, params Func<T, Task<Result<T>>>[] steps)
        {
            foreach (var step in Steps(steps))
            {
                value = await value.BindAsync(step).ConfigureAwait(false);
            }
            return value;
        }

        //ThenAsync on Result<T, TError>
        public static async Task<Result<T, TError>> ThenAsync<T, TError>(this Result<T, TError> value, params Func<T, Task<Result<T, TError>>>[] steps)
        {
            foreach (var step in Steps(steps))
            {
                value = await value.BindAsync(step).ConfigureAwait(false);
            }
            return value;
        }

        //CheckAsync on Result<T>
        public static async Task<Result<T>> CheckAsync<T>(this Result<T> value, params Func<T, Task<VoidResult>>[] checks)
        {
            foreach (var check in Steps(checks, nameof(checks)))
            {
                value = await value.BindAsync(async item => (await check(item).ConfigureAwait(false)).Map(item)).ConfigureAwait(false);
            }
            return value;
        }

        //CheckAsync on Result<T, TError>
        public static async Task<Result<T, TError>> CheckAsync<T, TError>(this Result<T, TError> value, params Func<T, Task<VoidResult<TError>>>[] checks)
        {
            foreach (var check in Steps(checks, nameof(checks)))
            {
                value = await value.BindAsync(async item => (await check(item).ConfigureAwait(false)).Map(item)).ConfigureAwait(false);
            }
            return value;
        }

        //Then/ThenAsync on Task<VoidResult>
        public static async Task<VoidResult> Then(this Task<VoidResult> value, params Func<VoidResult>[] steps)
            => (await value.ConfigureAwait(false)).Then(steps);

        public static async Task<Result<T>> Then<T>(this Task<VoidResult> value, Func<Result<T>> next)
            => (await value.ConfigureAwait(false)).Then(next);

        public static async Task<VoidResult> ThenAsync(this Task<VoidResult> value, params Func<Task<VoidResult>>[] steps)
            => await (await value.ConfigureAwait(false)).ThenAsync(steps).ConfigureAwait(false);

        public static async Task<Result<T>> ThenAsync<T>(this Task<VoidResult> value, Func<Task<Result<T>>> next)
            => await (await value.ConfigureAwait(false)).ThenAsync(next).ConfigureAwait(false);

        //Then/ThenAsync on Task<VoidResult<TError>>
        public static async Task<VoidResult<TError>> Then<TError>(this Task<VoidResult<TError>> value, params Func<VoidResult<TError>>[] steps)
            => (await value.ConfigureAwait(false)).Then(steps);

        public static async Task<Result<T, TError>> Then<T, TError>(this Task<VoidResult<TError>> value, Func<Result<T, TError>> next)
            => (await value.ConfigureAwait(false)).Then(next);

        public static async Task<VoidResult<TError>> ThenAsync<TError>(this Task<VoidResult<TError>> value, params Func<Task<VoidResult<TError>>>[] steps)
            => await (await value.ConfigureAwait(false)).ThenAsync(steps).ConfigureAwait(false);

        public static async Task<Result<T, TError>> ThenAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Task<Result<T, TError>>> next)
            => await (await value.ConfigureAwait(false)).ThenAsync(next).ConfigureAwait(false);

        //Then/ThenAsync/Check/CheckAsync on Task<Result<T>>
        public static async Task<Result<T>> Then<T>(this Task<Result<T>> value, params Func<T, Result<T>>[] steps)
            => (await value.ConfigureAwait(false)).Then(steps);

        public static async Task<Result<T>> ThenAsync<T>(this Task<Result<T>> value, params Func<T, Task<Result<T>>>[] steps)
            => await (await value.ConfigureAwait(false)).ThenAsync(steps).ConfigureAwait(false);

        public static async Task<Result<T>> Check<T>(this Task<Result<T>> value, params Func<T, VoidResult>[] checks)
            => (await value.ConfigureAwait(false)).Check(checks);

        public static async Task<Result<T>> CheckAsync<T>(this Task<Result<T>> value, params Func<T, Task<VoidResult>>[] checks)
            => await (await value.ConfigureAwait(false)).CheckAsync(checks).ConfigureAwait(false);

        //Then/ThenAsync/Check/CheckAsync on Task<Result<T, TError>>
        public static async Task<Result<T, TError>> Then<T, TError>(this Task<Result<T, TError>> value, params Func<T, Result<T, TError>>[] steps)
            => (await value.ConfigureAwait(false)).Then(steps);

        public static async Task<Result<T, TError>> ThenAsync<T, TError>(this Task<Result<T, TError>> value, params Func<T, Task<Result<T, TError>>>[] steps)
            => await (await value.ConfigureAwait(false)).ThenAsync(steps).ConfigureAwait(false);

        public static async Task<Result<T, TError>> Check<T, TError>(this Task<Result<T, TError>> value, params Func<T, VoidResult<TError>>[] checks)
            => (await value.ConfigureAwait(false)).Check(checks);

        public static async Task<Result<T, TError>> CheckAsync<T, TError>(this Task<Result<T, TError>> value, params Func<T, Task<VoidResult<TError>>>[] checks)
            => await (await value.ConfigureAwait(false)).CheckAsync(checks).ConfigureAwait(false);
    }
}
