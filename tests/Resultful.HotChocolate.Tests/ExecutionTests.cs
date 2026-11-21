using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Resultful.HotChocolate.Tests;

// Each field returns the result the test puts in the request's global state "result".
public sealed class StateQuery
{
    public Result<User, UserError> Hierarchy([GlobalState("result")] Result<User, UserError> result) => result;

    public async Task<Result<User, UserError>> HierarchyTask([GlobalState("result")] Result<User, UserError> result)
    {
        await Task.Yield();
        return result;
    }

    public async ValueTask<Result<User, UserError>> HierarchyValueTask([GlobalState("result")] Result<User, UserError> result)
    {
        await Task.Yield();
        return result;
    }

    public FindUserResult Shape([GlobalState("result")] Result<User, UserError> result) => result;

    public async Task<FindUserResult> ShapeTask([GlobalState("result")] Result<User, UserError> result)
    {
        await Task.Yield();
        return result;
    }

    public Result<User> Untyped([GlobalState("result")] Result<User> result) => result;

    public Validation<User, Error> Validated([GlobalState("result")] Validation<User, Error> result) => result;

    public Validation<User> ValidatedUntyped([GlobalState("result")] Validation<User, Error> result) =>
        result.Match(Validation.Ok, errors => Validation.Fail<User>(errors));

    public CheckUsersResult CheckUsers([GlobalState("result")] Validation<User, UserError> result) => result;

    public Result<Order, CheckoutError> Checkout([GlobalState("result")] Result<Order, CheckoutError> result) => result;

    public Result<Order, ShippingError> Ship([GlobalState("result")] Result<Order, ShippingError> result) => result;
}

public sealed class StateMutation
{
    public Result<User, UserError> RenameUser(int id, string name, [GlobalState("result")] Result<User, UserError> result) => result;

    public async Task<Result<User>> RegisterUser(string name, [GlobalState("result")] Result<User> result)
    {
        await Task.Yield();
        return result;
    }
}

public class ExecutionTests
{
    private const string UserSelection = "__typename ... on User { id name } ... on Error { message } ... on UserNotFound { id } ... on UserSuspended { id reason }";
    private const string ValidationSelection = "__typename ... on User { id } ... on ValidationError { message fields { field messages } }";

    private static Task<IRequestExecutor> Server(
        Action<ResultfulGraphQLOptions>? options = null,
        string environment = Graph.Production) =>
        Graph.BuildAsync(
            b => b.AddQueryType<StateQuery>().AddMutationType<StateMutation>().AddMutationConventions(),
            o =>
            {
                // These tests read the leaves' own fields, so they expose them; PersonalDataTests covers the default.
                o.ExposeLeafFields = true;
                o.AddErrorCases<UserError>(UserError.Cases)
                    .AddErrorCases<CheckoutError>(CheckoutError.Cases)
                    .AddErrorCases<ShippingError>(ShippingError.Cases);
                options?.Invoke(o);
            },
            environment);

    private static Result<User, UserError> Ok(int id) => Result.Ok<User, UserError>(new User(id, "Ada"));

    private static Result<User, UserError> Fail(UserError error) => Result.Fail<User, UserError>(error);

    [Fact]
    public async Task Success_is_the_value()
    {
        var executor = await Server();

        var json = await Graph.RunJsonAsync(executor, $"{{ hierarchy {{ {UserSelection} }} }}", Ok(7));

        Assert.Equal("""{"data":{"hierarchy":{"__typename":"User","id":7,"name":"Ada"}}}""", json);
    }

    [Fact]
    public async Task Each_leaf_is_its_own_type_with_message_and_its_own_fields()
    {
        var executor = await Server();

        var notFound = await Graph.RunJsonAsync(executor, $"{{ hierarchy {{ {UserSelection} }} }}", Fail(new UserNotFound(3)));
        var suspended = await Graph.RunJsonAsync(executor, $"{{ hierarchy {{ {UserSelection} }} }}", Fail(new UserSuspended(4, "spam")));

        Assert.Equal("""{"data":{"hierarchy":{"__typename":"UserNotFound","message":"User 3 was not found.","id":3}}}""", notFound);
        Assert.Equal("""{"data":{"hierarchy":{"__typename":"UserSuspended","message":"User 4 is suspended.","id":4,"reason":"spam"}}}""", suspended);
    }

    [Fact]
    public async Task Every_leaf_of_a_seven_leaf_hierarchy_is_selectable()
    {
        var executor = await Server();
        ShippingError[] leaves =
        [
            new AddressMissing(), new PostcodeInvalid("X1"), new CountryBlocked("Atlantis"), new CarrierDown("Owl"),
            new ParcelTooHeavy(40.5m), new ParcelTooLarge(300), new HolidayClosure("Midwinter"),
        ];
        Assert.Equal(ShippingError.Cases.Count, leaves.Length);

        foreach (var leaf in leaves)
        {
            var response = await Graph.RunAsync(executor, "{ ship { __typename ... on Error { message } } }", Result.Fail<Order, ShippingError>(leaf));
            Assert.Equal(leaf.GetType().Name, (string?)response["data"]!["ship"]!["__typename"]);
            Assert.Equal(leaf.Message, (string?)response["data"]!["ship"]!["message"]);
        }
    }

    [Fact]
    public async Task A_nested_hierarchy_shows_its_leaves()
    {
        var executor = await Server();

        var json = await Graph.RunJsonAsync(
            executor,
            "{ checkout { __typename ... on CardExpired { month year } ... on CardDeclined { reason } } }",
            Result.Fail<Order, CheckoutError>(new CardExpired(4, 2031)));

        Assert.Equal("""{"data":{"checkout":{"__typename":"CardExpired","month":4,"year":2031}}}""", json);
    }

    [Theory]
    [InlineData("hierarchyTask")]
    [InlineData("hierarchyValueTask")]
    [InlineData("shape")]
    [InlineData("shapeTask")]
    public async Task Async_and_shape_resolvers_answer_as_the_result_resolver_does(string field)
    {
        var executor = await Server();
        Result<User, UserError>[] results = [Ok(1), Fail(new UserNotFound(2)), Fail(new UserSuspended(3, "late"))];

        foreach (var result in results)
        {
            var expected = await Graph.RunJsonAsync(executor, $"{{ r: hierarchy {{ {UserSelection} }} }}", result);
            var actual = await Graph.RunJsonAsync(executor, $"{{ r: {field} {{ {UserSelection} }} }}", result);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task A_mutation_payload_lists_the_error_and_no_value()
    {
        var executor = await Server();
        const string Mutation = """mutation { renameUser(input: { id: 4, name: "Bo" }) { user { id name } errors { __typename ... on Error { message } ... on UserSuspended { reason } } } }""";

        var failed = await Graph.RunJsonAsync(executor, Mutation, Fail(new UserSuspended(4, "spam")));
        var renamed = await Graph.RunJsonAsync(executor, Mutation, Result.Ok<User, UserError>(new User(4, "Bo")));

        Assert.Equal("""{"data":{"renameUser":{"user":null,"errors":[{"__typename":"UserSuspended","message":"User 4 is suspended.","reason":"spam"}]}}}""", failed);
        Assert.Equal("""{"data":{"renameUser":{"user":{"id":4,"name":"Bo"},"errors":null}}}""", renamed);
    }

    [Fact]
    public async Task A_validation_failure_lists_its_fields_by_type_name()
    {
        var executor = await Server();
        var failed = Result.Fail<User>(new ValidationFailed<Error>([new Required("name"), new TooLong("bio", 3), new Required("email")]));

        var json = await Graph.RunJsonAsync(executor, $"{{ untyped {{ {ValidationSelection} }} }}", failed);

        Assert.Equal(
            """{"data":{"untyped":{"__typename":"ValidationError","message":"Validation failed.","fields":[{"field":"Required","messages":["name is required.","email is required."]},{"field":"TooLong","messages":["bio is longer than 3 characters."]}]}}}""",
            json);
    }

    [Fact]
    public async Task ValidationKey_names_the_fields()
    {
        var executor = await Server(o => o.ValidationKey = e => e switch { Required r => r.Field, TooLong t => t.Field, _ => null! });
        var failed = Result.Fail<User>(new ValidationFailed<Error>([new Required("name"), new TooLong("name", 3), new DatabaseDown("db1")]));

        var json = await Graph.RunJsonAsync(executor, "{ untyped { ... on ValidationError { fields { field messages } } } }", failed);

        Assert.Equal(
            """{"data":{"untyped":{"fields":[{"field":"name","messages":["name is required.","name is longer than 3 characters."]},{"field":"","messages":["The database is down."]}]}}}""",
            json);
    }

    [Fact]
    public async Task A_failed_validation_is_a_ValidationError_and_a_passed_one_its_value()
    {
        var executor = await Server();

        var failed = await Graph.RunJsonAsync(executor, $"{{ validated {{ {ValidationSelection} }} }}", Validation.Fail<User, Error>([new Required("name")]));
        var passed = await Graph.RunJsonAsync(executor, $"{{ validated {{ {ValidationSelection} }} }}", Validation.Ok<User, Error>(new User(5, "Cy")));

        Assert.Equal("""{"data":{"validated":{"__typename":"ValidationError","message":"Validation failed.","fields":[{"field":"Required","messages":["name is required."]}]}}}""", failed);
        Assert.Equal("""{"data":{"validated":{"__typename":"User","id":5}}}""", passed);
    }

    [Fact]
    public async Task A_Validation_of_Error_and_a_gathering_shape_answer_as_a_Validation_does()
    {
        var executor = await Server();
        Validation<User, Error>[] results = [Validation.Ok<User, Error>(new User(5, "Cy")), Validation.Fail<User, Error>([new Required("name"), new UserNotFound(9)])];

        foreach (var result in results)
        {
            var expected = await Graph.RunJsonAsync(executor, $"{{ r: validated {{ {ValidationSelection} }} }}", result);
            Assert.Equal(expected, await Graph.RunJsonAsync(executor, $"{{ r: validatedUntyped {{ {ValidationSelection} }} }}", result));
        }

        var failed = await Graph.RunJsonAsync(
            executor,
            $"{{ checkUsers {{ {ValidationSelection} }} }}",
            Validation.Fail<User, UserError>([new UserNotFound(1), new UserSuspended(2, "spam"), new UserNotFound(3)]));
        var passed = await Graph.RunJsonAsync(executor, $"{{ checkUsers {{ {ValidationSelection} }} }}", Validation.Ok<User, UserError>(new User(4, "Di")));

        Assert.Equal("""{"data":{"checkUsers":{"__typename":"ValidationError","message":"Validation failed.","fields":[{"field":"UserNotFound","messages":["User 1 was not found.","User 3 was not found."]},{"field":"UserSuspended","messages":["User 2 is suspended."]}]}}}""", failed);
        Assert.Equal("""{"data":{"checkUsers":{"__typename":"User","id":4}}}""", passed);
    }

    [Fact]
    public async Task A_mutation_validation_failure_is_in_the_payload_errors()
    {
        var executor = await Server();

        var json = await Graph.RunJsonAsync(
            executor,
            """mutation { registerUser(input: { name: "" }) { user { id } errors { __typename ... on ValidationError { fields { field } } } } }""",
            Result.Fail<User>(new ValidationFailed<Required>([new Required("name")])));

        Assert.Equal("""{"data":{"registerUser":{"user":null,"errors":[{"__typename":"ValidationError","fields":[{"field":"Required"}]}]}}}""", json);
    }

    [Theory]
    [InlineData(Graph.Production)]
    [InlineData(Graph.Development)]
    public async Task An_undeclared_error_is_a_top_level_error_with_its_code(string environment)
    {
        var executor = await Graph.BuildAsync(
            b => b.AddQueryType<StateQuery>(),
            o => o.ExposeFieldsOf<UserNotFound>().AddErrorCases<UserError>([typeof(UserNotFound)]).AddErrorCases<CheckoutError>(CheckoutError.Cases).AddErrorCases<ShippingError>(ShippingError.Cases),
            environment);
        var error = new UserSuspended(4, "spam").WithContext("loading the profile");

        var response = await Graph.RunAsync(executor, "{ hierarchy { __typename ... on UserNotFound { id } } }", Fail(error));

        var entry = Assert.Single(response["errors"]!.AsArray())!;
        var expectedMessage = environment == Graph.Development
            ? "UserSuspended: User 4 is suspended. (context: loading the profile)"
            : "An error occurred while processing your request.";
        Assert.Equal(expectedMessage, (string?)entry["message"]);
        Assert.Equal("""{"code":"UserSuspended"}""", entry["extensions"]!.ToJsonString());
        Assert.Equal("""["hierarchy"]""", entry["path"]!.ToJsonString());
        Assert.Null(response["data"]);
    }

    [Fact]
    public async Task No_host_environment_counts_as_Production()
    {
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType<StateQuery>()
            .AddResultfulErrors(o => o.AddErrorCases<UserError>(UserError.Cases).AddErrorCases<CheckoutError>(CheckoutError.Cases).AddErrorCases<ShippingError>(ShippingError.Cases))
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        var response = await Graph.RunAsync(executor, "{ untyped { __typename } }", Result.Fail<User>(new DatabaseDown("db1")));

        Assert.Equal("An error occurred while processing your request.", (string?)response["errors"]![0]!["message"]);
        Assert.Equal("DatabaseDown", (string?)response["errors"]![0]!["extensions"]!["code"]);
    }

    [Fact]
    public async Task A_validation_failure_under_another_error_stays_hidden()
    {
        var executor = await Server();
        var error = new DatabaseDown("db1") { Cause = new ValidationFailed<Error>([new Required("name")]) };

        var response = await Graph.RunAsync(executor, "{ untyped { __typename } }", Result.Fail<User>(error));

        Assert.Equal("""[{"message":"An error occurred while processing your request.","path":["untyped"],"extensions":{"code":"DatabaseDown"}}]""", response["errors"]!.ToJsonString());
    }

    [Fact]
    public async Task An_undeclared_mutation_error_is_a_top_level_error()
    {
        var executor = await Server();

        var response = await Graph.RunAsync(executor, """mutation { registerUser(input: { name: "x" }) { user { id } } }""", Result.Fail<User>(new DatabaseDown("db1")));

        Assert.Equal("DatabaseDown", (string?)response["errors"]![0]!["extensions"]!["code"]);
        Assert.Equal("An error occurred while processing your request.", (string?)response["errors"]![0]!["message"]);
    }

    [Fact]
    public async Task The_zero_value_is_an_error_not_a_value()
    {
        var executor = await Server();

        var response = await Graph.RunAsync(executor, $"{{ hierarchy {{ {UserSelection} }} }}", default(Result<User, UserError>));

        Assert.Single(response["errors"]!.AsArray());
        Assert.Null(response["data"]);
    }

    [Fact]
    public async Task Only_message_and_the_leaf_s_own_fields_are_shown_by_default()
    {
        var sdl = Graph.Sdl(await Server());

        foreach (var hidden in new[] { "context:", "cause:", "causes:", "inner:", "details:" }) Assert.DoesNotContain(hidden, sdl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncludeDetails_adds_details_as_Error_ToString()
    {
        var executor = await Server(o => o.IncludeDetails = true);
        var error = new UserSuspended(4, "spam").WithContext("loading") with { Cause = new DatabaseDown("db1") };

        var response = await Graph.RunAsync(executor, "{ hierarchy { ... on UserSuspended { details } } }", Fail(error));

        Assert.Contains("type UserSuspended implements Error {\n  id: Int!\n  reason: String!\n  \"What went wrong, for people to read.\"\n  message: String!\n  \"The error's type, message, context and cause chain, as Error.ToString() writes them.\"\n  details: String!\n}", Graph.Sdl(executor), StringComparison.Ordinal);
        Assert.Equal(error.ToString(), (string?)response["data"]!["hierarchy"]!["details"]);
        Assert.Equal("UserSuspended: User 4 is suspended. (context: loading) ---> DatabaseDown: The database is down.", error.ToString());
    }

    [Fact]
    public async Task Calling_AddResultfulErrors_twice_adds_to_the_options()
    {
        var services = new ServiceCollection();
        var executor = await services.AddGraphQL()
            .AddQueryType<StateQuery>()
            .AddResultfulErrors(o => o.AddErrorCases<UserError>(UserError.Cases))
            .AddResultfulErrors(o => o.AddErrorCases<CheckoutError>(CheckoutError.Cases).AddErrorCases<ShippingError>(ShippingError.Cases))
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        var response = await Graph.RunAsync(executor, "{ checkout { __typename } }", Result.Fail<Order, CheckoutError>(new CartEmpty()));

        Assert.Equal("CartEmpty", (string?)response["data"]!["checkout"]!["__typename"]);
        Assert.Contains("union HierarchyResult = User | UserNotFound | UserSuspended", Graph.Sdl(executor), StringComparison.Ordinal);
    }

    [Fact]
    public void AddResultfulErrors_rejects_a_null_builder() =>
        Assert.Throws<ArgumentNullException>("builder", () => ResultfulRequestExecutorBuilderExtensions.AddResultfulErrors(null!));
}
