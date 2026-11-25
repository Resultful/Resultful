using CsCheck;
using FluentValidation;
using FluentValidation.Results;

namespace Resultful.FluentValidation.Tests;

public sealed class Laws
{
    static readonly Gen<ValidationFailure> Failure =
        Gen.Select(Gen.String.AlphaNumeric, Gen.String, Gen.String.AlphaNumeric, Gen.Enum<Severity>())
            .Select((property, message, code, severity) => new ValidationFailure(property, message, "attempted:" + code) { ErrorCode = code, Severity = severity });

    // Values chosen so every rule of DetailedValidator both passes and fails often.
    static readonly Gen<string> Word = Gen.OneOf(Gen.Const(""), Gen.String[Gen.Char["abcAB1 !"], 1, 6]);
    static readonly Gen<SignUp> SignUps =
        Gen.Select(Word, Word, Gen.Select(Word, Word, (l, p) => new Address(l, p)).Null(), Word.List[0, 3].Null(),
                Gen.Select(Word, Gen.Int[-1, 2], (s, q) => new Line(s, q)).List[0, 3].Null())
            .Select((name, email, address, tags, lines) => new SignUp(name, email, address, tags, lines));

    [Fact]
    public void ValidateToResult_fails_exactly_when_FluentValidation_says_invalid_with_one_error_per_failure()
    {
        var validator = new DetailedValidator();
        SignUps.Sample(signUp =>
        {
            var raw = validator.Validate(signUp);
            var validation = validator.ValidateToResult(signUp);
            if (raw.IsValid) return validation.IsSuccess && ReferenceEquals(signUp, validation.Value);
            return validation.IsFailure
                && validation.Errors.Count == raw.Errors.Count
                && validation.Errors.Cast<FluentValidationError>().Select(e => e.PropertyName).SequenceEqual(raw.Errors.Select(f => f.PropertyName));
        });
    }

    [Fact]
    public void ToValidation_keeps_each_failure_in_order_and_succeeds_only_on_none()
    {
        Failure.List[0, 8].Sample(failures =>
        {
            var instance = new object();
            var validation = new ValidationResult(failures).ToValidation(instance);

            if (failures.Count == 0) return validation.IsSuccess && ReferenceEquals(instance, validation.Value);
            var errors = validation.Errors.Cast<FluentValidationError>().ToList();
            return validation.IsFailure
                && errors.Count == failures.Count
                && errors.Zip(failures).All(p => p.First.PropertyName == p.Second.PropertyName && p.First.ErrorCode == p.Second.ErrorCode && p.First.Message == p.Second.ErrorMessage && p.First.Severity == p.Second.Severity);
        });
    }

    [Fact]
    public void A_failure_crosses_into_a_result_as_one_ValidationFailed_holding_the_same_errors()
    {
        var validator = new DetailedValidator();
        SignUps.Sample(signUp =>
        {
            var validation = validator.ValidateToResult(signUp);
            var result = validation.ToResult();
            return validation.IsSuccess
                ? result.IsSuccess && ReferenceEquals(signUp, result.Value)
                : result.Error is ValidationFailed<Error> failed && failed.Errors.SequenceEqual(validation.Errors);
        });
    }

    [Fact]
    public void Two_errors_are_equal_exactly_when_their_message_property_code_and_severity_are()
    {
        Gen.Select(Failure, Failure).Sample((a, b) =>
        {
            var same = a.ErrorMessage == b.ErrorMessage && a.PropertyName == b.PropertyName && a.ErrorCode == b.ErrorCode && a.Severity == b.Severity;
            var (x, y) = (new FluentValidationError(a), new FluentValidationError(b));
            return x.Equals(y) == same && (!same || x.GetHashCode() == y.GetHashCode());
        });
    }

    [Fact]
    public void The_sync_and_async_paths_agree()
    {
        var validator = new DetailedValidator();
        SignUps.Sample(signUp =>
        {
            var sync = validator.ValidateToResult(signUp);
            var async = validator.ValidateToResultAsync(signUp, CancellationToken.None).GetAwaiter().GetResult();
            return sync.Equals(async);
        });
    }
}
