using System.Reflection;
using FluentValidation;
using FluentValidation.Results;

namespace Resultful.FluentValidation.Tests;

public sealed class ErrorTests
{
    const string Sentinel = "SENTINEL-4111-1111-1111-1111";

    [Fact]
    public void Only_the_property_path_code_and_severity_are_added_to_the_error()
    {
        var declared = typeof(FluentValidationError).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(p => p.Name).Order(StringComparer.Ordinal);
        var all = typeof(FluentValidationError).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(p => p.Name).Order(StringComparer.Ordinal);

        Assert.Equal(["ErrorCode", "PropertyName", "Severity"], declared);
        Assert.Equal(["Cause", "Context", "ErrorCode", "Message", "PropertyName", "Severity"], all);
    }

    public sealed record Payment(string CardNumber);

    [Fact]
    public void A_sentinel_attempted_value_and_custom_state_from_a_real_validator_appear_nowhere_on_the_error()
    {
        var validator = new InlineValidator<Payment>();
        validator.RuleFor(p => p.CardNumber).Must(_ => false).WithState(p => new { Card = p.CardNumber }).WithErrorCode("CARD");
        var payment = new Payment(Sentinel);

        var raw = validator.Validate(payment);
        var error = Assert.IsType<FluentValidationError>(Assert.Single(validator.ValidateToResult(payment).Errors));
        var inResult = Validation.Fail<int>(error.WithContext("checking out")).ToResult().Error;

        // FluentValidation itself holds the sentinel; the error doesn't.
        Assert.Equal(Sentinel, Assert.Single(raw.Errors).AttemptedValue);
        Assert.NotNull(raw.Errors[0].CustomState);
        Assert.Equal("The specified condition was not met for 'Card Number'.", error.Message);
        Assert.Equal(("CardNumber", "CARD", Severity.Error), (error.PropertyName, error.ErrorCode, error.Severity));
        Assert.DoesNotContain(Sentinel, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, inResult.ToString(), StringComparison.Ordinal);
        Assert.All(typeof(FluentValidationError).GetProperties(), p => Assert.DoesNotContain(Sentinel, p.GetValue(error)?.ToString() ?? "", StringComparison.Ordinal));
    }

    [Fact]
    public void A_built_in_message_that_embeds_the_value_is_kept_as_FluentValidation_formatted_it()
    {
        // Documented behaviour (FluentValidationError remarks): the adapter doesn't rewrite messages.
        var validator = new InlineValidator<Line>();
        validator.RuleFor(l => l.Quantity).InclusiveBetween(1, 10);
        var leaky = Assert.Single(validator.ValidateToResult(new Line("A", 4111)).Errors);

        var guarded = new InlineValidator<Line>();
        guarded.RuleFor(l => l.Quantity).InclusiveBetween(1, 10).WithMessage("'{PropertyName}' must be between {From} and {To}.");
        var safe = Assert.Single(guarded.ValidateToResult(new Line("A", 4111)).Errors);

        Assert.Equal("'Quantity' must be between 1 and 10. You entered 4111.", leaky.Message);
        Assert.Equal("'Quantity' must be between 1 and 10.", safe.Message);
    }

    [Fact]
    public void The_severity_is_kept()
    {
        Assert.Equal(Severity.Info, new FluentValidationError(new ValidationFailure("Name", "m") { Severity = Severity.Info }).Severity);
        Assert.Equal(Severity.Error, new FluentValidationError(new ValidationFailure("Name", "m")).Severity);
    }

    [Fact]
    public void Missing_strings_become_empty()
    {
        var error = new FluentValidationError(new ValidationFailure { PropertyName = null, ErrorMessage = null, ErrorCode = null });

        Assert.Equal("", error.PropertyName);
        Assert.Equal("", error.ErrorCode);
        Assert.Equal("", error.Message);
    }

    [Fact]
    public void Equality_is_structural_over_message_property_code_and_severity()
    {
        var failure = new ValidationFailure("Name", "required") { ErrorCode = "R", AttemptedValue = "secret", CustomState = 1 };
        var same = new FluentValidationError(new ValidationFailure("Name", "required") { ErrorCode = "R" });

        Assert.Equal(new FluentValidationError(failure), same);
        Assert.Equal(new FluentValidationError(failure).GetHashCode(), same.GetHashCode());
        Assert.NotEqual(new FluentValidationError(failure), new FluentValidationError(new ValidationFailure("Name", "required") { ErrorCode = "S" }));
        Assert.NotEqual(new FluentValidationError(failure), new FluentValidationError(new ValidationFailure("Email", "required") { ErrorCode = "R" }));
        Assert.NotEqual(new FluentValidationError(failure), new FluentValidationError(new ValidationFailure("Name", "needed") { ErrorCode = "R" }));
        Assert.NotEqual(new FluentValidationError(failure), new FluentValidationError(new ValidationFailure("Name", "required") { ErrorCode = "R", Severity = Severity.Warning }));
    }

    [Fact]
    public void WithContext_keeps_the_type_and_the_failure_details()
    {
        var error = new FluentValidationError(new ValidationFailure("Name", "required") { ErrorCode = "R" });

        var noted = error.WithContext("signing up");

        Assert.IsType<FluentValidationError>(noted);
        Assert.Equal(("Name", "R", "required", Severity.Error), (noted.PropertyName, noted.ErrorCode, noted.Message, noted.Severity));
        Assert.Equal(["signing up"], noted.Context);
        Assert.NotEqual(error, noted);
        Assert.Equal("FluentValidationError: required (context: signing up)", noted.ToString());
    }
}
