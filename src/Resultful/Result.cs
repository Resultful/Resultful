using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Resultful.Utils;

namespace Resultful
{
    /// <summary>A success holding a <typeparamref name="T"/> value, or a failure holding a <typeparamref name="TError"/> error.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TError">The type of the error.</typeparam>
    /// <remarks>The zero value (<c>default</c>) is neither a success nor a failure (decision 10): <see cref="IsSuccess"/> and <see cref="IsFailure"/> are both <c>false</c>, and every member that reads the value or the error throws <see cref="InvalidOperationException"/>. <c>ToString</c>, <c>Equals</c> and <c>GetHashCode</c> never throw.</remarks>
    [CodeAnalysis.MustObserve]
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public partial struct Result<T, TError>
    {
        //Private Members
        private readonly T _value;
        private readonly TError _error;
        // State.Zero only for the zero value (default), which no Ok or Fail produced (decision 1, decision 10).
        private readonly byte _state;

        //Constructors
        /// <summary>Not supported: <c>new Result&lt;T, TError&gt;()</c> would be the zero value, which is neither a success nor a failure. Use <c>Result.Ok</c> or <c>Result.Fail</c>.</summary>
        /// <exception cref="InvalidOperationException">Always.</exception>
        [Obsolete(NamedErrors.NoConstructor, error: true)]
        public Result() => throw new InvalidOperationException(NamedErrors.NoConstructor);

        internal Result(T value)
        {
            _value = value;
            _error = default!;
            _state = State.Success;
        }

        internal Result(TError value)
        {
            _value = default!;
            _error = value;
            _state = State.Failure;
        }

        //Implicit Converters
        /// <summary>Converts an error to a failure, so <c>return error;</c> works in a method that returns a result.</summary>
        /// <param name="value">The error.</param>
        /// <remarks>When <typeparamref name="T"/> and <typeparamref name="TError"/> are the same type, this and the conversion from <typeparamref name="T"/> are ambiguous (CS0457). When <typeparamref name="TError"/> is an interface, C# applies no user-defined conversion from it (CS0029). Use <c>error.Fail&lt;T, TError&gt;()</c> in both cases.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
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
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            if (Case()) successfulFunc(_value); else errorFunc(_error);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and returns what it returns.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<TError, TResult> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            return Case() ? successfulFunc(_value) : errorFunc(_error);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and returns a task that completes when the call's task does.</summary>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        /// <remarks>The function's task is awaited, so an exception the function throws, or its task throws, faults the returned task. Before 0.3 this returned <c>void</c> and dropped the task.</remarks>
        public Task SwitchAsync(Func<T, Task> successfulFunc, Func<TError, Task> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            return Case() ? AsyncStep.Await(successfulFunc, _value) : AsyncStep.Await(errorFunc, _error);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the error on failure, and returns its task.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the error on failure.</param>
        public Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> successfulFunc, Func<TError, Task<TResult>> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            return Case() ? AsyncStep.Await(successfulFunc, _value) : AsyncStep.Await(errorFunc, _error);
        }

        /// <summary>Two results are equal when both are successes with equal values, both failures with equal errors, or both the zero value.</summary>
        /// <param name="obj">The object to compare with.</param>
        /// <remarks>Values and errors are compared with <c>object.Equals</c>, as in 0.2.</remarks>
        public override bool Equals(object? obj)
            => obj is Result<T, TError> other && _state == other._state && (_state == State.Failure
                ? Equals(_error, other._error)
                : Equals(_value, other._value));

        /// <summary>Compares two results with <see cref="Equals(object)"/>.</summary>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        public static bool operator ==(Result<T, TError> left, Result<T, TError> right) => left.Equals(right);

        /// <summary>Compares two results with <see cref="Equals(object)"/>.</summary>
        /// <param name="left">The first result.</param>
        /// <param name="right">The second result.</param>
        public static bool operator !=(Result<T, TError> left, Result<T, TError> right) => !left.Equals(right);

        /// <summary>A hash of the case and its value or error.</summary>
        public override int GetHashCode()
            => _state == State.Failure ? State.Hash(_error, 1) : State.Hash(_value, 0);

        /// <summary>The type's name, as in 0.2; <c>"&lt;default Result&gt;"</c> for the zero value. Never throws.</summary>
        public override string ToString()
            => _state == State.Zero ? State.DefaultResult : typeof(Result<T, TError>).ToString();

        private string DebuggerDisplay
            => _state switch { State.Success => $"Ok({_value})", State.Failure => $"Fail({_error})", _ => State.DefaultResult };

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public Result<TResult, TError> Bind<TResult>(Func<T, Result<TResult, TError>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail<TResult, TError>);

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public Task<Result<TResult, TError>> BindAsync<TResult>(Func<T, Task<Result<TResult, TError>>> bindFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            return Case() ? AsyncStep.Await(bindFunc, _value) : Task.FromResult(_error.Fail<TResult, TError>());
        }

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

        /// <summary>On failure, maps the error to an <see cref="Error"/>, giving a <c>Result&lt;T&gt;</c>. A success keeps its value.</summary>
        /// <param name="errorMapFunc">Maps the error.</param>
        public Result<T> MapError(Func<TError, Error> errorMapFunc)
        {
            errorMapFunc.ThrowIfDefault(nameof(errorMapFunc));
            return Case() ? _value.Ok() : errorMapFunc(_error).Fail<T>();
        }

        /// <summary>On failure, maps the error to an <see cref="Error"/>, giving a <c>Result&lt;T&gt;</c>. A success keeps its value.</summary>
        /// <param name="errorMapFunc">Maps the error.</param>
        public Task<Result<T>> MapErrorAsync(Func<TError, Task<Error>> errorMapFunc)
            => Match(
                item => Task.FromResult(item.Ok()),
                async error => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(error).ConfigureAwait(false)).Fail<T>());

        /// <summary>Maps both sides: the value with <paramref name="mapFunc"/> on success, or the error with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TErrorResult">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the error.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public Result<TResult, TErrorResult> Map2<TResult, TErrorResult>(Func<TError, TErrorResult> errorMapFunc, Func<T, TResult> mapFunc)
            => Case()
                ? mapFunc.ThrowIfDefault(nameof(mapFunc))(_value).Ok<TResult, TErrorResult>()
                : errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(_error).Fail<TResult, TErrorResult>();

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
                    await errorAction.ThrowIfDefault(nameof(errorAction))(errors).ConfigureAwait(false);
                    return Option<T>.None;
                });

        /// <summary>Converts to an option: the value on success, or None on failure. The error is dropped.</summary>
        public Option<T> DiscardError()
            => Match(Option.Some, _ => Option<T>.None);

        /// <summary>On success, returns the result <paramref name="bindFunc"/> gives for the value, without a value. A failure keeps the error.</summary>
        /// <param name="bindFunc">Returns a result without a value from the value.</param>
        public Result<Unit, TError> DiscardValue(Func<T, Result<Unit, TError>> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail);

        /// <summary>On success, returns the result <paramref name="bindFunc"/> gives for the value, without a value. A failure keeps the error.</summary>
        /// <param name="bindFunc">Returns a result without a value from the value.</param>
        public Task<Result<Unit, TError>> DiscardValueAsync(Func<T, Task<Result<Unit, TError>>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Fail()));


        /// <summary>Drops the value: a success becomes a success with no value, and a failure keeps the error.</summary>
        public Result<Unit, TError> DiscardValue()
            => DiscardValue(_ => Result.Ok<TError>());


        /// <summary>Returns the value on success, or the value <paramref name="func"/> gives for the error on failure.</summary>
        /// <param name="func">Gives a value from the error.</param>
        public T ReturnOrValue(Func<TError, T> func)
            => Match(Result.Id, func);

        /// <summary>Returns the value on success, or the value <paramref name="func"/> gives for the error on failure.</summary>
        /// <param name="func">Gives a value from the error.</param>
        public Task<T> ReturnOrValueAsync(Func<TError, Task<T>> func)
        {
            func.ThrowIfDefault(nameof(func));
            return Case() ? Task.FromResult(_value) : AsyncStep.Await(func, _error);
        }

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

    // The state byte every result type keeps in place of OneOf's case index (decision 1). Zero is the zero value
    // (default), which no Ok or Fail produced.
    internal static class State
    {
        internal const byte Zero = 0;
        internal const byte Success = 1;
        internal const byte Failure = 2;

        // What ToString and the debugger show for the zero value; neither throws (decision 10).
        internal const string DefaultResult = "<default Result>";
        internal const string DefaultVoidResult = "<default VoidResult>";
        internal const string DefaultValidation = "<default Validation>";

        // OneOf<T0, T1>.GetHashCode, which 0.2's results hashed: the case's hash times 397, xor the case index.
        internal static int Hash<T>(T value, int index)
            => unchecked(((value?.GetHashCode() ?? 0) * 397) ^ index);
    }
}
