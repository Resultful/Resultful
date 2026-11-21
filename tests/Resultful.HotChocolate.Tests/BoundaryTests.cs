using GreenDonut;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Resultful.HotChocolate.Tests;

// Where a result meets the edges of GraphQL: success types a union can't hold, Unit, nullability, name collisions,
// error types that would show an abstract Error, [Error<T>], untyped resolvers, logging, subscriptions, DataLoaders.

public sealed class PlainQuery
{
    public int Ping => 1;
}

public sealed class UnitMutation
{
    public Result<Unit, UserNotFound> DeleteUser(int id, [GlobalState("result")] Result<Unit, UserNotFound> result) => result;
}

public sealed class UnitQuery
{
    public Result<Unit, UserNotFound> Touch() => Unit.Value;
}

public sealed class ListResultQuery
{
    public Result<List<User>, UserNotFound> Users() => new List<User>();
}

public sealed class ScalarResultQuery
{
    public Result<int, UserNotFound> Count() => 1;
}

public enum Colour
{
    Red,
    Blue,
}

public sealed class EnumResultQuery
{
    public Result<Colour, UserNotFound> Favourite() => Colour.Red;
}

public sealed class ListOfResultsQuery
{
    public List<Result<User, UserNotFound>> Many() => [new User(1, "Ada")];
}

public sealed class ExplicitTypeQuery
{
    [GraphQLType<NonNullType<IdType>>]
    public Result<string, UserNotFound> Key() => "k";
}

public sealed class PagedQuery
{
    [UsePaging]
    public Result<IQueryable<User>, UserNotFound> Users() => Result.Ok<IQueryable<User>, UserNotFound>(new[] { new User(1, "Ada") }.AsQueryable());
}

public sealed class ProjectedQuery
{
    [global::HotChocolate.Data.UseProjection]
    public Result<IQueryable<User>, UserNotFound> Users() => Result.Ok<IQueryable<User>, UserNotFound>(new[] { new User(1, "Ada") }.AsQueryable());
}

public sealed class NullableQuery
{
    public Result<User?, UserNotFound> Maybe([GlobalState("result")] Result<User?, UserNotFound> result) => result;
}

public sealed class ListMutation
{
    public Result<List<string>, UserNotFound> Tags([GlobalState("result")] Result<List<string>, UserNotFound> result) => result;

    public Result<List<string?>, UserNotFound> Notes() => new List<string?> { "a", null };

    public Result<int, UserNotFound> Count() => 3;
}

public sealed record Wrapped(Error Underlying) : Error("Something underneath failed.");

public sealed record Aggregate(IReadOnlyList<Error> Errors) : Error("Several things failed.")
{
    public override IEnumerable<Error> Inner() => Errors;
}

public sealed class WrappedQuery
{
    public Result<User, Wrapped> One([GlobalState("result")] Result<User, Wrapped> result) => result;

    public Result<User, Aggregate> Many([GlobalState("result")] Result<User, Aggregate> result) => result;
}

public static class Left
{
    public sealed record NotFound(int Id) : Error("Left not found.");
}

public static class Right
{
    public sealed record NotFound(string Key) : Error("Right not found.");
}

public sealed class CollisionQuery
{
    public Result<User, Left.NotFound> Left() => new User(1, "a");

    public Result<User, Right.NotFound> Right() => new User(2, "b");
}

public sealed record ValidationError(string Field) : Error("The app's own validation error.");

public sealed class OwnValidationErrorQuery
{
    public Result<User> Register() => new User(1, "a");

    public Result<User, ValidationError> Check() => new User(1, "a");
}

[GraphQLName("Error")]
public sealed record Misnamed() : Error("Named like the interface.");

public sealed class MisnamedQuery
{
    public Result<User, Misnamed> One() => new User(1, "a");
}

public sealed class AttributedQuery
{
    [Error<DatabaseDown>]
    public Result<User, UserNotFound> User() => new User(1, "Ada");

    [Error<DatabaseDown>]
    public Result<User> Any([GlobalState("result")] Result<User> result) => result;
}

[ExtendObjectType<User>]
public sealed class UserExtension
{
    public Result<Order, OutOfStock> LastOrder([Parent] User user) => user.Id == 1 ? new Order(1, "S", 1) : new OutOfStock("S");
}

public sealed class MeQuery
{
    public User Me(int id) => new(id, "Ada");
}

public sealed class Subscription
{
    [Subscribe(With = nameof(Stream))]
    public Result<User, UserNotFound> UserChanged([EventMessage] int id) => id == 1 ? new User(1, "Ada") : new UserNotFound(id);

    public async IAsyncEnumerable<int> Stream()
    {
        await Task.Yield();
        yield return 1;
        yield return 2;
    }
}

public sealed class UserLoader(IBatchScheduler scheduler, DataLoaderOptions options) : BatchDataLoader<int, User>(scheduler, options)
{
    private static int _batches;

    public static int Batches => Volatile.Read(ref _batches);

    protected override Task<IReadOnlyDictionary<int, User>> LoadBatchAsync(IReadOnlyList<int> keys, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _batches);
        return Task.FromResult<IReadOnlyDictionary<int, User>>(keys.Where(k => k < 100).ToDictionary(k => k, k => new User(k, $"user {k}")));
    }
}

public sealed class LoaderQuery
{
    public async Task<Result<User, UserNotFound>> UserById(int id, UserLoader loader, CancellationToken cancellationToken) =>
        await loader.LoadAsync(id, cancellationToken) is { } user ? user : new UserNotFound(id);
}

public class BoundaryTests
{
    [Fact]
    public async Task A_Unit_mutation_answers_a_nullable_Boolean()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<PlainQuery>().AddMutationType<UnitMutation>().AddMutationConventions());
        const string Mutation = "mutation { deleteUser(input: { id: 1 }) { boolean errors { __typename } } }";

        Assert.Contains("type DeleteUserPayload {\n  boolean: Boolean\n  errors: [DeleteUserError!]\n}", Graph.Sdl(executor), StringComparison.Ordinal);
        Assert.Equal("""{"data":{"deleteUser":{"boolean":true,"errors":null}}}""", await Graph.RunJsonAsync(executor, Mutation, Result.Ok<Unit, UserNotFound>(Unit.Value)));
        Assert.Equal(
            """{"data":{"deleteUser":{"boolean":null,"errors":[{"__typename":"UserNotFound"}]}}}""",
            await Graph.RunJsonAsync(executor, Mutation, Result.Fail<Unit, UserNotFound>(new UserNotFound(1))));
    }

    [Fact]
    public Task A_Unit_query_field_fails_the_schema() =>
        AssertSchemaError<UnitQuery>("The field UnitQuery.touch returns Result<Unit, …>, which has no value to show. A query field needs a value; a mutation with the mutation conventions answers a nullable Boolean.");

    [Fact]
    public Task A_list_success_on_a_query_field_fails_the_schema() =>
        AssertSchemaError<ListResultQuery>("The field ListResultQuery.users returns a result whose success type is [User!]!, but a GraphQL union holds only object types. Wrap the value in an object type, such as a payload with a field for it.");

    [Fact]
    public Task A_scalar_success_on_a_query_field_fails_the_schema() =>
        AssertSchemaError<ScalarResultQuery>("The field ScalarResultQuery.count returns a result whose success type is Int!, but a GraphQL union holds only object types. Wrap the value in an object type, such as a payload with a field for it.");

    [Fact]
    public Task An_enum_success_on_a_query_field_fails_the_schema() =>
        AssertSchemaError<EnumResultQuery>("The field EnumResultQuery.favourite returns a result whose success type is Colour!, but a GraphQL union holds only object types. Wrap the value in an object type, such as a payload with a field for it.");

    [Fact]
    public Task A_list_of_results_fails_the_schema() =>
        AssertSchemaError<ListOfResultsQuery>("The field ListOfResultsQuery.many returns a list of results. A GraphQL union can't be a list item's own outcome: return one result whose value is the list, or put each result on an object type's field.");

    [Fact]
    public Task An_explicit_GraphQL_type_fails_the_schema() =>
        AssertSchemaError<ExplicitTypeQuery>("The field ExplicitTypeQuery.key returns a result but declares its own GraphQL type. Remove the explicit type: the success type and the error cases decide the field's type.");

    [Fact]
    public async Task Projection_on_a_result_fails_the_schema()
    {
        var thrown = await Assert.ThrowsAsync<SchemaException>(() => Graph.BuildAsync(b => b.AddQueryType<ProjectedQuery>().AddProjections()));

        Assert.Contains(
            "The field ProjectedQuery.users returns a result and uses HotChocolate.Data.Projection. Paging, projection, filtering and sorting can't wrap a result: put them on a field that returns the list itself.",
            thrown.Errors.Select(e => e.Message));
    }

    // [UsePaging] reads the element type while the field is described, before this package sees the field, so the
    // schema fails with HotChocolate's own error.
    [Fact]
    public async Task Paging_on_a_result_fails_the_schema()
    {
        var thrown = await Assert.ThrowsAsync<SchemaException>(() => Graph.BuildAsync(b => b.AddQueryType<PagedQuery>()));

        Assert.Contains("Unable to infer the element type from the current resolver", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_nullable_success_makes_the_union_nullable()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<NullableQuery>());

        Assert.Contains("  maybe: MaybeResult\n", Graph.Sdl(executor), StringComparison.Ordinal);
        Assert.Equal("""{"data":{"maybe":null}}""", await Graph.RunJsonAsync(executor, "{ maybe { __typename } }", Result.Ok<User?, UserNotFound>(null)));
        Assert.Equal("""{"data":{"maybe":{"__typename":"User"}}}""", await Graph.RunJsonAsync(executor, "{ maybe { __typename } }", Result.Ok<User?, UserNotFound>(new User(1, "Ada"))));
        Assert.Equal("""{"data":{"maybe":{"__typename":"UserNotFound"}}}""", await Graph.RunJsonAsync(executor, "{ maybe { __typename } }", Result.Fail<User?, UserNotFound>(new UserNotFound(1))));
    }

    [Fact]
    public async Task A_mutation_keeps_a_list_or_scalar_success_with_its_nullability()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<PlainQuery>().AddMutationType<ListMutation>().AddMutationConventions());
        var sdl = Graph.Sdl(executor);

        Assert.Contains("type TagsPayload {\n  string: [String!]\n  errors: [TagsError!]\n}", sdl, StringComparison.Ordinal);
        Assert.Contains("type NotesPayload {\n  string: [String]\n  errors: [NotesError!]\n}", sdl, StringComparison.Ordinal);
        Assert.Contains("type CountPayload {\n  int: Int\n  errors: [CountError!]\n}", sdl, StringComparison.Ordinal);
        Assert.Equal(
            """{"data":{"tags":{"string":["a","b"],"errors":null}}}""",
            await Graph.RunJsonAsync(executor, "mutation { tags { string errors { __typename } } }", Result.Ok<List<string>, UserNotFound>(["a", "b"])));
        Assert.Equal(
            """{"data":{"tags":{"string":null,"errors":[{"__typename":"UserNotFound"}]}}}""",
            await Graph.RunJsonAsync(executor, "mutation { tags { string errors { __typename } } }", Result.Fail<List<string>, UserNotFound>(new UserNotFound(1))));
        Assert.Equal("""{"data":{"notes":{"string":["a",null]}}}""", await Graph.RunJsonAsync(executor, "mutation { notes { string } }"));
    }

    [Fact]
    public async Task An_error_type_holding_errors_shows_message_only_by_default()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<WrappedQuery>());
        var sdl = Graph.Sdl(executor);

        Assert.Contains("type Wrapped implements Error {\n  \"What went wrong, for people to read.\"\n  message: String!\n}", sdl, StringComparison.Ordinal);
        Assert.Contains("type Aggregate implements Error {\n  \"What went wrong, for people to read.\"\n  message: String!\n}", sdl, StringComparison.Ordinal);
        Assert.Equal(
            """{"data":{"many":{"__typename":"Aggregate","message":"Several things failed."}}}""",
            await Graph.RunJsonAsync(executor, "{ many { __typename ... on Error { message } } }", Result.Fail<User, Aggregate>(new Aggregate([new DatabaseDown("db1")]))));
    }

    [Fact]
    public async Task Exposing_an_error_type_that_shows_an_abstract_Error_fails_the_schema()
    {
        var thrown = await Assert.ThrowsAsync<SchemaException>(() => Graph.BuildAsync(b => b.AddQueryType<WrappedQuery>(), o => o.ExposeLeafFields = true));

        Assert.Equal(
            [
                "The error type Wrapped shows Underlying, typed as Error, which GraphQL can't show as a type. Hide it with [GraphQLIgnore], or don't expose Wrapped's fields.",
                "The error type Aggregate shows Errors, typed as Error, which GraphQL can't show as a type. Hide it with [GraphQLIgnore], or don't expose Aggregate's fields.",
            ],
            thrown.Errors.Select(e => e.Message).Order(StringComparer.Ordinal).Reverse());
    }

    [Fact]
    public async Task Two_leaves_with_one_GraphQL_name_fail_the_schema_naming_both()
    {
        var thrown = await Assert.ThrowsAsync<SchemaException>(() => Graph.BuildAsync(b => b.AddQueryType<CollisionQuery>()));

        Assert.Equal(
            "The error types Resultful.HotChocolate.Tests.Left+NotFound and Resultful.HotChocolate.Tests.Right+NotFound are both named NotFound in GraphQL. Rename one with [GraphQLName].",
            Assert.Single(thrown.Errors).Message);
    }

    [Fact]
    public async Task A_leaf_named_ValidationError_collides_until_ValidationErrorTypeName_moves_ours()
    {
        var thrown = await Assert.ThrowsAsync<SchemaException>(() => Graph.BuildAsync(b => b.AddQueryType<OwnValidationErrorQuery>()));
        var renamed = await Graph.BuildAsync(b => b.AddQueryType<OwnValidationErrorQuery>(), o => o.ValidationErrorTypeName = "InputError");

        Assert.Equal(
            "The error types Resultful.HotChocolate.ResultfulValidationError and Resultful.HotChocolate.Tests.ValidationError are both named ValidationError in GraphQL. Rename the app's error with [GraphQLName], or set ValidationErrorTypeName.",
            Assert.Single(thrown.Errors).Message);
        Assert.Contains("type InputError implements Error {", Graph.Sdl(renamed), StringComparison.Ordinal);
        Assert.Contains("union RegisterResult = User | InputError", Graph.Sdl(renamed), StringComparison.Ordinal);
        Assert.Equal(
            """{"data":{"register":{"__typename":"User"}}}""",
            await Graph.RunJsonAsync(renamed, "{ register { __typename ... on InputError { fields { field } } } }"));
    }

    [Fact]
    public Task A_leaf_named_Error_fails_the_schema() =>
        AssertSchemaError<MisnamedQuery>("The error type Resultful.HotChocolate.Tests.Misnamed is named Error, which the conventions' Error interface already uses. Rename it with [GraphQLName].");

    [Fact]
    public void ValidationErrorTypeName_rejects_an_empty_name()
    {
        var options = new ResultfulGraphQLOptions();

        Assert.Equal("ValidationError", options.ValidationErrorTypeName);
        Assert.Throws<ArgumentException>("value", () => options.ValidationErrorTypeName = "");
        Assert.Throws<ArgumentException>("value", () => options.ValidationErrorTypeName = null!);
    }

    [Fact]
    public async Task An_Error_attribute_on_a_result_field_adds_a_typed_error()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<AttributedQuery>());
        var sdl = Graph.Sdl(executor);

        var down = await Graph.RunJsonAsync(executor, "{ any { __typename ... on Error { message } } }", Result.Fail<User>(new DatabaseDown("db1")));

        Assert.Contains("union AnyResult = User | DatabaseDown | ValidationError", sdl, StringComparison.Ordinal);
        Assert.Contains("union UserResult = User | DatabaseDown | UserNotFound", sdl, StringComparison.Ordinal);
        Assert.Equal("""{"data":{"any":{"__typename":"DatabaseDown","message":"The database is down."}}}""", down);
    }

    [Fact]
    public async Task A_type_extension_and_a_nested_field_return_results()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<MeQuery>().AddTypeExtension<UserExtension>(), o => o.ExposeFieldsOf<OutOfStock>());

        var found = await Graph.RunJsonAsync(executor, "{ me(id: 1) { lastOrder { __typename ... on Order { id } } } }");
        var missing = await Graph.RunJsonAsync(executor, "{ me(id: 2) { lastOrder { __typename ... on OutOfStock { sku } } } }");

        Assert.Equal("""{"data":{"me":{"lastOrder":{"__typename":"Order","id":1}}}}""", found);
        Assert.Equal("""{"data":{"me":{"lastOrder":{"__typename":"OutOfStock","sku":"S"}}}}""", missing);
    }

    [Theory]
    [InlineData(Graph.Production, "A resolver returned a result to a field that doesn't declare one: return it from a typed resolver.")]
    [InlineData(Graph.Development, "The resolver parent type of field `User.id` is `Resultful.Result`2[")]
    public async Task An_untyped_resolver_s_result_is_not_seen_and_its_CLR_types_stay_hidden(string environment, string message)
    {
        var executor = await Graph.BuildAsync(
            b => b.AddQueryType(d =>
            {
                d.Name("Query");
                d.Field("user").Type<ObjectType<User>>().Resolve(_ => (object)Result.Fail<User, AccountLocked>(new AccountLocked(Sentinel.Email)));
            }),
            environment: environment);

        var response = await Graph.RunAsync(executor, "{ user { id } }");

        var text = (string?)response["errors"]![0]!["message"];
        Assert.StartsWith(message, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel.Email, response.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_undeclared_failure_is_logged_once_at_Error_and_a_declared_one_is_not()
    {
        var log = new ListLogger();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(log);
        var executor = await services.AddGraphQL()
            .AddQueryType<StateQuery>()
            .AddResultfulErrors(o => o.AddErrorCases<UserError>(UserError.Cases).AddErrorCases<CheckoutError>(CheckoutError.Cases))
            .AddResultfulErrors(o => o.AddErrorCases<ShippingError>(ShippingError.Cases))
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        var error = new DatabaseDown("db1").WithContext("saving") with { Cause = new AccountLocked(Sentinel.Email) };

        await Graph.RunAsync(executor, "{ hierarchy { __typename } }", Result.Fail<User, UserError>(new UserNotFound(1)));
        await Graph.RunAsync(executor, "{ untyped { __typename } }", Result.Fail<User>(new ValidationFailed<Error>([new Required("name")])));
        Assert.Empty(log.Entries);

        await Graph.RunAsync(executor, "{ untyped { __typename } }", Result.Fail<User>(error));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Resultful.HotChocolate.ResultfulErrorFilter", entry.Category);
        Assert.Equal($"A result failed with an error its field doesn't declare (DatabaseDown): {error}", entry.Message);
        Assert.DoesNotContain(Sentinel.Email, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_subscription_returns_results()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<PlainQuery>().AddSubscriptionType<Subscription>());

        var result = await executor.ExecuteAsync("subscription { userChanged { __typename ... on User { name } ... on Error { message } } }", TestContext.Current.CancellationToken);
        var events = new List<string>();
        await foreach (var item in result.ExpectResponseStream().ReadResultsAsync().WithCancellation(TestContext.Current.CancellationToken))
        {
            events.Add(System.Text.Json.Nodes.JsonNode.Parse(item.ToJson())!.ToJsonString());
        }

        Assert.Contains("union UserChangedResult = User | UserNotFound", Graph.Sdl(executor), StringComparison.Ordinal);
        Assert.Equal(
            [
                """{"data":{"userChanged":{"__typename":"User","name":"Ada"}}}""",
                """{"data":{"userChanged":{"__typename":"UserNotFound","message":"User 2 was not found."}}}""",
            ],
            events);
    }

    [Fact]
    public async Task A_DataLoader_backed_resolver_batches_and_returns_results()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<LoaderQuery>().AddDataLoader<UserLoader>());
        var before = UserLoader.Batches;

        var json = await Graph.RunJsonAsync(
            executor,
            "{ a: userById(id: 1) { __typename ... on User { name } } b: userById(id: 2) { __typename ... on User { name } } c: userById(id: 404) { __typename ... on Error { message } } }");

        Assert.Equal(
            """{"data":{"a":{"__typename":"User","name":"user 1"},"b":{"__typename":"User","name":"user 2"},"c":{"__typename":"UserNotFound","message":"User 404 was not found."}}}""",
            json);
        Assert.Equal(before + 1, UserLoader.Batches);
    }

    private static async Task AssertSchemaError<TQuery>(string message)
        where TQuery : class
    {
        var thrown = await Assert.ThrowsAsync<SchemaException>(() => Graph.BuildAsync(b => b.AddQueryType<TQuery>()));
        Assert.Contains(message, thrown.Errors.Select(e => e.Message));
    }

    private sealed class ListLogger : ILoggerFactory
    {
        public List<(string Category, LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class Logger(ListLogger owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (category.StartsWith("Resultful", StringComparison.Ordinal))
                {
                    lock (owner.Entries) owner.Entries.Add((category, logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
