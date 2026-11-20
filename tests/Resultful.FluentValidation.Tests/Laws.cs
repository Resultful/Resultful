using CsCheck;
using FluentValidation;
using FluentValidation.Results;

namespace Resultful.FluentValidation.Tests;

public sealed class Laws
{
    static readonly Gen<ValidationFailure> Failure =
        Gen.Select(Gen.String.AlphaNumeric, Gen.String, Gen.String.AlphaNumeric, Gen.Enum<Severity>())
            .Select((property, message, code, severity) => new ValidationFailure(property, message, "attempted:" + code) { ErrorCode = code, Severity = severity });

    [Fact]
    public void A_result_is_a_failure_exactly_when_FluentValidation_reported_failures_and_keeps_each_in_order()
    {
        Failure.List[0, 8].Sample(failures =>
        {
            var instance = new object();
            var validation = new ValidationResult(failures).ToValidation(instance);

            if (failures.Count == 0) return validation.IsSuccess && ReferenceEquals(instance, validation.Value);
            var errors = validation.Errors.Cast<FluentValidationError>().ToList();
            return validation.IsFailure
                && errors.Count == failures.Count
                && errors.Zip(failures).All(p => p.First.PropertyName == p.Second.PropertyName && p.First.ErrorCode == p.Second.ErrorCode && p.First.Message == p.Second.ErrorMessage);
        });
    }

    [Fact]
    public void Two_errors_are_equal_exactly_when_their_message_property_and_code_are()
    {
        Gen.Select(Failure, Failure).Sample((a, b) =>
        {
            var same = a.ErrorMessage == b.ErrorMessage && a.PropertyName == b.PropertyName && a.ErrorCode == b.ErrorCode;
            var (x, y) = (new FluentValidationError(a), new FluentValidationError(b));
            return x.Equals(y) == same && (!same || x.GetHashCode() == y.GetHashCode());
        });
    }

    [Fact]
    public void The_sync_and_async_paths_agree()
    {
        Gen.Select(Gen.String.AlphaNumeric, Gen.String).Sample((name, email) =>
        {
            var signUp = new SignUp(name, email);
            var sync = new SignUpValidator().ValidateToResult(signUp);
            var async = new SignUpValidator().ValidateToResultAsync(signUp, CancellationToken.None).GetAwaiter().GetResult();
            return sync.Equals(async);
        });
    }
}
