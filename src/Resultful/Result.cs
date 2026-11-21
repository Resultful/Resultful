using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resultful.Utils;
using OneOf.Types;
using OneOf;

namespace Resultful
{
    /// <summary>A success holding a <typeparamref name="T"/> value, or a failure holding a <typeparamref name="TError"/> error.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TError">The type of the error.</typeparam>
    /// <remarks>The zero value (<c>default</c>) is a success holding <c>default(T)</c>.</remarks>
    public struct Result<T, TError>
    {
        //Private Members
        private OneOf<T, TError> _value;

        //Constructors
        internal Result(T value)
            => _value = value;

        internal Result(TError value)
            => _value = value;

        //Implicit Converters
        /// <summary>Converts an error to a failure.</summary>
        /// <param name="value">The error.</param>
        public static implicit operator Result<T, TError>(TError value)
            => value.Fail<T, TError>();

        /// <summary>Converts a value to a success.</summary>
        /// <param name="value">The value.</param>
        public static implicit operator Result<T, TError>(T value)
            => value.Ok<T, TError>();

        //Local Methods
        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure.</summary>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public void Switch(Action<T> successfulFunc, Action<TError> errorFunc)
            => _value.Switch(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and returns what it returns.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<TError, TResult> errorFunc) =>
            _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and does not wait for the task it returns.</summary>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        /// <remarks>The task is dropped, so the method returns before the work finishes and an exception the task throws is lost. To wait for it, use <c>MatchAsync</c> and await the task.</remarks>
        public void SwitchAsync(Func<T, Task> successfulFunc, Func<TError, Task> errorFunc)
            => _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and returns its task.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> successfulFunc, Func<TError, Task<TResult>> errorFunc) =>
            _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        /// <summary>Returns the result as a <c>OneOf</c>: <c>T0</c> is the success value, <c>T1</c> is the error.</summary>
        public OneOf<T, TError> ToOneOf() => _value;

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public Result<TResult, TError> Bind<TResult>(Func<T, Result<TResult, TError>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail<TResult, TError>);

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public Task<Result<TResult, TError>> BindAsync<TResult>(Func<T, Task<Result<TResult, TError>>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), errors => Task.FromResult(errors.Fail<TResult, TError>()));

        /// <summary>On success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Maps the value.</param>
        public Result<TResult, TError> Map<TResult>(Func<T, TResult> mapFunc)
            => Map2(Result.Id, mapFunc);

        /// <summary>On success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Maps the value.</param>
        public Task<Result<TResult, TError>> MapAsync<TResult>(Func<T, Task<TResult>> mapFunc)
            => Map2Async(Task.FromResult, mapFunc);

        /// <summary>On failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        public Result<T, TErrorResult> MapError<TErrorResult>(Func<TError, TErrorResult> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        /// <summary>On failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        public Task<Result<T, TErrorResult>> MapErrorAsync<TErrorResult>(Func<TError, Task<TErrorResult>> errorMapFunc)
            => Map2Async(errorMapFunc, Task.FromResult);

        /// <summary>On failure, maps the error to a list of error messages. A success keeps its value.</summary>
        /// <param name="errorMapFunc">Maps the error.</param>
        public Result<T> MapError(Func<TError, IEnumerable<string>> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        /// <summary>On failure, maps the error to a list of error messages. A success keeps its value.</summary>
        /// <param name="errorMapFunc">Maps the error.</param>
        public Task<Result<T>> MapErrorAsync(Func<TError, Task<IEnumerable<string>>> errorMapFunc)
            => Match(
                item => Task.FromResult(item.Ok()),
                async error => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(error).ConfigureAwait(false)).Fail<T>());

        /// <summary>Maps both sides: the value with <paramref name="mapFunc"/> on success, or the error with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public Result<TResult, TErrorResult> Map2<TResult, TErrorResult>(Func<TError, TErrorResult> errorMapFunc, Func<T, TResult> mapFunc)
            => Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TErrorResult>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TErrorResult>());

        /// <summary>Maps both sides: the value with <paramref name="mapFunc"/> on success, or the error with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public Task<Result<TResult, TErrorResult>> Map2Async<TResult, TErrorResult>(
            Func<TError, Task<TErrorResult>> errorMapFunc, Func<T, Task<TResult>> mapFunc)
            => Match(
                async success => (await mapFunc(success).ConfigureAwait(false)).Ok<TResult, TErrorResult>(),
                async error => (await errorMapFunc(error).ConfigureAwait(false)).Fail<TResult, TErrorResult>());

        /// <summary>Calls <paramref name="teeAction"/> with the value on success, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <param name="teeAction">Called with the value.</param>
        public Result<T, TError> Tee(Action<T> teeAction)
            => Map(x =>
            {
                teeAction.ThrowIfDefault(nameof(teeAction))(x);
                return x;
            });

        /// <summary>Calls <paramref name="asyncFunc"/> with the value on success, awaits it, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <param name="asyncFunc">Called with the value.</param>
        public async Task<Result<T, TError>> TeeAsync(Func<T, Task> asyncFunc)
            => await MapAsync(async item =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(item).ConfigureAwait(false);
                return item;
            }).ConfigureAwait(false);

        /// <summary>Calls <paramref name="teeAction"/> with the error on failure, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <param name="teeAction">Called with the error.</param>
        public Result<T, TError> TeeError(Action<TError> teeAction)
            => MapError(x =>
            {
                teeAction.ThrowIfDefault(nameof(teeAction))(x);
                return x;
            });

        /// <summary>Calls <paramref name="asyncFunc"/> with the error on failure, awaits it, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <param name="asyncFunc">Called with the error.</param>
        public async Task<Result<T, TError>> TeeErrorAsync(Func<TError, Task> asyncFunc)
            => await MapErrorAsync(async item =>
            {
                await asyncFunc(item).ConfigureAwait(false);
                return item;
            }).ConfigureAwait(false);

        /// <summary>Converts to an option: the value on success, or None on failure, after calling <paramref name="errorAction"/> with the error.</summary>
        /// <param name="errorAction">Called with the error before it is dropped.</param>
        public Option<T> DiscardError(Action<TError> errorAction)
            => Match(Option.Some, errors =>
            {
                errorAction.ThrowIfDefault(nameof(errorAction))(errors);
                return new None();
            });

        /// <summary>Converts to an option: the value on success, or None on failure, after calling <paramref name="errorAction"/> with the error.</summary>
        /// <param name="errorAction">Called with the error before it is dropped.</param>
        public Task<Option<T>> DiscardErrorAsync(Func<TError, Task> errorAction)
            => Match(
                item => Task.FromResult(item.Some()),
                async errors =>
                {
                    await errorAction.ThrowIfDefault(nameof(errorAction))(errors);
                    return Option<T>.None;
                });

        /// <summary>Converts to an option: the value on success, or None on failure. The error is dropped.</summary>
        public Option<T> DiscardError()
            => Match(Option.Some, _ => Option<T>.None);

        /// <summary>On success, returns the result <paramref name="bindFunc"/> gives for the value, without a value. A failure keeps the error.</summary>
        /// <param name="bindFunc">Returns a result without a value from the value.</param>
        public VoidResult<TError> DiscardValue(Func<T, VoidResult<TError>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail);

        /// <summary>On success, returns the result <paramref name="bindFunc"/> gives for the value, without a value. A failure keeps the error.</summary>
        /// <param name="bindFunc">Returns a result without a value from the value.</param>
        public Task<VoidResult<TError>> DiscardValueAsync(Func<T, Task<VoidResult<TError>>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Fail()));


        /// <summary>Drops the value: a success becomes a success with no value, and a failure keeps the error.</summary>
        public VoidResult<TError> DiscardValue()
            => DiscardValue(_ => Result.Ok<TError>());


        /// <summary>Returns the value on success, or the value <paramref name="func"/> gives for the error on failure.</summary>
        /// <param name="func">Gives a value from the error.</param>
        public T ReturnOrValue(Func<TError, T> func)
            => Match(Result.Id, func);

        /// <summary>Returns the value on success, or the value <paramref name="func"/> gives for the error on failure.</summary>
        /// <param name="func">Gives a value from the error.</param>
        public Task<T> ReturnOrValueAsync(Func<TError, Task<T>> func)
            => Match(Result.IdAsync, func);

        /// <summary>Returns the value on success. On failure, throws the exception <paramref name="failFunc"/> builds from the error.</summary>
        /// <typeparam name="TExn">The type of the exception.</typeparam>
        /// <param name="failFunc">Builds the exception to throw from the error.</param>
        public T ReturnOrFail<TExn>(Func<TError, TExn> failFunc) where TExn : Exception
            => Match(Result.Id, err => throw failFunc.ThrowIfDefault(nameof(failFunc))(err));

        /// <summary>On failure, casts the error to <typeparamref name="TCast"/>. A success keeps its value.</summary>
        /// <typeparam name="TCast">The type to cast the error to.</typeparam>
        /// <remarks>Throws <see cref="System.InvalidCastException"/> if the error isn't a <typeparamref name="TCast"/>.</remarks>
        public Result<T, TCast> CastError<TCast>() => MapError(x => (TCast)(object)x!);

        /// <summary>On success, casts the value to <typeparamref name="TCast"/>. A failure keeps the error.</summary>
        /// <typeparam name="TCast">The type to cast the value to.</typeparam>
        /// <remarks>Throws <see cref="System.InvalidCastException"/> if the value isn't a <typeparamref name="TCast"/>.</remarks>
        public Result<TCast, TError> Cast<TCast>() => Map(x => (TCast)(object)x!);
    }
}
