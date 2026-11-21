using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Resultful.AspNetCore;

namespace Resultful.FluentValidation.Tests;

// A sign-up endpoint as an app writes it: FluentValidation checks the request (one rule async), a domain step may refuse
// it, an async store saves it, and Resultful.AspNetCore answers. The adapter only carries the failures across.
public sealed class ScenarioTests
{
    const string Sentinel = "SENTINEL-ada-at-example.com";

    public sealed record Account(int Id, string Name);

    public sealed record NameReserved(string Name) : Error("that name is reserved");

    sealed class Accounts
    {
        readonly HashSet<string> _emails = new(StringComparer.OrdinalIgnoreCase) { "taken@example.com" };
        public List<Account> Saved { get; } = [];

        public Task<bool> EmailIsFree(string email, CancellationToken ct) => Task.FromResult(!_emails.Contains(email));

        public static Result<SignUp> Admit(SignUp signUp) =>
            signUp.Name.Equals("admin", StringComparison.OrdinalIgnoreCase) ? Result.Fail<SignUp>(new NameReserved(signUp.Name)) : Result.Ok(signUp);

        public async Task<Result<Account>> Save(SignUp signUp)
        {
            await Task.Yield();
            var account = new Account(Saved.Count + 1, signUp.Name);
            Saved.Add(account);
            return Result.Ok(account);
        }
    }

    sealed class RequestValidator : AbstractValidator<SignUp>
    {
        public RequestValidator(Accounts accounts)
        {
            RuleFor(s => s.Name).NotEmpty().MinimumLength(3);
            RuleFor(s => s.Email).EmailAddress().MustAsync(accounts.EmailIsFree).WithErrorCode("EMAIL_TAKEN").WithMessage("That email is already registered.");
            RuleForEach(s => s.Lines).SetValidator(new LineValidator()).When(s => s.Lines is not null);
        }
    }

    static async Task<(IResult Http, Accounts Accounts)> SignUpEndpoint(SignUp request)
    {
        var accounts = new Accounts();
        var http = (await new RequestValidator(accounts).ValidateToResultAsync(request, TestContext.Current.CancellationToken))
            .ToResult()
            .Bind(Accounts.Admit)
            .BindAsync(accounts.Save);
        return ((await http).ToHttpResult(), accounts);
    }

    static async Task<(int Status, JsonElement Body, string Text)> Execute(IResult result)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddResultful(o =>
        {
            o.Map<NameReserved>(StatusCodes.Status409Conflict);
            o.ValidationKey = e => (e as FluentValidationError)?.PropertyName ?? "";
        });
        await using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        return (context.Response.StatusCode, JsonElement.Parse(text), text);
    }

    [Fact]
    public async Task Invalid_requests_answer_400_with_errors_keyed_by_property_path_and_nothing_is_saved()
    {
        var (http, accounts) = await SignUpEndpoint(new SignUp("", "taken@example.com", Lines: [new Line("", 1)]));

        var (status, body, _) = await Execute(http);

        Assert.Equal(400, status);
        var errors = body.GetProperty("errors");
        Assert.Equal(["Name", "Email", "Lines[0].Sku"], errors.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["'Name' must not be empty.", "The length of 'Name' must be at least 3 characters. You entered 0 characters."], errors.GetProperty("Name").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["That email is already registered."], errors.GetProperty("Email").EnumerateArray().Select(e => e.GetString()));
        Assert.Empty(accounts.Saved);
    }

    [Fact]
    public async Task A_valid_request_the_domain_refuses_answers_409_and_nothing_is_saved()
    {
        var (http, accounts) = await SignUpEndpoint(new SignUp("admin", "new@example.com"));

        var (status, body, _) = await Execute(http);

        Assert.Equal(409, status);
        Assert.Equal("that name is reserved", body.GetProperty("detail").GetString());
        Assert.Empty(accounts.Saved);
    }

    [Fact]
    public async Task A_valid_request_is_saved_and_answers_200()
    {
        var (http, accounts) = await SignUpEndpoint(new SignUp("ann", "new@example.com"));

        var (status, body, _) = await Execute(http);

        Assert.Equal(200, status);
        Assert.Equal("ann", body.GetProperty("name").GetString());
        Assert.Equal(new Account(1, "ann"), Assert.Single(accounts.Saved));
    }

    // A rule over personal data whose built-in message embeds values: NotEqual against another property puts that
    // property's value in the message ({ComparisonValue}), InclusiveBetween the attempted value ({PropertyValue}).
    static InlineValidator<SignUp> ValueRules(bool withMessages)
    {
        var validator = new InlineValidator<SignUp>();
        var notEmail = validator.RuleFor(s => s.Name).NotEqual(s => s.Email);
        var quantity = validator.RuleForEach(s => s.Lines).ChildRules(l =>
        {
            var range = l.RuleFor(x => x.Quantity).InclusiveBetween(1, 10);
            if (withMessages) range.WithMessage("'{PropertyName}' must be between {From} and {To}.");
        });
        if (withMessages) notEmail.WithMessage("'{PropertyName}' must not be the email address.");
        return validator;
    }

    static async Task<(int Status, JsonElement Body, string Text)> Respond(IValidator<SignUp> validator, SignUp request) =>
        await Execute((await validator.ValidateToResultAsync(request, TestContext.Current.CancellationToken)).ToResult().ToHttpResult());

    [Fact]
    public async Task By_default_a_built_in_message_carries_values_into_the_400_body()
    {
        // Documented behaviour (FluentValidationError remarks, README): the adapter doesn't rewrite messages.
        var (status, body, text) = await Respond(ValueRules(withMessages: false), new SignUp(Sentinel, Sentinel, Lines: [new Line("A", 4111)]));

        Assert.Equal(400, status);
        Assert.Equal($"'Name' must not be equal to '{Sentinel}'.", body.GetProperty("errors").GetProperty("Name")[0].GetString());
        Assert.Equal("'Quantity' must be between 1 and 10. You entered 4111.", body.GetProperty("errors").GetProperty("Lines[0].Quantity")[0].GetString());
        Assert.Contains("SENTINEL", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithMessage_keeps_the_values_out_of_the_400_body()
    {
        var (status, body, text) = await Respond(ValueRules(withMessages: true), new SignUp(Sentinel, Sentinel, Lines: [new Line("A", 4111)]));

        Assert.Equal(400, status);
        Assert.Equal("'Name' must not be the email address.", body.GetProperty("errors").GetProperty("Name")[0].GetString());
        Assert.Equal("'Quantity' must be between 1 and 10.", body.GetProperty("errors").GetProperty("Lines[0].Quantity")[0].GetString());
        Assert.DoesNotContain("SENTINEL", text, StringComparison.Ordinal);
        Assert.DoesNotContain("4111", text, StringComparison.Ordinal);
    }
}
