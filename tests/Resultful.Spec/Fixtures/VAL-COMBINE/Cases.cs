// Decision 14 (10): Validation.Combine runs every check and names the values, with no nested tuples.
using Resultful;
using Resultful.Spec.Support;

public abstract record InputError(string Field, string Message) : Error(Message);
public sealed record Required(string Field) : InputError(Field, $"{Field} is required");
public sealed record TooShort(string Field, int Min) : InputError(Field, $"{Field} needs at least {Min} characters");
public sealed record SignUp(string Name, string Email, string Password);

public static class Cases
{
    static Validation<string, InputError> Name(string? v) => string.IsNullOrWhiteSpace(v) ? new Required("name") : v;
    static Validation<string, InputError> Email(string? v) => string.IsNullOrWhiteSpace(v) ? new Required("email") : v;
    static Validation<string, InputError> Password(string? v) => v is null ? new Required("password") : v.Length < 12 ? new TooShort("password", 12) : v;

    static Validation<SignUp, InputError> Validate(string? n, string? e, string? p) =>
        Validation.Combine(Name(n), Email(e), Password(p), (name, email, password) => new SignUp(name, email, password));

    [SpecCase]
    public static void Every_failure_is_reported_at_once()
    {
        var v = Validate(null, "a@b.c", "short");
        Check.True(v.TryGetErrors(out var errors), "failed");
        Check.SequenceEqual(new[] { "name", "password" }, System.Linq.Enumerable.Select(errors, e => e.Field), "both fields, in order");
    }

    [SpecCase]
    public static void All_valid_combines_the_values()
    {
        var v = Validate("ann", "a@b.c", "long enough pw");
        Check.Equal(new SignUp("ann", "a@b.c", "long enough pw"), v.Value, "combined");
    }

    [SpecCase]
    public static void Two_combine_into_a_tuple()
    {
        Validation<(string, string), InputError> both = Validation.Combine(Name("a"), Email(null));
        Check.True(both.IsFailure && both.Errors.Count == 1, "one error");
    }
}
