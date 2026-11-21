using Resultful;
#if RESULTFUL_FLUENTASSERTIONS
using Resultful.Assertions.FluentAssertions;
#else
using Resultful.Assertions.AwesomeAssertions;
#endif

// In the library's own namespace, as its own Should() overloads are, so `using AwesomeAssertions;` (or
// `using FluentAssertions;`) is all a test needs. Shared with the FluentAssertions adapter, like ResultAssertions.cs.
#if RESULTFUL_FLUENTASSERTIONS
namespace FluentAssertions
#else
namespace AwesomeAssertions
#endif
{
    /// <summary><c>Should()</c> for Resultful's results, validations, options and generated shapes.</summary>
    public static class ResultfulAssertionExtensions
    {
        /// <summary>Starts assertions on a result.</summary>
        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <param name="result">The result.</param>
        /// <returns>Assertions on it.</returns>
        public static ResultAssertions<T, Error> Should<T>(this Result<T> result)
            => new(result.IsSuccess, result.IsFailure, result.IsSuccess ? result.Value : default!, result.IsFailure ? result.Error : default!);

        /// <summary>Starts assertions on a result.</summary>
        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="result">The result.</param>
        /// <returns>Assertions on it.</returns>
        public static ResultAssertions<T, TError> Should<T, TError>(this Result<T, TError> result)
            => new(result.IsSuccess, result.IsFailure, result.IsSuccess ? result.Value : default!, result.IsFailure ? result.Error : default!);

        /// <summary>Starts assertions on a generated result shape, through the result it wraps.</summary>
        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TError">The shape's error type: a closed hierarchy's base or an error union.</typeparam>
        /// <param name="shape">A <see cref="Resultful.CodeAnalysis.ResultShapeAttribute"/> struct.</param>
        /// <returns>Assertions on it.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="shape"/> is <c>null</c>.</exception>
        public static ResultAssertions<T, TError> Should<T, TError>(this IResultShape<T, TError> shape)
            => (shape ?? throw new System.ArgumentNullException(nameof(shape))).AsResult().Should();

        /// <summary>Starts assertions on a validation.</summary>
        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <param name="validation">The validation.</param>
        /// <returns>Assertions on it.</returns>
        public static ValidationAssertions<T, Error> Should<T>(this Validation<T> validation)
            => new(validation.IsSuccess, validation.IsSuccess ? validation.Value : default!, validation.IsFailure ? validation.Errors : null);

        /// <summary>Starts assertions on a validation.</summary>
        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TError">The type of the errors.</typeparam>
        /// <param name="validation">The validation.</param>
        /// <returns>Assertions on it.</returns>
        public static ValidationAssertions<T, TError> Should<T, TError>(this Validation<T, TError> validation)
            => new(validation.IsSuccess, validation.IsSuccess ? validation.Value : default!, validation.IsFailure ? validation.Errors : null);

        /// <summary>Starts assertions on a gathering generated shape, through the validation it wraps.</summary>
        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TError">The shape's error type: a closed hierarchy's base or an error union.</typeparam>
        /// <param name="shape">A <see cref="Resultful.CodeAnalysis.ResultShapeAttribute"/> struct with <c>ManyErrors = true</c>.</param>
        /// <returns>Assertions on it.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="shape"/> is <c>null</c>.</exception>
        public static ValidationAssertions<T, TError> Should<T, TError>(this IValidationShape<T, TError> shape)
            => (shape ?? throw new System.ArgumentNullException(nameof(shape))).AsValidation().Should();

        /// <summary>Starts assertions on an option.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="option">The option.</param>
        /// <returns>Assertions on it.</returns>
        public static OptionAssertions<T> Should<T>(this Option<T> option) => new(option);
    }
}
