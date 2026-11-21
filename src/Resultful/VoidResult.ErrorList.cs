using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Resultful.Utils;

namespace Resultful
{
    /// <summary>A success with no value, or a failure holding one <see cref="Error"/>. Obsolete: use <c>Result&lt;Unit&gt;</c>, which it converts to and from implicitly (decision 14).</summary>
    /// <remarks>The zero value (<c>default</c>) is neither a success nor a failure (decision 10): every member that reads the case or the error throws <see cref="InvalidOperationException"/>. <c>ToString</c>, <c>Equals</c> and <c>GetHashCode</c> never throw.</remarks>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    [Obsolete(NamedErrors.VoidResultObsolete)]
    public struct VoidResult
    {
        //Private Members
        private readonly Error _error;
        // State.Zero only for the zero value (default), which no Ok or Fail produced (decision 1, decision 10).
        private readonly byte _state;

        //Constructors
        /// <summary>Not supported: <c>new VoidResult()</c> would be the zero value, which is neither a success nor a failure. Use <c>Result.Ok</c> or <c>Result.Fail</c>.</summary>
        /// <exception cref="InvalidOperationException">Always.</exception>
        [Obsolete(NamedErrors.NoConstructor, error: true)]
        public VoidResult() => throw new InvalidOperationException(NamedErrors.NoConstructor);

        internal VoidResult(Unit value)
        {
            _error = default!;
            _state = value is null ? State.Zero : State.Success;
        }

        internal VoidResult(Error value)
        {
            _error = value;
            _state = State.Failure;
        }

        //Implicit Converters
        /// <summary>Converts a result whose value is <see cref="Unit"/> to a result with no value. The error is kept.</summary>
        /// <param name="value">The result to convert.</param>
        public static implicit operator VoidResult(Result<Unit> value)
            => value.Match(_ => Success, Failure);

        /// <summary>Converts to a result whose value is <see cref="Unit"/>: a success holds <see cref="Unit.Value"/>, and a failure keeps its error.</summary>
        /// <param name="value">The result to convert.</param>
        /// <remarks>With the conversion the other way, code can move from <c>VoidResult</c> to <c>Result&lt;Unit&gt;</c> one call at a time (decision 14, roadmap M2).</remarks>
        /// <exception cref="InvalidOperationException"><paramref name="value"/> is the zero value (<c>default</c>).</exception>
        public static implicit operator Result<Unit>(VoidResult value)
            => value.Match(u => u.Ok(), e => e.Fail<Unit>());

        /// <summary>Converts an error, including any record derived from <see cref="Error"/>, to a failure holding it.</summary>
        /// <param name="error">The error.</param>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <c>null</c>.</exception>
        public static implicit operator VoidResult(Error error)
            => Failure(error);

        /// <summary>Converts <see cref="Unit"/> to a success.</summary>
        /// <param name="value">The unit value.</param>
        public static implicit operator VoidResult(Unit value)
            => Success;

        //Local Methods
        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure, and returns what it returns.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public TResult Match<TResult>(Func<Unit, TResult> successfulFunc, Func<Error, TResult> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            return Case() ? successfulFunc(Unit.Value) : errorFunc(_error);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure.</summary>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public void Switch(Action<Unit> successfulFunc, Action<Error> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            if (Case()) successfulFunc(Unit.Value); else errorFunc(_error);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure, and returns a task that completes when the call's task does.</summary>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        /// <remarks>The function's task is awaited, so an exception the function throws, or its task throws, faults the returned task. Before 0.3 this returned <c>void</c> and dropped the task.</remarks>
        public Task SwitchAsync(Func<Unit, Task> successfulFunc, Func<Error, Task> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            return Case() ? AsyncStep.Await(successfulFunc, Unit.Value) : AsyncStep.Await(errorFunc, _error);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the error on failure, and returns its task.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public Task<TResult> MatchAsync<TResult>(Func<Unit, Task<TResult>> successfulFunc, Func<Error, Task<TResult>> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            return Case() ? AsyncStep.Await(successfulFunc, Unit.Value) : AsyncStep.Await(errorFunc, _error);
        }

        /// <summary>Two results are equal when both are successes, both failures with the same error, or both the zero value.</summary>
        /// <param name="obj">The object to compare with.</param>
        public override bool Equals(object? obj)
            => obj is VoidResult other && _state == other._state && (_state != State.Failure || Equals(_error, other._error));

        /// <summary>Compares two results with <see cref="Equals(object)"/>.</summary>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        public static bool operator ==(VoidResult left, VoidResult right) => left.Equals(right);

        /// <summary>Compares two results with <see cref="Equals(object)"/>.</summary>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        public static bool operator !=(VoidResult left, VoidResult right) => !left.Equals(right);

        /// <summary>A hash of the case and its error.</summary>
        public override int GetHashCode()
            => _state == State.Failure ? State.Hash(_error, 1) : State.Hash(SuccessUnit, 0);

        // The zero value hashes as 0.2's did: a success whose Unit is null.
        private Unit SuccessUnit => _state == State.Zero ? null! : Unit.Value;

        // true for a success, false for a failure; the zero value throws (decision 10).
        private bool Case() => _state == State.Zero ? throw NamedErrors.Zero(typeof(VoidResult)) : _state == State.Success;

        // A success and a failure, built without Result.Ok or Result.Fail, which return Result<Unit> from 0.3.
        internal static VoidResult Success => new VoidResult(Unit.Value);

        internal static VoidResult Failure(Error error) => new VoidResult(error.ThrowIfNoError(nameof(error)));

        // Throws on the zero value; for members that would otherwise return it unread (Then and Check with no steps).
        internal void ThrowIfZero() => _ = Case();

        /// <summary>The type's name, as in 0.2; <c>"&lt;default VoidResult&gt;"</c> for the zero value. Never throws.</summary>
        public override string ToString()
            => _state == State.Zero ? State.DefaultVoidResult : typeof(VoidResult).ToString();

        private string DebuggerDisplay
            => _state switch { State.Success => "Ok", State.Failure => $"Fail({_error})", _ => State.DefaultVoidResult };

        /// <summary>On success, returns the value <paramref name="mapFunc"/> gives as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Gives the new value. Its argument is the success's <see cref="Unit"/>.</param>
        public Result<TResult> Map<TResult>(Func<Unit, TResult> mapFunc)
        {
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Case() ? mapFunc(Unit.Value).Ok() : _error.Fail<TResult>();
        }

        /// <summary>On success, returns the value <paramref name="mapFunc"/> gives as a success. A failure keeps the error and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Gives the new value. Its argument is the success's <see cref="Unit"/>.</param>
        public Task<Result<TResult>> MapAsync<TResult>(Func<Unit, Task<TResult>> mapFunc)
            => BindValueAsync(async _ => (await mapFunc.ThrowIfDefault(nameof(mapFunc))(_).ConfigureAwait(false)).Ok());

        /// <summary>On success, returns <paramref name="value"/> as the success value. A failure keeps the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The new value.</param>
        public Result<T> Map<T>(T value)
            => Map(_ => value);

        /// <summary>On success, awaits <paramref name="value"/> and returns its value as a success. A failure keeps the error, and <paramref name="value"/> isn't awaited.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that gives the new value.</param>
        public Task<Result<T>> MapAsync<T>(Task<T> value)
            => MapAsync(_ => value);

        /// <summary>Maps both sides: the value with <paramref name="mapFunc"/> on success, or the error with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        /// <param name="mapFunc">Gives the new value.</param>
        public Result<TResult, TError> Map2<TResult, TError>(Func<Error, TError> errorMapFunc, Func<Unit, TResult> mapFunc)
            => Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TError>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TError>());

        /// <summary>Maps both sides: the value with <paramref name="mapFunc"/> on success, or the error with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        /// <param name="mapFunc">Gives the new value.</param>
        public Task<Result<TResult, TError>> Map2Async<TResult, TError>(Func<Error, Task<TError>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => Match(
                async success => (await mapFunc().ConfigureAwait(false)).Ok<TResult, TError>(),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail<TResult, TError>());

        /// <summary>On failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        /// <remarks>A function that returns a type derived from <see cref="Error"/> picks this overload, so <c>v.MapError(e =&gt; new SaveFailed(id) { Cause = e })</c> gives a <c>VoidResult&lt;SaveFailed&gt;</c>. To stay a <c>VoidResult</c>, return <see cref="Error"/>: <c>v.MapError(Error (e) =&gt; new SaveFailed(id) { Cause = e })</c>. <c>VoidResult</c> is replaced by <c>Result&lt;Unit&gt;</c>, which has no such overload.</remarks>
        public VoidResult<TErrorResult> MapError<TErrorResult>(Func<Error, TErrorResult> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        /// <summary>On failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        public Task<VoidResult<TErrorResult>> MapErrorAsync<TErrorResult>(Func<Error, Task<TErrorResult>> errorMapFunc)
            => Match(
                success => Task.FromResult(VoidResult<TErrorResult>.Success),
                async errors => VoidResult<TErrorResult>.Failure(await errorMapFunc(errors).ConfigureAwait(false)));

        /// <summary>On failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <param name="errorMapFunc">Maps the error.</param>
        public VoidResult MapError(Func<Error, Error> errorMapFunc)
        {
            errorMapFunc.ThrowIfDefault(nameof(errorMapFunc));
            return Match(_ => Success, e => Failure(errorMapFunc(e)));
        }

        /// <summary>On failure, returns the error <paramref name="errorMapFunc"/> gives for the error. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <param name="errorMapFunc">Maps the error.</param>
        public Task<VoidResult> MapErrorAsync(Func<Error, Task<Error>> errorMapFunc)
            => Match(
                success => Task.FromResult(Success),
                async errors => Failure(await errorMapFunc(errors).ConfigureAwait(false)));



        /// <summary>On success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        public VoidResult Bind(Func<Unit, VoidResult> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Failure);

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        public Task<VoidResult> BindAsync(Func<Unit, Task<VoidResult>> bindFunc)
            => Match(
                x => bindFunc.ThrowIfDefault(nameof(bindFunc))(Unit.Value),
                error => Task.FromResult(Failure(error)));

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        public Result<T> BindValue<T>(Func<Unit, Result<T>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail<T>);

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        public Task<Result<T>> BindValueAsync<T>(Func<Unit, Task<Result<T>>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Fail<T>()));

        /// <summary>Calls <paramref name="action"/> on success, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <param name="action">Called on success.</param>
        public VoidResult Tee(Action<Unit> action)
            => Map(_ =>
            {
                action.ThrowIfDefault(nameof(action))(_);
                return _;
            });

        /// <summary>Calls <paramref name="asyncFunc"/> on success, awaits it, and returns the result unchanged. Nothing is called on a failure.</summary>
        /// <param name="asyncFunc">Called on success.</param>
        public async Task<VoidResult> TeeAsync(Func<Unit, Task> asyncFunc)
            => await MapAsync(async _ =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(_).ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);

        /// <summary>Calls <paramref name="action"/> with the error on failure, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <param name="action">Called with the error.</param>
        public VoidResult TeeError(Action<Error> action)
            => MapError(error =>
            {
                action.ThrowIfDefault(nameof(action))(error);
                return error;
            });

        /// <summary>Calls <paramref name="asyncFunc"/> with the error on failure, awaits it, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <param name="asyncFunc">Called with the error.</param>
        public async Task<VoidResult> TeeErrorAsync(Func<Error, Task> asyncFunc)
            => await MapErrorAsync(async error =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(error).ConfigureAwait(false);
                return error;
            }).ConfigureAwait(false);
    }
}
