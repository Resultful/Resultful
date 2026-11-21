using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Resultful.Assertions
{
    // Decision 18: assertions with no assertion library. Each returns what the test reads next, so it can keep going.
    /// <summary>
    /// <c>ShouldSucceed</c> and <c>ShouldFail</c> on every Resultful result, validation and generated shape,
    /// <c>ShouldBeSome</c> and <c>ShouldBeNone</c> on <see cref="Option{T}"/>, and <c>ShouldFind</c> on the error a
    /// failure returns. A failed assertion throws <see cref="ResultAssertionException"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A failure message names the expected and the actual case, and prints errors with <see cref="Error.ToString"/>,
    /// never a derived record's own properties. A success value is never printed, only its runtime type; an error that
    /// isn't an <see cref="Error"/> (such as a <see cref="string"/>) is shown by its type name only.
    /// </para>
    /// <para>
    /// <c>because</c> is added after the expectation, as in "Expected Result&lt;Order&gt; to succeed because the order
    /// exists, but …". A leading "because" in it isn't repeated.
    /// </para>
    /// <para>
    /// <c>result.ShouldFail().ShouldFind&lt;TLeaf&gt;()</c> finds a leaf as <see cref="Error.TryFind{TError}"/> does:
    /// the error itself, its cause chain and its inner errors. It is declared on <see cref="Error"/>,
    /// <see cref="IErrorUnion"/> and <see cref="IEnumerable{T}"/> of <see cref="Error"/>, which take no type arguments,
    /// so C# lets the leaf's be written alone.
    /// </para>
    /// <para>The zero value (<c>default</c>) of a result is neither a success nor a failure, so every assertion on it fails.</para>
    /// </remarks>
    public static class ResultAssertions
    {
        private const string Zero = "it is the zero value (default), which is neither a success nor a failure.";

        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <param name="result">The result.</param>
        extension<T>(Result<T> result)
        {
            /// <summary>Asserts that the result is a success.</summary>
            /// <returns>The success value.</returns>
            /// <exception cref="ResultAssertionException">The result is a failure, or the zero value.</exception>
            public T ShouldSucceed() => result.ShouldSucceed(because: null);

            /// <summary>Asserts that the result is a success.</summary>
            /// <param name="because">Why it should succeed, for the failure message.</param>
            /// <returns>The success value.</returns>
            /// <exception cref="ResultAssertionException">The result is a failure, or the zero value.</exception>
            public T ShouldSucceed(string? because)
                => result.IsSuccess ? result.Value : throw NotSuccess<Error>(typeof(Result<T>), because, result.IsFailure ? [result.Error] : null);

            /// <summary>Asserts that the result is a failure.</summary>
            /// <returns>The error; <c>ShouldFind&lt;TLeaf&gt;()</c> on it finds a leaf.</returns>
            /// <exception cref="ResultAssertionException">The result is a success, or the zero value.</exception>
            public Error ShouldFail() => result.ShouldFail(because: null);

            /// <summary>Asserts that the result is a failure.</summary>
            /// <param name="because">Why it should fail, for the failure message.</param>
            /// <returns>The error; <c>ShouldFind&lt;TLeaf&gt;()</c> on it finds a leaf.</returns>
            /// <exception cref="ResultAssertionException">The result is a success, or the zero value.</exception>
            public Error ShouldFail(string? because)
                => result.IsFailure ? result.Error : throw NotFailure(typeof(Result<T>), because, result.IsSuccess, result.IsSuccess ? (object?)result.Value : null);
        }

        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TError">The type of the error: an <see cref="Error"/>, an <see cref="IErrorUnion"/>, or anything else.</typeparam>
        /// <param name="result">The result.</param>
        extension<T, TError>(Result<T, TError> result)
        {
            /// <summary>Asserts that the result is a success.</summary>
            /// <returns>The success value.</returns>
            /// <exception cref="ResultAssertionException">The result is a failure, or the zero value.</exception>
            public T ShouldSucceed() => result.ShouldSucceed(because: null);

            /// <summary>Asserts that the result is a success.</summary>
            /// <param name="because">Why it should succeed, for the failure message.</param>
            /// <returns>The success value.</returns>
            /// <exception cref="ResultAssertionException">The result is a failure, or the zero value.</exception>
            public T ShouldSucceed(string? because) => SucceedCore(result, typeof(Result<T, TError>), because);

            /// <summary>Asserts that the result is a failure.</summary>
            /// <returns>The error; <c>ShouldFind&lt;TLeaf&gt;()</c> on it finds a leaf of a hierarchy or a union's case.</returns>
            /// <exception cref="ResultAssertionException">The result is a success, or the zero value.</exception>
            public TError ShouldFail() => result.ShouldFail(because: null);

            /// <summary>Asserts that the result is a failure.</summary>
            /// <param name="because">Why it should fail, for the failure message.</param>
            /// <returns>The error; <c>ShouldFind&lt;TLeaf&gt;()</c> on it finds a leaf of a hierarchy or a union's case.</returns>
            /// <exception cref="ResultAssertionException">The result is a success, or the zero value.</exception>
            public TError ShouldFail(string? because) => FailCore(result, typeof(Result<T, TError>), because);
        }

        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <param name="validation">The validation.</param>
        extension<T>(Validation<T> validation)
        {
            /// <summary>Asserts that the validation is a success.</summary>
            /// <returns>The success value.</returns>
            /// <exception cref="ResultAssertionException">The validation is a failure, or the zero value.</exception>
            public T ShouldSucceed() => validation.ShouldSucceed(because: null);

            /// <summary>Asserts that the validation is a success.</summary>
            /// <param name="because">Why it should succeed, for the failure message.</param>
            /// <returns>The success value.</returns>
            /// <exception cref="ResultAssertionException">The validation is a failure, or the zero value.</exception>
            public T ShouldSucceed(string? because)
                => validation.IsSuccess ? validation.Value : throw NotSuccess(typeof(Validation<T>), because, validation.IsFailure ? validation.Errors : null, many: true);

            /// <summary>Asserts that the validation is a failure.</summary>
            /// <returns>The errors, in order, never empty; <c>ShouldFind&lt;TLeaf&gt;()</c> on them finds a leaf.</returns>
            /// <exception cref="ResultAssertionException">The validation is a success, or the zero value.</exception>
            public IReadOnlyList<Error> ShouldFail() => validation.ShouldFail(because: null);

            /// <summary>Asserts that the validation is a failure.</summary>
            /// <param name="because">Why it should fail, for the failure message.</param>
            /// <returns>The errors, in order, never empty; <c>ShouldFind&lt;TLeaf&gt;()</c> on them finds a leaf.</returns>
            /// <exception cref="ResultAssertionException">The validation is a success, or the zero value.</exception>
            public IReadOnlyList<Error> ShouldFail(string? because)
                => validation.IsFailure ? validation.Errors : throw NotFailure(typeof(Validation<T>), because, validation.IsSuccess, validation.IsSuccess ? (object?)validation.Value : null);
        }

        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TError">The type of the errors.</typeparam>
        /// <param name="validation">The validation.</param>
        extension<T, TError>(Validation<T, TError> validation)
        {
            /// <summary>Asserts that the validation is a success.</summary>
            /// <returns>The success value.</returns>
            /// <exception cref="ResultAssertionException">The validation is a failure, or the zero value.</exception>
            public T ShouldSucceed() => validation.ShouldSucceed(because: null);

            /// <summary>Asserts that the validation is a success.</summary>
            /// <param name="because">Why it should succeed, for the failure message.</param>
            /// <returns>The success value.</returns>
            /// <exception cref="ResultAssertionException">The validation is a failure, or the zero value.</exception>
            public T ShouldSucceed(string? because) => SucceedCore(validation, typeof(Validation<T, TError>), because);

            /// <summary>Asserts that the validation is a failure.</summary>
            /// <returns>The errors, in order, never empty. For errors that derive from <see cref="Error"/>, <c>ShouldFind&lt;TLeaf&gt;()</c> on them finds a leaf.</returns>
            /// <exception cref="ResultAssertionException">The validation is a success, or the zero value.</exception>
            public IReadOnlyList<TError> ShouldFail() => validation.ShouldFail(because: null);

            /// <summary>Asserts that the validation is a failure.</summary>
            /// <param name="because">Why it should fail, for the failure message.</param>
            /// <returns>The errors, in order, never empty. For errors that derive from <see cref="Error"/>, <c>ShouldFind&lt;TLeaf&gt;()</c> on them finds a leaf.</returns>
            /// <exception cref="ResultAssertionException">The validation is a success, or the zero value.</exception>
            public IReadOnlyList<TError> ShouldFail(string? because) => FailCore(validation, typeof(Validation<T, TError>), because);
        }

        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TError">The shape's error type: a closed hierarchy's base or an error union.</typeparam>
        /// <param name="shape">A <see cref="CodeAnalysis.ResultShapeAttribute"/> struct.</param>
        extension<T, TError>(IResultShape<T, TError> shape)
        {
            /// <summary>Asserts that the generated shape is a success.</summary>
            /// <returns>The success value.</returns>
            /// <exception cref="ArgumentNullException">The shape is <c>null</c>.</exception>
            /// <exception cref="ResultAssertionException">The shape is a failure, or the zero value.</exception>
            public T ShouldSucceed() => shape.ShouldSucceed(because: null);

            /// <summary>Asserts that the generated shape is a success.</summary>
            /// <param name="because">Why it should succeed, for the failure message.</param>
            /// <returns>The success value.</returns>
            /// <exception cref="ArgumentNullException">The shape is <c>null</c>.</exception>
            /// <exception cref="ResultAssertionException">The shape is a failure, or the zero value.</exception>
            public T ShouldSucceed(string? because) => SucceedCore(Unwrap(shape), shape.GetType(), because);

            /// <summary>Asserts that the generated shape is a failure.</summary>
            /// <returns>The error; <c>ShouldFind&lt;TLeaf&gt;()</c> on it finds a leaf of the hierarchy or a case of the union.</returns>
            /// <exception cref="ArgumentNullException">The shape is <c>null</c>.</exception>
            /// <exception cref="ResultAssertionException">The shape is a success, or the zero value.</exception>
            public TError ShouldFail() => shape.ShouldFail(because: null);

            /// <summary>Asserts that the generated shape is a failure.</summary>
            /// <param name="because">Why it should fail, for the failure message.</param>
            /// <returns>The error; <c>ShouldFind&lt;TLeaf&gt;()</c> on it finds a leaf of the hierarchy or a case of the union.</returns>
            /// <exception cref="ArgumentNullException">The shape is <c>null</c>.</exception>
            /// <exception cref="ResultAssertionException">The shape is a success, or the zero value.</exception>
            public TError ShouldFail(string? because) => FailCore(Unwrap(shape), shape.GetType(), because);
        }

        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TError">The shape's error type: a closed hierarchy's base or an error union.</typeparam>
        /// <param name="shape">A <see cref="CodeAnalysis.ResultShapeAttribute"/> struct with <c>ManyErrors = true</c>.</param>
        extension<T, TError>(IValidationShape<T, TError> shape)
        {
            /// <summary>Asserts that the gathering generated shape is a success.</summary>
            /// <returns>The success value.</returns>
            /// <exception cref="ArgumentNullException">The shape is <c>null</c>.</exception>
            /// <exception cref="ResultAssertionException">The shape is a failure, or the zero value.</exception>
            public T ShouldSucceed() => shape.ShouldSucceed(because: null);

            /// <summary>Asserts that the gathering generated shape is a success.</summary>
            /// <param name="because">Why it should succeed, for the failure message.</param>
            /// <returns>The success value.</returns>
            /// <exception cref="ArgumentNullException">The shape is <c>null</c>.</exception>
            /// <exception cref="ResultAssertionException">The shape is a failure, or the zero value.</exception>
            public T ShouldSucceed(string? because) => SucceedCore(Unwrap(shape), shape.GetType(), because);

            /// <summary>Asserts that the gathering generated shape is a failure.</summary>
            /// <returns>The errors, in order, never empty.</returns>
            /// <exception cref="ArgumentNullException">The shape is <c>null</c>.</exception>
            /// <exception cref="ResultAssertionException">The shape is a success, or the zero value.</exception>
            public IReadOnlyList<TError> ShouldFail() => shape.ShouldFail(because: null);

            /// <summary>Asserts that the gathering generated shape is a failure.</summary>
            /// <param name="because">Why it should fail, for the failure message.</param>
            /// <returns>The errors, in order, never empty.</returns>
            /// <exception cref="ArgumentNullException">The shape is <c>null</c>.</exception>
            /// <exception cref="ResultAssertionException">The shape is a success, or the zero value.</exception>
            public IReadOnlyList<TError> ShouldFail(string? because) => FailCore(Unwrap(shape), shape.GetType(), because);
        }

        /// <param name="error">The error, such as the one <c>ShouldFail()</c> returns.</param>
        extension(Error error)
        {
            /// <summary>Asserts that the error is, or contains, a <typeparamref name="TLeaf"/>, as <see cref="Error.TryFind{TError}"/> searches: the error itself, its cause chain and its inner errors.</summary>
            /// <typeparam name="TLeaf">The type of error expected.</typeparam>
            /// <returns>The first <typeparamref name="TLeaf"/>, outer to inner.</returns>
            /// <exception cref="ArgumentNullException">The error is <c>null</c>.</exception>
            /// <exception cref="ResultAssertionException">There is no <typeparamref name="TLeaf"/> in the error.</exception>
            public TLeaf ShouldFind<TLeaf>()
                where TLeaf : Error
                => error.ShouldFind<TLeaf>(because: null);

            /// <summary>Asserts that the error is, or contains, a <typeparamref name="TLeaf"/>, as <see cref="Error.TryFind{TError}"/> searches: the error itself, its cause chain and its inner errors.</summary>
            /// <typeparam name="TLeaf">The type of error expected.</typeparam>
            /// <param name="because">Why it should be there, for the failure message.</param>
            /// <returns>The first <typeparamref name="TLeaf"/>, outer to inner.</returns>
            /// <exception cref="ArgumentNullException">The error is <c>null</c>.</exception>
            /// <exception cref="ResultAssertionException">There is no <typeparamref name="TLeaf"/> in the error.</exception>
            public TLeaf ShouldFind<TLeaf>(string? because)
                where TLeaf : Error
                => Required(error, nameof(error)).TryFind(out TLeaf? leaf)
                    ? leaf
                    : throw NotFound(typeof(TLeaf), because, [error]);
        }

        /// <param name="union">The error union, such as the one <c>ShouldFail()</c> returns for a union shape.</param>
        extension(IErrorUnion union)
        {
            /// <summary>Asserts that the error the union holds is, or contains, a <typeparamref name="TLeaf"/>, as <see cref="Error.TryFind{TError}"/> searches.</summary>
            /// <typeparam name="TLeaf">The type of error expected: one of the union's cases, or an error under one.</typeparam>
            /// <returns>The first <typeparamref name="TLeaf"/>, outer to inner.</returns>
            /// <exception cref="ArgumentNullException">The union is <c>null</c>.</exception>
            /// <exception cref="ResultAssertionException">There is no <typeparamref name="TLeaf"/> in the held error.</exception>
            public TLeaf ShouldFind<TLeaf>()
                where TLeaf : Error
                => union.ShouldFind<TLeaf>(because: null);

            /// <summary>Asserts that the error the union holds is, or contains, a <typeparamref name="TLeaf"/>, as <see cref="Error.TryFind{TError}"/> searches.</summary>
            /// <typeparam name="TLeaf">The type of error expected: one of the union's cases, or an error under one.</typeparam>
            /// <param name="because">Why it should be there, for the failure message.</param>
            /// <returns>The first <typeparamref name="TLeaf"/>, outer to inner.</returns>
            /// <exception cref="ArgumentNullException">The union is <c>null</c>.</exception>
            /// <exception cref="ResultAssertionException">There is no <typeparamref name="TLeaf"/> in the held error.</exception>
            public TLeaf ShouldFind<TLeaf>(string? because)
                where TLeaf : Error
                => Required(union, nameof(union)).Error.ShouldFind<TLeaf>(because);
        }

        /// <param name="errors">The errors, such as the ones <c>ShouldFail()</c> returns for a validation.</param>
        extension(IEnumerable<Error> errors)
        {
            /// <summary>Asserts that one of the errors is, or contains, a <typeparamref name="TLeaf"/>, searching each in order as <see cref="Error.TryFind{TError}"/> does.</summary>
            /// <typeparam name="TLeaf">The type of error expected.</typeparam>
            /// <returns>The first <typeparamref name="TLeaf"/>.</returns>
            /// <exception cref="ArgumentNullException">The list is <c>null</c>.</exception>
            /// <exception cref="ResultAssertionException">No error is or contains a <typeparamref name="TLeaf"/>.</exception>
            public TLeaf ShouldFind<TLeaf>()
                where TLeaf : Error
                => errors.ShouldFind<TLeaf>(because: null);

            /// <summary>Asserts that one of the errors is, or contains, a <typeparamref name="TLeaf"/>, searching each in order as <see cref="Error.TryFind{TError}"/> does.</summary>
            /// <typeparam name="TLeaf">The type of error expected.</typeparam>
            /// <param name="because">Why it should be there, for the failure message.</param>
            /// <returns>The first <typeparamref name="TLeaf"/>.</returns>
            /// <exception cref="ArgumentNullException">The list is <c>null</c>.</exception>
            /// <exception cref="ResultAssertionException">No error is or contains a <typeparamref name="TLeaf"/>.</exception>
            public TLeaf ShouldFind<TLeaf>(string? because)
                where TLeaf : Error
            {
                var list = Required(errors, nameof(errors)).ToList();
                return ResultInspection.TryFindInErrors(list, out TLeaf? leaf) ? leaf : throw NotFound(typeof(TLeaf), because, list);
            }
        }

        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="option">The option.</param>
        extension<T>(Option<T> option)
        {
            /// <summary>Asserts that the option has a value.</summary>
            /// <returns>The value.</returns>
            /// <exception cref="ResultAssertionException">The option is None.</exception>
            public T ShouldBeSome() => option.ShouldBeSome(because: null);

            /// <summary>Asserts that the option has a value.</summary>
            /// <param name="because">Why it should have a value, for the failure message.</param>
            /// <returns>The value.</returns>
            /// <exception cref="ResultAssertionException">The option is None.</exception>
            public T ShouldBeSome(string? because)
            {
                var (isSome, value) = option.Match(v => (true, v), _ => (false, default(T)!));
                return isSome
                    ? value
                    : throw new ResultAssertionException($"Expected {ResultInspection.TypeName(typeof(Option<T>))} to be Some{Reason(because)}, but it was None.");
            }

            /// <summary>Asserts that the option has no value.</summary>
            /// <exception cref="ResultAssertionException">The option has a value. The value isn't printed.</exception>
            public void ShouldBeNone() => option.ShouldBeNone(because: null);

            /// <summary>Asserts that the option has no value.</summary>
            /// <param name="because">Why it should have no value, for the failure message.</param>
            /// <exception cref="ResultAssertionException">The option has a value. The value isn't printed.</exception>
            public void ShouldBeNone(string? because)
            {
                if (option.Match(_ => true, _ => false))
                {
                    throw new ResultAssertionException($"Expected {ResultInspection.TypeName(typeof(Option<T>))} to be None{Reason(because)}, but it was Some.");
                }
            }
        }

        /// <summary>
        /// " because …" for a failure message: empty for <c>null</c> or white space, and a leading "because" in
        /// <paramref name="because"/> isn't repeated.
        /// </summary>
        internal static string Reason(string? because)
        {
            if (string.IsNullOrWhiteSpace(because))
            {
                return string.Empty;
            }

            var text = because!.Trim();
            return text.StartsWith("because ", StringComparison.OrdinalIgnoreCase) ? " " + text : " because " + text;
        }

        private static T SucceedCore<T, TError>(Result<T, TError> result, Type subject, string? because)
            => result.IsSuccess ? result.Value : throw NotSuccess<TError>(subject, because, result.IsFailure ? [result.Error] : null);

        private static T SucceedCore<T, TError>(Validation<T, TError> validation, Type subject, string? because)
            => validation.IsSuccess ? validation.Value : throw NotSuccess(subject, because, validation.IsFailure ? validation.Errors : null, many: true);

        private static TError FailCore<T, TError>(Result<T, TError> result, Type subject, string? because)
            => result.IsFailure ? result.Error : throw NotFailure(subject, because, result.IsSuccess, result.IsSuccess ? (object?)result.Value : null);

        private static IReadOnlyList<TError> FailCore<T, TError>(Validation<T, TError> validation, Type subject, string? because)
            => validation.IsFailure ? validation.Errors : throw NotFailure(subject, because, validation.IsSuccess, validation.IsSuccess ? (object?)validation.Value : null);

        // A receiver is a parameter of the lowered method, which CA2208 can't see from inside an extension block.
        private static TValue Required<TValue>(TValue? value, string name)
            where TValue : class
            => value ?? throw new ArgumentNullException(name);

        private static Result<T, TError> Unwrap<T, TError>(IResultShape<T, TError> shape)
            => (shape ?? throw new ArgumentNullException(nameof(shape))).AsResult();

        private static Validation<T, TError> Unwrap<T, TError>(IValidationShape<T, TError> shape)
            => (shape ?? throw new ArgumentNullException(nameof(shape))).AsValidation();

        // errors null means the zero value: a success never gets here.
        private static ResultAssertionException NotSuccess<TError>(Type subject, string? because, IReadOnlyList<TError>? errors, bool many = false)
        {
            var text = new StringBuilder("Expected ").Append(ResultInspection.TypeName(subject)).Append(" to succeed").Append(Reason(because)).Append(", but ");
            if (errors is null)
            {
                return new ResultAssertionException(text.Append(Zero).ToString());
            }

            text.Append("it failed with");
            if (many)
            {
                text.Append(' ').Append(errors.Count).Append(errors.Count == 1 ? " error" : " errors");
            }

            AppendErrors(text.Append(':'), errors);
            return new ResultAssertionException(text.ToString());
        }

        // A success prints its value's runtime type, never the value; isSuccess false means the zero value.
        private static ResultAssertionException NotFailure(Type subject, string? because, bool isSuccess, object? value)
        {
            var text = new StringBuilder("Expected ").Append(ResultInspection.TypeName(subject)).Append(" to fail").Append(Reason(because)).Append(", but ");
            text.Append(isSuccess ? ResultInspection.DescribeSuccess(value) : Zero);
            return new ResultAssertionException(text.ToString());
        }

        private static ResultAssertionException NotFound(Type leaf, string? because, IReadOnlyList<Error> errors)
        {
            var text = new StringBuilder(errors.Count == 1 ? "Expected the error to be or contain " : "Expected one of the errors to be or contain ")
                .Append(ResultInspection.TypeName(leaf))
                .Append(Reason(because))
                .Append(errors.Count switch { 0 => ", but there are none.", 1 => ", but it is:", _ => ", but they are:" });
            AppendErrors(text, errors);
            return new ResultAssertionException(text.ToString());
        }

        private static void AppendErrors<TError>(StringBuilder text, IReadOnlyList<TError> errors)
        {
            foreach (var error in errors)
            {
                text.Append("\n  ").Append(ResultInspection.DescribeError(error));
            }
        }
    }
}
