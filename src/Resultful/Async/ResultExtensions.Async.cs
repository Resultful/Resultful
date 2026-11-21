using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resultful.Utils;
using TaskExt;

namespace Resultful
{

    public static partial class Result
    {
        //Switch on Task<Result<T>>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public static Task Switch<T>(this Task<Result<T>> value, Action<T> successFunc, Action<Error> errorFunc)
            => value.Discard(item => item.Switch(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and returns a task that completes when the call's task does.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public static Task SwitchAsync<T>(this Task<Result<T>> value, Func<T, Task> successFunc, Func<Error, Task> errorFunc)
            => value.DiscardAsync(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //Match on Task<Option<T>>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and returns its task.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public static Task<TResult> MatchAsync<T, TResult>(this Task<Result<T>> value, Func<T, Task<TResult>> successFunc, Func<Error, Task<TResult>> errorFunc)
            => value.Bind(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and returns what it returns.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public static Task<TResult> Match<T, TResult>(this Task<Result<T>> value, Func<T, TResult> successFunc, Func<Error, TResult> errorFunc)
            => value.Map(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //Switch on Task<Result<T, TError>>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public static Task Switch<T, TError>(this Task<Result<T, TError>> value, Action<T> successFunc, Action<TError> errorFunc)
            => value.Discard(item => item.Switch(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and returns a task that completes when the call's task does.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public static Task SwitchAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, Task> successFunc, Func<TError, Task> errorFunc)
            => value.DiscardAsync(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //Match on Task<Result<T, TError>>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and returns its task.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public static Task<TResult> MatchAsync<T, TError, TResult>(this Task<Result<T, TError>> value, Func<T, Task<TResult>> successFunc, Func<TError, Task<TResult>> errorFunc)
            => value.Bind(item => item.MatchAsync(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and returns what it returns.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public static Task<TResult> Match<T, TError, TResult>(this Task<Result<T, TError>> value, Func<T, TResult> successFunc, Func<TError, TResult> errorFunc)
            => value.Map(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //BindAsync on Result<T, TError>
        /// <summary>Awaits <paramref name="value"/>, then: on success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public static Task<Result<TResult, TError>> Bind<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Result<TResult, TError>> bindFunc)
            => value.Map(item => item.Bind(bindFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public static Task<Result<TResult, TError>> BindAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Task<Result<TResult, TError>>> bindFunc)
            => value.Bind(item => item.BindAsync(bindFunc));

        //BindAsync on Result<T>
        /// <summary>Awaits <paramref name="value"/>, then: on success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public static Task<Result<TResult>> Bind<TResult, T>(this Task<Result<T>> value, Func<T, Result<TResult>> bindFunc)
            => value.Map(item => item.Bind(bindFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public static Task<Result<TResult>> BindAsync<TResult, T>(this Task<Result<T>> value, Func<T, Task<Result<TResult>>> bindFunc)
            => value.Bind(item => item.BindAsync(bindFunc));

        //OrElse and ValueOr on Task<Result<…>>
        /// <summary>Awaits <paramref name="value"/>, then returns it if it is a success, otherwise <paramref name="other"/>. The first success wins.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="other">The result to use when the awaited result is a failure.</param>
        public static Task<Result<T, TError>> OrElse<T, TError>(this Task<Result<T, TError>> value, Result<T, TError> other)
            => value.Map(item => item.OrElse(other));

        /// <summary>Awaits <paramref name="value"/>, then returns it if it is a success, otherwise <paramref name="other"/>. The first success wins.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="other">The result to use when the awaited result is a failure.</param>
        public static Task<Result<T>> OrElse<T>(this Task<Result<T>> value, Result<T> other)
            => value.Map(item => item.OrElse(other));

        /// <summary>Awaits <paramref name="value"/>, then returns the value of a success, otherwise <paramref name="fallback"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="fallback">The value to return when the awaited result is a failure.</param>
        public static Task<T> ValueOr<T, TError>(this Task<Result<T, TError>> value, T fallback)
            => value.Map(item => item.ValueOr(fallback));

        /// <summary>Awaits <paramref name="value"/>, then returns the value of a success, otherwise <paramref name="fallback"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="fallback">The value to return when the awaited result is a failure.</param>
        public static Task<T> ValueOr<T>(this Task<Result<T>> value, T fallback)
            => value.Map(item => item.ValueOr(fallback));

        //MapAsync on Result<T, TError>
        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Task<Result<TResult, TError>> Map<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, TResult> mapFunc)
            => value.Map(item => item.Map(mapFunc));

        /// <summary>Awaits <paramref name="value"/>, then maps both sides: the value with <paramref name="mapFunc"/> on success, or the error with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Task<Result<TResult, TErrorResult>> Map2<TResult, T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<TError, TErrorResult> errorMapFunc, Func<T, TResult> mapFunc)
            => value.Map(item => item.Map2(errorMapFunc, mapFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Task<Result<TResult, TError>> MapAsync<TResult, T, TError>(this Task<Result<T, TError>> value, Func<T, Task<TResult>> mapFunc)
            => value.Bind(item => item.MapAsync(mapFunc));

        /// <summary>Awaits <paramref name="value"/>, then maps both sides: the value with <paramref name="mapFunc"/> on success, or the error with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, T, TError, TErrorResult>(
            this Task<Result<T, TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc, Func<T, Task<TResult>> mapFunc)
            => value.Bind(item => item.Map2Async(errorMapFunc, mapFunc));

        //MapAsync on Result<T>
        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Task<Result<TResult>> Map<T, TResult>(this Task<Result<T>> value, Func<T, TResult> mapFunc)
            => value.Map(item => item.Map(mapFunc));

        /// <summary>Awaits <paramref name="value"/>, then maps both sides: the value with <paramref name="mapFunc"/> on success, or the errors with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the errors.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Task<Result<TResult, TError>> Map2<TResult, T, TError>(this Task<Result<T>> value, Func<Error, TError> errorMapFunc, Func<T, TResult> mapFunc)
            => value.Map(item => item.Map2(errorMapFunc, mapFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Task<Result<TResult>> MapAsync<T, TResult>(this Task<Result<T>> value, Func<T, Task<TResult>> mapFunc)
            => value.Bind(item => item.MapAsync(mapFunc));

        /// <summary>Awaits <paramref name="value"/>, then maps both sides: the value with <paramref name="mapFunc"/> on success, or the errors with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the errors.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Task<Result<TResult, TError>> Map2Async<TResult, T, TError>(this Task<Result<T>> value, Func<Error, Task<TError>> errorMapFunc, Func<T, Task<TResult>> mapFunc)
            => value.Bind(item => item.Map2Async(errorMapFunc, mapFunc));

        //MapErrorAsync on Result<T, TError>
        /// <summary>Awaits <paramref name="value"/>, then: on failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error.</param>
        public static Task<Result<T, TErrorResult>> MapError<T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<TError, TErrorResult> errorMapFunc)
            => value.Map(item => item.MapError(errorMapFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error.</param>
        public static Task<Result<T, TErrorResult>> MapErrorAsync<T, TError, TErrorResult>(this Task<Result<T, TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc)
            => value.Bind(item => item.MapErrorAsync(errorMapFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on failure, maps the error to an <see cref="Error"/>. A success keeps its value.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error.</param>
        public static Task<Result<T>> MapError<T, TError>(this Task<Result<T, TError>> value, Func<TError, Error> errorMapFunc)
            => value.Map(item => item.MapError(errorMapFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on failure, maps the error to an <see cref="Error"/>. A success keeps its value.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error.</param>
        public static Task<Result<T>> MapErrorAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, Task<Error>> errorMapFunc)
            => value.Bind(item => item.MapErrorAsync(errorMapFunc));

        //MapError on Result<T>
        /// <summary>Awaits <paramref name="value"/>, then: on failure, returns a failure with the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error to another <see cref="Error"/>.</param>
        public static Task<Result<T>> MapError<T>(this Task<Result<T>> value, Func<Error, Error> errorMapFunc)
            => value.Map(item => item.MapError(errorMapFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on failure, returns a failure with the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error to another <see cref="Error"/>.</param>
        public static Task<Result<T>> MapErrorAsync<T>(this Task<Result<T>> value, Func<Error, Task<Error>> errorMapFunc)
            => value.Bind(item => item.MapErrorAsync(errorMapFunc));

        //TeeAsync on Result<T, TError>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="action"/> with the value on success, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="action">Called with the value.</param>
        public static Task<Result<T, TError>> Tee<T, TError>(this Task<Result<T, TError>> value, Action<T> action)
            => value.Map(item => item.Tee(action));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="asyncFunc"/> with the value on success, awaits it, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="asyncFunc">Called with the value.</param>
        public static Task<Result<T, TError>> TeeAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, Task> asyncFunc)
            => value.Bind(item => item.TeeAsync(asyncFunc));

        //TeeAsync on Result<T>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="action"/> with the value on success, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="action">Called with the value.</param>
        public static Task<Result<T>> Tee<T>(this Task<Result<T>> value, Action<T> action)
            => value.Map(item => item.Tee(action));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="asyncFunc"/> with the value on success, awaits it, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="asyncFunc">Called with the value.</param>
        public static Task<Result<T>> TeeAsync<T>(this Task<Result<T>> value, Func<T, Task> asyncFunc)
            => value.Tee(item => item.TeeAsync(asyncFunc).Discard());

        //TeeErrorAsync on Result<T, TError>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="action"/> with the error on failure, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="action">Called with the error.</param>
        public static Task<Result<T, TError>> TeeError<T, TError>(this Task<Result<T, TError>> value, Action<TError> action)
            => value.Map(item => item.TeeError(action));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="asyncFunc"/> with the error on failure, awaits it, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="asyncFunc">Called with the error.</param>
        public static Task<Result<T, TError>> TeeErrorAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, Task> asyncFunc)
            => value.Tee(item => item.TeeErrorAsync(asyncFunc).Discard());

        //TeeErrorAsync on Result<T>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="action"/> with the error on failure, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="action">Called with the error.</param>
        public static Task<Result<T>> TeeError<T>(this Task<Result<T>> value, Action<Error> action)
            => value.Map(item => item.TeeError(action));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="asyncFunc"/> with the error on failure, awaits it, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="asyncFunc">Called with the error.</param>
        public static Task<Result<T>> TeeErrorAsync<T>(this Task<Result<T>> value, Func<Error, Task> asyncFunc)
            => value.Tee(item => item.TeeErrorAsync(asyncFunc).Discard());


        //FlattenAsync on Result<T, TError>
        /// <summary>Awaits <paramref name="value"/>, then returns the inner result if the outer result is a success, or the outer error if it is a failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        public static Task<Result<T, TError>> Flatten<T, TError>(this Task<Result<Result<T, TError>, TError>> value)
            => value.Map(item => item.Flatten());

        /// <summary>Awaits <paramref name="value"/>, then returns the inner result if the outer result is a success, or the outer error if it is a failure.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<VoidResult<TError>> Flatten<TError>(this Task<Result<VoidResult<TError>, TError>> value)
            => value.Map(item => item.Flatten());

        //FlattenAsync on Result<T>
        /// <summary>Awaits <paramref name="value"/>, then returns the inner result if the outer result is a success, or the outer error if it is a failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        public static Task<Result<T>> Flatten<T>(this Task<Result<Result<T>>> value)
            => value.Map(item => item.Flatten());

        /// <summary>Awaits <paramref name="value"/>, then returns the inner result if the outer result is a success, or the outer error if it is a failure.</summary>
        /// <param name="value">The task that produces the result.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<VoidResult> Flatten(this Task<Result<VoidResult>> value)
            => value.Map(item => item.Flatten());

        //UnwrapAsync on Result<T, TError>
        /// <summary>On success, awaits the task the result holds and returns its value as a success. A failure keeps the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        public static Task<Result<T, TError>> UnwrapAsync<T, TError>(this Result<Task<T>, TError> value)
            => value.Match(
                async item => (await item.ConfigureAwait(false)).Ok<T, TError>(),
                errors => Task.FromResult(errors.Fail<T, TError>()));

        /// <summary>On failure, awaits the task that holds the error and returns its error as a failure. A success keeps its value.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        public static Task<Result<T, TError>> UnwrapAsync<T, TError>(this Result<T, Task<TError>> value)
            => value.Match(
                item => Task.FromResult(item.Ok<T, TError>()),
                async errors => (await errors.ConfigureAwait(false)).Fail<T, TError>());

        /// <summary>On success, awaits the task the result holds and returns a success with no value. A failure keeps the error.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        public static Task<Result<Unit, TError>> UnwrapAsync<TError>(this Result<Task, TError> value)
            => value.Match(
                async item =>
                {
                    await item.ConfigureAwait(false);
                    return Ok<TError>();
                },
                errors => Task.FromResult(errors.Fail<Unit, TError>()));

        //UnwrapAsync on Result<T>
        /// <summary>On success, awaits the task the result holds and returns its value as a success. A failure keeps the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The result.</param>
        public static Task<Result<T>> Unwrap<T>(this Result<Task<T>> value)
            => value.Match(
                async item => (await item.ConfigureAwait(false)).Ok(),
                errors => Task.FromResult(errors.Fail<T>()));

        /// <summary>On success, awaits the task the result holds and returns a success with no value. A failure keeps the error.</summary>
        /// <typeparam name="T">Not used. It can't be inferred, so the caller names it.</typeparam>
        /// <param name="value">The result.</param>
        public static Task<Result<Unit>> Unwrap<T>(this Result<Task> value)
            => value.Match(
                async item =>
                {
                    await item.ConfigureAwait(false);
                    return Ok();
                },
                errors => Task.FromResult(Fail(errors)));

        //DiscardErrorAsync on Result<T, TError>
        /// <summary>Converts to an option: the value on success, or None on failure, after calling <paramref name="errorAction"/> with the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="errorAction">Called with the error before it is dropped.</param>
        public static Task<Option<T>> DiscardErrorAsync<T, TError>(this Result<T, TError> value, Func<TError, Task> errorAction)
            => value.Match(
                item => Task.FromResult(item.Some()),
                async errors =>
                {
                    await errorAction.ThrowIfDefault(nameof(errorAction))(errors).ConfigureAwait(false);
                    return Option<T>.None;
                });

        /// <summary>Awaits <paramref name="value"/>, then converts to an option: the value on success, or None on failure, after calling <paramref name="errorAction"/> with the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorAction">Called with the error before it is dropped.</param>
        public static Task<Option<T>> DiscardError<T, TError>(this Task<Result<T, TError>> value, Action<TError> errorAction)
            => value.Map(item => item.DiscardError(errorAction));

        /// <summary>Awaits <paramref name="value"/>, then converts to an option: the value on success, or None on failure, after calling <paramref name="errorAction"/> with the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorAction">Called with the error before it is dropped.</param>
        public static Task<Option<T>> DiscardErrorAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, Task> errorAction)
            => value.Bind(item => item.DiscardErrorAsync(errorAction));

        /// <summary>Awaits <paramref name="value"/>, then converts to an option: the value on success, or None on failure. The error is dropped.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        public static Task<Option<T>> DiscardError<T, TError>(this Task<Result<T, TError>> value)
            => value.Map(x => x.DiscardError());

        //DiscardErrorAsync on Result<T>
        /// <summary>Awaits <paramref name="value"/>, then converts to an option: the value on success, or None on failure, after calling <paramref name="errorAction"/> with the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorAction">Called with the error before it is dropped.</param>
        public static Task<Option<T>> DiscardError<T>(this Task<Result<T>> value, Action<Error> errorAction)
            => value.Map(item => item.DiscardError(errorAction));

        /// <summary>Awaits <paramref name="value"/>, then converts to an option: the value on success, or None on failure, after calling <paramref name="errorAction"/> with the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorAction">Called with the error before it is dropped.</param>
        public static Task<Option<T>> DiscardErrorAsync<T>(this Task<Result<T>> value, Func<Error, Task> errorAction)
            => value.Bind(item => item.DiscardErrorAsync(errorAction));

        /// <summary>Awaits <paramref name="value"/>, then converts to an option: the value on success, or None on failure. The error is dropped.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        public static Task<Option<T>> DiscardError<T>(this Task<Result<T>> value)
            => value.Map(item => item.DiscardError());

        //DiscardValueAsync on Result<T, TError>
        /// <summary>Awaits <paramref name="value"/>, then: drops the value: a success becomes a success with no value, and a failure keeps the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        public static Task<Result<Unit, TError>> DiscardValue<T, TError>(this Task<Result<T, TError>> value)
            => value.Map(item => item.DiscardValue());

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the result <paramref name="bindFunc"/> gives for the value, without a value. A failure keeps the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns a result without a value from the value.</param>
        public static Task<Result<Unit, TError>> DiscardValue<T, TError>(this Task<Result<T, TError>> value, Func<T, Result<Unit, TError>> bindFunc)
            => value.Map(x => x.DiscardValue(bindFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the result <paramref name="bindFunc"/> gives for the value, without a value. A failure keeps the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns a result without a value from the value.</param>
        public static Task<Result<Unit, TError>> DiscardValueAsync<T, TError>(this Task<Result<T, TError>> value, Func<T, Task<Result<Unit, TError>>> bindFunc)
            => value.Bind(x => x.DiscardValueAsync(bindFunc));

        //DiscardValueAsync on Result<T>
        /// <summary>Awaits <paramref name="value"/>, then: drops the value: a success becomes a success with no value, and a failure keeps the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        public static Task<Result<Unit>> DiscardValue<T>(this Task<Result<T>> value)
            => value.Map(item => item.DiscardValue());

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the result <paramref name="bindFunc"/> gives for the value, without a value. A failure keeps the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns a result without a value from the value.</param>
        public static Task<Result<Unit>> DiscardValue<T>(this Task<Result<T>> value, Func<T, Result<Unit>> bindFunc)
            => value.Map(x => x.DiscardValue(bindFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the result <paramref name="bindFunc"/> gives for the value, without a value. A failure keeps the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns a result without a value from the value.</param>
        public static Task<Result<Unit>> DiscardValueAsync<T>(this Task<Result<T>> value, Func<T, Task<Result<Unit>>> bindFunc)
            => value.Bind(x => x.DiscardValueAsync(bindFunc));


        //ReturnOrValue on Result<T>
        /// <summary>Awaits <paramref name="value"/>, then returns the value on success, or the value <paramref name="func"/> gives for the error on failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="func">Gives a value from the error.</param>
        public static Task<T> ReturnOrValue<T>(this Task<Result<T>> value, Func<Error, T> func)
            => value.Map(x => x.ReturnOrValue(func));

        /// <summary>Awaits <paramref name="value"/>, then returns the value on success, or the value <paramref name="func"/> gives for the error on failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="func">Gives a value from the error.</param>
        public static Task<T> ReturnOrValueAsync<T>(this Task<Result<T>> value, Func<Error, Task<T>> func)
            => value.Bind(x => x.ReturnOrValueAsync(func));

        //ReturnOrFail on Result<T>
        /// <summary>Awaits <paramref name="value"/>, then returns the value on success. On failure, throws the exception <paramref name="func"/> builds from the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TExn">The type of the exception.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="func">Builds the exception to throw from the error.</param>
        public static Task<T> ReturnOrFail<T, TExn>(this Task<Result<T>> value, Func<Error, TExn> func) where TExn : Exception
            => value.Map(x => x.ReturnOrFail(func));

        //ReturnOrValue on Result<T, TError>
        /// <summary>Awaits <paramref name="value"/>, then returns the value on success, or the value <paramref name="func"/> gives for the error on failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="func">Gives a value from the error.</param>
        public static Task<T> ReturnOrValue<T, TError>(this Task<Result<T, TError>> value, Func<TError, T> func)
            => value.Map(x => x.ReturnOrValue(func));

        /// <summary>Awaits <paramref name="value"/>, then returns the value on success, or the value <paramref name="func"/> gives for the error on failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="func">Gives a value from the error.</param>
        public static Task<T> ReturnOrValueAsync<T, TError>(this Task<Result<T, TError>> value, Func<TError, Task<T>> func)
            => value.Bind(x => x.ReturnOrValueAsync(func));

        //ReturnOrFail on Result<T, TError>
        /// <summary>Awaits <paramref name="value"/>, then returns the value on success. On failure, throws the exception <paramref name="func"/> builds from the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TExn">The type of the exception.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="func">Builds the exception to throw from the error.</param>
        public static Task<T> ReturnOrFail<T, TError, TExn>(this Task<Result<T, TError>> value, Func<TError, TExn> func) where TExn : Exception
            => value.Map(x => x.ReturnOrFail(func));

        //FoldUntilAsync on Result<T>
        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Task<Result<TResult>> FoldUntilAsync<TResult, T>(this IEnumerable<Result<T>> values,
            TResult seed, Func<TResult, T, Task<TResult>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).FoldUntilAsync(seed.Ok(), aggrFunc);

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value. If it is a failure, its error is kept.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static async Task<Result<TResult>> FoldUntilAsync<TResult, T>(this IEnumerable<Result<T>> values,
            Result<TResult> seed, Func<TResult, T, Task<TResult>> aggrFunc) =>
            await values.FoldUntilAsync(seed,
                    (acc, value) => AggrHelper.FuncUntilAsync(acc, value, aggrFunc))
                .ConfigureAwait(false);

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The task that gives the starting value. It is awaited first.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static async Task<Result<TResult>> FoldUntilAsync<TResult, T>(this IEnumerable<Result<T>> values,
            Task<Result<TResult>> seed, Func<TResult, T, Task<TResult>> aggrFunc)
        {
            var seedValue = await seed.ConfigureAwait(false);
            return await values.FoldUntilAsync(seedValue, aggrFunc)
                .ConfigureAwait(false);
        }

        //ReduceUntil on Result<T>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Task<Result<T>> ReduceUntilAsync<T>(this IEnumerable<Result<T>> values,
            Func<T, T, Task<T>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).ReduceUntilAsync(
                (acc, val) => AggrHelper.FuncUntilAsync(acc, val, aggrFunc));

        //TryReduceUntilAsync on Result<T>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>, stopping at the first failure, which is returned. Returns None if there are no items.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Task<Option<Result<T>>> TryReduceUntilAsync<T>(this IEnumerable<Result<T>> values,
            Func<T, T, Task<T>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).TryReduceUntilAsync(
                (acc, val) => AggrHelper.FuncUntilAsync(acc, val, aggrFunc));

        //FoldAsync on Result<T, TError>
        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <paramref name="mergeFunc"/>.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The task that gives the starting value. It is awaited first.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static async Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(
            this IEnumerable<Result<T, TError>> values,
            Task<Result<TResult, TError>> seed,
            Func<TError, TError, TError> mergeFunc,
            Func<TResult, T, Task<TResult>> aggrFunc)
        {
            var seedVal = await seed.ConfigureAwait(false);
            return await values.FoldAsync(seedVal, mergeFunc, aggrFunc).ConfigureAwait(false);
        }

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <paramref name="mergeFunc"/>.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value. If it is a failure, its errors are kept.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static async Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(
            this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TError, TError, TError> mergeFunc,
            Func<TResult, T, Task<TResult>> aggrFunc)
            => await EnumerableExtensions.FoldAsync(values, seed,
                    (acc, value) => AggrHelper.FuncAsync(acc, value, aggrFunc, mergeFunc))
                .ConfigureAwait(false);

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <paramref name="mergeFunc"/>.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(
            this IEnumerable<Result<T, TError>> values,
            TResult seed, Func<TError, TError, TError> mergeFunc,
            Func<TResult, T, Task<TResult>> aggrFunc)
            => values.FoldAsync(seed.Ok<TResult, TError>(), mergeFunc, aggrFunc);

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <typeparamref name="TError"/>'s <c>Plus</c>.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(
            this IEnumerable<Result<T, TError>> values,
            TResult seed,
            Func<TResult, T, Task<TResult>> aggrFunc) where TError : IPlus<TError, TError>
            => values.FoldAsync(seed, PlusHelper.Plus, aggrFunc);

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <typeparamref name="TError"/>'s <c>Plus</c>.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value. If it is a failure, its errors are kept.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(
            this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed,
            Func<TResult, T, Task<TResult>> aggrFunc) where TError : IPlus<TError, TError>
            => values.FoldAsync(seed, PlusHelper.Plus, aggrFunc);

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <typeparamref name="TError"/>'s <c>Plus</c>.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The task that gives the starting value. It is awaited first.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Task<Result<TResult, TError>> FoldAsync<TResult, T, TError>(
            this IEnumerable<Result<T, TError>> values,
            Task<Result<TResult, TError>> seed,
            Func<TResult, T, Task<TResult>> aggrFunc) where TError : IPlus<TError, TError>
            => values.FoldAsync(seed, PlusHelper.Plus, aggrFunc);


        //FoldUntil on Result<T, TError>
        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Task<Result<TResult, TError>> FoldUntilAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            TResult seed, Func<TResult, T, Task<TResult>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).FoldUntilAsync(seed.Ok<TResult, TError>(), aggrFunc);

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The starting value. If it is a failure, its errors are kept.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Task<Result<TResult, TError>> FoldUntilAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Result<TResult, TError> seed, Func<TResult, T, Task<TResult>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).FoldUntilAsync(seed,
                (acc, value) => AggrHelper.FuncUntilAsync(acc, value, aggrFunc));

        /// <summary>Combines <paramref name="seed"/> and the values in order with <paramref name="aggrFunc"/>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="TResult">The type of the accumulated value.</typeparam>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="seed">The task that gives the starting value. It is awaited first.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static async Task<Result<TResult, TError>> FoldUntilAsync<TResult, T, TError>(this IEnumerable<Result<T, TError>> values,
            Task<Result<TResult, TError>> seed, Func<TResult, T, Task<TResult>> aggrFunc)
        {
            var seedValue = await seed.ConfigureAwait(false);
            return await FoldUntilAsync(values.ThrowIfDefault(nameof(values)), seedValue, aggrFunc).ConfigureAwait(false);
        }

        //Reduce on Result<T, TError>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <paramref name="mergeFunc"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Task<Result<T, TError>> ReduceAsync<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<TError, TError, TError> mergeFunc, Func<T, T, Task<T>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).ReduceAsync(
                (acc, value) => AggrHelper.FuncAsync(acc, value, aggrFunc, mergeFunc));

        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <typeparamref name="TError"/>'s <c>Plus</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Task<Result<T, TError>> ReduceAsync<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<T, T, Task<T>> aggrFunc) where TError : IPlus<TError, TError>
            => values.ThrowIfDefault(nameof(values)).ReduceAsync(
                (acc, value) => AggrHelper.FuncAsync(acc, value, aggrFunc, PlusHelper.Plus));

        //ReduceUntil on Result<T, TError>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. Stops after the first failure, which is returned. At most one more result is read.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <remarks>Throws <see cref="System.ArgumentException"/> if there are no items.</remarks>
        public static Task<Result<T, TError>> ReduceUntilAsync<T, TError>(this IEnumerable<Result<T, TError>> values, Func<T, T, Task<T>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).ReduceUntilAsync(
                (acc, value) => AggrHelper.FuncUntilAsync(acc, value, aggrFunc));

        //TryReduce on Result<T, TError>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <paramref name="mergeFunc"/>. Returns None if there are no items.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        /// <param name="mergeFunc">Merges two errors into one.</param>
        public static Task<Option<Result<T, TError>>> TryReduceAsync<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<T, T, Task<T>> aggrFunc, Func<TError, TError, TError> mergeFunc)
            => values.ThrowIfDefault(nameof(values)).TryReduceAsync(
                (acc, value) => AggrHelper.FuncAsync(acc, value, aggrFunc, mergeFunc));

        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>. If any result fails, returns a failure with the errors of every failed result, merged with <typeparamref name="TError"/>'s <c>Plus</c>. Returns None if there are no items.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Task<Option<Result<T, TError>>> TryReduceAsync<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<T, T, Task<T>> aggrFunc) where TError : IPlus<TError, TError>
            => values.ThrowIfDefault(nameof(values)).TryReduceAsync(
                (acc, value) => AggrHelper.FuncAsync(acc, value, aggrFunc, PlusHelper.Plus));

        //TryReduceUntil on Result<T, TError>
        /// <summary>Combines the values in order with <paramref name="aggrFunc"/>, stopping at the first failure, which is returned. Returns None if there are no items.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="values">The results.</param>
        /// <param name="aggrFunc">Combines the accumulated value with the next value.</param>
        public static Task<Option<Result<T, TError>>> TryReduceUntilAsync<T, TError>(this IEnumerable<Result<T, TError>> values,
            Func<T, T, Task<T>> aggrFunc)
            => values.ThrowIfDefault(nameof(values)).TryReduceUntilAsync(
                (acc, val) => AggrHelper.FuncUntilAsync(acc, val, aggrFunc));

    }
}
