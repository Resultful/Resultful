using FluentValidation;

namespace Resultful.FluentValidation.Tests;

public sealed record Address(string Line1, string Postcode);

public sealed record SignUp(string Name, string Email, Address? Address = null, IReadOnlyList<string>? Tags = null);

public sealed class SignUpValidator : AbstractValidator<SignUp>
{
    public SignUpValidator()
    {
        RuleFor(s => s.Name).NotEmpty();
        RuleFor(s => s.Email).EmailAddress();
    }
}

public sealed class DetailedValidator : AbstractValidator<SignUp>
{
    public DetailedValidator()
    {
        RuleFor(s => s.Name).NotEmpty().WithErrorCode("NAME_REQUIRED").WithMessage("Tell us your name.");
        RuleFor(s => s.Address!.Line1).NotEmpty().When(s => s.Address is not null);
        RuleForEach(s => s.Tags).NotEmpty().When(s => s.Tags is not null);
    }
}

public sealed class SeverityValidator : AbstractValidator<SignUp>
{
    public SeverityValidator()
    {
        RuleFor(s => s.Name).NotEmpty().WithSeverity(Severity.Warning);
        RuleFor(s => s.Email).NotEmpty().WithSeverity(Severity.Info);
    }
}
