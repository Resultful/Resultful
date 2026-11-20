using System.Reflection;
using FluentValidation;
using FluentValidation.Results;

namespace Resultful.FluentValidation.Tests;

public sealed class ErrorTests
{
    [Fact]
    public void The_attempted_value_and_custom_state_are_not_kept()
    {
        var fields = typeof(FluentValidationError).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.All(fields, f => Assert.Equal(typeof(string), f.FieldType));
        Assert.Equal(2, fields.Length);
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
    public void Equality_is_structural_over_message_property_and_code()
    {
        var failure = new ValidationFailure("Name", "required") { ErrorCode = "R", AttemptedValue = "secret", Severity = Severity.Warning };

        Assert.Equal(new FluentValidationError(failure), new FluentValidationError(new ValidationFailure("Name", "required") { ErrorCode = "R" }));
        Assert.NotEqual(new FluentValidationError(failure), new FluentValidationError(new ValidationFailure("Name", "required") { ErrorCode = "S" }));
        Assert.NotEqual(new FluentValidationError(failure), new FluentValidationError(new ValidationFailure("Email", "required") { ErrorCode = "R" }));
        Assert.NotEqual(new FluentValidationError(failure), new FluentValidationError(new ValidationFailure("Name", "needed") { ErrorCode = "R" }));
    }

    [Fact]
    public void WithContext_keeps_the_type_and_the_failure_details()
    {
        var error = new FluentValidationError(new ValidationFailure("Name", "required") { ErrorCode = "R" });

        var noted = error.WithContext("signing up");

        Assert.IsType<FluentValidationError>(noted);
        Assert.Equal(("Name", "R", "required"), (noted.PropertyName, noted.ErrorCode, noted.Message));
        Assert.Equal(["signing up"], noted.Context);
        Assert.NotEqual(error, noted);
    }
}
