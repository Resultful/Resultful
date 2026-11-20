using Microsoft.AspNetCore.Http;
using static Resultful.AspNetCore.Tests.Http;

namespace Resultful.AspNetCore.Tests;

public class UnionAndHierarchyTests
{
    [Fact]
    public async Task A_closed_hierarchy_can_be_mapped_by_its_base()
    {
        void Map(ResultfulHttpOptions o) => o.Map<OrderError>(StatusCodes.Status422UnprocessableEntity);
        Assert.Equal(422, (await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(), configure: Map)).Status);
        Assert.Equal(422, (await Execute(Result.Fail<int>(new OrderCancelled(1)).ToHttpResult(), configure: Map)).Status);
    }

    [Fact]
    public async Task A_leaf_entry_beats_its_base_entry()
    {
        void Map(ResultfulHttpOptions o) => o.Map<OrderError>(StatusCodes.Status422UnprocessableEntity).Map<OrderNotFound>(StatusCodes.Status404NotFound);
        Result<Order, OrderError> notFound = new OrderNotFound(1);
        Result<Order, OrderError> cancelled = new OrderCancelled(1);
        Assert.Equal(404, (await Execute(notFound.ToHttpResult(), configure: Map)).Status);
        Assert.Equal(422, (await Execute(cancelled.ToHttpResult(), configure: Map)).Status);
    }

    [Fact]
    public async Task An_error_union_widens_and_its_leaf_is_mapped()
    {
        // Decision 17: result.AsResult().ToHttpResult(); IErrorUnion.Error is the leaf the map sees.
        var declined = Result.Fail<Order, PaymentError>(new PaymentError(new CardDeclined()));
        var fraud = Result.Fail<Order, PaymentError>(new PaymentError(new FraudSuspected(97)));
        void Map(ResultfulHttpOptions o) => o.Map<CardDeclined>(StatusCodes.Status409Conflict);

        var a = await Execute(declined.AsResult().ToHttpResult(), configure: Map);
        Assert.Equal(409, a.Status);
        Assert.Equal("The card was declined.", a.Get("detail"));

        var b = await Execute(fraud.AsResult().ToHttpResult(), configure: Map);
        Assert.Equal(500, b.Status);
        Assert.DoesNotContain("risk score", b.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_successful_union_result_answers_200()
    {
        var ok = Result.Ok<Order, PaymentError>(new Order(1, "teapot"));
        Assert.Equal(200, (await Execute(ok.AsResult().ToHttpResult())).Status);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public async Task A_csharp_union_widens_the_same_way()
    {
        Result<Order, CheckoutError> locked = (CheckoutError)new OrderLocked(1, "ann");
        var response = await Execute(locked.AsResult().ToHttpResult(), configure: MapOrders);
        Assert.Equal(409, response.Status);
        Assert.Equal("order 1 is locked by ann", response.Get("detail"));
    }
#endif
}

#if NET11_0_OR_GREATER
// A C# 15 union (decision 17), as UERR-RESULT writes it by hand; the generator writes IErrorUnion from 13b.
public union CheckoutError(OrderNotFound, OrderLocked) : IErrorUnion
{
    public Error Error => Value switch { Error e => e, _ => throw new InvalidOperationException() };
}
#endif
