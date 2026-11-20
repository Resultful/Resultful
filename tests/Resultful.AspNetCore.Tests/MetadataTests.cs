using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Resultful.AspNetCore.Tests;

// The type keeps TError (the spec: change in this PR), so metadata can be read from it. 20b completes it (HTTP-OPENAPI).
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
