using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Resultful
{
    // Decision 22 item 1: early return. `if (r.Failed(out var value, out var error)) return error;` reads the case once,
    // and the nullable annotations tell the compiler that the error is set inside the branch and the value after it.
    // Every Failed reads the case through Case(), so a zero value throws exactly what TryGetValue throws (decision 10).
    public partial struct Result<T, TError>
    {
        /// <summary>
        /// Reads the result for an early return: <c>if (result.Failed(out var value, out var error)) return error;</c>.
        /// After the <c>if</c>, <paramref name="value"/> holds the success value.
        /// </summary>
        /// <param name="value">The value on success; <c>default</c> on failure.</param>
        /// <param name="error">The error on failure; <c>default</c> on success.</param>
        /// <returns><c>true</c> on failure, <c>false</c> on success.</returns>
        /// <remarks>
        /// The nullable annotations say <paramref name="error"/> is not null when this returns <c>true</c>, and
        /// <paramref name="value"/> has <typeparamref name="T"/>'s own nullability when it returns <c>false</c>. A value-type <typeparamref name="T"/> is
        /// <c>default</c> inside the failed branch, which the compiler can't flag: Resultful.Analyzers' RF0011 does.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public bool Failed([MaybeNullWhen(true)] out T value, [NotNullWhen(true)] out TError? error)
        {
            var isSuccess = Case();
            value = isSuccess ? _value : default;
            error = isSuccess ? default : _error;
            return !isSuccess;
        }
    }

    public partial struct Result<T>
    {
        /// <summary>
        /// Reads the result for an early return: <c>if (result.Failed(out var value, out var error)) return error;</c>.
        /// After the <c>if</c>, <paramref name="value"/> holds the success value.
        /// </summary>
        /// <param name="value">The value on success; <c>default</c> on failure.</param>
        /// <param name="error">The error on failure; <c>null</c> on success.</param>
        /// <returns><c>true</c> on failure, <c>false</c> on success.</returns>
        /// <remarks>
        /// The nullable annotations say <paramref name="error"/> is not null when this returns <c>true</c>, and
        /// <paramref name="value"/> has <typeparamref name="T"/>'s own nullability when it returns <c>false</c>. A value-type <typeparamref name="T"/> is
        /// <c>default</c> inside the failed branch, which the compiler can't flag: Resultful.Analyzers' RF0011 does.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public bool Failed([MaybeNullWhen(true)] out T value, [NotNullWhen(true)] out Error? error)
        {
            var isSuccess = Case();
            value = isSuccess ? _value : default;
            error = isSuccess ? null : _error;
            return !isSuccess;
        }
    }

    public readonly partial struct Validation<T, TError>
    {
        /// <summary>
        /// Reads the validation for an early return: <c>if (validation.Failed(out var value, out var errors)) return …;</c>.
        /// After the <c>if</c>, <paramref name="value"/> holds the success value.
        /// </summary>
        /// <param name="value">The value on success; <c>default</c> on failure.</param>
        /// <param name="errors">The errors on failure, in order and never empty; <c>null</c> on success.</param>
        /// <returns><c>true</c> on failure, <c>false</c> on success.</returns>
        /// <remarks>
        /// The nullable annotations say <paramref name="errors"/> is not null when this returns <c>true</c>, and
        /// <paramref name="value"/> has <typeparamref name="T"/>'s own nullability when it returns <c>false</c>. A value-type <typeparamref name="T"/> is
        /// <c>default</c> inside the failed branch, which the compiler can't flag: Resultful.Analyzers' RF0011 does.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public bool Failed([MaybeNullWhen(true)] out T value, [NotNullWhen(true)] out IReadOnlyList<TError>? errors)
        {
            var isSuccess = Case();
            value = isSuccess ? _value : default;
            errors = isSuccess ? null : _errors;
            return !isSuccess;
        }
    }

    public readonly partial struct Validation<T>
    {
        /// <summary>
        /// Reads the validation for an early return: <c>if (validation.Failed(out var value, out var errors)) return …;</c>.
        /// After the <c>if</c>, <paramref name="value"/> holds the success value.
        /// </summary>
        /// <param name="value">The value on success; <c>default</c> on failure.</param>
        /// <param name="errors">The errors on failure, in order and never empty; <c>null</c> on success.</param>
        /// <returns><c>true</c> on failure, <c>false</c> on success.</returns>
        /// <remarks>
        /// The nullable annotations say <paramref name="errors"/> is not null when this returns <c>true</c>, and
        /// <paramref name="value"/> has <typeparamref name="T"/>'s own nullability when it returns <c>false</c>. A value-type <typeparamref name="T"/> is
        /// <c>default</c> inside the failed branch, which the compiler can't flag: Resultful.Analyzers' RF0011 does.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is the zero value (<c>default</c>).</exception>
        public bool Failed([MaybeNullWhen(true)] out T value, [NotNullWhen(true)] out IReadOnlyList<Error>? errors)
            => _inner.Failed(out value, out errors);
    }
}
