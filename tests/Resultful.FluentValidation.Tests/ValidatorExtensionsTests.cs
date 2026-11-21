using System.Globalization;
using FluentValidation;
using FluentValidation.Results;

namespace Resultful.FluentValidation.Tests;

public sealed class ValidatorExtensionsTests
{
    static List<FluentValidationError> Errors<T>(Validation<T> validation) => validation.Errors.Cast<FluentValidationError>().ToList();

    [Fact]
    public void Every_failure_becomes_one_error_in_order()
    {
        var bad = new SignUpValidator().ValidateToResult(new SignUp("", "nope"));

        Assert.True(bad.IsFailure);
        Assert.Equal(["Name", "Email"], Errors(bad).Select(e => e.PropertyName));
        Assert.Equal(["NotEmptyValidator", "EmailValidator"], Errors(bad).Select(e => e.ErrorCode));
        Assert.Equal(["'Name' must not be empty.", "'Email' is not a valid email address."], bad.Errors.Select(e => e.Message));
    }

    [Fact]
    public void A_success_holds_the_same_instance_unchanged()
    {
        var signUp = new SignUp("ann", "a@b.c", new Address("1 Road", "N1"), ["x"]);

        var good = new DetailedValidator().ValidateToResult(signUp);

        Assert.True(good.IsSuccess);
        Assert.Same(signUp, good.Value);
        Assert.Equal(new SignUp("ann", "a@b.c", new Address("1 Road", "N1"), good.Value.Tags), good.Value);
    }

    [Fact]
    public void A_null_success_value_is_passed_through_by_ToValidation()
    {
        var validation = new ValidationResult().ToValidation<SignUp?>(null);

        Assert.True(validation.IsSuccess);
        Assert.Null(validation.Value);
    }

    [Fact]
    public void Nested_and_collection_paths_are_kept_as_FluentValidation_reports_them()
    {
        var signUp = new SignUp("", "x", new Address("", "n1!"), ["a", ""], [new Line("A", 1), new Line("", 0)]);

        var errors = Errors(new DetailedValidator().ValidateToResult(signUp));

        Assert.Equal(["Name", "Address.Line1", "Address.Postcode", "Tags[1]", "Lines[1].Sku", "Lines[1].Quantity"], errors.Select(e => e.PropertyName));
        Assert.Equal(["NAME_REQUIRED", "NotEmptyValidator", "POSTCODE", "NotEmptyValidator", "NotEmptyValidator", "GreaterThanValidator"], errors.Select(e => e.ErrorCode));
        Assert.Equal("Tell us your name.", errors[0].Message);
    }

    [Fact]
    public void A_member_path_rule_reports_the_dotted_path()
    {
        var error = Assert.Single(Errors(new MemberPathValidator().ValidateToResult(new SignUp("a", "b", new Address("", "N1")))));

        Assert.Equal("Address.Line1", error.PropertyName);
    }

    [Fact]
    public void Several_failures_on_one_property_are_each_an_error()
    {
        var errors = Errors(new TwoRulesValidator().ValidateToResult(new SignUp("", "b")));

        Assert.Equal(["Name", "Name"], errors.Select(e => e.PropertyName));
        Assert.Equal(["NotEmptyValidator", "MinimumLengthValidator"], errors.Select(e => e.ErrorCode));
    }

    [Fact]
    public void A_stopping_cascade_is_FluentValidations_choice_and_is_kept()
    {
        var error = Assert.Single(Errors(new TwoRulesValidator(stopAtFirst: true).ValidateToResult(new SignUp("", "b"))));

        Assert.Equal("NotEmptyValidator", error.ErrorCode);
    }

    [Fact]
    public void Each_error_equals_the_failure_it_came_from()
    {
        var signUp = new SignUp("", "x", new Address("", "N1"));
        var result = new DetailedValidator().Validate(signUp);

        var errors = result.ToValidation(signUp).Errors;

        Assert.Equal(result.Errors.Select(f => new FluentValidationError(f)), errors.Cast<FluentValidationError>());
        Assert.Equal(result.Errors.Select(f => f.ErrorMessage), errors.Select(e => e.Message));
    }

    [Theory]
    [InlineData(Severity.Error)]
    [InlineData(Severity.Warning)]
    [InlineData(Severity.Info)]
    public void A_failure_of_any_severity_is_an_error_because_FluentValidation_calls_the_result_invalid(Severity severity)
    {
        var validator = new InlineValidator<SignUp>();
        validator.RuleFor(s => s.Name).NotEmpty().WithSeverity(severity);
        var signUp = new SignUp("", "b");

        var raw = validator.Validate(signUp);
        var validation = validator.ValidateToResult(signUp);

        Assert.False(raw.IsValid);
        var error = Assert.IsType<FluentValidationError>(Assert.Single(validation.Errors));
        Assert.Equal(("Name", severity), (error.PropertyName, error.Severity));
    }

    [Fact]
    public void Mixed_severities_keep_their_order()
    {
        var signUp = new SignUp("", "");
        var raw = new SeverityValidator().Validate(signUp);

        var validation = raw.ToValidation(signUp);

        Assert.Equal([Severity.Warning, Severity.Info], raw.Errors.Select(f => f.Severity));
        Assert.Equal(["Name", "Email"], Errors(validation).Select(e => e.PropertyName));
        Assert.Equal([Severity.Warning, Severity.Info], Errors(validation).Select(e => e.Severity));
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

        var failed = Assert.IsType<ValidationFailed<Error>>(result.Error);
        Assert.Equal(bad.Errors, failed.Errors);
        Assert.Equal("Validation failed.", failed.Message);
        Assert.True(result.Error.TryFind<FluentValidationError>(out var first));
        Assert.Equal("Name", first.PropertyName);
        Assert.Equal(
            "ValidationFailed<Error>: Validation failed. [FluentValidationError: 'Name' must not be empty.; FluentValidationError: 'Email' is not a valid email address.]",
            result.Error.ToString());
    }

    [Fact]
    public void Validations_from_several_validators_combine_and_keep_every_error()
    {
        var signUp = new SignUp("", "nope", new Address("", "N1"));

        var both = Validation.Combine(
            new SignUpValidator().ValidateToResult(signUp),
            new AddressValidator().ValidateToResult(signUp.Address!),
            (s, a) => (s, a));

        Assert.Equal(["Name", "Email", "Line1"], Errors(both).Select(e => e.PropertyName));
    }

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>("validator", () => ((IValidator<SignUp>)null!).ValidateToResult(new SignUp("a", "b")));
        Assert.Throws<ArgumentNullException>("result", () => ((ValidationResult)null!).ToValidation(1));
        Assert.Throws<ArgumentNullException>("failure", () => new FluentValidationError(null!));
    }

    [Fact]
    public void A_null_instance_is_FluentValidations_to_reject()
    {
        var thrown = Record.Exception(() => new SignUpValidator().ValidateToResult(null!));

        Assert.Equal(Record.Exception(() => new SignUpValidator().Validate((SignUp)null!))?.GetType(), thrown?.GetType());
        Assert.NotNull(thrown);
    }

    [Fact]
    public void A_rule_that_throws_propagates()
    {
        var validator = new InlineValidator<SignUp>();
        validator.RuleFor(s => s.Name).Must(_ => throw new InvalidOperationException("boom"));

        Assert.Equal("boom", Assert.Throws<InvalidOperationException>(() => validator.ValidateToResult(new SignUp("a", "b"))).Message);
    }

    [Fact]
    public void An_async_rule_run_synchronously_throws_FluentValidations_own_exception()
    {
        var validator = new InlineValidator<SignUp>();
        validator.RuleFor(s => s.Name).MustAsync((_, _) => Task.FromResult(true));

        Assert.Throws<AsyncValidatorInvokedSynchronouslyException>(() => validator.ValidateToResult(new SignUp("a", "b")));
    }

    [Fact]
    public void A_message_is_in_the_ui_culture_the_validator_ran_under()
    {
        var item = new SignUp("", "a@b.c");
        var english = Under("en-US", () => new SignUpValidator().ValidateToResult(item)).Errors[0].Message;
        var turkishRaw = Under("tr-TR", () => new SignUpValidator().Validate(item)).Errors[0].ErrorMessage;

        var turkish = Under("tr-TR", () => new SignUpValidator().ValidateToResult(item)).Errors[0];

        Assert.Equal(turkishRaw, turkish.Message);
        Assert.NotEqual(english, turkish.Message);
        Assert.Equal(("Name", "NotEmptyValidator"), (((FluentValidationError)turkish).PropertyName, ((FluentValidationError)turkish).ErrorCode));
    }

    internal static T Under<T>(string culture, Func<T> run)
    {
        var (ui, current) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            return run();
        }
        finally
        {
            (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (ui, current);
        }
    }
}
