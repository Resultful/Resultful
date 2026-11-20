using FluentValidation;
using FluentValidation.Results;

namespace Resultful.FluentValidation.Tests;

public sealed class ValidatorExtensionsTests
{
    [Fact]
    public void Every_failure_becomes_one_error_in_order()
    {
        var bad = new SignUpValidator().ValidateToResult(new SignUp("", "nope"));

        Assert.True(bad.IsFailure);
        Assert.Equal(["Name", "Email"], bad.Errors.Cast<FluentValidationError>().Select(e => e.PropertyName));
        Assert.Equal(["NotEmptyValidator", "EmailValidator"], bad.Errors.Cast<FluentValidationError>().Select(e => e.ErrorCode));
    }

    [Fact]
    public void A_success_holds_the_same_instance()
    {
        var signUp = new SignUp("ann", "a@b.c");

        var good = new SignUpValidator().ValidateToResult(signUp);

        Assert.True(good.IsSuccess);
        Assert.Same(signUp, good.Value);
    }

    [Fact]
    public void Message_property_path_and_code_come_from_the_failure()
    {
        var bad = new DetailedValidator().ValidateToResult(new SignUp("", "x", new Address("", "N1"), ["a", ""]));

        var errors = bad.Errors.Cast<FluentValidationError>().ToList();
        Assert.Equal(["Name", "Address.Line1", "Tags[1]"], errors.Select(e => e.PropertyName));
        Assert.Equal("NAME_REQUIRED", errors[0].ErrorCode);
        Assert.Equal("Tell us your name.", errors[0].Message);
    }

    [Fact]
    public void Each_error_equals_the_failure_it_came_from()
    {
        var result = new DetailedValidator().Validate(new SignUp("", "x", new Address("", "N1")));

        var errors = result.ToValidation(new SignUp("", "x")).Errors;

        Assert.Equal(result.Errors.Select(f => new FluentValidationError(f)), errors.Cast<FluentValidationError>());
        Assert.Equal(result.Errors.Select(f => f.ErrorMessage), errors.Select(e => e.Message));
    }

    [Fact]
    public void Warnings_and_infos_are_errors_because_FluentValidation_calls_the_result_invalid()
    {
        var signUp = new SignUp("", "");
        var raw = new SeverityValidator().Validate(signUp);

        var validation = raw.ToValidation(signUp);

        Assert.False(raw.IsValid);
        Assert.Equal([Severity.Warning, Severity.Info], raw.Errors.Select(f => f.Severity));
        Assert.True(validation.IsFailure);
        Assert.Equal(2, validation.Errors.Count);
    }

    [Fact]
    public void An_empty_result_is_a_success()
    {
        var signUp = new SignUp("ann", "a@b.c");

        var validation = new ValidationResult().ToValidation(signUp);

        Assert.Same(signUp, validation.Value);
    }

    [Fact]
    public void The_errors_cross_into_a_result_chain_as_one_ValidationFailed()
    {
        var bad = new SignUpValidator().ValidateToResult(new SignUp("", "nope"));

        var result = bad.ToResult();

        Assert.True(result.Error.TryFind<FluentValidationError>(out var first));
        Assert.Equal("Name", first.PropertyName);
        Assert.Equal(2, Assert.IsType<ValidationFailed<Error>>(result.Error).Errors.Count);
    }

    [Fact]
    public void A_null_validator_result_or_failure_throws()
    {
        Assert.Throws<ArgumentNullException>("validator", () => ((IValidator<SignUp>)null!).ValidateToResult(new SignUp("a", "b")));
        Assert.Throws<ArgumentNullException>("validator", () => ((IValidator<SignUp>)null!).ValidateToResultAsync(new SignUp("a", "b"), CancellationToken.None).GetAwaiter().GetResult());
        Assert.Throws<ArgumentNullException>("result", () => ((ValidationResult)null!).ToValidation(1));
        Assert.Throws<ArgumentNullException>("failure", () => new FluentValidationError(null!));
    }

    [Fact]
    public void A_rule_that_throws_propagates()
    {
        var validator = new InlineValidator<SignUp>();
        validator.RuleFor(s => s.Name).Must(_ => throw new InvalidOperationException("boom"));

        Assert.Throws<InvalidOperationException>(() => validator.ValidateToResult(new SignUp("a", "b")));
    }

    [Fact]
    public void An_async_rule_run_synchronously_throws_FluentValidations_own_exception()
    {
        var validator = new InlineValidator<SignUp>();
        validator.RuleFor(s => s.Name).MustAsync((_, _) => Task.FromResult(true));

        Assert.Throws<AsyncValidatorInvokedSynchronouslyException>(() => validator.ValidateToResult(new SignUp("a", "b")));
    }
}
