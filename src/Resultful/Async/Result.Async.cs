using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resultful.Utils;

namespace Resultful
{
    // M2 async (stack PR 19). A CancellationToken flows to each step that takes one. Nothing here catches: an exception a
    // step throws faults the task, and a cancelled token cancels it. Result.Try and Result.TryAsync are the opt-in.
    public partial struct Result<T, TError>
    {
        /// <summary>On success, awaits the value <paramref name="mapFunc"/> gives for the value and <paramref name="cancellationToken"/>, as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Maps the value; it receives <paramref name="cancellationToken"/>.</param>
        /// <param name="cancellationToken">Passed to <paramref name="mapFunc"/>. If it is already cancelled on a success, the task is cancelled and <paramref name="mapFunc"/> isn't called.</param>
        /// <remarks>An exception <paramref name="mapFunc"/> throws faults the task; it never becomes a failure.</remarks>
        public Task<Result<TResult, TError>> MapAsync<TResult>(Func<T, CancellationToken, Task<TResult>> mapFunc, CancellationToken cancellationToken)
        {
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Case() ? Run(mapFunc, _value, cancellationToken) : Task.FromResult(_error.Fail<TResult, TError>());

            static async Task<Result<TResult, TError>> Run(Func<T, CancellationToken, Task<TResult>> f, T value, CancellationToken ct)
                => (await AsyncStep.Run(f, value, ct).ConfigureAwait(false)).Ok<TResult, TError>();
        }

        /// <summary>On success, awaits the result <paramref name="bindFunc"/> returns for the value and <paramref name="cancellationToken"/>. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="bindFunc">Returns the next result from the value; it receives <paramref name="cancellationToken"/>.</param>
        /// <param name="cancellationToken">Passed to <paramref name="bindFunc"/>. If it is already cancelled on a success, the task is cancelled and <paramref name="bindFunc"/> isn't called.</param>
        /// <remarks>An exception <paramref name="bindFunc"/> throws faults the task; it never becomes a failure.</remarks>
        public Task<Result<TResult, TError>> BindAsync<TResult>(Func<T, CancellationToken, Task<Result<TResult, TError>>> bindFunc, CancellationToken cancellationToken)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            return Case() ? AsyncStep.Run(bindFunc, _value, cancellationToken) : Task.FromResult(_error.Fail<TResult, TError>());
        }

        /// <summary>On success, calls <paramref name="asyncFunc"/> with the value and <paramref name="cancellationToken"/>, awaits it, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <param name="asyncFunc">Called with the value; it receives <paramref name="cancellationToken"/>.</param>
        /// <param name="cancellationToken">Passed to <paramref name="asyncFunc"/>. If it is already cancelled on a success, the task is cancelled and <paramref name="asyncFunc"/> isn't called.</param>
        /// <remarks>An exception <paramref name="asyncFunc"/> throws faults the task; it never becomes a failure.</remarks>
        public Task<Result<T, TError>> TeeAsync(Func<T, CancellationToken, Task> asyncFunc, CancellationToken cancellationToken)
        {
            asyncFunc.ThrowIfDefault(nameof(asyncFunc));
            return Case() ? Run(asyncFunc, this, _value, cancellationToken) : Task.FromResult(this);

            static async Task<Result<T, TError>> Run(Func<T, CancellationToken, Task> f, Result<T, TError> self, T value, CancellationToken ct)
            {
                await AsyncStep.Run(f, value, ct).ConfigureAwait(false);
                return self;
            }
        }
    }

    public partial struct Result<T>
    {
        /// <summary>On success, awaits the value <paramref name="mapFunc"/> gives for the value and <paramref name="cancellationToken"/>, as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Maps the value; it receives <paramref name="cancellationToken"/>.</param>
        /// <param name="cancellationToken">Passed to <paramref name="mapFunc"/>. If it is already cancelled on a success, the task is cancelled and <paramref name="mapFunc"/> isn't called.</param>
        /// <remarks>An exception <paramref name="mapFunc"/> throws faults the task; it never becomes a failure.</remarks>
        public Task<Result<TResult>> MapAsync<TResult>(Func<T, CancellationToken, Task<TResult>> mapFunc, CancellationToken cancellationToken)
        {
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Case() ? Run(mapFunc, _value, cancellationToken) : Task.FromResult(_error.Fail<TResult>());

            static async Task<Result<TResult>> Run(Func<T, CancellationToken, Task<TResult>> f, T value, CancellationToken ct)
                => (await AsyncStep.Run(f, value, ct).ConfigureAwait(false)).Ok();
        }

        /// <summary>On success, awaits the result <paramref name="bindFunc"/> returns for the value and <paramref name="cancellationToken"/>. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="bindFunc">Returns the next result from the value; it receives <paramref name="cancellationToken"/>.</param>
        /// <param name="cancellationToken">Passed to <paramref name="bindFunc"/>. If it is already cancelled on a success, the task is cancelled and <paramref name="bindFunc"/> isn't called.</param>
        /// <remarks>An exception <paramref name="bindFunc"/> throws faults the task; it never becomes a failure.</remarks>
        public Task<Result<TResult>> BindAsync<TResult>(Func<T, CancellationToken, Task<Result<TResult>>> bindFunc, CancellationToken cancellationToken)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            return Case() ? AsyncStep.Run(bindFunc, _value, cancellationToken) : Task.FromResult(_error.Fail<TResult>());
        }

        /// <summary>On success, calls <paramref name="asyncFunc"/> with the value and <paramref name="cancellationToken"/>, awaits it, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <param name="asyncFunc">Called with the value; it receives <paramref name="cancellationToken"/>.</param>
        /// <param name="cancellationToken">Passed to <paramref name="asyncFunc"/>. If it is already cancelled on a success, the task is cancelled and <paramref name="asyncFunc"/> isn't called.</param>
        /// <remarks>An exception <paramref name="asyncFunc"/> throws faults the task; it never becomes a failure.</remarks>
        public Task<Result<T>> TeeAsync(Func<T, CancellationToken, Task> asyncFunc, CancellationToken cancellationToken)
        {
            asyncFunc.ThrowIfDefault(nameof(asyncFunc));
            return Case() ? Run(asyncFunc, this, _value, cancellationToken) : Task.FromResult(this);

            static async Task<Result<T>> Run(Func<T, CancellationToken, Task> f, Result<T> self, T value, CancellationToken ct)
            {
                await AsyncStep.Run(f, value, ct).ConfigureAwait(false);
                return self;
            }
        }
    }

    public static partial class Result
    {
        /// <summary>Calls <paramref name="func"/> and returns its value as a success. If it throws, returns a failure with the error <paramref name="onException"/> builds from the exception.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="func">The work that may throw.</param>
        /// <param name="onException">Builds the error from the exception.</param>
        /// <remarks>The opt-in way to turn an exception into a failure: nothing else in the core catches. Never caught: an <see cref="OperationCanceledException"/> (cancellation propagates), an <see cref="AggregateException"/> of cancellations only, and the fatal <see cref="OutOfMemoryException"/>, <see cref="StackOverflowException"/>, <see cref="AccessViolationException"/> and <see cref="ThreadAbortException"/>.</remarks>
        public static Result<T> Try<T>(Func<T> func, Func<Exception, Error> onException)
        {
            func.ThrowIfDefault(nameof(func));
            onException.ThrowIfDefault(nameof(onException));
            T value;
            try
            {
                value = func();
            }
            catch (Exception e) when (AsyncStep.IsCaught(e))
            {
                return onException(e).Fail<T>();
            }

            return value.Ok();
        }

        /// <summary>Calls <paramref name="func"/>, awaits it, and returns its value as a success. If it throws, or its task faults, returns a failure with the error <paramref name="onException"/> builds from the exception.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="func">The work that may throw.</param>
        /// <param name="onException">Builds the error from the exception.</param>
        /// <remarks>The opt-in way to turn an exception into a failure: nothing else in the core catches. Never caught: an <see cref="OperationCanceledException"/> (a cancelled task included: the returned task is cancelled), an <see cref="AggregateException"/> of cancellations only, and the fatal <see cref="OutOfMemoryException"/>, <see cref="StackOverflowException"/>, <see cref="AccessViolationException"/> and <see cref="ThreadAbortException"/>.</remarks>
        public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> func, Func<Exception, Error> onException)
        {
            func.ThrowIfDefault(nameof(func));
            onException.ThrowIfDefault(nameof(onException));
            T value;
            try
            {
                value = await func().ConfigureAwait(false);
            }
            catch (Exception e) when (AsyncStep.IsCaught(e))
            {
                return onException(e).Fail<T>();
            }

            return value.Ok();
        }
        /// <summary>Calls <paramref name="func"/> with <paramref name="cancellationToken"/>, awaits it, and returns its value as a success. If it throws, or its task faults, returns a failure with the error <paramref name="onException"/> builds from the exception.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="func">The work that may throw; it receives <paramref name="cancellationToken"/>.</param>
        /// <param name="onException">Builds the error from the exception.</param>
        /// <param name="cancellationToken">Passed to <paramref name="func"/>. If it is already cancelled, the task is cancelled and <paramref name="func"/> isn't called.</param>
        /// <remarks>Catches what <see cref="TryAsync{T}(Func{Task{T}}, Func{Exception, Error})"/> catches; cancellation always propagates.</remarks>
        public static Task<Result<T>> TryAsync<T>(Func<CancellationToken, Task<T>> func, Func<Exception, Error> onException, CancellationToken cancellationToken)
        {
            func.ThrowIfDefault(nameof(func));
            onException.ThrowIfDefault(nameof(onException));
            return TryAsync(() => AsyncStep.Run((_, ct) => func(ct), 0, cancellationToken), onException);
        }
#if NET

        // ValueTask receivers and IAsyncEnumerable helpers, net10.0 and later: netstandard2.0 has neither type without a
        // package, and the core references nothing (decision 1).

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static ValueTask<Result<TResult, TError>> Map<TResult, T, TError>(this ValueTask<Result<T, TError>> value, Func<T, TResult> mapFunc)
        {
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, mapFunc);

            static async ValueTask<Result<TResult, TError>> Core(ValueTask<Result<T, TError>> value, Func<T, TResult> mapFunc)
                => (await value.ConfigureAwait(false)).Map(mapFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public static ValueTask<Result<TResult, TError>> Bind<TResult, T, TError>(this ValueTask<Result<T, TError>> value, Func<T, Result<TResult, TError>> bindFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            return Core(value, bindFunc);

            static async ValueTask<Result<TResult, TError>> Core(ValueTask<Result<T, TError>> value, Func<T, Result<TResult, TError>> bindFunc)
                => (await value.ConfigureAwait(false)).Bind(bindFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then: on success, awaits and returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public static ValueTask<Result<TResult, TError>> BindAsync<TResult, T, TError>(this ValueTask<Result<T, TError>> value, Func<T, ValueTask<Result<TResult, TError>>> bindFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            return Core(value, bindFunc);

            static async ValueTask<Result<TResult, TError>> Core(ValueTask<Result<T, TError>> value, Func<T, ValueTask<Result<TResult, TError>>> bindFunc)
            {
                var result = await value.ConfigureAwait(false);
                return result.TryGetValue(out var item)
                    ? await bindFunc(item).ConfigureAwait(false)
                    : result.Error.Fail<TResult, TError>();
            }
        }

        /// <summary>Awaits <paramref name="value"/>, then: on failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error.</param>
        public static ValueTask<Result<T, TErrorResult>> MapError<T, TError, TErrorResult>(this ValueTask<Result<T, TError>> value, Func<TError, TErrorResult> errorMapFunc)
        {
            errorMapFunc.ThrowIfDefault(nameof(errorMapFunc));
            return Core(value, errorMapFunc);

            static async ValueTask<Result<T, TErrorResult>> Core(ValueTask<Result<T, TError>> value, Func<TError, TErrorResult> errorMapFunc)
                => (await value.ConfigureAwait(false)).MapError(errorMapFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with the value on success, or <paramref name="errorFunc"/> with the error on failure, and returns what it returns.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public static ValueTask<TResult> Match<T, TError, TResult>(this ValueTask<Result<T, TError>> value, Func<T, TResult> successFunc, Func<TError, TResult> errorFunc)
        {
            successFunc.ThrowIfDefault(nameof(successFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            return Core(value, successFunc, errorFunc);

            static async ValueTask<TResult> Core(ValueTask<Result<T, TError>> value, Func<T, TResult> successFunc, Func<TError, TResult> errorFunc)
                => (await value.ConfigureAwait(false)).Match(successFunc, errorFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="action"/> with the value on success, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="action">Called with the value.</param>
        public static ValueTask<Result<T, TError>> Tee<T, TError>(this ValueTask<Result<T, TError>> value, Action<T> action)
        {
            action.ThrowIfDefault(nameof(action));
            return Core(value, action);

            static async ValueTask<Result<T, TError>> Core(ValueTask<Result<T, TError>> value, Action<T> action)
                => (await value.ConfigureAwait(false)).Tee(action);
        }

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static ValueTask<Result<TResult>> Map<T, TResult>(this ValueTask<Result<T>> value, Func<T, TResult> mapFunc)
        {
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, mapFunc);

            static async ValueTask<Result<TResult>> Core(ValueTask<Result<T>> value, Func<T, TResult> mapFunc)
                => (await value.ConfigureAwait(false)).Map(mapFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public static ValueTask<Result<TResult>> Bind<TResult, T>(this ValueTask<Result<T>> value, Func<T, Result<TResult>> bindFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            return Core(value, bindFunc);

            static async ValueTask<Result<TResult>> Core(ValueTask<Result<T>> value, Func<T, Result<TResult>> bindFunc)
                => (await value.ConfigureAwait(false)).Bind(bindFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then: on success, awaits and returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public static ValueTask<Result<TResult>> BindAsync<TResult, T>(this ValueTask<Result<T>> value, Func<T, ValueTask<Result<TResult>>> bindFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            return Core(value, bindFunc);

            static async ValueTask<Result<TResult>> Core(ValueTask<Result<T>> value, Func<T, ValueTask<Result<TResult>>> bindFunc)
            {
                var result = await value.ConfigureAwait(false);
                return result.TryGetValue(out var item)
                    ? await bindFunc(item).ConfigureAwait(false)
                    : result.Error.Fail<TResult>();
            }
        }

        /// <summary>Awaits <paramref name="value"/>, then: on failure, returns a failure with the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error to another <see cref="Error"/>.</param>
        public static ValueTask<Result<T>> MapError<T>(this ValueTask<Result<T>> value, Func<Error, Error> errorMapFunc)
        {
            errorMapFunc.ThrowIfDefault(nameof(errorMapFunc));
            return Core(value, errorMapFunc);

            static async ValueTask<Result<T>> Core(ValueTask<Result<T>> value, Func<Error, Error> errorMapFunc)
                => (await value.ConfigureAwait(false)).MapError(errorMapFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with the value on success, or <paramref name="errorFunc"/> with the error on failure, and returns what it returns.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public static ValueTask<TResult> Match<T, TResult>(this ValueTask<Result<T>> value, Func<T, TResult> successFunc, Func<Error, TResult> errorFunc)
        {
            successFunc.ThrowIfDefault(nameof(successFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            return Core(value, successFunc, errorFunc);

            static async ValueTask<TResult> Core(ValueTask<Result<T>> value, Func<T, TResult> successFunc, Func<Error, TResult> errorFunc)
                => (await value.ConfigureAwait(false)).Match(successFunc, errorFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="action"/> with the value on success, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="action">Called with the value.</param>
        public static ValueTask<Result<T>> Tee<T>(this ValueTask<Result<T>> value, Action<T> action)
        {
            action.ThrowIfDefault(nameof(action));
            return Core(value, action);

            static async ValueTask<Result<T>> Core(ValueTask<Result<T>> value, Action<T> action)
                => (await value.ConfigureAwait(false)).Tee(action);
        }

        /// <summary>Reads <paramref name="values"/> until the first failure, and returns it; if every item is a success, returns a success with the values in order.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results to read.</param>
        /// <param name="cancellationToken">Passed to the enumerator and checked before each item: a cancelled token cancels the task.</param>
        public static ValueTask<Result<IReadOnlyList<T>, TError>> CollectAsync<T, TError>(this IAsyncEnumerable<Result<T, TError>> values, CancellationToken cancellationToken)
        {
            values.ThrowIfDefault(nameof(values));
            return Core(values, cancellationToken);

            static async ValueTask<Result<IReadOnlyList<T>, TError>> Core(IAsyncEnumerable<Result<T, TError>> values, CancellationToken cancellationToken)
            {
                var list = new List<T>();
                await foreach (var item in values.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!item.TryGetValue(out var value))
                        return item.Error.Fail<IReadOnlyList<T>, TError>();
                    list.Add(value);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return list.AsReadOnly().Ok<IReadOnlyList<T>, TError>();
            }
        }

        /// <summary>Reads <paramref name="values"/> until the first failure, and returns it; if every item is a success, returns a success with the values in order.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <param name="values">The results to read.</param>
        /// <param name="cancellationToken">Passed to the enumerator and checked before each item: a cancelled token cancels the task.</param>
        public static ValueTask<Result<IReadOnlyList<T>>> CollectAsync<T>(this IAsyncEnumerable<Result<T>> values, CancellationToken cancellationToken)
        {
            values.ThrowIfDefault(nameof(values));
            return Core(values, cancellationToken);

            static async ValueTask<Result<IReadOnlyList<T>>> Core(IAsyncEnumerable<Result<T>> values, CancellationToken cancellationToken)
            {
                var list = new List<T>();
                await foreach (var item in values.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!item.TryGetValue(out var value))
                        return item.Error.Fail<IReadOnlyList<T>>();
                    list.Add(value);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return list.AsReadOnly().Ok<IReadOnlyList<T>>();
            }
        }

        /// <summary>Reads every item of <paramref name="values"/>, and returns the successes' values and the failures' errors, each in order.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results to read.</param>
        /// <param name="cancellationToken">Passed to the enumerator and checked before each item: a cancelled token cancels the task.</param>
        public static ValueTask<(IReadOnlyList<T> Values, IReadOnlyList<TError> Errors)> PartitionAsync<T, TError>(this IAsyncEnumerable<Result<T, TError>> values, CancellationToken cancellationToken)
        {
            values.ThrowIfDefault(nameof(values));
            return Core(values, cancellationToken);

            static async ValueTask<(IReadOnlyList<T> Values, IReadOnlyList<TError> Errors)> Core(IAsyncEnumerable<Result<T, TError>> values, CancellationToken cancellationToken)
            {
                var successes = new List<T>();
                var errors = new List<TError>();
                await foreach (var item in values.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.TryGetValue(out var value))
                        successes.Add(value);
                    else
                        errors.Add(item.Error);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return (successes.AsReadOnly(), errors.AsReadOnly());
            }
        }

        /// <summary>Reads every item of <paramref name="values"/>, and returns the successes' values and the failures' errors, each in order.</summary>
        /// <typeparam name="T">The type of the values.</typeparam>
        /// <param name="values">The results to read.</param>
        /// <param name="cancellationToken">Passed to the enumerator and checked before each item: a cancelled token cancels the task.</param>
        public static ValueTask<(IReadOnlyList<T> Values, IReadOnlyList<Error> Errors)> PartitionAsync<T>(this IAsyncEnumerable<Result<T>> values, CancellationToken cancellationToken)
        {
            values.ThrowIfDefault(nameof(values));
            return Core(values, cancellationToken);

            static async ValueTask<(IReadOnlyList<T> Values, IReadOnlyList<Error> Errors)> Core(IAsyncEnumerable<Result<T>> values, CancellationToken cancellationToken)
            {
                var successes = new List<T>();
                var errors = new List<Error>();
                await foreach (var item in values.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.TryGetValue(out var value))
                        successes.Add(value);
                    else
                        errors.Add(item.Error);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return (successes.AsReadOnly(), errors.AsReadOnly());
            }
        }
#endif
    }

    // Runs a user's async step so that a synchronous throw faults the task instead of escaping the call, and so that an
    // already-cancelled token cancels it before the step runs. Never catches.
    internal static class AsyncStep
    {
        internal static async Task<TResult> Run<TArg, TResult>(Func<TArg, CancellationToken, Task<TResult>> func, TArg arg, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await func(arg, cancellationToken).ConfigureAwait(false);
        }

        internal static async Task Run<TArg>(Func<TArg, CancellationToken, Task> func, TArg arg, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await func(arg, cancellationToken).ConfigureAwait(false);
        }

        internal static async Task Await<TArg>(Func<TArg, Task> func, TArg arg)
            => await func(arg).ConfigureAwait(false);

        internal static async Task<TResult> Await<TArg, TResult>(Func<TArg, Task<TResult>> func, TArg arg)
            => await func(arg).ConfigureAwait(false);

        // What Result.Try and Result.TryAsync turn into a failure: anything but cancellation and the fatal exceptions.
        internal static bool IsCaught(Exception e) => e switch
        {
            OperationCanceledException or OutOfMemoryException or StackOverflowException or AccessViolationException or ThreadAbortException => false,
            AggregateException a => !(a.InnerExceptions.Count > 0 && a.InnerExceptions.All(i => i is OperationCanceledException)),
            _ => true,
        };
    }
}
