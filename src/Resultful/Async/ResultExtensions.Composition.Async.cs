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
        /// <summary>Runs <paramref name="steps"/> in order, starting from a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static Task<VoidResult> SequenceAsync(params Func<Task<VoidResult>>[] steps)
            => Ok().ThenAsync(steps);

        /// <summary>Runs <paramref name="steps"/> in order, starting from a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static Task<VoidResult<TError>> SequenceAsync<TError>(params Func<Task<VoidResult<TError>>>[] steps)
            => Ok<TError>().ThenAsync(steps);

        //ThenAsync on VoidResult
        /// <summary>Runs <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <param name="value">The result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static async Task<VoidResult> ThenAsync(this VoidResult value, params Func<Task<VoidResult>>[] steps)
        {
            foreach (var step in Steps(steps))
            {
                value = await value.BindAsync(_ => step()).ConfigureAwait(false);
            }
            return value;
        }

        /// <summary>On success, returns the result <paramref name="next"/> gives. A failure keeps the errors and <paramref name="next"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="next">Gives the next result.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="next"/> is <c>null</c>, even on a failure.</remarks>
        public static Task<Result<T>> ThenAsync<T>(this VoidResult value, Func<Task<Result<T>>> next)
        {
            next.ThrowIfDefault(nameof(next));
            return value.BindValueAsync(_ => next());
        }

        //ThenAsync on VoidResult<TError>
        /// <summary>Runs <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static async Task<VoidResult<TError>> ThenAsync<TError>(this VoidResult<TError> value, params Func<Task<VoidResult<TError>>>[] steps)
        {
            foreach (var step in Steps(steps))
            {
                value = await value.BindAsync(_ => step()).ConfigureAwait(false);
            }
            return value;
        }

        /// <summary>On success, returns the result <paramref name="next"/> gives. A failure keeps the error and <paramref name="next"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="next">Gives the next result.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="next"/> is <c>null</c>, even on a failure.</remarks>
        public static Task<Result<T, TError>> ThenAsync<T, TError>(this VoidResult<TError> value, Func<Task<Result<T, TError>>> next)
        {
            next.ThrowIfDefault(nameof(next));
            return value.BindValueAsync(_ => next());
        }

        //ThenAsync on Result<T>
        /// <summary>Passes the value through <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static async Task<Result<T>> ThenAsync<T>(this Result<T> value, params Func<T, Task<Result<T>>>[] steps)
        {
            foreach (var step in Steps(steps))
            {
                value = await value.BindAsync(step).ConfigureAwait(false);
            }
            return value;
        }

        //ThenAsync on Result<T, TError>
        /// <summary>Passes the value through <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static async Task<Result<T, TError>> ThenAsync<T, TError>(this Result<T, TError> value, params Func<T, Task<Result<T, TError>>>[] steps)
        {
            foreach (var step in Steps(steps))
            {
                value = await value.BindAsync(step).ConfigureAwait(false);
            }
            return value;
        }

        //CheckAsync on Result<T>
        /// <summary>Runs <paramref name="checks"/> in order on the value while the result is a success. A check that fails stops the chain and its errors become the result; otherwise the value is kept. Later checks don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="checks">The checks to run on the value.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="checks"/> or any check is <c>null</c>, before any check runs.</remarks>
        public static async Task<Result<T>> CheckAsync<T>(this Result<T> value, params Func<T, Task<VoidResult>>[] checks)
        {
            foreach (var check in Steps(checks, nameof(checks)))
            {
                value = await value.BindAsync(async item => (await check(item).ConfigureAwait(false)).Map(item)).ConfigureAwait(false);
            }
            return value;
        }

        //CheckAsync on Result<T, TError>
        /// <summary>Runs <paramref name="checks"/> in order on the value while the result is a success. A check that fails stops the chain and its errors become the result; otherwise the value is kept. Later checks don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="checks">The checks to run on the value.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="checks"/> or any check is <c>null</c>, before any check runs.</remarks>
        public static async Task<Result<T, TError>> CheckAsync<T, TError>(this Result<T, TError> value, params Func<T, Task<VoidResult<TError>>>[] checks)
        {
            foreach (var check in Steps(checks, nameof(checks)))
            {
                value = await value.BindAsync(async item => (await check(item).ConfigureAwait(false)).Map(item)).ConfigureAwait(false);
            }
            return value;
        }

        //Then/ThenAsync on Task<VoidResult>
        /// <summary>Awaits <paramref name="value"/>, then: runs <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static async Task<VoidResult> Then(this Task<VoidResult> value, params Func<VoidResult>[] steps)
            => (await value.ConfigureAwait(false)).Then(steps);

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the result <paramref name="next"/> gives. A failure keeps the errors and <paramref name="next"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="next">Gives the next result.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="next"/> is <c>null</c>, even on a failure.</remarks>
        public static async Task<Result<T>> Then<T>(this Task<VoidResult> value, Func<Result<T>> next)
            => (await value.ConfigureAwait(false)).Then(next);

        /// <summary>Awaits <paramref name="value"/>, then: runs <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static async Task<VoidResult> ThenAsync(this Task<VoidResult> value, params Func<Task<VoidResult>>[] steps)
            => await (await value.ConfigureAwait(false)).ThenAsync(steps).ConfigureAwait(false);

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the result <paramref name="next"/> gives. A failure keeps the errors and <paramref name="next"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="next">Gives the next result.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="next"/> is <c>null</c>, even on a failure.</remarks>
        public static async Task<Result<T>> ThenAsync<T>(this Task<VoidResult> value, Func<Task<Result<T>>> next)
            => await (await value.ConfigureAwait(false)).ThenAsync(next).ConfigureAwait(false);

        //Then/ThenAsync on Task<VoidResult<TError>>
        /// <summary>Awaits <paramref name="value"/>, then: runs <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static async Task<VoidResult<TError>> Then<TError>(this Task<VoidResult<TError>> value, params Func<VoidResult<TError>>[] steps)
            => (await value.ConfigureAwait(false)).Then(steps);

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the result <paramref name="next"/> gives. A failure keeps the error and <paramref name="next"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="next">Gives the next result.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="next"/> is <c>null</c>, even on a failure.</remarks>
        public static async Task<Result<T, TError>> Then<T, TError>(this Task<VoidResult<TError>> value, Func<Result<T, TError>> next)
            => (await value.ConfigureAwait(false)).Then(next);

        /// <summary>Awaits <paramref name="value"/>, then: runs <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static async Task<VoidResult<TError>> ThenAsync<TError>(this Task<VoidResult<TError>> value, params Func<Task<VoidResult<TError>>>[] steps)
            => await (await value.ConfigureAwait(false)).ThenAsync(steps).ConfigureAwait(false);

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the result <paramref name="next"/> gives. A failure keeps the error and <paramref name="next"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="next">Gives the next result.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="next"/> is <c>null</c>, even on a failure.</remarks>
        public static async Task<Result<T, TError>> ThenAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Task<Result<T, TError>>> next)
            => await (await value.ConfigureAwait(false)).ThenAsync(next).ConfigureAwait(false);

        //Then/ThenAsync/Check/CheckAsync on Task<Result<T>>
        /// <summary>Awaits <paramref name="value"/>, then: passes the value through <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static async Task<Result<T>> Then<T>(this Task<Result<T>> value, params Func<T, Result<T>>[] steps)
            => (await value.ConfigureAwait(false)).Then(steps);

        /// <summary>Awaits <paramref name="value"/>, then: passes the value through <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static async Task<Result<T>> ThenAsync<T>(this Task<Result<T>> value, params Func<T, Task<Result<T>>>[] steps)
            => await (await value.ConfigureAwait(false)).ThenAsync(steps).ConfigureAwait(false);

        /// <summary>Awaits <paramref name="value"/>, then: runs <paramref name="checks"/> in order on the value while the result is a success. A check that fails stops the chain and its errors become the result; otherwise the value is kept. Later checks don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="checks">The checks to run on the value.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="checks"/> or any check is <c>null</c>, before any check runs.</remarks>
        public static async Task<Result<T>> Check<T>(this Task<Result<T>> value, params Func<T, VoidResult>[] checks)
            => (await value.ConfigureAwait(false)).Check(checks);

        /// <summary>Awaits <paramref name="value"/>, then: runs <paramref name="checks"/> in order on the value while the result is a success. A check that fails stops the chain and its errors become the result; otherwise the value is kept. Later checks don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="checks">The checks to run on the value.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="checks"/> or any check is <c>null</c>, before any check runs.</remarks>
        public static async Task<Result<T>> CheckAsync<T>(this Task<Result<T>> value, params Func<T, Task<VoidResult>>[] checks)
            => await (await value.ConfigureAwait(false)).CheckAsync(checks).ConfigureAwait(false);

        //Then/ThenAsync/Check/CheckAsync on Task<Result<T, TError>>
        /// <summary>Awaits <paramref name="value"/>, then: passes the value through <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static async Task<Result<T, TError>> Then<T, TError>(this Task<Result<T, TError>> value, params Func<T, Result<T, TError>>[] steps)
            => (await value.ConfigureAwait(false)).Then(steps);

        /// <summary>Awaits <paramref name="value"/>, then: passes the value through <paramref name="steps"/> in order while the result is a success, and stops at the first failure, which is returned unchanged. Later steps don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="steps">The steps to run.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="steps"/> or any step is <c>null</c>, before any step runs.</remarks>
        public static async Task<Result<T, TError>> ThenAsync<T, TError>(this Task<Result<T, TError>> value, params Func<T, Task<Result<T, TError>>>[] steps)
            => await (await value.ConfigureAwait(false)).ThenAsync(steps).ConfigureAwait(false);

        /// <summary>Awaits <paramref name="value"/>, then: runs <paramref name="checks"/> in order on the value while the result is a success. A check that fails stops the chain and its errors become the result; otherwise the value is kept. Later checks don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="checks">The checks to run on the value.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="checks"/> or any check is <c>null</c>, before any check runs.</remarks>
        public static async Task<Result<T, TError>> Check<T, TError>(this Task<Result<T, TError>> value, params Func<T, VoidResult<TError>>[] checks)
            => (await value.ConfigureAwait(false)).Check(checks);

        /// <summary>Awaits <paramref name="value"/>, then: runs <paramref name="checks"/> in order on the value while the result is a success. A check that fails stops the chain and its errors become the result; otherwise the value is kept. Later checks don't run.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="checks">The checks to run on the value.</param>
        /// <remarks>Throws <see cref="System.ArgumentNullException"/> if <paramref name="checks"/> or any check is <c>null</c>, before any check runs.</remarks>
        public static async Task<Result<T, TError>> CheckAsync<T, TError>(this Task<Result<T, TError>> value, params Func<T, Task<VoidResult<TError>>>[] checks)
            => await (await value.ConfigureAwait(false)).CheckAsync(checks).ConfigureAwait(false);
    }
}
