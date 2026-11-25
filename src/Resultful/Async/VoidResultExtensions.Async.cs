using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resultful.Utils;
using TaskExt;

namespace Resultful
{
    public static partial class Result
    {
        //Switch on Task<Result<T>>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task Switch<T>(this Task<VoidResult> value, Action<Unit> successFunc, Action<Error> errorFunc)
            => value.Discard(item => item.Switch(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure, and returns a task that completes when the call's task does.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task SwitchAsync<T>(this Task<VoidResult> value, Func<Unit, Task> successFunc, Func<Error, Task> errorFunc)
            => value.DiscardAsync(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //Match on Task<Option<T>>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure, and returns its task.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<TResult> MatchAsync<T, TResult>(this Task<VoidResult> value, Func<Unit, Task<TResult>> successFunc, Func<Error, Task<TResult>> errorFunc)
            => value.Bind(item => item.MatchAsync(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure, and returns what it returns.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<TResult> Match<T, TResult>(this Task<VoidResult> value, Func<Unit, TResult> successFunc, Func<Error, TResult> errorFunc)
            => value.Map(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //Switch on Task<Result<T, TError>>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task Switch<T, TError>(this Task<VoidResult<TError>> value, Action<Unit> successFunc, Action<TError> errorFunc)
            => value.Discard(item => item.Switch(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure, and returns a task that completes when the call's task does.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task SwitchAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Task> successFunc, Func<TError, Task> errorFunc)
            => value.DiscardAsync(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //Match on Task<Result<T, TError>>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure, and returns its task.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<TResult> MatchAsync<T, TError, TResult>(this Task<VoidResult<TError>> value, Func<Unit, Task<TResult>> successFunc, Func<TError, Task<TResult>> errorFunc)
            => value.Bind(item => item.MatchAsync(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="successFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure, and returns what it returns.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="successFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<TResult> Match<T, TError, TResult>(this Task<VoidResult<TError>> value, Func<Unit, TResult> successFunc, Func<TError, TResult> errorFunc)
            => value.Map(item => item.Match(
                successFunc.ThrowIfDefault(nameof(successFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc))));

        //MapAsync on VoidResult<TError>
        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the value <paramref name="mapFunc"/> gives as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="mapFunc">Gives the new value. Its argument is the success's <see cref="Unit"/>.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<Result<TResult, TError>> Map<TResult, TError>(this Task<VoidResult<TError>> value, Func<Unit, TResult> mapFunc)
            => value.Map(item => item.Map(mapFunc));

        /// <summary>Awaits <paramref name="value"/>, then maps both sides: the value with <paramref name="mapFunc"/> on success, or the error with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error.</param>
        /// <param name="mapFunc">Gives the new value.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<Result<TResult, TErrorResult>> Map2<TResult, TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, TErrorResult> errorMapFunc, Func<Unit, TResult> mapFunc)
            => value.Map(item => item.Map2(errorMapFunc, mapFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, awaits <paramref name="mapFunc"/> and returns its value as a success. A failure keeps the error, and <paramref name="mapFunc"/> isn't awaited.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="mapFunc">The task that gives the new value.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<Result<TResult, TError>> MapAsync<TResult, TError>(this Task<VoidResult<TError>> value, Task<TResult> mapFunc)
            => value.Bind(item => item.MapAsync(mapFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns <paramref name="mapFunc"/> as the success value. A failure keeps the error.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="mapFunc">The new value.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<Result<TResult, TError>> Map<TResult, TError>(this Task<VoidResult<TError>> value, TResult mapFunc)
            => value.Map(item => item.Map(mapFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the value <paramref name="bindFunc"/> gives as a success. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Gives the new value. Its argument is the success's <see cref="Unit"/>.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<Result<T, TError>> MapAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Task<T>> bindFunc)
            => value.Bind(item => item.MapAsync(bindFunc));

        /// <summary>Awaits <paramref name="value"/>, then maps both sides: the value with <paramref name="mapFunc"/> on success, or the error with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error.</param>
        /// <param name="mapFunc">Gives the new value.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<Result<TResult, TErrorResult>> Map2Async<TResult, TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => value.Bind(item => item.Map2Async(errorMapFunc, mapFunc));

        //MapAsync on VoidResult
        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the value <paramref name="mapFunc"/> gives as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="mapFunc">Gives the new value. Its argument is the success's <see cref="Unit"/>.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<Result<TResult>> Map<TResult>(this Task<VoidResult> value, Func<Unit, TResult> mapFunc)
            => value.Map(item => item.Map(mapFunc));

        /// <summary>Awaits <paramref name="value"/>, then maps both sides: the value with <paramref name="mapFunc"/> on success, or the errors with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the errors.</param>
        /// <param name="mapFunc">Gives the new value.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<Result<TResult, TError>> Map2<TResult, TError>(this Task<VoidResult> value, Func<Error, TError> errorMapFunc, Func<Unit, TResult> mapFunc)
            => value.Map(item => item.Map2(errorMapFunc, mapFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, awaits <paramref name="mapFunc"/> and returns its value as a success. A failure keeps the error, and <paramref name="mapFunc"/> isn't awaited.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="mapFunc">The task that gives the new value.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<Result<TResult>> MapAsync<TResult>(this Task<VoidResult> value, Task<TResult> mapFunc)
            => value.Bind(item => item.MapAsync(mapFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns <paramref name="bindFunc"/> as the success value. A failure keeps the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">The new value.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<Result<T>> Map<T>(this Task<VoidResult> value, T bindFunc)
            => value.Map(item => item.Map(bindFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns the value <paramref name="bindFunc"/> gives as a success. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Gives the new value. Its argument is the success's <see cref="Unit"/>.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<Result<T>> MapAsync<T>(this Task<VoidResult> value, Func<Unit, Task<T>> bindFunc)
            => value.Bind(item => item.MapAsync(bindFunc));

        /// <summary>Awaits <paramref name="value"/>, then maps both sides: the value with <paramref name="mapFunc"/> on success, or the errors with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the errors.</param>
        /// <param name="mapFunc">Gives the new value.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<Result<TResult, TError>> Map2Async<TResult, TError>(this Task<VoidResult> value, Func<Error, Task<TError>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => value.Bind(item => item.Map2Async(errorMapFunc, mapFunc));

        //MapErrorAync on VoidResult
        /// <summary>Awaits <paramref name="value"/>, then: on failure, returns the error <paramref name="errorMapFunc"/> gives for the errors. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the errors.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static async Task<VoidResult<TErrorResult>> MapError<TErrorResult>(this Task<VoidResult> value, Func<Error, TErrorResult> errorMapFunc)
            => await value.Map(item => item.Map2(errorMapFunc, Id)).ConfigureAwait(false);

        /// <summary>Awaits <paramref name="value"/>, then: on failure, returns the error <paramref name="errorMapFunc"/> gives for the errors. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the errors.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static async Task<VoidResult<TErrorResult>> MapErrorAsync<TErrorResult>(this Task<VoidResult> value, Func<Error, Task<TErrorResult>> errorMapFunc)
            => await value.Bind(item => item.Map2Async(errorMapFunc, () => Task.FromResult(Unit.Value))).ConfigureAwait(false);

        //MapErrorAsync on VoidResult<Terror>
        /// <summary>Awaits <paramref name="value"/>, then: on failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static async Task<VoidResult<TErrorResult>> MapError<TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, TErrorResult> errorMapFunc)
            => await value.Map(item => item.MapError(errorMapFunc)).ConfigureAwait(false);

        /// <summary>Awaits <paramref name="value"/>, then: on failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<VoidResult<TErrorResult>> MapErrorAsync<TError, TErrorResult>(this Task<VoidResult<TError>> value, Func<TError, Task<TErrorResult>> errorMapFunc)
            => value.Bind(item => item.MapErrorAsync(errorMapFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<VoidResult> MapError(this Task<VoidResult> value, Func<Error, Error> errorMapFunc)
            => value.Map(item => item.MapError(errorMapFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="errorMapFunc">Maps the error.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<VoidResult> MapErrorAsync(this Task<VoidResult> value, Func<Error, Task<Error>> errorMapFunc)
            => value.Bind(item => item.MapErrorAsync(errorMapFunc));

        //BindAsync on VoidResult<TError>
        /// <summary>Awaits <paramref name="value"/>, then: on success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<VoidResult<TError>> Bind<TError>(this Task<VoidResult<TError>> value, Func<Unit, VoidResult<TError>> bindFunc)
            => value.Map(item => item.Bind(bindFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<VoidResult<TError>> BindAsync<TError>(this Task<VoidResult<TError>> value, Func<Unit, Task<VoidResult<TError>>> bindFunc)
            => value.Bind(item => item.BindAsync(bindFunc));

        //BindAsync on VoidResult
        /// <summary>Awaits <paramref name="value"/>, then: on success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<VoidResult> Bind(this Task<VoidResult> value, Func<Unit, VoidResult> bindFunc)
            => value.Map(item => item.Bind(bindFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<VoidResult> BindAsync(this Task<VoidResult> value, Func<Unit, Task<VoidResult>> bindFunc)
            => value.Bind(item => item.BindAsync(bindFunc));

        //BindValueAsync on VoidResult<TError>
        /// <summary>Awaits <paramref name="value"/>, then: on success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<Result<T, TError>> BindValue<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Result<T, TError>> bindFunc)
            => value.Map(item => item.BindValue(bindFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<Result<T, TError>> BindValueAsync<T, TError>(this Task<VoidResult<TError>> value, Func<Unit, Task<Result<T, TError>>> bindFunc)
            => value.Bind(item => item.BindValueAsync(bindFunc));

        //BindValueAsync on VoidResult
        /// <summary>Awaits <paramref name="value"/>, then: on success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<Result<T>> BindValue<T>(this Task<VoidResult> value, Func<Unit, Result<T>> bindFunc)
            => value.Map(item => item.BindValue(bindFunc));

        /// <summary>Awaits <paramref name="value"/>, then: on success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<Result<T>> BindValueAsync<T>(this Task<VoidResult> value, Func<Unit, Task<Result<T>>> bindFunc)
            => value.Bind(item => item.BindValueAsync(bindFunc));

        //TeeAsync on VoidResult<TError>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="action"/> on success, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="action">Called on success.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<VoidResult<TError>> Tee<TError>(this Task<VoidResult<TError>> value, Action<Unit> action)
            => value.Map(item => item.Tee(action));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="asyncFunc"/> on success, awaits it, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="asyncFunc">Called on success.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<VoidResult<TError>> TeeAsync<TError>(this Task<VoidResult<TError>> value, Func<Unit, Task> asyncFunc)
            => value.Tee(item => item.TeeAsync(asyncFunc).Discard());

        //TeeAsync on VoidResult
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="action"/> on success, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="action">Called on success.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<VoidResult> Tee(this Task<VoidResult> value, Action<Unit> action)
            => value.Map(item => item.Tee(action));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="asyncFunc"/> on success, awaits it, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="asyncFunc">Called on success.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<VoidResult> TeeAsync(this Task<VoidResult> value, Func<Unit, Task> asyncFunc)
            => value.Tee(item => item.TeeAsync(asyncFunc).Discard());

        //TeeErrorAsync on VoidResult<TError>
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="action"/> with the error on failure, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="action">Called with the error.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<VoidResult<TError>> TeeError<TError>(this Task<VoidResult<TError>> value, Action<TError> action)
            => value.Map(item => item.TeeError(action));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="asyncFunc"/> with the error on failure, awaits it, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="asyncFunc">Called with the error.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<VoidResult<TError>> TeeErrorAsync<TError>(this Task<VoidResult<TError>> value, Func<TError, Task> asyncFunc)
            => value.Tee(item => item.TeeErrorAsync(asyncFunc).Discard());

        //TeeErrorAsync on VoidResult
        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="action"/> with the error on failure, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="action">Called with the error.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<VoidResult> TeeError(this Task<VoidResult> value, Action<Error> action)
            => value.Map(item => item.TeeError(action));

        /// <summary>Awaits <paramref name="value"/>, then calls <paramref name="asyncFunc"/> with the error on failure, awaits it, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="asyncFunc">Called with the error.</param>
        [Obsolete(NamedErrors.VoidResultObsolete)]
        public static Task<VoidResult> TeeErrorAsync(this Task<VoidResult> value, Func<Error, Task> asyncFunc)
            => value.Tee(item => item.TeeErrorAsync(asyncFunc).Discard());


        //FlattenAsync on VoidResult<T>
        /// <summary>Awaits <paramref name="value"/>, then returns the inner result if the outer result is a success, or the outer error if it is a failure.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        [Obsolete(NamedErrors.VoidResultOfErrorObsolete)]
        public static Task<VoidResult<TError>> Flatten<TError>(this Task<VoidResult<VoidResult<TError>>> value)
            => value.Map(item => item.Flatten());
    }
}
