using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Resultful.Utils;
using OneOf;

namespace Resultful
{
    /// <summary>A success with no value, or a failure holding a list of error messages.</summary>
    /// <remarks>The zero value (<c>default</c>) is neither a success nor a failure (decision 10): every member that reads the case or the error throws <see cref="InvalidOperationException"/>. <c>ToString</c>, <c>Equals</c> and <c>GetHashCode</c> never throw.</remarks>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
#pragma warning disable CA2231 // Equals keeps 0.2's equality; == and != would be new API, not part of 08a.
    public struct VoidResult
#pragma warning restore CA2231
    {
        //Private Members
        private readonly IEnumerable<string> _errors;
        // State.Zero only for the zero value (default), which no Ok or Fail produced (decision 1, decision 10).
        private readonly byte _state;

        //Constructors
        /// <summary>Not supported: <c>new VoidResult()</c> would be the zero value, which is neither a success nor a failure. Use <c>Result.Ok</c> or <c>Result.Fail</c>.</summary>
        /// <exception cref="InvalidOperationException">Always.</exception>
        [Obsolete(NamedErrors.NoConstructor, error: true)]
        public VoidResult() => throw new InvalidOperationException(NamedErrors.NoConstructor);

        internal VoidResult(Unit value)
        {
            _errors = default!;
            _state = value is null ? State.Zero : State.Success;
        }

        internal VoidResult(IEnumerable<string> value)
        {
            _errors = value;
            _state = State.Failure;
        }

        //Implicit Converters
        /// <summary>Converts a result whose value is <see cref="Unit"/> to a result with no value. Errors are kept.</summary>
        /// <param name="value">The result to convert.</param>
        public static implicit operator VoidResult(Result<Unit, IEnumerable<string>> value)
            => value.Match(_ => Result.Ok(), Result.Fail);

        /// <summary>Converts a result whose value is <see cref="Unit"/> to a result with no value. Errors are kept.</summary>
        /// <param name="value">The result to convert.</param>
        public static implicit operator VoidResult(Result<Unit> value)
            => value.Match(_ => Result.Ok(), Result.Fail);

        /// <summary>Converts a result whose error is a list of messages to a <c>VoidResult</c>. Errors are kept.</summary>
        /// <param name="value">The result to convert.</param>
        public static implicit operator VoidResult(VoidResult<IEnumerable<string>> value)
            => value.Match(_ => Result.Ok(), Result.Fail);

        /// <summary>Converts an array of messages to a failure with those errors.</summary>
        /// <param name="value">The error messages.</param>
        public static implicit operator VoidResult(string[] value)
            => Result.Fail(value);

        /// <summary>Converts a list of messages to a failure with those errors.</summary>
        /// <param name="value">The error messages. They are copied.</param>
        /// <remarks>Throws <see cref="System.NullReferenceException"/> if the list is <c>null</c>.</remarks>
        public static implicit operator VoidResult(List<string> value)
            => Result.Fail(value.ToArray());

        /// <summary>Converts a message to a failure with that one error.</summary>
        /// <param name="value">The error message.</param>
        public static implicit operator VoidResult(string value)
            => Result.Fail(value);

        /// <summary>Converts <see cref="Unit"/> to a success.</summary>
        /// <param name="value">The unit value.</param>
        public static implicit operator VoidResult(Unit value)
            => value.Ok();

        //Local Methods
        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the errors on failure, and returns what it returns.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the errors on failure.</param>
        public TResult Match<TResult>(Func<Unit, TResult> successfulFunc, Func<IEnumerable<string>, TResult> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            return Case() ? successfulFunc(Unit.Value) : errorFunc(_errors);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the errors on failure.</summary>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the errors on failure.</param>
        public void Switch(Action<Unit> successfulFunc, Action<IEnumerable<string>> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            if (Case()) successfulFunc(Unit.Value); else errorFunc(_errors);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the errors on failure, and does not wait for the task it returns.</summary>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the errors on failure.</param>
        /// <remarks>The task is dropped, so the method returns before the work finishes and an exception the task throws is lost. To wait for it, use <c>MatchAsync</c> and await the task.</remarks>
        public void SwitchAsync(Func<Unit, Task> successfulFunc, Func<IEnumerable<string>, Task> errorFunc)
            => _ = Match(successfulFunc, errorFunc);

        /// <summary>Calls <paramref name="successfulFunc"/> with <see cref="Unit.Value"/> on success, or <paramref name="errorFunc"/> with the errors on failure, and returns its task.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called on success.</param>
        /// <param name="errorFunc">Called with the errors on failure.</param>
        public Task<TResult> MatchAsync<TResult>(Func<Unit, Task<TResult>> successfulFunc, Func<IEnumerable<string>, Task<TResult>> errorFunc)
            => Match(successfulFunc, errorFunc);

        /// <summary>Returns the result as a <c>OneOf</c>: <c>T0</c> is the <see cref="Unit"/> of a success, <c>T1</c> is the errors.</summary>
        public OneOf<Unit, IEnumerable<string>> ToOneOf()
            => Case() ? OneOf<Unit, IEnumerable<string>>.FromT0(Unit.Value) : OneOf<Unit, IEnumerable<string>>.FromT1(_errors);

        /// <summary>Two results are equal when both are successes, both failures with the same error list, or both the zero value.</summary>
        /// <param name="obj">The object to compare with.</param>
        public override bool Equals(object? obj)
            => obj is VoidResult other && _state == other._state && (_state != State.Failure || Equals(_errors, other._errors));

        /// <summary>A hash of the case and its error.</summary>
        public override int GetHashCode()
            => _state == State.Failure ? State.Hash(_errors, 1) : State.Hash(SuccessUnit, 0);

        // The zero value hashes as 0.2's did: a success whose Unit is null.
        private Unit SuccessUnit => _state == State.Zero ? null! : Unit.Value;

        // true for a success, false for a failure; the zero value throws (decision 10).
        private bool Case() => _state == State.Zero ? throw NamedErrors.Zero(typeof(VoidResult)) : _state == State.Success;

        // Throws on the zero value; for members that would otherwise return it unread (Then and Check with no steps).
        internal void ThrowIfZero() => _ = Case();

        /// <summary>The type's name, as in 0.2; <c>"&lt;default VoidResult&gt;"</c> for the zero value. Never throws.</summary>
        public override string ToString()
            => _state == State.Zero ? State.DefaultVoidResult : typeof(VoidResult).ToString();

        private string DebuggerDisplay
            => _state switch { State.Success => "Ok", State.Failure => $"Fail({string.Join(", ", _errors)})", _ => State.DefaultVoidResult };

        /// <summary>On success, returns the value <paramref name="mapFunc"/> gives as a success. A failure keeps the errors and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Gives the new value. Its argument is the success's <see cref="Unit"/>.</param>
        public Result<TResult> Map<TResult>(Func<Unit, TResult> mapFunc)
            => Map2(Result.Id, mapFunc);

        /// <summary>On success, returns the value <paramref name="mapFunc"/> gives as a success. A failure keeps the errors and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Gives the new value. Its argument is the success's <see cref="Unit"/>.</param>
        public Task<Result<TResult>> MapAsync<TResult>(Func<Unit, Task<TResult>> mapFunc)
            => BindValueAsync(async _ => (await mapFunc.ThrowIfDefault(nameof(mapFunc))(_).ConfigureAwait(false)).Ok());

        /// <summary>On success, returns <paramref name="value"/> as the success value. A failure keeps the errors.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The new value.</param>
        public Result<T> Map<T>(T value)
            => Map(_ => value);

        /// <summary>On success, awaits <paramref name="value"/> and returns its value as a success. A failure keeps the errors, and <paramref name="value"/> isn't awaited.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that gives the new value.</param>
        public Task<Result<T>> MapAsync<T>(Task<T> value)
            => MapAsync(_ => value);

        /// <summary>Maps both sides: the value with <paramref name="mapFunc"/> on success, or the errors with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="errorMapFunc">Maps the errors.</param>
        /// <param name="mapFunc">Gives the new value.</param>
        public Result<TResult, TError> Map2<TResult, TError>(Func<IEnumerable<string>, TError> errorMapFunc, Func<Unit, TResult> mapFunc)
            => Match(
                success => mapFunc.ThrowIfDefault(nameof(mapFunc))(success).Ok<TResult, TError>(),
                errors => errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(errors).Fail<TResult, TError>());

        /// <summary>Maps both sides: the value with <paramref name="mapFunc"/> on success, or the errors with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="errorMapFunc">Maps the errors.</param>
        /// <param name="mapFunc">Gives the new value.</param>
        public Task<Result<TResult, TError>> Map2Async<TResult, TError>(Func<IEnumerable<string>, Task<TError>> errorMapFunc, Func<Task<TResult>> mapFunc)
            => Match(
                async success => (await mapFunc().ConfigureAwait(false)).Ok<TResult, TError>(),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail<TResult, TError>());

        /// <summary>On failure, returns the error <paramref name="errorMapFunc"/> gives for the errors. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the errors.</param>
        public VoidResult<TErrorResult> MapError<TErrorResult>(Func<IEnumerable<string>, TErrorResult> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        /// <summary>On failure, returns the error <paramref name="errorMapFunc"/> gives for the errors. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the errors.</param>
        public Task<VoidResult<TErrorResult>> MapErrorAsync<TErrorResult>(Func<IEnumerable<string>, Task<TErrorResult>> errorMapFunc)
            => Match(
                success => Task.FromResult(Result.Ok<TErrorResult>()),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail());

        /// <summary>On failure, returns the error <paramref name="errorMapFunc"/> gives for the errors. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <param name="errorMapFunc">Maps the errors.</param>
        public VoidResult MapError(Func<IEnumerable<string>, IEnumerable<string>> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        /// <summary>On failure, returns the error <paramref name="errorMapFunc"/> gives for the errors. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <param name="errorMapFunc">Maps the errors.</param>
        public Task<VoidResult> MapErrorAsync(Func<IEnumerable<string>, Task<IEnumerable<string>>> errorMapFunc)
            => Match(
                success => Task.FromResult(Result.Ok()),
                async errors => (await errorMapFunc(errors).ConfigureAwait(false)).Fail());



        /// <summary>On success, returns what <paramref name="bindFunc"/> returns. A failure keeps the errors and <paramref name="bindFunc"/> isn't called.</summary>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        public VoidResult Bind(Func<Unit, VoidResult> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail);

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns. A failure keeps the errors and <paramref name="bindFunc"/> isn't called.</summary>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        public Task<VoidResult> BindAsync(Func<Unit, Task<VoidResult>> bindFunc)
            => Match(
                x => bindFunc.ThrowIfDefault(nameof(bindFunc))(Unit.Value),
                error => Task.FromResult(error.Fail()));

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns. A failure keeps the errors and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="bindFunc">Returns the next result. Its argument is the success's <see cref="Unit"/>.</param>
        public Result<T> BindValue<T>(Func<Unit, Result<T>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail<T>);

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns. A failure keeps the errors and <paramref name="bindFunc"/> isn't called.</summary>
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

        /// <summary>Calls <paramref name="action"/> with the errors on failure, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <param name="action">Called with the errors.</param>
        public VoidResult TeeError(Action<IEnumerable<string>> action)
            => MapError(error =>
            {
                action.ThrowIfDefault(nameof(action))(error);
                return error;
            });

        /// <summary>Calls <paramref name="asyncFunc"/> with the errors on failure, awaits it, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <param name="asyncFunc">Called with the errors.</param>
        public async Task<VoidResult> TeeErrorAsync(Func<IEnumerable<string>, Task> asyncFunc)
            => await MapErrorAsync(async error =>
            {
                await asyncFunc.ThrowIfDefault(nameof(asyncFunc))(error).ConfigureAwait(false);
                return error;
            }).ConfigureAwait(false);
    }
}
