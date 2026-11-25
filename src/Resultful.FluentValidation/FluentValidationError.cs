using FluentValidation;
using FluentValidation.Results;

namespace Resultful.FluentValidation;

/// <summary>
/// One FluentValidation failure as a Resultful <see cref="Error"/>. <see cref="Error.Message"/> is the failure's
/// formatted message, in the culture the validator ran under.
/// </summary>
/// <remarks>
/// <para>
/// Only the property path, the error code, the severity and the message are kept. The attempted value and custom
/// state are dropped, because they may hold personal data and an error travels into logs and HTTP responses.
/// </para>
/// <para>
/// <b>The message is kept as FluentValidation formatted it, and several built-in rules put values in it.</b>
/// <c>InclusiveBetween</c>, <c>ExclusiveBetween</c> and <c>IsInEnum</c> embed the attempted value
/// (<c>{PropertyValue}</c>: "You entered 7."); <c>Equal</c>, <c>NotEqual</c>, <c>GreaterThan</c>,
/// <c>GreaterThanOrEqualTo</c>, <c>LessThan</c> and <c>LessThanOrEqualTo</c> embed the value compared against
/// (<c>{ComparisonValue}</c>), which for a rule such as <c>Equal(x =&gt; x.Password)</c> is another property's value.
/// That text reaches <see cref="Error.ToString"/>, logs and HTTP problem bodies. For rules over personal data, set
/// the message with <c>WithMessage</c>, or install a <c>ValidatorOptions.Global.LanguageManager</c> whose messages
/// don't use <c>{PropertyValue}</c> or <c>{ComparisonValue}</c>. The adapter does not rewrite messages.
/// </para>
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
        Severity = failure.Severity;
    }

    /// <summary>The path of the property that failed, as FluentValidation reports it (<c>Address.Line1</c>, <c>Items[0]</c>).</summary>
    public string PropertyName { get; }

    /// <summary>FluentValidation's error code for the rule (<c>NotEmptyValidator</c>, or a code set with <c>WithErrorCode</c>).</summary>
    public string ErrorCode { get; }

    /// <summary>The rule's severity. Every severity is a failure, because FluentValidation's <c>IsValid</c> is false for any failure.</summary>
    public Severity Severity { get; }

    private static string MessageOf(ValidationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure.ErrorMessage ?? string.Empty;
    }
}
