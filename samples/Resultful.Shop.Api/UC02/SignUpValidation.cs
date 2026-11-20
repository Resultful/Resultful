using Resultful.AspNetCore;
using Resultful.Shop.Domain;

namespace Resultful.Shop.Api;

internal static class SignUpValidation
{
    #region snippet: uc02-validate
    public static Validation<string, InputError> Name(string? v) =>
        string.IsNullOrWhiteSpace(v) ? new Required("name") : v;

    public static Validation<Email, InputError> EmailOf(string? v) =>
        string.IsNullOrWhiteSpace(v) ? new Required("email")
        : Email.TryParse(v, out var e) ? e
        : new NotAnEmail("email");

    public static Validation<string, InputError> Password(string? v) =>
        v is null ? new Required("password")
        : v.Length < 12 ? new TooShort("password", 12)
        : v;

    public static Validation<SignUp, InputError> Validate(SignUpRequest r) =>
        Validation.Combine(Name(r.Name), EmailOf(r.Email), Password(r.Password),
            (name, email, password) => new SignUp(name, email, password));
    #endregion

    public static Dictionary<string, string[]> ToDictionary(IReadOnlyList<InputError> errors) =>
        errors.GroupBy(e => e.Field).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
}

internal static class SignUpEndpoints
{
    public static void MapErrors(ResultfulHttpOptions http) =>
        http.ValidationKey = error => error is InputError input ? input.Field : string.Empty;

    public static void Map(WebApplication app)
    {
        #region snippet: uc02-endpoint
        app.MapPost("/sign-up/check", (SignUpRequest r) =>
            SignUpValidation.Validate(r).Match(
                signUp => Results.Ok(),
                errors => Results.ValidationProblem(
                    errors.GroupBy(e => e.Field)
                          .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()))));
        #endregion

        app.MapPost("/api/sign-up/check", (SignUpRequest r) => SignUpValidation.Validate(r).ToHttpResult());
    }
}
