using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Resultful.Utils;
using OneOf;

namespace Resultful
{
    /// <summary>A success with no value, or a failure holding a <typeparamref name="TError"/> error.</summary>
    /// <typeparam name="TError">The type of the error.</typeparam>
    /// <remarks>The zero value (<c>default</c>) is neither a success nor a failure (decision 10): every member that reads the case or the error throws <see cref="InvalidOperationException"/>. <c>ToString</c>, <c>Equals</c> and <c>GetHashCode</c> never throw.</remarks>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
#pragma warning disable CA2231 // Equals keeps 0.2's equality; == and != would be new API, not part of 08a.
    public struct VoidResult<TError>
#pragma warning restore CA2231
    {
        //Private Members
        private readonly TError _error;
        // State.Zero only for the zero value (default), which no Ok or Fail produced (decision 1, decision 10).
        private readonly byte _state;

        //Constructors
        /// <summary>Not supported: <c>new VoidResult&lt;TError&gt;()</c> would be the zero value, which is neither a success nor a failure. Use <c>Result.Ok</c> or <c>Result.Fail</c>.</summary>
        /// <exception cref="InvalidOperationException">Always.</exception>
        [Obsolete(NamedErrors.NoConstructor, error: true)]
        public VoidResult() => throw new InvalidOperationException(NamedErrors.NoConstructor);

        internal VoidResult(Unit value)
        {
            _error = default!;
            _state = value is null ? State.Zero : State.Success;
        }

        internal VoidResult(TError value)
        {
            _error = value;
            _state = State.Failure;
        }

        //Implicit Converters
        /// <summary>Converts a result whose value is <see cref="Unit"/> to a result with no value. Errors are kept.</summary>
        /// <param name="value">The result to convert.</param>
        public static implicit operator VoidResult<TError>(Result<Unit, TError> value)
            => value.Match(
                result => Result.Ok<TError>(),
                error => error.Fail());

        /// <summary>Converts to a result whose value is <see cref="Unit"/>: a success holds <see cref="Unit.Value"/>, and a failure keeps its error.</summary>
        /// <param name="value">The result to convert.</param>
        /// <exception cref="InvalidOperationException"><paramref name="value"/> is the zero value (<c>default</c>).</exception>
        public static implicit operator Result<Unit, TError>(VoidResult<TError> value)
            => value.Match(u => u.Ok<Unit, TError>(), e => e.Fail<Unit, TError>());

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
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            return Case() ? successfulFunc(Unit.Value) : errorFunc(_error);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure.</summary>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public void Switch(Action<Unit> successfulFunc, Action<TError> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            if (Case()) successfulFunc(Unit.Value); else errorFunc(_error);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure, and does not wait for the task it returns.</summary>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        /// <remarks>The task is dropped, so the method returns before the work finishes and an exception the task throws is lost. To wait for it, use <c>MatchAsync</c> and await the task.</remarks>
        public void SwitchAsync(Func<Unit, Task> successfulFunc, Func<TError, Task> errorFunc)
            => _ = Match(successfulFunc, errorFunc);

        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure, and returns its task.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public Task<TResult> MatchAsync<TResult>(Func<Unit, Task<TResult>> successfulFunc, Func<TError, Task<TResult>> errorFunc)
            => Match(successfulFunc, errorFunc);

        /// <summary>Returns the result as a <c>OneOf</c>: <c>T0</c> is the <see cref="Unit"/> of a success, <c>T1</c> is the error.</summary>
        public OneOf<Unit, TError> ToOneOf()
            => Case() ? OneOf<Unit, TError>.FromT0(Unit.Value) : OneOf<Unit, TError>.FromT1(_error);

        /// <summary>Two results are equal when both are successes, both failures with equal errors, or both the zero value.</summary>
        /// <param name="obj">The object to compare with.</param>
        /// <remarks>Values and errors are compared with <c>object.Equals</c>, as in 0.2.</remarks>
        public override bool Equals(object? obj)
            => obj is VoidResult<TError> other && _state == other._state && (_state != State.Failure || Equals(_error, other._error));

        /// <summary>A hash of the case and its error.</summary>
        public override int GetHashCode()
            => _state == State.Failure ? State.Hash(_error, 1) : State.Hash(SuccessUnit, 0);

        // The zero value hashes as 0.2's did: a success whose Unit is null.
        private Unit SuccessUnit => _state == State.Zero ? null! : Unit.Value;

        // true for a success, false for a failure; the zero value throws (decision 10).
        private bool Case() => _state == State.Zero ? throw NamedErrors.Zero(typeof(VoidResult<TError>)) : _state == State.Success;

        // Throws on the zero value; for members that would otherwise return it unread (Then and Check with no steps).
        internal void ThrowIfZero() => _ = Case();

        /// <summary>The type's name, as in 0.2; <c>"&lt;default VoidResult&gt;"</c> for the zero value. Never throws.</summary>
        public override string ToString()
            => _state == State.Zero ? State.DefaultVoidResult : typeof(VoidResult<TError>).ToString();

        private string DebuggerDisplay
            => _state switch { State.Success => "Ok", State.Failure => $"Fail({_error})", _ => State.DefaultVoidResult };

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

        /// <summary>On failure, maps the error to an <see cref="Error"/>. A success keeps its value.</summary>
        /// <param name="errorMapFunc">Maps the error.</param>
        public VoidResult MapError(Func<TError, Error> errorMapFunc)
        {
            errorMapFunc.ThrowIfDefault(nameof(errorMapFunc));
            return Match(_ => Result.Ok(), e => Result.Fail(errorMapFunc(e)));
        }

        /// <summary>On failure, maps the error to an <see cref="Error"/>. A success keeps its value.</summary>
        /// <param name="errorMapFunc">Maps the error.</param>
        public Task<VoidResult> MapErrorAsync(Func<TError, Task<Error>> errorMapFunc)
            => Match(
                success => Task.FromResult(Result.Ok()),
                async errors => Result.Fail(await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).ConfigureAwait(false)));

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
