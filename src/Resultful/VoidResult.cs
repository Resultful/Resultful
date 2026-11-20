using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resultful.Utils;
using OneOf;

namespace Resultful
{
    /// <summary>A success with no value, or a failure holding a <typeparamref name="TError"/> error.</summary>
    /// <typeparam name="TError">The type of the error.</typeparam>
    /// <remarks>The zero value (<c>default</c>) is a success whose <see cref="Unit"/> is <c>null</c>, not <see cref="Unit.Value"/>.</remarks>
    public struct VoidResult<TError>
    {
        //Private Members
        private OneOf<Unit, TError> _value;

        //Constructors
        internal VoidResult(Unit value)
            => _value = value;

        internal VoidResult(TError value)
            => _value = value;

        //Implicit Converters
        /// <summary>Converts a result whose value is <see cref="Unit"/> to a result with no value. Errors are kept.</summary>
        /// <param name="value">The result to convert.</param>
        public static implicit operator VoidResult<TError>(Result<Unit, TError> value)
            => value.Match(
                result => Result.Ok<TError>(),
                error => error.Fail());

        /// <summary>Converts an error to a failure.</summary>
        /// <param name="value">The error.</param>
        public static implicit operator VoidResult<TError>(TError value)
            => value.Fail();

        /// <summary>Converts <see cref="Unit"/> to a success.</summary>
        /// <param name="value">The unit value.</param>
        public static implicit operator VoidResult<TError>(Unit value)
            => Result.Ok<TError>();

        //Local Methods
        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure, and returns what it returns.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public TResult Match<TResult>(Func<Unit, TResult> successfulFunc, Func<TError, TResult> errorFunc)
            => _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure.</summary>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public void Switch(Action<Unit> successfulFunc, Action<TError> errorFunc)
            => _value.Switch(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure, and does not wait for the task it returns.</summary>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        /// <remarks>The task is dropped, so the method returns before the work finishes and an exception the task throws is lost. To wait for it, use <c>MatchAsync</c> and await the task.</remarks>
        public void SwitchAsync(Func<Unit, Task> successfulFunc, Func<TError, Task> errorFunc)
            => _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure, and returns its task.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public Task<TResult> MatchAsync<TResult>(Func<Unit, Task<TResult>> successfulFunc, Func<TError, Task<TResult>> errorFunc) =>
            _value.Match(
                successfulFunc.ThrowIfDefault(nameof(successfulFunc)),
                errorFunc.ThrowIfDefault(nameof(errorFunc)));

        /// <summary>Returns the result as a <c>OneOf</c>: <c>T0</c> is the <see cref="Unit"/> of a success, <c>T1</c> is the error.</summary>
        public OneOf<Unit, TError> ToOneOf() => _value;

        /// <summary>On success, returns the value <paramref name="mapFunc"/> gives as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Gives the new value. Its argument is the success's <see cref="Unit"/>.</param>
        public Result<TResult, TError> Map<TResult>(Func<Unit, TResult> mapFunc)
            => Map2(Result.Id, mapFunc);

        /// <summary>On success, returns the value <paramref name="mapFunc"/> gives as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Gives the new value. Its argument is the success's <see cref="Unit"/>.</param>
        public Task<Result<TResult, TError>> MapAsync<TResult>(Func<Unit, Task<TResult>> mapFunc)
            => BindValueAsync(async _ => (await mapFunc.ThrowIfDefault(nameof(mapFunc))(_).ConfigureAwait(false)).Ok<TResult, TError>());

        /// <summary>On success, returns <paramref name="value"/> as the success value. A failure keeps the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The new value.</param>
        public Result<T, TError> Map<T>(T value)
            => Map(_ => value);

        /// <summary>On success, awaits <paramref name="value"/> and returns its value as a success. A failure keeps the error, and <paramref name="value"/> isn't awaited.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that gives the new value.</param>
        public Task<Result<T, TError>> MapAsync<T>(Task<T> value)
            => MapAsync(_ => value);

        /// <summary>Maps both sides: the value with <paramref name="mapFunc"/> on success, or the error with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        /// <param name="mapFunc">Gives the new value.</param>
        public Result<TResult, TErrorResult> Map2<TResult, TErrorResult>(Func<TError, TErrorResult> errorMapFunc, Func<Unit, TResult> mapFunc)
            => Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TErrorResult>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TErrorResult>());

        /// <summary>Maps both sides: the value with <paramref name="mapFunc"/> on success, or the error with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        /// <param name="mapFunc">Gives the new value.</param>
        public Task<Result<TResult, TErrorResult>> Map2Async<TResult, TErrorResult>(Func<TError, Task<TErrorResult>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => Match(
                async success => (await mapFunc.ThrowIfDefault(nameof(mapFunc))().ConfigureAwait(false)).Ok<TResult, TErrorResult>(),
                async errors => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).ConfigureAwait(false)).Fail<TResult, TErrorResult>());


        /// <summary>On failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        public VoidResult<TErrorResult> MapError<TErrorResult>(Func<TError, TErrorResult> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        /// <summary>On failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        public Task<VoidResult<TErrorResult>> MapErrorAsync<TErrorResult>(Func<TError, Task<TErrorResult>> errorMapFunc)
            => Match(
                success => Task.FromResult(Result.Ok<TErrorResult>()),
                async errors => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).ConfigureAwait(false)).Fail());

        /// <summary>On failure, maps the error to a list of error messages. A success keeps its value.</summary>
        /// <param name="errorMapFunc">Maps the error.</param>
        public VoidResult MapError(Func<TError, IEnumerable<string>> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        /// <summary>On failure, maps the error to a list of error messages. A success keeps its value.</summary>
        /// <param name="errorMapFunc">Maps the error.</param>
        public Task<VoidResult> MapErrorAsync(Func<TError, Task<IEnumerable<string>>> errorMapFunc)
            => Match(
                success => Task.FromResult(Result.Ok()),
                async errors => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).ConfigureAwait(false)).Fail());

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        public VoidResult<TError> Bind(Func<Unit, VoidResult<TError>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                Result.Fail);

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        public Task<VoidResult<TError>> BindAsync(Func<Unit, Task<VoidResult<TError>>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Fail()));

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        public Result<T, TError> BindValue<T>(Func<Unit, Result<T, TError>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                Result.Fail<T, TError>);

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        public Task<Result<T, TError>> BindValueAsync<T>(Func<Unit, Task<Result<T, TError>>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Fail<T, TError>()));

        /// <summary>Calls <paramref name="action"/> on success, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <param name="action">Called on success.</param>
        public VoidResult<TError> Tee(Action<Unit> action)
            => Map(_ =>
            {
                action.ThrowIfDefault(nameof(action))(_);
                return _;
            });

        /// <summary>Calls <paramref name="asyncFunc"/> on success, awaits it, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <param name="asyncFunc">Called on success.</param>
        public async Task<VoidResult<TError>> TeeAsync(Func<Unit, Task> asyncFunc)
            => await MapAsync(async _ =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(_).ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);


        /// <summary>Calls <paramref name="action"/> with the error on failure, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <param name="action">Called with the error.</param>
        public VoidResult<TError> TeeError(Action<TError> action)
            => MapError(error =>
            {
                action.ThrowIfDefault(nameof(action))(error);
                return error;
            });

        /// <summary>Calls <paramref name="asyncFunc"/> with the error on failure, awaits it, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <param name="asyncFunc">Called with the error.</param>
        public async Task<VoidResult<TError>> TeeErrorAsync(Func<TError, Task> asyncFunc)
            => await MapErrorAsync(async error =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(error).ConfigureAwait(false);
                return error;
            }).ConfigureAwait(false);

        /// <summary>On failure, casts the error to <typeparamref name="TCast"/>. A success stays a success.</summary>
        /// <typeparam name="TCast">The type to cast the error to.</typeparam>
        /// <remarks>Throws <see cref="System.InvalidCastException"/> if the error isn't a <typeparamref name="TCast"/>.</remarks>
        public VoidResult<TCast> Cast<TCast>() => MapError(x => (TCast)(object)x!);

    }

}
