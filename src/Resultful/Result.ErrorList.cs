using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Resultful.Utils;
using OneOf.Types;
using OneOf;

namespace Resultful
{
    /// <summary>A success holding a <typeparamref name="T"/> value, or a failure holding a list of error messages.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <remarks>The zero value (<c>default</c>) is neither a success nor a failure (decision 10): <see cref="IsSuccess"/> and <see cref="IsFailure"/> are both <c>false</c>, and every member that reads the value or the errors throws <see cref="InvalidOperationException"/>. <c>ToString</c>, <c>Equals</c> and <c>GetHashCode</c> never throw. A bare <see cref="string"/> converts to a failure, even when <typeparamref name="T"/> is <see cref="string"/>.</remarks>
    [CodeAnalysis.MustObserve]
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
#pragma warning disable CA2231 // Equals keeps 0.2's equality; == and != would be new API, not part of 08a.
    public partial struct Result<T>
#pragma warning restore CA2231
    {
        //Private Members
        private readonly T _value;
        private readonly IEnumerable<string> _errors;
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
            _errors = default!;
            _state = State.Success;
        }

        internal Result(IEnumerable<string> value)
        {
            _value = default!;
            _errors = value;
            _state = State.Failure;
        }

        //Implicit Converters
        /// <summary>Converts a result whose error is a list of messages to a <c>Result&lt;T&gt;</c>. The value or errors are kept.</summary>
        /// <param name="value">The result to convert.</param>
        public static implicit operator Result<T>(Result<T, IEnumerable<string>> value)
            => value.Match(
                result => result.Ok(),
                errors => errors.Fail<T>());

        /// <summary>Converts an array of messages to a failure with those errors.</summary>
        /// <param name="value">The error messages.</param>
        public static implicit operator Result<T>(string[] value)
            => value.Fail<T>();

        /// <summary>Converts a list of messages to a failure with those errors.</summary>
        /// <param name="value">The error messages.</param>
        public static implicit operator Result<T>(List<string> value)
            => value.Fail<T>();

        /// <summary>Converts a message to a failure with that one error.</summary>
        /// <param name="value">The error message.</param>
        /// <remarks>This applies wherever a <c>Result&lt;T&gt;</c> is expected, even for <c>Result&lt;string&gt;</c>: a bare string is always an error.</remarks>
        public static implicit operator Result<T>(string value)
            => Result.Fail<T>(value);

        //Local Methods
        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the errors on failure.</summary>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the errors on failure.</param>
        public void Switch(Action<T> successfulFunc, Action<IEnumerable<string>> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            if (Case()) successfulFunc(_value); else errorFunc(_errors);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the errors on failure, and returns what it returns.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the errors on failure.</param>
        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<IEnumerable<string>, TResult> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            return Case() ? successfulFunc(_value) : errorFunc(_errors);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the errors on failure, and does not wait for the task it returns.</summary>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the errors on failure.</param>
        /// <remarks>The task is dropped, so the method returns before the work finishes and an exception the task throws is lost. To wait for it, use <c>MatchAsync</c> and await the task.</remarks>
        public void SwitchAsync(Func<T, Task> successfulFunc, Func<IEnumerable<string>, Task> errorFunc)
            => _ = Match(successfulFunc, errorFunc);

        /// <summary>Calls <paramref name="successfulFunc"/> with the success value on success, or <paramref name="errorFunc"/> with the errors on failure, and returns its task.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the errors on failure.</param>
        public Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> successfulFunc, Func<IEnumerable<string>, Task<TResult>> errorFunc)
            => Match(successfulFunc, errorFunc);

        /// <summary>Returns the result as a <c>OneOf</c>: <c>T0</c> is the success value, <c>T1</c> is the errors.</summary>
        public OneOf<T, IEnumerable<string>> ToOneOf()
            => Case() ? OneOf<T, IEnumerable<string>>.FromT0(_value) : OneOf<T, IEnumerable<string>>.FromT1(_errors);

        /// <summary>Two results are equal when both are successes with equal values, both failures with the same error list, or both the zero value.</summary>
        /// <param name="obj">The object to compare with.</param>
        /// <remarks>Values and error lists are compared with <c>object.Equals</c>, as in 0.2.</remarks>
        public override bool Equals(object? obj)
            => obj is Result<T> other && _state == other._state && (_state == State.Failure
                ? Equals(_errors, other._errors)
                : Equals(_value, other._value));

        /// <summary>A hash of the case and its value or error list.</summary>
        public override int GetHashCode()
            => _state == State.Failure ? State.Hash(_errors, 1) : State.Hash(_value, 0);

        /// <summary>The type's name, as in 0.2; <c>"&lt;default Result&gt;"</c> for the zero value. Never throws.</summary>
        public override string ToString()
            => _state == State.Zero ? State.DefaultResult : typeof(Result<T>).ToString();

        private string DebuggerDisplay
            => _state switch { State.Success => $"Ok({_value})", State.Failure => $"Fail({string.Join(", ", _errors)})", _ => State.DefaultResult };

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the errors and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                errors => errors.Fail<TResult>());

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps the errors and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public Task<Result<TResult>> BindAsync<TResult>(Func<T, Task<Result<TResult>>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Fail<TResult>()));

        /// <summary>On success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps the errors and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Maps the value.</param>
        public Result<TResult> Map<TResult>(Func<T, TResult> mapFunc)
            => Map2(Result.Id, mapFunc);

        /// <summary>On success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps the errors and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Maps the value.</param>
        public async Task<Result<TResult>> MapAsync<TResult>(Func<T, Task<TResult>> mapFunc)
            => await Map2Async(Task.FromResult, mapFunc).ConfigureAwait(false);

        /// <summary>On failure, returns the error <paramref name="errorMapFunc"/> gives for the errors. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TError">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the errors.</param>
        public Result<T, TError> MapError<TError>(Func<IEnumerable<string>, TError> errorMapFunc)
            => Map2(errorMapFunc, Result.Id);

        /// <summary>On failure, returns the error <paramref name="errorMapFunc"/> gives for the errors. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TError">The type of the new error.</typeparam>
        /// <param name="errorMapFunc">Maps the errors.</param>
        public Task<Result<T, TError>> MapErrorAsync<TError>(Func<IEnumerable<string>, Task<TError>> errorMapFunc)
            => Match(
                item => Task.FromResult(item.Ok<T, TError>()),
                async error => (await errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(error).ConfigureAwait(false)).Fail<T, TError>());


        /// <summary>Maps both sides: the value with <paramref name="mapFunc"/> on success, or the errors with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="errorMapFunc">Maps the errors.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public Result<TResult, TError> Map2<TResult, TError>(Func<IEnumerable<string>, TError> errorMapFunc, Func<T, TResult> mapFunc)
            => Case()
                ? mapFunc.ThrowIfDefault(nameof(mapFunc))(_value).Ok<TResult, TError>()
                : errorMapFunc.ThrowIfDefault(nameof(errorMapFunc))(_errors).Fail<TResult, TError>();

        /// <summary>Maps both sides: the value with <paramref name="mapFunc"/> on success, or the errors with <paramref name="errorMapFunc"/> on failure.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="errorMapFunc">Maps the errors.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public Task<Result<TResult, TError>> Map2Async<TResult, TError>(Func<IEnumerable<string>, Task<TError>> errorMapFunc, Func<T, Task<TResult>> mapFunc)
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

        /// <summary>Calls <paramref name="teeAction"/> with the errors on failure, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <param name="teeAction">Called with the errors.</param>
        public Result<T> TeeError(Action<IEnumerable<string>> teeAction)
            => MapError(x =>
            {
                teeAction.ThrowIfDefault(nameof(teeAction))(x);
                return x;
            });

        /// <summary>Calls <paramref name="asyncFunc"/> with the errors on failure, awaits it, and returns the result unchanged. Nothing is called on a success.</summary>
        /// <param name="asyncFunc">Called with the errors.</param>
        public async Task<Result<T>> TeeErrorAsync(Func<IEnumerable<string>, Task> asyncFunc)
            => await MapErrorAsync(async item =>
            {
                await asyncFunc(item).ConfigureAwait(false);
                return item;
            }).ConfigureAwait(false);

        /// <summary>Converts to an option: the value on success, or None on failure, after calling <paramref name="errorAction"/> with the errors.</summary>
        /// <param name="errorAction">Called with the errors before they are dropped.</param>
        public Option<T> DiscardError(Action<IEnumerable<string>> errorAction)
            => Match(Option.Some, errors =>
            {
                errorAction.ThrowIfDefault(nameof(errorAction))(errors);
                return new None();
            });

        /// <summary>Converts to an option: the value on success, or None on failure, after calling <paramref name="errorAction"/> with the errors.</summary>
        /// <param name="errorAction">Called with the errors before they are dropped.</param>
        public Task<Option<T>> DiscardErrorAsync(Func<IEnumerable<string>, Task> errorAction)
            => Match(
                item => Task.FromResult(item.Some()),
                async errors =>
                {
                    await errorAction.ThrowIfDefault(nameof(errorAction))(errors);
                    return Option<T>.None;
                });

        /// <summary>Converts to an option: the value on success, or None on failure. The errors are dropped.</summary>
        public Option<T> DiscardError()
            => Match(Option.Some, _ => Option<T>.None);

        /// <summary>On success, returns the result <paramref name="bindFunc"/> gives for the value, without a value. A failure keeps the errors.</summary>
        /// <param name="bindFunc">Returns a result without a value from the value.</param>
        public VoidResult DiscardValue(Func<T, VoidResult> bindFunc)
            => Match(bindFunc.ThrowIfDefault(nameof(bindFunc)), Result.Fail);

        /// <summary>On success, returns the result <paramref name="bindFunc"/> gives for the value, without a value. A failure keeps the errors.</summary>
        /// <param name="bindFunc">Returns a result without a value from the value.</param>
        public Task<VoidResult> DiscardValueAsync(Func<T, Task<VoidResult>> bindFunc)
            => Match(
                bindFunc.ThrowIfDefault(nameof(bindFunc)),
                error => Task.FromResult(error.Fail()));

        /// <summary>Drops the value: a success becomes a success with no value, and a failure keeps the errors.</summary>
        public VoidResult DiscardValue()
            => Match(_ => Result.Ok(), Result.Fail);

        /// <summary>Returns the value on success, or the value <paramref name="func"/> gives for the errors on failure.</summary>
        /// <param name="func">Gives a value from the errors.</param>
        public T ReturnOrValue(Func<IEnumerable<string>, T> func)
            => Match(Result.Id, func);

        /// <summary>Returns the value on success, or the value <paramref name="func"/> gives for the errors on failure.</summary>
        /// <param name="func">Gives a value from the errors.</param>
        public Task<T> ReturnOrValueAsync(Func<IEnumerable<string>, Task<T>> func)
            => Match(Result.IdAsync, func);

        /// <summary>Returns the value on success. On failure, throws the exception <paramref name="failFunc"/> builds from the errors.</summary>
        /// <typeparam name="TExn">The type of the exception.</typeparam>
        /// <param name="failFunc">Builds the exception to throw from the errors.</param>
        public T ReturnOrFail<TExn>(Func<IEnumerable<string>, TExn> failFunc) where TExn : Exception
            => Match(Result.Id, err => throw failFunc.ThrowIfDefault(nameof(failFunc))(err));

        /// <summary>On success, casts the value to <typeparamref name="TCast"/>. A failure keeps the errors.</summary>
        /// <typeparam name="TCast">The type to cast the value to.</typeparam>
        /// <remarks>Throws <see cref="System.InvalidCastException"/> if the value isn't a <typeparamref name="TCast"/>.</remarks>
        public Result<TCast> Cast<TCast>() => Map(x => (TCast)(object)x!);

    }
}
