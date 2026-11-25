namespace Resultful
{
    // Decision 16 item 8: a generated shape is a named wrapper over one core value and implements this (or, when it
    // gathers, IValidationShape).
    /// <summary>A result shape that stops at the first failure: a named wrapper over one <see cref="Result{T, TError}"/>.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TError">The error base or error union the shape's failures hold.</typeparam>
    /// <remarks><c>Resultful.Generators</c> implements it on each <see cref="CodeAnalysis.ResultShapeAttribute"/> struct, so code can work over any shape.</remarks>
    public interface IResultShape<T, TError>
    {
        /// <summary>The core result this shape wraps.</summary>
        /// <returns>The same success or failure, as a <see cref="Result{T, TError}"/>.</returns>
        Result<T, TError> AsResult();
    }

    /// <summary>A result shape that gathers every failure (<c>ManyErrors = true</c>): a named wrapper over one <see cref="Validation{T, TError}"/>.</summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TError">The error base or error union the shape's failures hold.</typeparam>
    /// <remarks><c>Resultful.Generators</c> implements it on each gathering <see cref="CodeAnalysis.ResultShapeAttribute"/> struct (decision 16).</remarks>
    public interface IValidationShape<T, TError>
    {
        /// <summary>The core validation this shape wraps.</summary>
        /// <returns>The same success or failures, as a <see cref="Validation{T, TError}"/>.</returns>
        Validation<T, TError> AsValidation();
    }
}
