using FluentValidation;
using FluentValidation.Results;

namespace Resultful.FluentValidation;

/// <summary>Runs a FluentValidation validator and returns a <see cref="Validation{T}"/>.</summary>
/// <remarks>
/// An adapter, not rules (decision 11): every failure FluentValidation reports becomes one
/// <see cref="FluentValidationError"/>, in the order reported, whatever its severity, because FluentValidation's own
/// <see cref="ValidationResult.IsValid"/> is false for any failure. Exceptions and cancellation propagate.
/// </remarks>
public static class ValidatorExtensions
{
    /// <summary>Validates <paramref name="instance"/>: a success holds the instance, a failure one error per failure.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="validator"/> is null.</exception>
    public static Validation<T> ValidateToResult<T>(this IValidator<T> validator, T instance)
    {
        ArgumentNullException.ThrowIfNull(validator);
        return validator.Validate(instance).ToValidation(instance);
    }

    /// <summary>Validates <paramref name="instance"/> with the validator's async rules.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="validator"/> is null.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static async Task<Validation<T>> ValidateToResultAsync<T>(this IValidator<T> validator, T instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validator);
        var result = await validator.ValidateAsync(instance, cancellationToken).ConfigureAwait(false);
        return result.ToValidation(instance);
    }

    /// <summary>Turns a <see cref="ValidationResult"/> into a <see cref="Validation{T}"/>.</summary>
    /// <param name="result">The result of validating <paramref name="instance"/>.</param>
    /// <param name="instance">The value a success holds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is null.</exception>
    public static Validation<T> ToValidation<T>(this ValidationResult result, T instance)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsValid) return Validation.Ok(instance);

        var errors = new Error[result.Errors.Count];
        for (var i = 0; i < errors.Length; i++) errors[i] = new FluentValidationError(result.Errors[i]);
        return Validation.Fail<T>(errors);
    }
}
