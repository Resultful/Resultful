using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using static Resultful.AspNetCore.Tests.Http;

namespace Resultful.AspNetCore.Tests;

// The map: the error is walked outside in (Error.Causes()) and the first error with a status wins.
public class MapTests
{
    [Fact]
    public async Task Each_mapped_type_gets_its_status()
    {
        Assert.Equal(404, (await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(), configure: MapOrders)).Status);
        Assert.Equal(409, (await Execute(Result.Fail<int>(new OrderLocked(1, "ann")).ToHttpResult(), configure: MapOrders)).Status);
    }

    [Fact]
    public async Task A_note_changes_nothing()
    {
        var error = new OrderLocked(1, "ann").WithContext("loading the basket").WithContext("checkout");
        Assert.Equal(409, (await Execute(Result.Fail<int>(error).ToHttpResult(), configure: MapOrders)).Status);
    }

    [Fact]
    public async Task The_cause_answers_when_the_wrapper_is_not_mapped()
    {
        var error = new SaveFailed(1) { Cause = new OrderLocked(1, "ann") };
        Assert.Equal(409, (await Execute(Result.Fail<int>(error).ToHttpResult(), configure: MapOrders)).Status);
    }

    [Fact]
    public async Task A_deep_cause_answers_too()
    {
        var error = new SaveFailed(1) { Cause = new DbTimeout("q") { Cause = new OrderNotFound(1) } };
        Assert.Equal(404, (await Execute(Result.Fail<int>(error).ToHttpResult(), configure: MapOrders)).Status);
    }

    [Fact]
    public async Task Inner_errors_are_walked()
    {
        var error = new BatchFailed([new DbTimeout("q"), new OrderNotFound(7)]);
        var response = await Execute(Result.Fail<int>(error).ToHttpResult(), configure: MapOrders);
        Assert.Equal(404, response.Status);
        Assert.Equal("order 7 was not found", response.Get("detail"));
    }

    [Fact]
    public async Task The_outermost_mapped_error_wins()
    {
        var error = new OrderLocked(1, "ann") { Cause = new OrderNotFound(1) };
        Assert.Equal(409, (await Execute(Result.Fail<int>(error).ToHttpResult(), configure: MapOrders)).Status);
    }

    [Fact]
    public async Task The_cause_chain_comes_before_inner_errors()
    {
        // Causes() is depth-first: the error, its Cause chain, then Inner().
        var error = new BatchFailed([new OrderNotFound(1)]) { Cause = new OrderLocked(1, "ann") };
        Assert.Equal(409, (await Execute(Result.Fail<int>(error).ToHttpResult(), configure: MapOrders)).Status);
    }

    [Fact]
    public async Task An_outer_error_mapped_by_its_base_type_beats_an_exactly_mapped_cause()
    {
        var error = new OrderCancelled(1) { Cause = new SaveFailed(1) };
        var response = await Execute(Result.Fail<int>(error).ToHttpResult(),
            configure: o => o.Map<OrderError>(StatusCodes.Status422UnprocessableEntity).Map<SaveFailed>(StatusCodes.Status503ServiceUnavailable));
        Assert.Equal(422, response.Status);
    }

    [Fact]
    public async Task Anything_unmapped_is_a_500()
    {
        Assert.Equal(500, (await Execute(Result.Fail<int>(new SaveFailed(1)).ToHttpResult(), configure: MapOrders)).Status);
        Assert.Equal(500, (await Execute(Result.Fail<int>("plain").ToHttpResult())).Status);
        Assert.Equal(500, (await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult())).Status);
    }

    [Fact]
    public async Task The_default_status_can_change()
    {
        var response = await Execute(Result.Fail<int>(new SaveFailed(1)).ToHttpResult(), configure: o => o.DefaultStatusCode = StatusCodes.Status502BadGateway);
        Assert.Equal(502, response.Status);
        Assert.Null(response.Get("detail"));
    }

    [Fact]
    public async Task Mapping_a_type_again_replaces_its_status()
    {
        var response = await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(),
            configure: o => o.Map<OrderNotFound>(StatusCodes.Status404NotFound).Map<OrderNotFound>(StatusCodes.Status410Gone));
        Assert.Equal(410, response.Status);
    }

    [Fact]
    public async Task Configure_runs_in_order_and_adds_to_one_map()
    {
        static void Twice(IServiceCollection services) =>
            services.AddResultful(o => o.Map<OrderNotFound>(404)).AddResultful(o => o.Map<OrderLocked>(409));
        Assert.Equal(404, (await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(), register: false, moreServices: Twice)).Status);
        Assert.Equal(409, (await Execute(Result.Fail<int>(new OrderLocked(1, "ann")).ToHttpResult(), register: false, moreServices: Twice)).Status);
    }

    [Fact]
    public async Task A_typed_result_maps_the_same_way()
    {
        Result<Order, OrderError> result = new OrderNotFound(3);
        var response = await Execute(result.ToHttpResult(), configure: MapOrders);
        Assert.Equal(404, response.Status);
        Assert.Equal("order 3 was not found", response.Get("detail"));
    }

    [Fact]
    public async Task Without_registration_the_defaults_apply()
    {
        var response = await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(), register: false);
        Assert.Equal(500, response.Status);
        Assert.Equal("application/problem+json", response.ContentType);
        Assert.Equal("An error occurred while processing your request.", response.Get("title"));
    }
}

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
        // The union overload: IErrorUnion.Error is the leaf the map sees.
        var declined = Result.Fail<Order, PaymentError>(new PaymentError(new CardDeclined()));
        var fraud = Result.Fail<Order, PaymentError>(new PaymentError(new FraudSuspected(97)));
        void Map(ResultfulHttpOptions o) => o.Map<CardDeclined>(StatusCodes.Status409Conflict);

        var a = await Execute(declined.ToHttpResult(), configure: Map);
        Assert.Equal(409, a.Status);
        Assert.Equal("The card was declined.", a.Get("detail"));

        var b = await Execute(fraud.ToHttpResult(), configure: Map);
        Assert.Equal(500, b.Status);
        Assert.DoesNotContain("risk score", b.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_successful_union_result_answers_200()
    {
        var ok = Result.Ok<Order, PaymentError>(new Order(1, "teapot"));
        Assert.IsType<ResultHttpResult<Order, Error>>(ok.ToHttpResult());
        Assert.Equal(200, (await Execute(ok.ToHttpResult())).Status);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public async Task A_csharp_union_widens_the_same_way()
    {
        Result<Order, CheckoutError> locked = (CheckoutError)new OrderLocked(1, "ann");
        Result<Order, CheckoutError> missing = (CheckoutError)new OrderNotFound(4);
        ResultHttpResult<Order, Error> http = locked.ToHttpResult();
        var response = await Execute(http, configure: MapOrders);
        Assert.Equal(409, response.Status);
        Assert.Equal("order 1 is locked by ann", response.Get("detail"));
        Assert.Equal(404, (await Execute(missing.ToHttpResult(), configure: MapOrders)).Status);
        Assert.Equal(200, (await Execute(Result.Ok<Order, CheckoutError>(new Order(1, "teapot")).ToHttpResult())).Status);
        Assert.Throws<InvalidOperationException>(() => default(Result<Order, CheckoutError>).ToHttpResult());
    }
#endif
}

#if NET11_0_OR_GREATER
// A C# 15 union, written by hand as Resultful.Generators writes one.
public union CheckoutError(OrderNotFound, OrderLocked) : IErrorUnion
{
    public Error Error => Value switch { Error e => e, _ => throw new InvalidOperationException() };
}
#endif

public class SuccessTests
{
    [Fact]
    public async Task A_success_is_200_with_the_value_as_json()
    {
        var response = await Execute(Result.Ok(new Order(1, "teapot")).ToHttpResult(), configure: MapOrders);
        Assert.Equal(200, response.Status);
        Assert.StartsWith("application/json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal(1, response.Body.GetProperty("id").GetInt32());
        Assert.Equal("teapot", response.Get("name"));
    }

    [Fact]
    public async Task Each_overload_answers_200_on_success()
    {
        Assert.Equal(200, (await Execute(Result.Ok<Order, OrderError>(new Order(1, "a")).ToHttpResult())).Status);
        Assert.Equal(200, (await Execute(Validation.Ok(new Order(1, "a")).ToHttpResult())).Status);
        Assert.Equal(200, (await Execute(Validation.Ok<Order, Required>(new Order(1, "a")).ToHttpResult())).Status);
    }

    [Fact]
    public async Task A_unit_success_is_200_with_no_body()
    {
        var response = await Execute(Result.Ok().ToHttpResult());
        Assert.Equal(200, response.Status);
        Assert.False(response.HasBody, response.Text);
    }

    [Fact]
    public void A_zero_result_throws_like_reading_it()
    {
        Assert.Throws<InvalidOperationException>(() => default(Result<int>).ToHttpResult());
        Assert.Throws<InvalidOperationException>(() => default(Result<int, OrderError>).ToHttpResult());
        Assert.Throws<InvalidOperationException>(() => default(Validation<int>).ToHttpResult());
        Assert.Throws<InvalidOperationException>(() => default(Validation<int, Required>).ToHttpResult());
    }

    [Fact]
    public async Task The_map_is_read_when_the_result_executes()
    {
        var result = Result.Fail<int>(new OrderNotFound(1)).ToHttpResult();
        Assert.Equal(500, (await Execute(result)).Status);
        Assert.Equal(404, (await Execute(result, configure: MapOrders)).Status);
    }

    [Fact]
    public async Task Execute_checks_its_argument()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Result.Ok(1).ToHttpResult().ExecuteAsync(null!));
    }
}

public class OptionsTests
{
    [Fact]
    public async Task The_defaults()
    {
        var o = new ResultfulHttpOptions();
        Assert.Equal(500, o.DefaultStatusCode);
        Assert.Equal("Required", o.ValidationKey(new Required("x")));
        Assert.Equal(500, (await Execute(Result.Fail<int>(new OrderNotFound(1)).ToHttpResult(), configure: _ => { })).Status);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(302)]
    [InlineData(399)]
    [InlineData(600)]
    [InlineData(-1)]
    public void A_status_outside_4xx_and_5xx_is_refused(int status)
    {
        var o = new ResultfulHttpOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => o.Map<OrderNotFound>(status));
        Assert.Throws<ArgumentOutOfRangeException>(() => o.DefaultStatusCode = status);
    }

    [Fact]
    public void Map_chains()
    {
        var o = new ResultfulHttpOptions();
        Assert.Same(o, o.Map<OrderNotFound>(StatusCodes.Status404NotFound));
    }

    [Fact]
    public void A_null_validation_key_is_refused() =>
        Assert.Throws<ArgumentNullException>(() => new ResultfulHttpOptions().ValidationKey = null!);

    [Fact]
    public void Registration_checks_its_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => ResultfulServiceCollectionExtensions.AddResultful(null!));
        Assert.Throws<ArgumentNullException>(() => new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddResultful(null!));
    }
}

// The type keeps TError, so an endpoint's metadata lists 200, 400 for a validation, and 500.
public class MetadataTests
{
    public static ResultHttpResult<Order, Error> Handler() => throw new NotSupportedException();

    static Dictionary<int, Type?> Statuses<T, TError>() where TError : Error
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/orders/{id}"), 0);
        ResultHttpResult<T, TError>.PopulateMetadata(typeof(MetadataTests).GetMethod(nameof(Handler))!, builder);
        return builder.Metadata.OfType<IProducesResponseTypeMetadata>().ToDictionary(m => m.StatusCode, m => m.Type);
    }

    [Fact]
    public void Any_error_may_be_a_validation_problem_or_a_500()
    {
        var statuses = Statuses<Order, Error>();
        Assert.Equal(typeof(Order), statuses[200]);
        Assert.Equal(typeof(HttpValidationProblemDetails), statuses[400]);
        Assert.Equal(typeof(ProblemDetails), statuses[500]);
    }

    [Fact]
    public void A_validation_lists_400()
    {
        Assert.Contains(400, Statuses<Order, ValidationFailed<Error>>().Keys);
        Assert.Contains(400, Statuses<Order, ValidationFailed<Required>>().Keys);
    }

    [Fact]
    public void A_closed_hierarchy_does_not_list_400()
    {
        Assert.Equal([200, 500], Statuses<Order, OrderError>().Keys.Order());
    }

    [Fact]
    public void A_unit_success_has_no_body_type()
    {
        Assert.Null(Statuses<Unit, Error>()[200]);
    }

    [Fact]
    public void The_return_types_carry_the_error_type()
    {
        Assert.IsType<ResultHttpResult<int, Error>>(Result.Ok(1).ToHttpResult());
        Assert.IsType<ResultHttpResult<int, OrderError>>(Result.Ok<int, OrderError>(1).ToHttpResult());
        Assert.IsType<ResultHttpResult<int, ValidationFailed<Error>>>(Validation.Ok(1).ToHttpResult());
        Assert.IsType<ResultHttpResult<int, ValidationFailed<Required>>>(Validation.Ok<int, Required>(1).ToHttpResult());
    }
}
