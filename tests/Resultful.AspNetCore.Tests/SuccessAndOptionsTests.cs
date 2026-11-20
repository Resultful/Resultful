using Microsoft.AspNetCore.Http;
using static Resultful.AspNetCore.Tests.Http;

namespace Resultful.AspNetCore.Tests;

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
    public void The_defaults()
    {
        var o = new ResultfulHttpOptions();
        Assert.Equal(500, o.DefaultStatusCode);
        Assert.Equal("Required", o.ValidationKey(new Required("x")));
        Assert.False(o.TryGetStatus(typeof(OrderNotFound), out _));
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
