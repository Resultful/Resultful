using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Resultful.Utils;

namespace Resultful
{
    /// <summary>A success holding a <typeparamref name="T"/> value, or a failure holding a non-empty list of <typeparamref name="TError"/> errors.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TError">The type of each error.</typeparam>
    /// <remarks>
    /// Decision 14: a <c>Validation</c> gathers. <see cref="Validation.Combine{T1, T2, TError}(Validation{T1, TError}, Validation{T2, TError})"/> keeps every error from every side, in order, so
    /// independent checks all run and all their failures are reported. It has no <c>Bind</c>: a step that needs the value comes after the checks,
    /// in a <c>Result</c>, through <see cref="ToResult(Func{IReadOnlyList{TError}, TError})"/> or <see cref="Validation.ToResult{T, TError}"/>.
    /// The zero value (<c>default</c>) is neither a success nor a failure (decision 10): <see cref="IsSuccess"/> and <see cref="IsFailure"/> are both
    /// <c>false</c>, and every member that reads the value or the errors throws <see cref="InvalidOperationException"/>. <c>ToString</c>,
    /// <c>Equals</c> and <c>GetHashCode</c> never throw.
    /// </remarks>
    [CodeAnalysis.MustObserve]
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public readonly struct Validation<T, TError> : IEquatable<Validation<T, TError>>
    {
        private readonly T _value;
        // Never empty on a failure; a read-only wrapper over an array no caller can reach.
        private readonly ReadOnlyCollection<TError> _errors;
        // State.Zero only for the zero value (default), which no Ok or Fail produced (decision 1, decision 10).
        private readonly byte _state;

        /// <summary>Not supported: <c>new Validation&lt;T, TError&gt;()</c> would be the zero value, which is neither a success nor a failure. Use <c>Validation.Ok</c> or <c>Validation.Fail</c>.</summary>
        /// <exception cref="InvalidOperationException">Always.</exception>
        [Obsolete(NamedErrors.NoValidationConstructor, error: true)]
        public Validation() => throw new InvalidOperationException(NamedErrors.NoValidationConstructor);

        internal Validation(T value)
        {
            _value = value;
            _errors = null!;
            _state = State.Success;
        }

        // The list is non-empty, holds no null, and wraps an array no caller can reach, so failures can share it.
        private Validation(ReadOnlyCollection<TError> errors)
        {
            _value = default!;
            _errors = errors;
            _state = State.Failure;
        }

        private static Validation<T, TError> Owned(TError[] errors) => new(new ReadOnlyCollection<TError>(errors));

        internal static Validation<T, TError> Failed(TError error)
            => Owned([error.ThrowIfNoError(nameof(error))]);

        internal static Validation<T, TError> Failed(IEnumerable<TError> errors, string argName)
        {
            TError[] copy;
            try
            {
                copy = (errors ?? throw new ArgumentNullException(argName)).ToArray();
            }
            catch (InvalidOperationException e)
            {
                // A default ImmutableArray, or a sequence that can't be read, is a bad argument, not a bad state.
                throw new ArgumentException("The errors could not be read; a default ImmutableArray holds none.", argName, e);
            }

            if (copy.Length == 0)
                throw new ArgumentException("A failed Validation needs at least one error.", argName);
            for (var i = 0; i < copy.Length; i++)
                copy[i].ThrowIfNoError(argName);
            return Owned(copy);
        }

        // Errors already checked: the lists of failed Validations, joined.
        internal static Validation<T, TError> Gathered(List<TError> errors) => Owned(errors.ToArray());

        /// <summary>Converts a value to a success holding it.</summary>
        /// <param name="value">The value.</param>
        public static implicit operator Validation<T, TError>(T value)
            => new(value);

        /// <summary>Converts an error to a failure holding a list of one.</summary>
        /// <param name="error">The error.</param>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <c>null</c>.</exception>
        public static implicit operator Validation<T, TError>(TError error)
            => Failed(error);

        /// <summary>Converts a result: a success keeps its value, and a failure's one error becomes a list of one. Nothing is lost.</summary>
        /// <param name="result">The result.</param>
        /// <exception cref="InvalidOperationException"><paramref name="result"/> is the zero value (<c>default</c>).</exception>
        public static implicit operator Validation<T, TError>(Result<T, TError> result)
            => result.Match(v => new Validation<T, TError>(v), e => Owned([e]));

        /// <summary><c>true</c> if this is a success. Then <see cref="Value"/> is set and <see cref="Errors"/> throws.</summary>
        /// <remarks>The zero value (<c>default</c>) is neither a success nor a failure: <see cref="IsSuccess"/> and <see cref="IsFailure"/> are both <c>false</c>. Never throws.</remarks>
        [MemberNotNullWhen(true, nameof(Value))]
        [MemberNotNullWhen(false, nameof(Errors))]
        public bool IsSuccess => _state == State.Success;

        /// <summary><c>true</c> if this is a failure. Then <see cref="Errors"/> is set and <see cref="Value"/> throws.</summary>
        /// <remarks>The zero value (<c>default</c>) is neither a success nor a failure: <see cref="IsSuccess"/> and <see cref="IsFailure"/> are both <c>false</c>. Never throws.</remarks>
        [MemberNotNullWhen(false, nameof(Value))]
        [MemberNotNullWhen(true, nameof(Errors))]
        public bool IsFailure => _state == State.Failure;

        /// <summary>The value of a success.</summary>
        /// <exception cref="InvalidOperationException">This is a failure, or the zero value (<c>default</c>).</exception>
        public T Value => Case() ? _value : throw NamedErrors.NoValue();

        /// <summary>The errors of a failure, in order. Never empty.</summary>
        /// <exception cref="InvalidOperationException">This is a success, or the zero value (<c>default</c>).</exception>
        public IReadOnlyList<TError> Errors => Case() ? throw NamedErrors.NoErrors() : _errors;

        /// <summary>Gets the value of a success, like <c>Dictionary.TryGetValue</c>.</summary>
        /// <param name="value">The value on success; <c>default</c> on failure.</param>
        /// <returns><c>true</c> on success, <c>false</c> on failure.</returns>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public bool TryGetValue([MaybeNullWhen(false)] out T value)
        {
            var isSuccess = Case();
            value = isSuccess ? _value : default;
            return isSuccess;
        }

        /// <summary>Gets the errors of a failure, like <c>Dictionary.TryGetValue</c>.</summary>
        /// <param name="errors">The errors on failure, never empty; <c>null</c> on success.</param>
        /// <returns><c>true</c> on failure, <c>false</c> on success.</returns>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public bool TryGetErrors([MaybeNullWhen(false)] out IReadOnlyList<TError> errors)
        {
            var isSuccess = Case();
            errors = isSuccess ? null : _errors;
            return !isSuccess;
        }

        /// <summary>Deconstructs the validation: <c>var (isSuccess, value, errors) = validation;</c>.</summary>
        /// <param name="isSuccess"><c>true</c> on success.</param>
        /// <param name="value">The value on success; <c>default</c> on failure.</param>
        /// <param name="errors">The errors on failure; <c>null</c> on success.</param>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public void Deconstruct(out bool isSuccess, out T? value, out IReadOnlyList<TError>? errors)
        {
            isSuccess = Case();
            value = isSuccess ? _value : default;
            errors = isSuccess ? null : _errors;
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with the value on success, or <paramref name="errorFunc"/> with the errors on failure, and returns what it returns.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the errors on failure; never empty.</param>
        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<IReadOnlyList<TError>, TResult> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            return Case() ? successfulFunc(_value) : errorFunc(_errors);
        }

        /// <summary>Calls <paramref name="successfulFunc"/> with the value on success, or <paramref name="errorFunc"/> with the errors on failure.</summary>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the errors on failure; never empty.</param>
        public void Switch(Action<T> successfulFunc, Action<IReadOnlyList<TError>> errorFunc)
        {
            successfulFunc.ThrowIfDefault(nameof(successfulFunc));
            errorFunc.ThrowIfDefault(nameof(errorFunc));
            if (Case()) successfulFunc(_value); else errorFunc(_errors);
        }

        /// <summary>On success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps its errors and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Maps the value.</param>
        public Validation<TResult, TError> Map<TResult>(Func<T, TResult> mapFunc)
        {
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Case() ? new Validation<TResult, TError>(mapFunc(_value)) : new Validation<TResult, TError>(_errors);
        }

        /// <summary>On failure, maps each error with <paramref name="errorMapFunc"/>, keeping their order. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <typeparam name="TErrorResult">The type of the new errors.</typeparam>
        /// <param name="errorMapFunc">Maps one error.</param>
        /// <exception cref="ArgumentNullException"><paramref name="errorMapFunc"/> is <c>null</c>, or returns <c>null</c>.</exception>
        public Validation<T, TErrorResult> MapError<TErrorResult>(Func<TError, TErrorResult> errorMapFunc)
        {
            errorMapFunc.ThrowIfDefault(nameof(errorMapFunc));
            return Case()
                ? new Validation<T, TErrorResult>(_value)
                : Validation<T, TErrorResult>.Failed(_errors.Select(errorMapFunc), nameof(errorMapFunc));
        }

        /// <summary>Calls <paramref name="teeAction"/> with the value on success, and returns the validation unchanged. Nothing is called on a failure.</summary>
        /// <param name="teeAction">Called with the value.</param>
        public Validation<T, TError> Tee(Action<T> teeAction)
        {
            teeAction.ThrowIfDefault(nameof(teeAction));
            if (Case()) teeAction(_value);
            return this;
        }

        /// <summary>Converts to a result: a success keeps its value, and a failure's errors become the one error <paramref name="combine"/> gives for them.</summary>
        /// <param name="combine">Says how many errors become one. Called with the errors, never empty, only on a failure.</param>
        /// <remarks>Many errors become one only when the caller says how (decision 14). For a failure with one error, <c>ToResult(es =&gt; es[0])</c> gives back the result it came from. When <typeparamref name="TError"/> derives from <see cref="Error"/>, <see cref="Validation.ToResult{T, TError}"/> needs no function.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>, or returns <c>null</c>.</exception>
        public Result<T, TError> ToResult(Func<IReadOnlyList<TError>, TError> combine)
        {
            combine.ThrowIfDefault(nameof(combine));
            return Case() ? _value.Ok<T, TError>() : combine(_errors).ThrowIfNoError(nameof(combine)).Fail<T, TError>();
        }

        // Joins this failure's errors onto the list (made on the first failure) and returns true for a success. Combine
        // calls it on every side, so every error is kept, in order. The zero value throws.
        internal bool Gather(ref List<TError>? errors)
        {
            if (Case()) return true;
            (errors ??= []).AddRange(_errors);
            return false;
        }

        // The value of a success that Gather has seen.
        internal T SuccessValue => _value;

        /// <summary>Two validations are equal when both are successes with equal values, both failures with equal errors element by element and in order, or both the zero value.</summary>
        /// <param name="other">The validation to compare with.</param>
        public bool Equals(Validation<T, TError> other)
            => _state == other._state && _state switch
            {
                State.Success => EqualityComparer<T>.Default.Equals(_value, other._value),
                State.Failure => _errors.SequenceEqual(other._errors),
                _ => true,
            };

        /// <summary>Two validations are equal when both are successes with equal values, both failures with equal errors element by element and in order, or both the zero value.</summary>
        /// <param name="obj">The object to compare with.</param>
        public override bool Equals(object? obj)
            => obj is Validation<T, TError> other && Equals(other);

        /// <summary>A hash of the case and its value or errors, which agrees with <see cref="Equals(Validation{T, TError})"/>.</summary>
        public override int GetHashCode()
        {
            if (_state != State.Failure) return State.Hash(_value, _state);
            unchecked
            {
                var hash = 2;
                foreach (var error in _errors)
                    hash = (hash * -1521134295) + (error?.GetHashCode() ?? 0);
                return hash;
            }
        }

        /// <summary>The type's name, as a result prints; <c>"&lt;default Validation&gt;"</c> for the zero value. Never throws.</summary>
        public override string ToString()
            => _state == State.Zero ? State.DefaultValidation : typeof(Validation<T, TError>).ToString();

        /// <summary>Compares two validations with <see cref="Equals(Validation{T, TError})"/>.</summary>
        /// <param name="left">The first validation.</param>
        /// <param name="right">The second validation.</param>
        public static bool operator ==(Validation<T, TError> left, Validation<T, TError> right) => left.Equals(right);

        /// <summary>Compares two validations with <see cref="Equals(Validation{T, TError})"/>.</summary>
        /// <param name="left">The first validation.</param>
        /// <param name="right">The second validation.</param>
        public static bool operator !=(Validation<T, TError> left, Validation<T, TError> right) => !left.Equals(right);

        internal string DebuggerDisplay
            => _state switch
            {
                State.Success => $"Ok({_value})",
                State.Failure => $"Fail([{string.Join("; ", _errors)}])",
                _ => State.DefaultValidation,
            };

        // true for a success, false for a failure; the zero value throws (decision 10). Every member that reads the case goes through it.
        private bool Case() => _state == State.Zero ? throw NamedErrors.Zero(typeof(Validation<T, TError>)) : _state == State.Success;
    }

    /// <summary>A success holding a <typeparamref name="T"/> value, or a failure holding a non-empty list of <see cref="Resultful.Error"/>s.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <remarks>It means <c>Validation&lt;T, Error&gt;</c> (decision 14), as a separate type. <see cref="ToResult()"/> carries the errors into a <c>Result&lt;T&gt;</c> as one <see cref="ValidationFailed{TError}"/>. The zero value (<c>default</c>) behaves as <see cref="Validation{T, TError}"/>'s does.</remarks>
    [CodeAnalysis.MustObserve]
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public readonly struct Validation<T> : IEquatable<Validation<T>>
    {
        // The storage: Validation<T, Error> holds the state byte, the value and the list.
        private readonly Validation<T, Error> _inner;

        /// <summary>Not supported: <c>new Validation&lt;T&gt;()</c> would be the zero value, which is neither a success nor a failure. Use <c>Validation.Ok</c> or <c>Validation.Fail</c>.</summary>
        /// <exception cref="InvalidOperationException">Always.</exception>
        [Obsolete(NamedErrors.NoValidationConstructor, error: true)]
        public Validation() => throw new InvalidOperationException(NamedErrors.NoValidationConstructor);

        internal Validation(Validation<T, Error> inner) => _inner = inner;

        internal Validation<T, Error> Inner => _inner;

        /// <summary>Converts a value to a success holding it.</summary>
        /// <param name="value">The value.</param>
        public static implicit operator Validation<T>(T value)
            => new(new Validation<T, Error>(value));

        /// <summary>Converts an error, including any record derived from <see cref="Resultful.Error"/>, to a failure holding a list of one.</summary>
        /// <param name="error">The error.</param>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <c>null</c>.</exception>
        public static implicit operator Validation<T>(Error error)
            => new(Validation<T, Error>.Failed(error));

        /// <summary>Converts a result: a success keeps its value, and a failure's one error becomes a list of one. Nothing is lost.</summary>
        /// <param name="result">The result.</param>
        /// <exception cref="InvalidOperationException"><paramref name="result"/> is the zero value (<c>default</c>).</exception>
        public static implicit operator Validation<T>(Result<T> result)
            => result.Match(v => (Validation<T>)v, e => (Validation<T>)e);

        /// <summary><c>true</c> if this is a success. Then <see cref="Value"/> is set and <see cref="Errors"/> throws.</summary>
        /// <remarks>The zero value (<c>default</c>) is neither a success nor a failure: <see cref="IsSuccess"/> and <see cref="IsFailure"/> are both <c>false</c>. Never throws.</remarks>
        [MemberNotNullWhen(true, nameof(Value))]
        [MemberNotNullWhen(false, nameof(Errors))]
        public bool IsSuccess => _inner.IsSuccess;

        /// <summary><c>true</c> if this is a failure. Then <see cref="Errors"/> is set and <see cref="Value"/> throws.</summary>
        /// <remarks>The zero value (<c>default</c>) is neither a success nor a failure: <see cref="IsSuccess"/> and <see cref="IsFailure"/> are both <c>false</c>. Never throws.</remarks>
        [MemberNotNullWhen(false, nameof(Value))]
        [MemberNotNullWhen(true, nameof(Errors))]
        public bool IsFailure => _inner.IsFailure;

        /// <summary>The value of a success.</summary>
        /// <exception cref="InvalidOperationException">This is a failure, or the zero value (<c>default</c>).</exception>
        public T Value => _inner.Value;

        /// <summary>The errors of a failure, in order. Never empty.</summary>
        /// <exception cref="InvalidOperationException">This is a success, or the zero value (<c>default</c>).</exception>
        public IReadOnlyList<Error> Errors => _inner.Errors;

        /// <summary>Gets the value of a success, like <c>Dictionary.TryGetValue</c>.</summary>
        /// <param name="value">The value on success; <c>default</c> on failure.</param>
        /// <returns><c>true</c> on success, <c>false</c> on failure.</returns>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public bool TryGetValue([MaybeNullWhen(false)] out T value) => _inner.TryGetValue(out value);

        /// <summary>Gets the errors of a failure, like <c>Dictionary.TryGetValue</c>.</summary>
        /// <param name="errors">The errors on failure, never empty; <c>null</c> on success.</param>
        /// <returns><c>true</c> on failure, <c>false</c> on success.</returns>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public bool TryGetErrors([MaybeNullWhen(false)] out IReadOnlyList<Error> errors) => _inner.TryGetErrors(out errors);

        /// <summary>Deconstructs the validation: <c>var (isSuccess, value, errors) = validation;</c>.</summary>
        /// <param name="isSuccess"><c>true</c> on success.</param>
        /// <param name="value">The value on success; <c>default</c> on failure.</param>
        /// <param name="errors">The errors on failure; <c>null</c> on success.</param>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public void Deconstruct(out bool isSuccess, out T? value, out IReadOnlyList<Error>? errors)
            => _inner.Deconstruct(out isSuccess, out value, out errors);

        /// <summary>Calls <paramref name="successfulFunc"/> with the value on success, or <paramref name="errorFunc"/> with the errors on failure, and returns what it returns.</summary>
        /// <typeparam name="TResult">The type both functions return.</typeparam>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the errors on failure; never empty.</param>
        public TResult Match<TResult>(Func<T, TResult> successfulFunc, Func<IReadOnlyList<Error>, TResult> errorFunc)
            => _inner.Match(successfulFunc, errorFunc);

        /// <summary>Calls <paramref name="successfulFunc"/> with the value on success, or <paramref name="errorFunc"/> with the errors on failure.</summary>
        /// <param name="successfulFunc">Called with the value on success.</param>
        /// <param name="errorFunc">Called with the errors on failure; never empty.</param>
        public void Switch(Action<T> successfulFunc, Action<IReadOnlyList<Error>> errorFunc)
            => _inner.Switch(successfulFunc, errorFunc);

        /// <summary>On success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps its errors and <paramref name="mapFunc"/> isn't called.</summary>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="mapFunc">Maps the value.</param>
        public Validation<TResult> Map<TResult>(Func<T, TResult> mapFunc)
            => new(_inner.Map(mapFunc));

        /// <summary>On failure, maps each error to another <see cref="Resultful.Error"/>, keeping their order. A success keeps its value and <paramref name="errorMapFunc"/> isn't called.</summary>
        /// <param name="errorMapFunc">Maps one error: a note (<c>e =&gt; e.WithContext("…")</c>) or a wrapper.</param>
        /// <remarks>Stays a <c>Validation&lt;T&gt;</c> whatever <see cref="Resultful.Error"/> the function returns, as <c>Result&lt;T&gt;.MapError</c> does (decision 14, 09).</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="errorMapFunc"/> is <c>null</c>, or returns <c>null</c>.</exception>
        public Validation<T> MapError(Func<Error, Error> errorMapFunc)
            => new(_inner.MapError(errorMapFunc));

        /// <summary>Calls <paramref name="teeAction"/> with the value on success, and returns the validation unchanged. Nothing is called on a failure.</summary>
        /// <param name="teeAction">Called with the value.</param>
        public Validation<T> Tee(Action<T> teeAction)
            => new(_inner.Tee(teeAction));

        /// <summary>Converts to a result: a success keeps its value, and a failure's errors become the one error <paramref name="combine"/> gives for them.</summary>
        /// <param name="combine">Says how many errors become one. Called with the errors, never empty, only on a failure.</param>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>, or returns <c>null</c>.</exception>
        public Result<T> ToResult(Func<IReadOnlyList<Error>, Error> combine)
            => _inner.ToResult(combine).AsResult();

        /// <summary>Converts to a result: a success keeps its value, and a failure's errors travel as one <see cref="ValidationFailed{TError}"/>, in order, which <see cref="Resultful.Error.TryFind{TError}"/> searches.</summary>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public Result<T> ToResult()
            => _inner.ToResult();

        /// <summary>Two validations are equal when both are successes with equal values, both failures with equal errors element by element and in order, or both the zero value.</summary>
        /// <param name="other">The validation to compare with.</param>
        public bool Equals(Validation<T> other) => _inner.Equals(other._inner);

        /// <summary>Two validations are equal when both are successes with equal values, both failures with equal errors element by element and in order, or both the zero value.</summary>
        /// <param name="obj">The object to compare with.</param>
        public override bool Equals(object? obj) => obj is Validation<T> other && Equals(other);

        /// <summary>A hash of the case and its value or errors, which agrees with <see cref="Equals(Validation{T})"/>.</summary>
        public override int GetHashCode() => _inner.GetHashCode();

        /// <summary>The type's name, as a result prints; <c>"&lt;default Validation&gt;"</c> for the zero value. Never throws.</summary>
        public override string ToString()
            => IsSuccess || IsFailure ? typeof(Validation<T>).ToString() : State.DefaultValidation;

        /// <summary>Compares two validations with <see cref="Equals(Validation{T})"/>.</summary>
        /// <param name="left">The first validation.</param>
        /// <param name="right">The second validation.</param>
        public static bool operator ==(Validation<T> left, Validation<T> right) => left.Equals(right);

        /// <summary>Compares two validations with <see cref="Equals(Validation{T})"/>.</summary>
        /// <param name="left">The first validation.</param>
        /// <param name="right">The second validation.</param>
        public static bool operator !=(Validation<T> left, Validation<T> right) => !left.Equals(right);

        private string DebuggerDisplay => _inner.DebuggerDisplay;
    }

    /// <summary>Builders for <see cref="Validation{T, TError}"/> and <see cref="Validation{T}"/>, <c>Combine</c>, which gathers every error, and <c>ToResult</c>.</summary>
    /// <remarks>There is no <c>Bind</c> and no <c>+</c> on a validation (decision 14): checks that don't depend on each other are combined, and a step that needs their value comes after them, in a <c>Result</c>.</remarks>
    public static class Validation
    {
        /// <summary>Returns a success holding <paramref name="value"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="value">The success value. <c>null</c> is allowed.</param>
        public static Validation<T, TError> Ok<T, TError>(T value) => new(value);

        /// <summary>Returns a failure holding a list of one error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="error">The error.</param>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <c>null</c>.</exception>
        public static Validation<T, TError> Fail<T, TError>(TError error) => Validation<T, TError>.Failed(error);

        /// <summary>Returns a failure holding <paramref name="errors"/>, in order, copied.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="errors">The errors. At least one, and none <c>null</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <c>null</c>, or holds <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="errors"/> is empty: a failed validation is never empty.</exception>
        public static Validation<T, TError> Fail<T, TError>(IEnumerable<TError> errors) => Validation<T, TError>.Failed(errors, nameof(errors));

        /// <summary>Returns a success holding <paramref name="value"/>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The success value. <c>null</c> is allowed.</param>
        public static Validation<T> Ok<T>(T value) => value;

        /// <summary>Returns a failure holding a list of one error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="error">The error.</param>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <c>null</c>.</exception>
        public static Validation<T> Fail<T>(Error error) => error;

        /// <summary>Returns a failure holding <paramref name="errors"/>, in order, copied.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="errors">The errors. At least one, and none <c>null</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <c>null</c>, or holds <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="errors"/> is empty: a failed validation is never empty.</exception>
        public static Validation<T> Fail<T>(IEnumerable<Error> errors) => new(Validation<T, Error>.Failed(errors, nameof(errors)));

        /// <summary>Converts to a result: a success keeps its value, and a failure's errors travel as one <see cref="ValidationFailed{TError}"/>, in order, which <see cref="Error.TryFind{TError}"/> searches.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of each error, derived from <see cref="Error"/>.</typeparam>
        /// <param name="validation">The validation.</param>
        /// <remarks>Decision 14: no function is needed to say how many errors become one, because <see cref="ValidationFailed{TError}"/> holds them all.</remarks>
        /// <exception cref="InvalidOperationException"><paramref name="validation"/> is the zero value (<c>default</c>).</exception>
        public static Result<T> ToResult<T, TError>(this Validation<T, TError> validation) where TError : Error
            => validation.Match(v => v.Ok(), errors => new ValidationFailed<TError>(errors).Fail<T>());

        /// <summary>Widens a validation whose errors derive from <see cref="Error"/> to a <c>Validation&lt;T&gt;</c>. The value or the errors are kept, flat and in order, and so is each error's type at run time.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of each error, derived from <see cref="Error"/>.</typeparam>
        /// <param name="validation">The validation to widen.</param>
        /// <remarks>Named, as <c>AsResult()</c> is, because C# can't write a conversion generic over every <typeparamref name="TError"/> deriving from <see cref="Error"/>. Lets checks with different error types meet in one <c>Combine</c>.</remarks>
        /// <exception cref="InvalidOperationException"><paramref name="validation"/> is the zero value (<c>default</c>).</exception>
        public static Validation<T> AsValidation<T, TError>(this Validation<T, TError> validation) where TError : Error
            => new(validation.MapError(e => (Error)e));

        /// <summary>Combines two validations into a tuple: both values, or every error from both sides, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<(T1, T2), TError> Combine<T1, T2, TError>(Validation<T1, TError> v1, Validation<T2, TError> v2)
            => Combine(v1, v2, (a, b) => (a, b));

        /// <summary>Combines two validations into a tuple: both values, or every error from both sides, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<(T1, T2)> Combine<T1, T2>(Validation<T1> v1, Validation<T2> v2)
            => Combine(v1, v2, (a, b) => (a, b));


        /// <summary>Combines 2 validations with <paramref name="combine"/>: its value for every value, or every error from every side, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <param name="combine">Names the values and builds the combined one. Called only when every validation is a success.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<TResult, TError> Combine<T1, T2, TError, TResult>(Validation<T1, TError> v1, Validation<T2, TError> v2, Func<T1, T2, TResult> combine)
        {
            combine.ThrowIfDefault(nameof(combine));
            List<TError>? errors = null;
            // & rather than &&: every side is read, so no error is dropped.
            return v1.Gather(ref errors) & v2.Gather(ref errors)
                ? new Validation<TResult, TError>(combine(v1.SuccessValue, v2.SuccessValue))
                : Validation<TResult, TError>.Gathered(errors!);
        }

        /// <summary>Combines 2 validations with <paramref name="combine"/>: its value for every value, or every error from every side, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <param name="combine">Names the values and builds the combined one. Called only when every validation is a success.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<TResult> Combine<T1, T2, TResult>(Validation<T1> v1, Validation<T2> v2, Func<T1, T2, TResult> combine)
            => new(Combine(v1.Inner, v2.Inner, combine.ThrowIfDefault(nameof(combine))));

        /// <summary>Combines 3 validations with <paramref name="combine"/>: its value for every value, or every error from every side, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <param name="v3">The third validation.</param>
        /// <param name="combine">Names the values and builds the combined one. Called only when every validation is a success.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<TResult, TError> Combine<T1, T2, T3, TError, TResult>(Validation<T1, TError> v1, Validation<T2, TError> v2, Validation<T3, TError> v3, Func<T1, T2, T3, TResult> combine)
        {
            combine.ThrowIfDefault(nameof(combine));
            List<TError>? errors = null;
            // & rather than &&: every side is read, so no error is dropped.
            return v1.Gather(ref errors) & v2.Gather(ref errors) & v3.Gather(ref errors)
                ? new Validation<TResult, TError>(combine(v1.SuccessValue, v2.SuccessValue, v3.SuccessValue))
                : Validation<TResult, TError>.Gathered(errors!);
        }

        /// <summary>Combines 3 validations with <paramref name="combine"/>: its value for every value, or every error from every side, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <param name="v3">The third validation.</param>
        /// <param name="combine">Names the values and builds the combined one. Called only when every validation is a success.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<TResult> Combine<T1, T2, T3, TResult>(Validation<T1> v1, Validation<T2> v2, Validation<T3> v3, Func<T1, T2, T3, TResult> combine)
            => new(Combine(v1.Inner, v2.Inner, v3.Inner, combine.ThrowIfDefault(nameof(combine))));

        /// <summary>Combines 4 validations with <paramref name="combine"/>: its value for every value, or every error from every side, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <param name="v3">The third validation.</param>
        /// <param name="v4">The fourth validation.</param>
        /// <param name="combine">Names the values and builds the combined one. Called only when every validation is a success.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<TResult, TError> Combine<T1, T2, T3, T4, TError, TResult>(Validation<T1, TError> v1, Validation<T2, TError> v2, Validation<T3, TError> v3, Validation<T4, TError> v4, Func<T1, T2, T3, T4, TResult> combine)
        {
            combine.ThrowIfDefault(nameof(combine));
            List<TError>? errors = null;
            // & rather than &&: every side is read, so no error is dropped.
            return v1.Gather(ref errors) & v2.Gather(ref errors) & v3.Gather(ref errors) & v4.Gather(ref errors)
                ? new Validation<TResult, TError>(combine(v1.SuccessValue, v2.SuccessValue, v3.SuccessValue, v4.SuccessValue))
                : Validation<TResult, TError>.Gathered(errors!);
        }

        /// <summary>Combines 4 validations with <paramref name="combine"/>: its value for every value, or every error from every side, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <param name="v3">The third validation.</param>
        /// <param name="v4">The fourth validation.</param>
        /// <param name="combine">Names the values and builds the combined one. Called only when every validation is a success.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<TResult> Combine<T1, T2, T3, T4, TResult>(Validation<T1> v1, Validation<T2> v2, Validation<T3> v3, Validation<T4> v4, Func<T1, T2, T3, T4, TResult> combine)
            => new(Combine(v1.Inner, v2.Inner, v3.Inner, v4.Inner, combine.ThrowIfDefault(nameof(combine))));

        /// <summary>Combines 5 validations with <paramref name="combine"/>: its value for every value, or every error from every side, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="T5">The type of the fifth value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <param name="v3">The third validation.</param>
        /// <param name="v4">The fourth validation.</param>
        /// <param name="v5">The fifth validation.</param>
        /// <param name="combine">Names the values and builds the combined one. Called only when every validation is a success.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<TResult, TError> Combine<T1, T2, T3, T4, T5, TError, TResult>(Validation<T1, TError> v1, Validation<T2, TError> v2, Validation<T3, TError> v3, Validation<T4, TError> v4, Validation<T5, TError> v5, Func<T1, T2, T3, T4, T5, TResult> combine)
        {
            combine.ThrowIfDefault(nameof(combine));
            List<TError>? errors = null;
            // & rather than &&: every side is read, so no error is dropped.
            return v1.Gather(ref errors) & v2.Gather(ref errors) & v3.Gather(ref errors) & v4.Gather(ref errors) & v5.Gather(ref errors)
                ? new Validation<TResult, TError>(combine(v1.SuccessValue, v2.SuccessValue, v3.SuccessValue, v4.SuccessValue, v5.SuccessValue))
                : Validation<TResult, TError>.Gathered(errors!);
        }

        /// <summary>Combines 5 validations with <paramref name="combine"/>: its value for every value, or every error from every side, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="T5">The type of the fifth value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <param name="v3">The third validation.</param>
        /// <param name="v4">The fourth validation.</param>
        /// <param name="v5">The fifth validation.</param>
        /// <param name="combine">Names the values and builds the combined one. Called only when every validation is a success.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<TResult> Combine<T1, T2, T3, T4, T5, TResult>(Validation<T1> v1, Validation<T2> v2, Validation<T3> v3, Validation<T4> v4, Validation<T5> v5, Func<T1, T2, T3, T4, T5, TResult> combine)
            => new(Combine(v1.Inner, v2.Inner, v3.Inner, v4.Inner, v5.Inner, combine.ThrowIfDefault(nameof(combine))));

        /// <summary>Combines 6 validations with <paramref name="combine"/>: its value for every value, or every error from every side, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="T5">The type of the fifth value.</typeparam>
        /// <typeparam name="T6">The type of the sixth value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <param name="v3">The third validation.</param>
        /// <param name="v4">The fourth validation.</param>
        /// <param name="v5">The fifth validation.</param>
        /// <param name="v6">The sixth validation.</param>
        /// <param name="combine">Names the values and builds the combined one. Called only when every validation is a success.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<TResult, TError> Combine<T1, T2, T3, T4, T5, T6, TError, TResult>(Validation<T1, TError> v1, Validation<T2, TError> v2, Validation<T3, TError> v3, Validation<T4, TError> v4, Validation<T5, TError> v5, Validation<T6, TError> v6, Func<T1, T2, T3, T4, T5, T6, TResult> combine)
        {
            combine.ThrowIfDefault(nameof(combine));
            List<TError>? errors = null;
            // & rather than &&: every side is read, so no error is dropped.
            return v1.Gather(ref errors) & v2.Gather(ref errors) & v3.Gather(ref errors) & v4.Gather(ref errors) & v5.Gather(ref errors) & v6.Gather(ref errors)
                ? new Validation<TResult, TError>(combine(v1.SuccessValue, v2.SuccessValue, v3.SuccessValue, v4.SuccessValue, v5.SuccessValue, v6.SuccessValue))
                : Validation<TResult, TError>.Gathered(errors!);
        }

        /// <summary>Combines 6 validations with <paramref name="combine"/>: its value for every value, or every error from every side, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="T5">The type of the fifth value.</typeparam>
        /// <typeparam name="T6">The type of the sixth value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <param name="v3">The third validation.</param>
        /// <param name="v4">The fourth validation.</param>
        /// <param name="v5">The fifth validation.</param>
        /// <param name="v6">The sixth validation.</param>
        /// <param name="combine">Names the values and builds the combined one. Called only when every validation is a success.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<TResult> Combine<T1, T2, T3, T4, T5, T6, TResult>(Validation<T1> v1, Validation<T2> v2, Validation<T3> v3, Validation<T4> v4, Validation<T5> v5, Validation<T6> v6, Func<T1, T2, T3, T4, T5, T6, TResult> combine)
            => new(Combine(v1.Inner, v2.Inner, v3.Inner, v4.Inner, v5.Inner, v6.Inner, combine.ThrowIfDefault(nameof(combine))));

        /// <summary>Combines 7 validations with <paramref name="combine"/>: its value for every value, or every error from every side, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="T5">The type of the fifth value.</typeparam>
        /// <typeparam name="T6">The type of the sixth value.</typeparam>
        /// <typeparam name="T7">The type of the seventh value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <param name="v3">The third validation.</param>
        /// <param name="v4">The fourth validation.</param>
        /// <param name="v5">The fifth validation.</param>
        /// <param name="v6">The sixth validation.</param>
        /// <param name="v7">The seventh validation.</param>
        /// <param name="combine">Names the values and builds the combined one. Called only when every validation is a success.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<TResult, TError> Combine<T1, T2, T3, T4, T5, T6, T7, TError, TResult>(Validation<T1, TError> v1, Validation<T2, TError> v2, Validation<T3, TError> v3, Validation<T4, TError> v4, Validation<T5, TError> v5, Validation<T6, TError> v6, Validation<T7, TError> v7, Func<T1, T2, T3, T4, T5, T6, T7, TResult> combine)
        {
            combine.ThrowIfDefault(nameof(combine));
            List<TError>? errors = null;
            // & rather than &&: every side is read, so no error is dropped.
            return v1.Gather(ref errors) & v2.Gather(ref errors) & v3.Gather(ref errors) & v4.Gather(ref errors) & v5.Gather(ref errors) & v6.Gather(ref errors) & v7.Gather(ref errors)
                ? new Validation<TResult, TError>(combine(v1.SuccessValue, v2.SuccessValue, v3.SuccessValue, v4.SuccessValue, v5.SuccessValue, v6.SuccessValue, v7.SuccessValue))
                : Validation<TResult, TError>.Gathered(errors!);
        }

        /// <summary>Combines 7 validations with <paramref name="combine"/>: its value for every value, or every error from every side, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="T5">The type of the fifth value.</typeparam>
        /// <typeparam name="T6">The type of the sixth value.</typeparam>
        /// <typeparam name="T7">The type of the seventh value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <param name="v3">The third validation.</param>
        /// <param name="v4">The fourth validation.</param>
        /// <param name="v5">The fifth validation.</param>
        /// <param name="v6">The sixth validation.</param>
        /// <param name="v7">The seventh validation.</param>
        /// <param name="combine">Names the values and builds the combined one. Called only when every validation is a success.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<TResult> Combine<T1, T2, T3, T4, T5, T6, T7, TResult>(Validation<T1> v1, Validation<T2> v2, Validation<T3> v3, Validation<T4> v4, Validation<T5> v5, Validation<T6> v6, Validation<T7> v7, Func<T1, T2, T3, T4, T5, T6, T7, TResult> combine)
            => new(Combine(v1.Inner, v2.Inner, v3.Inner, v4.Inner, v5.Inner, v6.Inner, v7.Inner, combine.ThrowIfDefault(nameof(combine))));

        /// <summary>Combines 8 validations with <paramref name="combine"/>: its value for every value, or every error from every side, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="T5">The type of the fifth value.</typeparam>
        /// <typeparam name="T6">The type of the sixth value.</typeparam>
        /// <typeparam name="T7">The type of the seventh value.</typeparam>
        /// <typeparam name="T8">The type of the eighth value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <param name="v3">The third validation.</param>
        /// <param name="v4">The fourth validation.</param>
        /// <param name="v5">The fifth validation.</param>
        /// <param name="v6">The sixth validation.</param>
        /// <param name="v7">The seventh validation.</param>
        /// <param name="v8">The eighth validation.</param>
        /// <param name="combine">Names the values and builds the combined one. Called only when every validation is a success.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<TResult, TError> Combine<T1, T2, T3, T4, T5, T6, T7, T8, TError, TResult>(Validation<T1, TError> v1, Validation<T2, TError> v2, Validation<T3, TError> v3, Validation<T4, TError> v4, Validation<T5, TError> v5, Validation<T6, TError> v6, Validation<T7, TError> v7, Validation<T8, TError> v8, Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult> combine)
        {
            combine.ThrowIfDefault(nameof(combine));
            List<TError>? errors = null;
            // & rather than &&: every side is read, so no error is dropped.
            return v1.Gather(ref errors) & v2.Gather(ref errors) & v3.Gather(ref errors) & v4.Gather(ref errors) & v5.Gather(ref errors) & v6.Gather(ref errors) & v7.Gather(ref errors) & v8.Gather(ref errors)
                ? new Validation<TResult, TError>(combine(v1.SuccessValue, v2.SuccessValue, v3.SuccessValue, v4.SuccessValue, v5.SuccessValue, v6.SuccessValue, v7.SuccessValue, v8.SuccessValue))
                : Validation<TResult, TError>.Gathered(errors!);
        }

        /// <summary>Combines 8 validations with <paramref name="combine"/>: its value for every value, or every error from every side, in order.</summary>
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="T5">The type of the fifth value.</typeparam>
        /// <typeparam name="T6">The type of the sixth value.</typeparam>
        /// <typeparam name="T7">The type of the seventh value.</typeparam>
        /// <typeparam name="T8">The type of the eighth value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <param name="v1">The first validation.</param>
        /// <param name="v2">The second validation.</param>
        /// <param name="v3">The third validation.</param>
        /// <param name="v4">The fourth validation.</param>
        /// <param name="v5">The fifth validation.</param>
        /// <param name="v6">The sixth validation.</param>
        /// <param name="v7">The seventh validation.</param>
        /// <param name="v8">The eighth validation.</param>
        /// <param name="combine">Names the values and builds the combined one. Called only when every validation is a success.</param>
        /// <remarks>Every validation is read, so every check's errors are kept; none stops the others.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="combine"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A validation is the zero value (<c>default</c>).</exception>
        public static Validation<TResult> Combine<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(Validation<T1> v1, Validation<T2> v2, Validation<T3> v3, Validation<T4> v4, Validation<T5> v5, Validation<T6> v6, Validation<T7> v7, Validation<T8> v8, Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult> combine)
            => new(Combine(v1.Inner, v2.Inner, v3.Inner, v4.Inner, v5.Inner, v6.Inner, v7.Inner, v8.Inner, combine.ThrowIfDefault(nameof(combine))));
    }
}
