using Microsoft.Extensions.DependencyInjection;

namespace Resultful.HotChocolate.Tests;

public sealed class LeafQuery
{
    public Result<User, UserNotFound> UserById(int id) => id == 1 ? new User(1, "Ada") : new UserNotFound(id);
}

public sealed class HierarchyQuery
{
    public Result<User, UserError> UserById(int id) => new User(id, "Ada");
}

public sealed class ShapeQuery
{
    public FindUserResult FindUser(int id) => new User(id, "Ada");
}

public sealed class NestedQuery
{
    public Result<Order, CheckoutError> Checkout(int cart) => new Order(cart, "SKU", 1);
}

public sealed class ValidationQuery
{
    public Result<User> Register(string name) => new User(1, name);

    public Validation<User, Error> Validate(string name) => new User(1, name);
}

public sealed class ShippingQuery
{
    public Result<Order, ShippingError> Ship(int order) => new Order(order, "SKU", 1);
}

public sealed class MutationRoot
{
    public Result<User, UserError> RenameUser(int id, string name) => new User(id, name);
}

public class SchemaTests
{
    // The decision's own example: FieldResult<User, UserNotFound> under AddQueryConventions. An error type shows
    // message only by default.
    [Fact]
    public async Task Query_conventions_make_the_field_a_union_of_the_value_and_the_error()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<LeafQuery>());

        Assert.Equal(
            """
            schema {
              query: LeafQuery
            }

            type LeafQuery {
              userById(id: Int!): UserByIdResult!
            }

            type User {
              id: Int!
              name: String!
            }

            type UserNotFound implements Error {
              "What went wrong, for people to read."
              message: String!
            }

            interface Error {
              message: String!
            }

            union UserByIdResult = User | UserNotFound
            """,
            Graph.Sdl(executor));
    }

    [Fact]
    public async Task A_closed_hierarchy_declares_one_error_type_per_case()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<HierarchyQuery>(), o => o.AddErrorCases<UserError>(UserError.Cases));

        Assert.Equal(
            """
            schema {
              query: HierarchyQuery
            }

            type HierarchyQuery {
              userById(id: Int!): UserByIdResult!
            }

            type User {
              id: Int!
              name: String!
            }

            type UserNotFound implements Error {
              "What went wrong, for people to read."
              message: String!
            }

            type UserSuspended implements Error {
              "What went wrong, for people to read."
              message: String!
            }

            interface Error {
              message: String!
            }

            union UserByIdResult = User | UserNotFound | UserSuspended
            """,
            Graph.Sdl(executor));
    }

    [Fact]
    public async Task ExposeFieldsOf_shows_one_leaf_s_own_fields()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<HierarchyQuery>(), o => o.AddErrorCases<UserError>(UserError.Cases).ExposeFieldsOf<UserSuspended>());

        Assert.Equal(
            """
            schema {
              query: HierarchyQuery
            }

            type HierarchyQuery {
              userById(id: Int!): UserByIdResult!
            }

            type User {
              id: Int!
              name: String!
            }

            type UserNotFound implements Error {
              "What went wrong, for people to read."
              message: String!
            }

            type UserSuspended implements Error {
              id: Int!
              reason: String!
              "What went wrong, for people to read."
              message: String!
            }

            interface Error {
              message: String!
            }

            union UserByIdResult = User | UserNotFound | UserSuspended
            """,
            Graph.Sdl(executor));
    }

    [Fact]
    public async Task A_generated_shape_declares_its_error_s_cases()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<ShapeQuery>(), o => o.AddErrorCases<UserError>(UserError.Cases));

        Assert.Equal(
            """
            schema {
              query: ShapeQuery
            }

            type ShapeQuery {
              findUser(id: Int!): FindUserResult!
            }

            type User {
              id: Int!
              name: String!
            }

            type UserNotFound implements Error {
              "What went wrong, for people to read."
              message: String!
            }

            type UserSuspended implements Error {
              "What went wrong, for people to read."
              message: String!
            }

            interface Error {
              message: String!
            }

            union FindUserResult = User | UserNotFound | UserSuspended
            """,
            Graph.Sdl(executor));
    }

    // PaymentError sits between CheckoutError and its two leaves: GraphQL shows only leaves.
    [Fact]
    public async Task A_nested_hierarchy_is_flattened_to_its_leaves()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<NestedQuery>(), o => o.AddErrorCases<CheckoutError>(CheckoutError.Cases));

        Assert.Equal(
            """
            schema {
              query: NestedQuery
            }

            type NestedQuery {
              checkout(cart: Int!): CheckoutResult!
            }

            type CardDeclined implements Error {
              "What went wrong, for people to read."
              message: String!
            }

            type CardExpired implements Error {
              "What went wrong, for people to read."
              message: String!
            }

            type CartEmpty implements Error {
              "What went wrong, for people to read."
              message: String!
            }

            type Order {
              id: Int!
              sku: String!
              quantity: Int!
            }

            interface Error {
              message: String!
            }

            union CheckoutResult = Order | CardDeclined | CardExpired | CartEmpty
            """,
            Graph.Sdl(executor));
    }

    // Result<User> (any Error) and Validation<User, Error> both declare ValidationError, with the failing fields.
    [Fact]
    public async Task A_result_that_can_fail_validation_declares_ValidationError()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<ValidationQuery>());

        Assert.Equal(
            """
            schema {
              query: ValidationQuery
            }

            type ValidationQuery {
              register(name: String!): RegisterResult!
              validate(name: String!): ValidateResult!
            }

            type User {
              id: Int!
              name: String!
            }

            "A validation failure: the failing fields and their messages."
            type ValidationError implements Error {
              "Always \"Validation failed.\""
              message: String!
              "The failing fields, in the order their first error appears."
              fields: [ValidationFieldError!]!
            }

            "One failing field and the messages of its errors."
            type ValidationFieldError {
              "The field's key."
              field: String!
              "The message of each error listed under the field, in order."
              messages: [String!]!
            }

            interface Error {
              message: String!
            }

            union RegisterResult = User | ValidationError

            union ValidateResult = User | ValidationError
            """,
            Graph.Sdl(executor));
    }

    // Seven leaves, where FieldResult stops at five, with every leaf's own fields exposed.
    [Fact]
    public async Task A_hierarchy_has_no_five_case_limit()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<ShippingQuery>(), o =>
            {
                o.AddErrorCases<ShippingError>(ShippingError.Cases);
                o.ExposeLeafFields = true;
            });

        Assert.Equal(
            """
            schema {
              query: ShippingQuery
            }

            type ShippingQuery {
              ship(order: Int!): ShipResult!
            }

            type AddressMissing implements Error {
              "What went wrong, for people to read."
              message: String!
            }

            type CarrierDown implements Error {
              carrier: String!
              "What went wrong, for people to read."
              message: String!
            }

            type CountryBlocked implements Error {
              country: String!
              "What went wrong, for people to read."
              message: String!
            }

            type HolidayClosure implements Error {
              holiday: String!
              "What went wrong, for people to read."
              message: String!
            }

            type Order {
              id: Int!
              sku: String!
              quantity: Int!
            }

            type ParcelTooHeavy implements Error {
              kilograms: Decimal!
              "What went wrong, for people to read."
              message: String!
            }

            type ParcelTooLarge implements Error {
              centimetres: Int!
              "What went wrong, for people to read."
              message: String!
            }

            type PostcodeInvalid implements Error {
              postcode: String!
              "What went wrong, for people to read."
              message: String!
            }

            interface Error {
              message: String!
            }

            union ShipResult =
              | Order
              | AddressMissing
              | CarrierDown
              | CountryBlocked
              | HolidayClosure
              | ParcelTooHeavy
              | ParcelTooLarge
              | PostcodeInvalid

            "The `Decimal` scalar type represents a decimal floating-point number with high precision."
            scalar Decimal
              @specifiedBy(url: "https://scalars.graphql.org/chillicream/decimal.html")
            """,
            Graph.Sdl(executor));
    }

    [Fact]
    public async Task Mutation_conventions_put_the_cases_in_the_payload_errors()
    {
        var executor = await Graph.BuildAsync(b => b.AddQueryType<LeafQuery>().AddMutationType<MutationRoot>().AddMutationConventions(), o => o.AddErrorCases<UserError>(UserError.Cases));

        Assert.Equal(
            """
            schema {
              query: LeafQuery
              mutation: MutationRoot
            }

            type LeafQuery {
              userById(id: Int!): UserByIdResult!
            }

            type MutationRoot {
              renameUser(input: RenameUserInput!): RenameUserPayload!
            }

            type RenameUserPayload {
              user: User
              errors: [RenameUserError!]
            }

            type User {
              id: Int!
              name: String!
            }

            type UserNotFound implements Error {
              "What went wrong, for people to read."
              message: String!
            }

            type UserSuspended implements Error {
              "What went wrong, for people to read."
              message: String!
            }

            interface Error {
              message: String!
            }

            union RenameUserError = UserNotFound | UserSuspended

            union UserByIdResult = User | UserNotFound

            input RenameUserInput {
              id: Int!
              name: String!
            }
            """,
            Graph.Sdl(executor));
    }

}
