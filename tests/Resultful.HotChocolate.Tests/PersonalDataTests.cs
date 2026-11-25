using CsCheck;
using HotChocolate;
using Microsoft.Extensions.DependencyInjection;

namespace Resultful.HotChocolate.Tests;

public sealed class AccountQuery
{
    public Result<User> Login([GlobalState("result")] Result<User> result) => result;

    public Result<User, AccountLocked> Unlock([GlobalState("result")] Result<User, AccountLocked> result) => result;
}

public sealed class AccountMutation
{
    public Result<User, AccountLocked> ResetPassword(int id, [GlobalState("result")] Result<User, AccountLocked> result) => result;
}

// No output shows a leaf's own properties by default: not messages, extensions, top-level errors, validation messages or
// details, and an error type has only `message`. An app that exposes a leaf's fields (ExposeFieldsOf, ExposeLeafFields)
// shows them on that error type alone, and only when a client selects them.
public class PersonalDataTests
{
    private const string Generic = "__typename ... on Error { message } ... on ValidationError { fields { field messages } }";

    private static readonly AccountLocked Locked = new(Sentinel.Email);

    [Theory]
    [InlineData(Graph.Production)]
    [InlineData(Graph.Development)]
    public async Task An_undeclared_error_never_shows_a_leaf_s_properties(string environment)
    {
        var executor = await Server(environment);
        Error[] failures =
        [
            Locked,
            Locked.WithContext("signing in"),
            new DatabaseDown("db1") { Cause = Locked },
            new DatabaseDown("db1") { Cause = new ValidationFailed<Error>([Locked]) },
        ];

        foreach (var failure in failures)
        {
            var json = await Graph.RunJsonAsync(executor, $"{{ login {{ {Generic} }} }}", Result.Fail<User>(failure));
            Assert.Contains("\"errors\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain(Sentinel.Email, json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_development_message_names_the_leaf_but_not_its_properties()
    {
        var executor = await Server(Graph.Development);

        var response = await Graph.RunAsync(executor, "{ login { __typename } }", Result.Fail<User>(new DatabaseDown("db1") { Cause = Locked }));

        Assert.Equal("DatabaseDown: The database is down. ---> AccountLocked: The account is locked.", (string?)response["errors"]![0]!["message"]);
    }

    [Fact]
    public async Task Validation_messages_never_show_a_leaf_s_properties()
    {
        var executor = await Server(Graph.Production);
        await Gen.Int[1, 5].SampleAsync(async count =>
        {
            var errors = Enumerable.Range(0, count).Select(i => i % 2 == 0 ? (Error)Locked : new Required("name")).ToArray();

            var json = await Graph.RunJsonAsync(executor, $"{{ login {{ {Generic} }} }}", Result.Fail<User>(new ValidationFailed<Error>(errors)));

            Assert.Contains("ValidationError", json, StringComparison.Ordinal);
            Assert.DoesNotContain(Sentinel.Email, json, StringComparison.Ordinal);
        }, iter: 20);
    }

    [Fact]
    public async Task A_declared_error_shows_message_only_by_default()
    {
        var executor = await Server(Graph.Production);

        var generic = await Graph.RunJsonAsync(executor, "{ unlock { __typename ... on Error { message } } }", Result.Fail<User, AccountLocked>(Locked));
        var selected = await Graph.RunJsonAsync(executor, "{ unlock { ... on AccountLocked { email } } }", Result.Fail<User, AccountLocked>(Locked));
        var payload = await Graph.RunJsonAsync(
            executor,
            "mutation { resetPassword(input: { id: 1 }) { errors { __typename ... on Error { message } } } }",
            Result.Fail<User, AccountLocked>(Locked));

        Assert.Contains("type AccountLocked implements Error {\n  \"What went wrong, for people to read.\"\n  message: String!\n}", Graph.Sdl(executor), StringComparison.Ordinal);
        Assert.Equal("""{"data":{"unlock":{"__typename":"AccountLocked","message":"The account is locked."}}}""", generic);
        Assert.Equal("""{"data":{"resetPassword":{"errors":[{"__typename":"AccountLocked","message":"The account is locked."}]}}}""", payload);
        Assert.Contains("\"errors\"", selected, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel.Email, selected, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_exposed_leaf_shows_its_field_only_when_selected(bool everyLeaf)
    {
        var executor = await Server(Graph.Production, expose: o =>
        {
            if (everyLeaf) o.ExposeLeafFields = true;
            else o.ExposeFieldsOf<AccountLocked>();
        });

        var generic = await Graph.RunJsonAsync(executor, "{ unlock { __typename ... on Error { message } } }", Result.Fail<User, AccountLocked>(Locked));
        var selected = await Graph.RunJsonAsync(executor, "{ unlock { ... on AccountLocked { email } } }", Result.Fail<User, AccountLocked>(Locked));

        Assert.DoesNotContain(Sentinel.Email, generic, StringComparison.Ordinal);
        Assert.Equal("{\"data\":{\"unlock\":{\"email\":\"" + Sentinel.Email + "\"}}}", selected);
    }

    [Fact]
    public async Task Details_never_show_a_leaf_s_properties()
    {
        var executor = await Server(Graph.Production, includeDetails: true);
        var error = Locked.WithContext("signing in") with { Cause = new AccountLocked(Sentinel.Email + "-cause") };

        var json = await Graph.RunJsonAsync(executor, "{ unlock { ... on AccountLocked { details } } }", Result.Fail<User, AccountLocked>(error));

        Assert.Equal("""{"data":{"unlock":{"details":"AccountLocked: The account is locked. (context: signing in) ---> AccountLocked: The account is locked."}}}""", json);
    }

    private static Task<global::HotChocolate.Execution.IRequestExecutor> Server(
        string environment,
        bool includeDetails = false,
        Action<ResultfulGraphQLOptions>? expose = null) =>
        Graph.BuildAsync(
            b => b.AddQueryType<AccountQuery>().AddMutationType<AccountMutation>().AddMutationConventions(),
            o =>
            {
                o.IncludeDetails = includeDetails;
                expose?.Invoke(o);
            },
            environment);
}
