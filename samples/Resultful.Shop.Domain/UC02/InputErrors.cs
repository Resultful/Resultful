using System.Diagnostics.CodeAnalysis;
using Resultful.CodeAnalysis;

namespace Resultful.Shop.Domain;

// Use case 2: a sign-up form that reports every mistake at once.
#region snippet: uc02-errors
[ClosedHierarchy]
public abstract record InputError : Error
{
    private protected InputError(string field, string message) : base(message) => Field = field;
    public string Field { get; }
}
public sealed record Required(string Field) : InputError(Field, $"{Field} is required");
public sealed record TooShort(string Field, int Min) : InputError(Field, $"{Field} needs at least {Min} characters");
public sealed record NotAnEmail(string Field) : InputError(Field, $"{Field} is not an email address");
#endregion

public sealed record SignUpRequest(string? Name, string? Email, string? Password);

public sealed record SignUp(string Name, Email Email, string Password);

public sealed record Email
{
    private Email(string value) => Value = value;

    public string Value { get; }

    public static bool TryParse(string value, [NotNullWhen(true)] out Email? email)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        email = at > 0 && at < value.Length - 1 ? new Email(value.Trim()) : null;
        return email is not null;
    }

    public override string ToString() => Value;
}
