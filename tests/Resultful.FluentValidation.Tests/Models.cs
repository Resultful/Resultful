using FluentValidation;

namespace Resultful.FluentValidation.Tests;

public sealed record Address(string Line1, string Postcode);

public sealed record Line(string Sku, int Quantity);

public sealed record SignUp(string Name, string Email, Address? Address = null, IReadOnlyList<string>? Tags = null, IReadOnlyList<Line>? Lines = null);

public sealed class SignUpValidator : AbstractValidator<SignUp>
{
    public SignUpValidator()
    {
        RuleFor(s => s.Name).NotEmpty();
        RuleFor(s => s.Email).EmailAddress();
    }
}

public sealed class AddressValidator : AbstractValidator<Address>
{
    public AddressValidator()
    {
        RuleFor(a => a.Line1).NotEmpty();
        RuleFor(a => a.Postcode).Matches("^[A-Z0-9 ]+$").WithErrorCode("POSTCODE");
    }
}

public sealed class LineValidator : AbstractValidator<Line>
{
    public LineValidator()
    {
        RuleFor(l => l.Sku).NotEmpty();
        RuleFor(l => l.Quantity).GreaterThan(0);
    }
}

// Every path shape FluentValidation reports: a custom code and message, a nested child validator, a flattened member
// path, scalar collection items and complex collection items.
public sealed class DetailedValidator : AbstractValidator<SignUp>
{
    public DetailedValidator()
    {
        RuleFor(s => s.Name).NotEmpty().WithErrorCode("NAME_REQUIRED").WithMessage("Tell us your name.");
        RuleFor(s => s.Address!).SetValidator(new AddressValidator()).When(s => s.Address is not null);
        RuleForEach(s => s.Tags).NotEmpty().When(s => s.Tags is not null);
        RuleForEach(s => s.Lines).SetValidator(new LineValidator()).When(s => s.Lines is not null);
    }
}

public sealed class MemberPathValidator : AbstractValidator<SignUp>
{
    public MemberPathValidator() => RuleFor(s => s.Address!.Line1).NotEmpty().When(s => s.Address is not null);
}

// Two rules on one property, both reported (FluentValidation's default cascade is Continue).
public sealed class TwoRulesValidator : AbstractValidator<SignUp>
{
    public TwoRulesValidator(bool stopAtFirst = false)
    {
        var name = RuleFor(s => s.Name);
        if (stopAtFirst) name = name.Cascade(CascadeMode.Stop);
        name.NotEmpty().MinimumLength(3);
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
