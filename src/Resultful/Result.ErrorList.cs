using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Resultful.Utils;

namespace Resultful
{
    /// <summary>A success holding a <typeparamref name="T"/> value, or a failure holding one <see cref="Resultful.Error"/>.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <remarks>The zero value (<c>default</c>) is neither a success nor a failure (decision 10): <see cref="IsSuccess"/> and <see cref="IsFailure"/> are both <c>false</c>, and every member that reads the value or the error throws <see cref="InvalidOperationException"/>. <c>ToString</c>, <c>Equals</c> and <c>GetHashCode</c> never throw. It means <c>Result&lt;T, Error&gt;</c> (decision 14): one error, and <c>Bind</c> stops at the first failure. Nothing converts a <see cref="string"/> to an error.</remarks>
    [CodeAnalysis.MustObserve]
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
#pragma warning disable CA2231 // Equals keeps 0.2's equality; == and != would be new API, not part of 08a.
    public partial struct Result<T>
#pragma warning restore CA2231
    {
        //Private Members
        private readonly T _value;
        private readonly Error _error;
        // State.Zero only for the zero value (default), which no Ok or Fail produced (decision 1, decision 10).
        private readonly byte _state;

        //Constructors
        /// <summary>Not supported: <c>new Result&lt;T&gt;()</c> would be the zero value, which is neither a success nor a failure. Use <c>Result.Ok</c> or <c>Result.Fail</c>.</summary>
        /// <exception cref="InvalidOperationException">Always.</exception>
        [Obsolete(NamedErrors.NoConstructor, error: true)]
        public Result() => throw new InvalidOperationException(NamedErrors.NoConstructor);

        internal Result(T value)
        {
            _value = value;
            _error = default!;
            _state = State.Success;
        }

        internal Result(Error value)
        {
            _value = default!;
            _error = value;
            _state = State.Failure;
        }

        //Implicit Converters
        /// <summary>Converts a value to a success holding it.</summary>
        /// <param name="value">The value.</param>
        /// <remarks>Decision 14: with no conversion from <see cref="string"/> to an error, <c>Result&lt;string&gt; r = "message"</c> is a success.</remarks>
        public static implicit operator Result<T>(T value)
            => value.Ok();

        /// <summary>Converts an error, including any record derived from <see cref="Resultful.Error"/>, to a failure holding it.</summary>
        /// <param name="error">The error.</param>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <c>null</c>.</exception>
        public static implicit operator Result<T>(Error error)
            => error.Fail<T>();

        //Local Methods
        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure.</summary>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public void Switch(Action<T> successfulFunc, Action<Error> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            if (Case()) successfulFunc(_value); else errorFunc(_error);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and returns what it returns.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<Error, TResult> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            return Case() ? successfulFunc(_value) : errorFunc(_error);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and does not wait for the task it returns.</summary>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        /// <remarks>The task is dropped, so the method returns before the work finishes and an exception the task throws is lost. To wait for it, use <c>MatchAsync</c> and await the task.</remarks>
        public void SwitchAsync(Func<T, Task> successfulFunc, Func<Error, Task> errorFunc)
            => _ = Match(successfulFunc, errorFunc);

        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and returns its task.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> successfulFunc, Func<Error, Task<TResult>> errorFunc)
            => Match(successfulFunc, errorFunc);

        /// <summary>Two results are equal when both are successes with equal values, both failures with equal errors, or both the zero value.</summary>
        /// <param name="obj">The object to compare with.</param>
        /// <remarks>Values and errors are compared with <c>object.Equals</c>; an <see cref="Resultful.Error"/> compares by value.</remarks>
        public override bool Equals(object? obj)
            => obj is Result<T> other && _state == other._state && (_state == State.Failure
                ? Equals(_error, other._error)
                : Equals(_value, other._value));

        /// <summary>A hash of the case and its value or error.</summary>
        public override int GetHashCode()
            => _state == State.Failure ? State.Hash(_error, 1) : State.Hash(_value, 0);

        /// <summary>The type's name, as in 0.2; <c>"&lt;default Result&gt;"</c> for the zero value. Never throws.</summary>
        public override string ToString()
            => _state == State.Zero ? State.DefaultResult : typeof(Result<T>).ToString();

        private string DebuggerDisplay
            => _state switch { State.Success => $"Ok({_value})", State.Failure => $"Fail({_error})", _ => State.DefaultResult };

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                errors => errors.Fail<TResult>());

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public Task<Result<TResult>> BindAsync<TResult>(Func<T, Task<Result<TResult>>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Fail<TResult>()));

        /// <summary>On success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Maps the value.</param>
        public Result<TResult> Map<TResult>(Func<T, TResult> mapFunc)
        {
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Case() ? mapFunc(_value).Ok() : _error.Fail<TResult>();
        }

        /// <summary>On success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Maps the value.</param>
        public Task<Result<TResult>> MapAsync<TResult>(Func<T, Task<TResult>> mapFunc)
            => Match(
                async success => (await mapFunc.ThrowIfDefault(nameof(mapFunc))(success).ConfigureAwait(false)).Ok(),
                error => Task.FromResult(error.Fail<TResult>()));

        /// <summary>On failure, returns a failure with the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <param name="errorMapFunc">Maps the error to another <see cref="Resultful.Error"/>: a note (<c>e =&gt; e.WithContext("…")</c>) or a wrapper (<c>e =&gt; new SaveFailed(id) { Cause = e }</c>).</param>
        /// <remarks>The result stays a <c>Result&lt;T&gt;</c> whatever <see cref="Resultful.Error"/> the function returns. To change the error to another type, including a derived error's own type, use <c>Map2(f, v =&gt; v)</c>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="errorMapFunc"/> is <c>null</c>, or returns <c>null</c>.</exception>
        public Result<T> MapError(Func<Error, Error> errorMapFunc)
        {
            errorMapFunc.ThrowIfDefault(nameof(errorMapFunc));
            return Case() ? this : errorMapFunc(_error).Fail<T>();
        }

        /// <summary>On failure, returns a failure with the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <param name="errorMapFunc">Maps the error to another <see cref="Resultful.Error"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="errorMapFunc"/> is <c>null</c>, or its task gives <c>null</c>.</exception>
        public Task<Result<T>> MapErrorAsync(Func<Error, Task<Error>> errorMapFunc)
            => Match(
                item => Task.FromResult(item.Ok()),
                async error => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(error).ConfigureAwait(false)).Fail<T>());

        /// <summary>On failure, adds <paramref name="message"/> to the error's <see cref="Resultful.Error.Context"/>, keeping the error's type. A success is unchanged.</summary>
        /// <param name="message">The note, such as what the caller was doing.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
        public Result<T> WithContext(string message)
        {
            message.ThrowIfDefault(nameof(message));
            return MapError(e => e.WithContext(message));
        }


        /// <summary>Maps both sides: the value with <paramref name="mapFunc"/> on success, or the error with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public Result<TResult, TError> Map2<TResult, TError>(Func<Error, TError> errorMapFunc, Func<T, TResult> mapFunc)
            => Case()
                ? mapFunc.ThrowIfDefault(nameof(mapFunc))(_value).Ok<TResult, TError>()
                : errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(_error).Fail<TResult, TError>();

        /// <summary>Maps both sides: the value with <paramref name="mapFunc"/> on success, or the error with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public Task<Result<TResult, TError>> Map2Async<TResult, TError>(Func<Error, Task<TError>> errorMapFunc, Func<T, Task<TResult>> mapFunc)
            => Match(
                async success => (await mapFunc(success).ConfigureAwait(false)).Ok<TResult, TError>(),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail<TResult, TError>());

        /// <summary>Calls <paramref name="teeAction"/> with the value on success, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <param name="teeAction">Called with the value.</param>
        public Result<T> Tee(Action<T> teeAction)
            => Map(x =>
            {
                teeAction.ThrowIfDefault(nameof(teeAction))(x);
                return x;
            });

        /// <summary>Calls <paramref name="asyncFunc"/> with the value on success, awaits it, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <param name="asyncFunc">Called with the value.</param>
        public async Task<Result<T>> TeeAsync(Func<T, Task> asyncFunc)
            => await MapAsync(async item =>
            {
                await asyncFunc(item).ConfigureAwait(false);
                return item;
            }).ConfigureAwait(false);

        /// <summary>Calls <paramref name="teeAction"/> with the error on failure, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <param name="teeAction">Called with the error.</param>
        public Result<T> TeeError(Action<Error> teeAction)
            => MapError(x =>
            {
                teeAction.ThrowIfDefault(nameof(teeAction))(x);
                return x;
            });

        /// <summary>Calls <paramref name="asyncFunc"/> with the error on failure, awaits it, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <param name="asyncFunc">Called with the error.</param>
        public async Task<Result<T>> TeeErrorAsync(Func<Error, Task> asyncFunc)
            => await MapErrorAsync(async item =>
            {
                await asyncFunc(item).ConfigureAwait(false);
                return item;
            }).ConfigureAwait(false);

        /// <summary>Converts to an option: the value on success, or None on failure, after calling <paramref name="errorAction"/> with the error.</summary>
        /// <param name="errorAction">Called with the error before it is dropped.</param>
        public Option<T> DiscardError(Action<Error> errorAction)
            => Match(Option.Some, errors =>
            {
                errorAction.ThrowIfDefault(nameof(errorAction))(errors);
                return new None();
            });

        /// <summary>Converts to an option: the value on success, or None on failure, after calling <paramref name="errorAction"/> with the error.</summary>
        /// <param name="errorAction">Called with the error before it is dropped.</param>
        public Task<Option<T>> DiscardErrorAsync(Func<Error, Task> errorAction)
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
        public VoidResult DiscardValue(Func<T, VoidResult> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), e => Result.Fail(e));

        /// <summary>On success, returns the result <paramref name="bindFunc"/> gives for the value, without a value. A failure keeps the error.</summary>
        /// <param name="bindFunc">Returns a result without a value from the value.</param>
        public Task<VoidResult> DiscardValueAsync(Func<T, Task<VoidResult>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(Result.Fail(error)));

        /// <summary>Drops the value: a success becomes a success with no value, and a failure keeps the error.</summary>
        public VoidResult DiscardValue()
            => Match(_ => Result.Ok(), e => Result.Fail(e));

        /// <summary>Returns the value on success, or the value <paramref name="func"/> gives for the error on failure.</summary>
        /// <param name="func">Gives a value from the error.</param>
        public T ReturnOrValue(Func<Error, T> func)
            => Match(Result.Id, func);

        /// <summary>Returns the value on success, or the value <paramref name="func"/> gives for the error on failure.</summary>
        /// <param name="func">Gives a value from the error.</param>
        public Task<T> ReturnOrValueAsync(Func<Error, Task<T>> func)
            => Match(Result.IdAsync, func);

        /// <summary>Returns the value on success. On failure, throws the exception <paramref name="failFunc"/> builds from the error.</summary>
        /// <typeparam name="TExn">The type of the exception.</typeparam>
        /// <param name="failFunc">Builds the exception to throw from the error.</param>
        public T ReturnOrFail<TExn>(Func<Error, TExn> failFunc) where TExn : Exception
            => Match(Result.Id, err => throw failFunc.ThrowIfDefault(nameof(failFunc))(err));

        /// <summary>On success, casts the value to <typeparamref name="TCast"/>. A failure keeps the error.</summary>
        /// <typeparam name="TCast">The type to cast the value to.</typeparam>
        /// <remarks>Throws <see cref="System.InvalidCastException"/> if the value isn't a <typeparamref name="TCast"/>.</remarks>
        public Result<TCast> Cast<TCast>() => Map(x => (TCast)(object)x!);

    }
}
