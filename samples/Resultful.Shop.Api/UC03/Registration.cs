using Resultful.AspNetCore;
using Resultful.Shop.Domain;

namespace Resultful.Shop.Api;

internal sealed class AccountStore
{
    private readonly List<Account> _accounts = [new Account(1, "taken", CreateEmail("taken@example.com"))];

    public bool Exists(Email email) => _accounts.Exists(a => a.Email == email);

    public Result<Account> Create(SignUp s)
    {
        var account = new Account(_accounts.Count + 1, s.Name, s.Email);
        _accounts.Add(account);
        return account;
    }

    private static Email CreateEmail(string value) => Email.TryParse(value, out var e) ? e : throw new ArgumentException(value, nameof(value));
}

internal sealed class Registration
{
    private readonly AccountStore _accounts = new();

    #region snippet: uc03-register
    public Result<Account> Register(SignUp s) =>
        _accounts.Exists(s.Email)
            ? new EmailTaken(s.Email)
            : _accounts.Create(s);
    #endregion
}

internal static class RegisterEndpoints
{
    public static void MapErrors(ResultfulHttpOptions http) => http.Map<EmailTaken>(StatusCodes.Status409Conflict);

    public static void Map(WebApplication app)
    {
        #region snippet: uc03-endpoint
        app.MapPost("/sign-up", (SignUpRequest r, Registration registration) =>
            SignUpValidation.Validate(r).Match(
                signUp => registration.Register(signUp).Match(
                    account => Results.Created($"/accounts/{account.Id}", account),
                    error => error.TryFind<EmailTaken>(out _) ? Results.Conflict() : Results.Problem()),
                errors => Results.ValidationProblem(SignUpValidation.ToDictionary(errors))));
        #endregion

        // Through Resultful.AspNetCore: the Validation's errors travel as one ValidationFailed (400), EmailTaken is 409.
        app.MapPost("/api/sign-up", (SignUpRequest r, Registration registration) =>
            SignUpValidation.Validate(r).ToResult().Bind(registration.Register).ToHttpResult());
    }
}
