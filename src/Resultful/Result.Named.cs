using System;
using System.Diagnostics.CodeAnalysis;
using Resultful.Utils;

namespace Resultful
{
    // M2 named methods: ask a result which case it is, read it, or fall back, without a callback.
    public partial struct Result<T, TError>
    {
        /// <summary><c>true</c> if this is a success. Then <see cref="Value"/> is set and <see cref="Error"/> throws.</summary>
        /// <remarks>The zero value (<c>default</c>) is neither a success nor a failure: <see cref="IsSuccess"/> and <see cref="IsFailure"/> are both <c>false</c>. Never throws.</remarks>
        [MemberNotNullWhen(true, nameof(Value))]
        [MemberNotNullWhen(false, nameof(Error))]
        public bool IsSuccess => _state == State.Success;

        /// <summary><c>true</c> if this is a failure. Then <see cref="Error"/> is set and <see cref="Value"/> throws.</summary>
        /// <remarks>The zero value (<c>default</c>) is neither a success nor a failure: <see cref="IsSuccess"/> and <see cref="IsFailure"/> are both <c>false</c>. Never throws.</remarks>
        [MemberNotNullWhen(false, nameof(Value))]
        [MemberNotNullWhen(true, nameof(Error))]
        public bool IsFailure => _state == State.Failure;

        /// <summary>The value of a success.</summary>
        /// <exception cref="InvalidOperationException">This is a failure, or the zero value (<c>default</c>).</exception>
        public T Value => Case() ? _value : throw NamedErrors.NoValue();

        /// <summary>The error of a failure.</summary>
        /// <exception cref="InvalidOperationException">This is a success, or the zero value (<c>default</c>).</exception>
        public TError Error => Case() ? throw NamedErrors.NoError() : _error;

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

        /// <summary>Gets the error of a failure, like <c>Dictionary.TryGetValue</c>.</summary>
        /// <param name="error">The error on failure; <c>default</c> on success.</param>
        /// <returns><c>true</c> on failure, <c>false</c> on success.</returns>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public bool TryGetError([MaybeNullWhen(false)] out TError error)
        {
            var isSuccess = Case();
            error = isSuccess ? default : _error;
            return !isSuccess;
        }

        /// <summary>Deconstructs the result: <c>var (isSuccess, value, error) = result;</c>.</summary>
        /// <param name="isSuccess"><c>true</c> on success.</param>
        /// <param name="value">The value on success; <c>default</c> on failure.</param>
        /// <param name="error">The error on failure; <c>default</c> on success.</param>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public void Deconstruct(out bool isSuccess, out T? value, out TError? error)
        {
            isSuccess = Case();
            value = isSuccess ? _value : default;
            error = isSuccess ? default : _error;
        }

        /// <summary>Returns this result if it is a success, otherwise <paramref name="other"/>. The first success wins.</summary>
        /// <param name="other">The result to use when this is a failure.</param>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public Result<T, TError> OrElse(Result<T, TError> other)
            => Case() ? this : other;

        /// <summary>Returns this result if it is a success, otherwise the result <paramref name="other"/> returns for the error.</summary>
        /// <param name="other">Called with the error, only when this is a failure.</param>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public Result<T, TError> OrElse(Func<TError, Result<T, TError>> other)
        {
            other.ThrowIfDefault(nameof(other));
            return Case() ? this : other(_error);
        }

        /// <summary>Returns the value of a success, otherwise <paramref name="fallback"/>.</summary>
        /// <param name="fallback">The value to return when this is a failure.</param>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public T ValueOr(T fallback)
            => Case() ? _value : fallback;

        // true for a success, false for a failure; the zero value throws (decision 10). Every member that reads the case goes through it.
        private bool Case() => _state == State.Zero ? throw NamedErrors.Zero(typeof(Result<T, TError>)) : _state == State.Success;

        // Throws on the zero value; for members that would otherwise return it unread (Then and Check with no steps).
        internal void ThrowIfZero() => _ = Case();
    }

    public partial struct Result<T>
    {
        /// <summary><c>true</c> if this is a success. Then <see cref="Value"/> is set.</summary>
        /// <remarks>The zero value (<c>default</c>) is neither a success nor a failure: <see cref="IsSuccess"/> and <see cref="IsFailure"/> are both <c>false</c>. Never throws.</remarks>
        [MemberNotNullWhen(true, nameof(Value))]
        public bool IsSuccess => _state == State.Success;

        /// <summary><c>true</c> if this is a failure. Then <see cref="Value"/> throws.</summary>
        /// <remarks>The zero value (<c>default</c>) is neither a success nor a failure: <see cref="IsSuccess"/> and <see cref="IsFailure"/> are both <c>false</c>. Never throws.</remarks>
        [MemberNotNullWhen(false, nameof(Value))]
        public bool IsFailure => _state == State.Failure;

        /// <summary>The value of a success.</summary>
        /// <exception cref="InvalidOperationException">This is a failure, or the zero value (<c>default</c>).</exception>
        public T Value => Case() ? _value : throw NamedErrors.NoValue();

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

        /// <summary>Returns this result if it is a success, otherwise <paramref name="other"/>. The first success wins.</summary>
        /// <param name="other">The result to use when this is a failure.</param>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public Result<T> OrElse(Result<T> other)
            => Case() ? this : other;

        /// <summary>Returns the value of a success, otherwise <paramref name="fallback"/>.</summary>
        /// <param name="fallback">The value to return when this is a failure.</param>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public T ValueOr(T fallback)
            => Case() ? _value : fallback;

        // true for a success, false for a failure; the zero value throws (decision 10). Every member that reads the case goes through it.
        private bool Case() => _state == State.Zero ? throw NamedErrors.Zero(typeof(Result<T>)) : _state == State.Success;

        // Throws on the zero value; for members that would otherwise return it unread (Then and Check with no steps).
        internal void ThrowIfZero() => _ = Case();
    }

    internal static class NamedErrors
    {
        internal const string NoConstructor
            = "The parameterless constructor gives the zero value, which is neither a success nor a failure. Create results with Result.Ok or Result.Fail.";

        internal static InvalidOperationException NoValue()
            => new("The result is a failure, so it has no value. Check IsSuccess or call TryGetValue first.");

        internal static InvalidOperationException NoError()
            => new("The result is a success, so it has no error. Check IsFailure or call TryGetError first.");

        internal static InvalidOperationException Zero(Type type)
            => new($"This {type.Name.Split('`')[0]} is the zero value (default): it is neither a success nor a failure. Create results with Result.Ok or Result.Fail.");
    }
}
