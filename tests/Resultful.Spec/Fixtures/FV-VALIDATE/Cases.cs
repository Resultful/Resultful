// M5 (21): Resultful.FluentValidation is an adapter. A validator's failures become a Validation<T>, one error each.
// spec: uses Resultful.FluentValidation
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Resultful;
using Resultful.FluentValidation;
using Resultful.Spec.Support;

public sealed record SignUp(string Name, string Email);

public sealed class SignUpValidator : AbstractValidator<SignUp>
{
    public SignUpValidator()
    {
        RuleFor(s => s.Name).NotEmpty();
        RuleFor(s => s.Email).EmailAddress();
    }
}

public static class Cases
{
    [SpecCase]
    public static async Task Every_failure_becomes_one_error()
    {
        var bad = new SignUpValidator().ValidateToResult(new SignUp("", "nope"));
        Check.SequenceEqual(new[] { "Name", "Email" }, bad.Errors.OfType<FluentValidationError>().Select(e => e.PropertyName), "one error per failure");
        var good = await new SignUpValidator().ValidateToResultAsync(new SignUp("ann", "a@b.c"), CancellationToken.None);
        Check.Equal("ann", good.Value.Name, "the instance on success");
    }
}
