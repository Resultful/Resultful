using Microsoft.AspNetCore.Http;
using static Resultful.AspNetCore.Tests.Http;

namespace Resultful.AspNetCore.Tests;

// HTTP-VALIDATION-400: ValidationFailed answers 400 with a validation problem, one entry per key.
public class ValidationTests
{
    static void ByField(ResultfulHttpOptions o) => o.ValidationKey = e => e switch
    {
        Required r => r.Field,
        TooShort t => t.Field,
        _ => "",
    };

    [Fact]
    public async Task A_failed_validation_is_a_400_with_every_error()
    {
        var v = Validation.Fail<int>(new Error[] { new Required("name"), new Required("email") });
        var response = await Execute(v.ToHttpResult(), configure: ByField);
        Assert.Equal(400, response.Status);
        Assert.Equal("application/problem+json", response.ContentType);
        Assert.Equal("One or more validation errors occurred.", response.Get("title"));
        Assert.Equal(400, response.Body.GetProperty("status").GetInt32());
        Assert.Equal("name is required", response.Body.GetProperty("errors").GetProperty("name")[0].GetString());
        Assert.Equal("email is required", response.Body.GetProperty("errors").GetProperty("email")[0].GetString());
        Assert.Equal("trace-1", response.Get("traceId"));
        Assert.Null(response.Get("detail"));
    }

    [Fact]
    public async Task Errors_with_one_key_are_listed_together_in_order()
    {
        var v = Validation.Fail<int>(new Error[] { new Required("password"), new TooShort("password", 12), new Required("name") });
        var errors = (await Execute(v.ToHttpResult(), configure: ByField)).Body.GetProperty("errors");
        Assert.Equal(["password is required", "password needs at least 12 characters"], errors.GetProperty("password").EnumerateArray().Select(e => e.GetString()));
        Assert.Single(errors.GetProperty("name").EnumerateArray());
    }

    [Fact]
    public async Task The_default_key_is_the_error_type_name()
    {
        var v = Validation.Fail<int>(new Error[] { new Required("name"), new Required("email"), new TooShort("password", 12) });
        var errors = (await Execute(v.ToHttpResult())).Body.GetProperty("errors");
        Assert.Equal(2, errors.GetProperty("Required").GetArrayLength());
        Assert.Equal(1, errors.GetProperty("TooShort").GetArrayLength());
    }

    [Fact]
    public async Task A_typed_validation_is_a_400()
    {
        var v = Validation.Fail<int, Required>(new[] { new Required("name") });
        var response = await Execute(v.ToHttpResult(), configure: ByField);
        Assert.Equal(400, response.Status);
        Assert.Equal("name is required", response.Body.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task ValidationFailed_inside_a_result_is_a_400_too()
    {
        Result<int> r = Validation.Fail<int>(new Error[] { new Required("name") }).ToResult();
        Assert.Equal(400, (await Execute(r.ToHttpResult())).Status);
        Result<int> typed = Validation.Fail<int, Required>(new[] { new Required("name") }).ToResult();
        Assert.Equal(400, (await Execute(typed.ToHttpResult())).Status);
    }

    [Fact]
    public async Task ValidationFailed_as_a_cause_is_not_a_400_by_default()
    {
        // The built-in 400 is for the result's own error. A server error wrapping a validation failure stays a 500
        // and shows none of its messages.
        var error = new SaveFailed(1) { Cause = new ValidationFailed<Error>([new Required("name")]) };
        var response = await Execute(Result.Fail<int>(error).ToHttpResult(), configure: ByField);
        Assert.Equal(500, response.Status);
        Assert.DoesNotContain("name is required", response.Text, StringComparison.Ordinal);
        Assert.False(response.Body.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task ValidationFailed_as_a_cause_is_a_400_when_mapped()
    {
        var error = new SaveFailed(1) { Cause = new ValidationFailed<Error>([new Required("name")]) };
        var response = await Execute(Result.Fail<int>(error).ToHttpResult(), configure: o =>
        {
            ByField(o);
            o.Map<ValidationFailed<Error>>(StatusCodes.Status400BadRequest);
        });
        Assert.Equal(400, response.Status);
        Assert.Equal("name is required", response.Body.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task A_context_note_keeps_the_built_in_400()
    {
        Result<int> r = Validation.Fail<int>(new Error[] { new Required("name") }).ToResult();
        var noted = Result.Fail<int>(r.Error.WithContext("sign-up"));
        Assert.Equal(400, (await Execute(noted.ToHttpResult())).Status);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public async Task A_ValidationFailed_mapped_to_a_5xx_shows_no_messages(int status)
    {
        var v = Validation.Fail<int>(new Error[] { new Required("name") });
        var response = await Execute(v.ToHttpResult(), configure: o => o.Map<ValidationFailed<Error>>(status));
        Assert.Equal(status, response.Status);
        Assert.False(response.Body.TryGetProperty("errors", out _));
        Assert.Null(response.Get("detail"));
        Assert.DoesNotContain("name is required", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_null_key_is_an_empty_key()
    {
        var v = Validation.Fail<int>(new Error[] { new Required("name") });
        var response = await Execute(v.ToHttpResult(), configure: o => o.ValidationKey = _ => null!);
        Assert.Equal(400, response.Status);
        Assert.Equal("name is required", response.Body.GetProperty("errors").GetProperty("")[0].GetString());
    }

    [Fact]
    public async Task A_validation_error_that_is_also_mapped_is_still_listed_not_mapped()
    {
        // The ValidationFailed is met first in the walk, so the 400 wins over its inner errors' own entries.
        var v = Validation.Fail<int>(new Error[] { new OrderNotFound(1) });
        Assert.Equal(400, (await Execute(v.ToHttpResult(), configure: MapOrders)).Status);
    }

    [Fact]
    public async Task An_entry_for_the_exact_ValidationFailed_type_changes_its_status_not_its_body()
    {
        var v = Validation.Fail<int>(new Error[] { new Required("name") });
        var response = await Execute(v.ToHttpResult(), configure: o => o.Map<ValidationFailed<Error>>(StatusCodes.Status422UnprocessableEntity));
        Assert.Equal(422, response.Status);
        Assert.Equal(1, response.Body.GetProperty("errors").GetProperty("Required").GetArrayLength());
    }

    [Fact]
    public async Task An_entry_for_Error_does_not_take_a_ValidationFailed()
    {
        var v = Validation.Fail<int>(new Error[] { new Required("name") });
        Assert.Equal(400, (await Execute(v.ToHttpResult(), configure: o => o.Map<Error>(StatusCodes.Status418ImATeapot))).Status);
        Assert.Equal(418, (await Execute(Result.Fail<int>(new SaveFailed(1)).ToHttpResult(), configure: o => o.Map<Error>(StatusCodes.Status418ImATeapot))).Status);
    }

    [Fact]
    public async Task A_validation_message_is_shown_in_production()
    {
        var v = Validation.Fail<int>(new Error[] { new TooShort("password", 12) });
        Assert.Contains("needs at least 12", (await Execute(v.ToHttpResult())).Text, StringComparison.Ordinal);
    }
}
