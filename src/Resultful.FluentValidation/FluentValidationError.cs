using FluentValidation.Results;

namespace Resultful.FluentValidation;

/// <summary>
/// One FluentValidation failure as a Resultful <see cref="Error"/>. <see cref="Error.Message"/> is the failure's
/// formatted message, in the culture the validator ran under.
/// </summary>
/// <remarks>
/// Only the property name, the error code and the message are kept. The attempted value and custom state are
/// dropped, because they may hold personal data and an error travels into logs and HTTP responses.
/// </remarks>
public sealed record FluentValidationError : Error
{
    /// <summary>Creates the error from one FluentValidation failure.</summary>
    /// <param name="failure">The failure. A missing message, property name or code becomes an empty string.</param>
    /// <exception cref="ArgumentNullException"><paramref name="failure"/> is null.</exception>
    public FluentValidationError(ValidationFailure failure)
        : base(MessageOf(failure))
    {
        PropertyName = failure.PropertyName ?? string.Empty;
        ErrorCode = failure.ErrorCode ?? string.Empty;
    }

    /// <summary>The path of the property that failed, as FluentValidation reports it (<c>Address.Line1</c>, <c>Items[0]</c>).</summary>
    public string PropertyName { get; }

    /// <summary>FluentValidation's error code for the rule (<c>NotEmptyValidator</c>, or a code set with <c>WithErrorCode</c>).</summary>
    public string ErrorCode { get; }

    private static string MessageOf(ValidationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure.ErrorMessage ?? string.Empty;
    }
}
