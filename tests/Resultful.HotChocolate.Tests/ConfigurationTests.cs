using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Resultful.HotChocolate.Tests;

public sealed class NotAnErrorQuery
{
    public Result<User, string> UserById(int id) => new User(id, "Ada");
}

public sealed class RenameOnlyMutation
{
    public Result<User, UserNotFound> RenameUser(int id) => new User(id, "Ada");
}

public class ConfigurationTests
{
    [Fact]
    public async Task A_hierarchy_without_registered_cases_fails_the_schema_and_says_how_to_fix_it()
    {
        var thrown = await Assert.ThrowsAsync<SchemaException>(() => Graph.BuildAsync(b => b.AddQueryType<HierarchyQuery>()));

        Assert.Equal(
            "The field HierarchyQuery.userById returns a result, but the cases of UserError aren't registered: call AddErrorCases<UserError>(UserError.Cases) in AddResultfulErrors.",
            Assert.Single(thrown.Errors).Message);
    }

    [Fact]
    public async Task An_error_type_that_is_not_an_Error_fails_the_schema()
    {
        var thrown = await Assert.ThrowsAsync<SchemaException>(() => Graph.BuildAsync(b => b.AddQueryType<NotAnErrorQuery>()));

        Assert.Equal(
            "The field NotAnErrorQuery.userById returns a result, but String isn't an Error or an error union, so it has no GraphQL error types.",
            Assert.Single(thrown.Errors).Message);
    }

    [Fact]
    public async Task A_mutation_returning_a_result_needs_the_mutation_conventions()
    {
        var thrown = await Assert.ThrowsAsync<SchemaException>(() => Graph.BuildAsync(b => b.AddQueryType<LeafQuery>().AddMutationType<RenameOnlyMutation>()));

        Assert.Equal(
            "The mutation RenameOnlyMutation.renameUser returns a result, whose error cases are shown by the mutation conventions: call AddMutationConventions().",
            Assert.Single(thrown.Errors).Message);
    }

    [Fact]
    public async Task Mutation_conventions_off_for_other_mutations_still_apply_to_a_result()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<LeafQuery>().AddMutationType<RenameOnlyMutation>().AddMutationConventions(applyToAllMutations: false));

        var response = await Graph.RunJsonAsync(executor, "mutation { renameUser(input: { id: 9 }) { user { id } errors { __typename } } }");

        Assert.Equal("""{"data":{"renameUser":{"user":{"id":9},"errors":null}}}""", response);
    }

    [Fact]
    public void Registering_cases_checks_each_one()
    {
        var options = new ResultfulGraphQLOptions();

        Assert.Throws<ArgumentNullException>("cases", () => options.AddErrorCases<UserError>(null!));
        AssertRejects(() => options.AddErrorCases<UserError>([null!]), "The cases of UserError contain null.");
        AssertRejects(() => options.AddErrorCases<Error>([typeof(UserError)]), "UserError isn't a concrete Error, so it can't be a case of Error.");
        AssertRejects(() => options.AddErrorCases<Error>([typeof(ValidationFailed<>)]), "ValidationFailed`1 isn't a concrete Error, so it can't be a case of Error.");
        AssertRejects(() => options.AddErrorCases<Error>([typeof(string)]), "String isn't a concrete Error, so it can't be a case of Error.");
        AssertRejects(() => options.AddErrorCases<UserError>([typeof(CartEmpty)]), "CartEmpty isn't a UserError, so it can't be one of its cases.");
    }

    [Fact]
    public async Task Registering_the_same_type_again_adds_to_its_cases_once()
    {
        var executor = await Graph.BuildAsync(
            b => b.AddQueryType<HierarchyQuery>(),
            o => o.AddErrorCases<UserError>([typeof(UserNotFound), typeof(UserNotFound)]).AddErrorCases<UserError>([typeof(UserSuspended), typeof(UserNotFound)]));

        Assert.Contains("union UserByIdResult = User | UserNotFound | UserSuspended\n", Graph.Sdl(executor) + "\n", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cases_registered_for_Error_are_declared_with_ValidationError()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<ValidationQuery>(), o => o.AddErrorCases<Error>([typeof(DatabaseDown)]));

        Assert.Contains("union RegisterResult = User | DatabaseDown | ValidationError\n", Graph.Sdl(executor), StringComparison.Ordinal);
        Assert.Contains("union ValidateResult = User | ValidationError\n", Graph.Sdl(executor) + "\n", StringComparison.Ordinal);
    }

    [Fact]
    public void The_defaults_name_validation_fields_by_type_and_hide_details()
    {
        var options = new ResultfulGraphQLOptions();

        Assert.False(options.IncludeDetails);
        Assert.Equal("Required", options.ValidationKey(new Required("name")));
        Assert.Equal("ValidationFailed`1", options.ValidationKey(new ValidationFailed<Required>([new Required("name")])));
        Assert.Throws<ArgumentNullException>("value", () => options.ValidationKey = null!);
        Func<Error, string> key = e => e.Message;
        options.ValidationKey = key;
        Assert.Same(key, options.ValidationKey);
    }

    [Fact]
    public async Task Two_named_schemas_each_get_their_own_options()
    {
        var services = new ServiceCollection();
        services.AddGraphQL("users").AddQueryType<HierarchyQuery>().AddResultfulErrors(o => o.AddErrorCases<UserError>(UserError.Cases));
        services.AddGraphQL("shipping").AddQueryType<ShippingQuery>().AddResultfulErrors(o => o.AddErrorCases<ShippingError>(ShippingError.Cases));
        var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IRequestExecutorProvider>();

        var users = await resolver.GetExecutorAsync("users", TestContext.Current.CancellationToken);
        var shipping = await resolver.GetExecutorAsync("shipping", TestContext.Current.CancellationToken);

        Assert.Contains("union UserByIdResult = User | UserNotFound | UserSuspended", Graph.Sdl(users), StringComparison.Ordinal);
        Assert.Contains("| HolidayClosure", Graph.Sdl(shipping), StringComparison.Ordinal);
        Assert.DoesNotContain("UserNotFound", Graph.Sdl(shipping), StringComparison.Ordinal);
    }

    private static void AssertRejects(Action register, string message)
    {
        var thrown = Assert.Throws<ArgumentException>("cases", register);
        Assert.StartsWith(message, thrown.Message, StringComparison.Ordinal);
    }
}
