#if NET11_0_OR_GREATER
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Resultful.CodeAnalysis;

namespace Resultful.HotChocolate.Tests;

// C# 15 error unions on net11.0, written as an app writes them: Resultful.Generators adds IErrorUnion and Cases.
public partial union PaymentFailure(CardDeclined, CardExpired);

public partial union PurchaseError(CartEmpty, PaymentFailure, ValidationFailed<Required>);

[ResultShape(typeof(PurchaseError), Success = typeof(Order))]
public readonly partial struct PurchaseResult;

public sealed class PurchaseQuery
{
    public Result<Order, PurchaseError> Purchase([GlobalState("result")] Result<Order, PurchaseError> result) => result;

    public PurchaseResult PurchaseShape([GlobalState("result")] Result<Order, PurchaseError> result) => result;

    public FieldResult<Order, CartEmpty, CardDeclined, CardExpired, ResultfulValidationError> PurchaseField([GlobalState("result")] Result<Order, PurchaseError> result) =>
        result.ToFieldResult<Order, PurchaseError, CartEmpty, CardDeclined, CardExpired, ResultfulValidationError>();
}

public sealed class UnregisteredUnionQuery
{
    public Result<Order, PaymentFailure> Pay() => new Order(1, "SKU", 1);
}

public class UnionTests
{
    private const string Selection = "__typename ... on Order { id } ... on Error { message } ... on CardDeclined { reason } ... on ValidationError { fields { field messages } }";

    private static readonly Result<Order, PurchaseError>[] Results =
    [
        Result.Ok<Order, PurchaseError>(new Order(1, "SKU", 1)),
        Result.Fail<Order, PurchaseError>(new PurchaseError(new CartEmpty())),
        Result.Fail<Order, PurchaseError>(new PurchaseError(new PaymentFailure(new CardDeclined("limit")))),
        Result.Fail<Order, PurchaseError>(new PurchaseError(new PaymentFailure(new CardExpired(1, 2030)))),
        Result.Fail<Order, PurchaseError>(new PurchaseError(new ValidationFailed<Required>([new Required("card")]))),
    ];

    private static Task<IRequestExecutor> Server() =>
        Graph.BuildAsync(b => b.AddQueryType<PurchaseQuery>(), o =>
        {
            o.AddErrorCases<PurchaseError>(PurchaseError.Cases);
            o.ExposeLeafFields = true;
        });

    // A union can't hold a union in GraphQL: the nested PaymentFailure is shown as its leaves, for a result, a shape
    // and a FieldResult alike.
    [Fact]
    public async Task A_nested_error_union_is_flattened_to_its_leaves()
    {
        var executor = await Server();

        Assert.Equal(
            """
            schema {
              query: PurchaseQuery
            }

            type PurchaseQuery {
              purchase: PurchaseResult!
              purchaseShape: PurchaseShapeResult!
              purchaseField: PurchaseFieldResult!
            }

            type CardDeclined implements Error {
              reason: String!
              "What went wrong, for people to read."
              message: String!
            }

            type CardExpired implements Error {
              month: Int!
              year: Int!
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

            union PurchaseFieldResult =
              | Order
              | CartEmpty
              | CardDeclined
              | CardExpired
              | ValidationError

            union PurchaseResult =
              | Order
              | CardDeclined
              | CardExpired
              | CartEmpty
              | ValidationError

            union PurchaseShapeResult =
              | Order
              | CardDeclined
              | CardExpired
              | CartEmpty
              | ValidationError
            """,
            Graph.Sdl(executor));
    }

    [Fact]
    public async Task Each_leaf_of_a_nested_union_is_selectable_and_a_validation_case_is_a_ValidationError()
    {
        var executor = await Server();

        var answers = new List<string>();
        foreach (var result in Results) answers.Add(await Graph.RunJsonAsync(executor, $"{{ purchase {{ {Selection} }} }}", result));

        Assert.Equal(
            [
                """{"data":{"purchase":{"__typename":"Order","id":1}}}""",
                """{"data":{"purchase":{"__typename":"CartEmpty","message":"The cart is empty."}}}""",
                """{"data":{"purchase":{"__typename":"CardDeclined","message":"The card was declined.","reason":"limit"}}}""",
                """{"data":{"purchase":{"__typename":"CardExpired","message":"The card has expired."}}}""",
                """{"data":{"purchase":{"__typename":"ValidationError","message":"Validation failed.","fields":[{"field":"Required","messages":["card is required."]}]}}}""",
            ],
            answers);
    }

    [Fact]
    public async Task A_shape_and_a_FieldResult_over_the_union_answer_as_the_result_does()
    {
        var executor = await Server();

        foreach (var result in Results)
        {
            var expected = await Graph.RunJsonAsync(executor, $"{{ r: purchase {{ {Selection} }} }}", result);
            Assert.Equal(expected, await Graph.RunJsonAsync(executor, $"{{ r: purchaseShape {{ {Selection} }} }}", result));
            Assert.Equal(expected, await Graph.RunJsonAsync(executor, $"{{ r: purchaseField {{ {Selection} }} }}", result));
        }
    }

    [Fact]
    public void ToFieldResult_unwraps_the_union_to_its_leaf()
    {
        var declined = new CardDeclined("limit");
        var converted = Result.Fail<Order, PurchaseError>(new PurchaseError(new PaymentFailure(declined)))
            .ToFieldResult<Order, PurchaseError, CardDeclined>();

        Assert.Same(declined, Assert.Single(converted.Errors!));
        Assert.ThrowsAny<Exception>(() => Result.Fail<Order, PurchaseError>(new PurchaseError(new CartEmpty())).ToFieldResult<Order, PurchaseError, CardDeclined>());
    }

    [Fact]
    public async Task An_unregistered_error_union_fails_the_schema()
    {
        var thrown = await Assert.ThrowsAsync<SchemaException>(() => Graph.BuildAsync(b => b.AddQueryType<UnregisteredUnionQuery>()));

        Assert.Equal(
            "The field UnregisteredUnionQuery.pay returns a result, but the cases of PaymentFailure aren't registered: call AddErrorCases<PaymentFailure>(PaymentFailure.Cases) in AddResultfulErrors.",
            Assert.Single(thrown.Errors).Message);
    }

    [Fact]
    public void A_union_s_cases_may_be_any_Error()
    {
        var options = new ResultfulGraphQLOptions().AddErrorCases<PurchaseError>(PurchaseError.Cases);

        Assert.Throws<ArgumentException>("cases", () => options.AddErrorCases<PurchaseError>([typeof(PaymentFailure)]));
    }
}
#endif
