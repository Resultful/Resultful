using System.Diagnostics;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Resultful.FluentValidation;

namespace Resultful.Diagnostics.Tests;

// A checkout as an app writes it: FluentValidation checks the request, a payment step may fail with a wrapped cause, and
// the app opts in to a log entry and its own span at the points it chooses. Leaf data never reaches either.
public sealed class ScenarioTests
{
    const string Card = "SENTINEL-4111111111111111";
    const string Email = "SENTINEL-ada-at-example.com";
    const int Quantity = 987654;

    public sealed record Checkout(string Email, string CardNumber, int Quantity);

    public sealed record Receipt(int OrderId);

    sealed class CheckoutValidator : AbstractValidator<Checkout>
    {
        public CheckoutValidator()
        {
            RuleFor(c => c.Email).EmailAddress();
            // InclusiveBetween's built-in message would add "You entered 987654."; an app keeps values out of logs this way.
            RuleFor(c => c.Quantity).InclusiveBetween(1, 10).WithMessage("'{PropertyName}' must be between {From} and {To}.");
        }
    }

    static async Task<Result<Receipt>> Pay(Checkout checkout)
    {
        await Task.Yield();
        return checkout.CardNumber.EndsWith('1')
            ? Result.Fail<Receipt>(new CardDeclined(checkout.CardNumber) { Cause = new GatewayTimeout("acme") }.WithContext("charging order 7"))
            : Result.Ok(new Receipt(7));
    }

    static async Task<Result<Receipt>> Handle(Checkout request, ILogger logger, Spans spans)
    {
        using var activity = spans.Start("checkout");
        var validated = (await new CheckoutValidator().ValidateToResultAsync(request, TestContext.Current.CancellationToken))
            .LogFailure(logger, LogLevel.Information);
        var paid = await validated.ToResult().BindAsync(Pay);
        return paid.LogFailure(logger, LogLevel.Warning).RecordFailure(activity);
    }

    [Fact]
    public async Task An_invalid_request_logs_its_failures_and_marks_the_apps_span()
    {
        using var spans = new Spans();
        var logger = new ListLogger();

        var result = await Handle(new Checkout(Email, Card, Quantity), logger, spans);

        Assert.IsType<ValidationFailed<Error>>(result.Error);
        const string Line = "Result failed with ValidationFailed<Error>: Validation failed. Causes: FluentValidationError: 'Email' is not a valid email address.; FluentValidationError: 'Quantity' must be between 1 and 10.";
        Assert.Equal([Line, Line], logger.Entries.Select(e => e.Text));
        Assert.Equal([LogLevel.Information, LogLevel.Warning], logger.Entries.Select(e => e.Level));
        var span = Assert.Single(spans.Stopped);
        Assert.Equal((ActivityStatusCode.Error, (object?)"ValidationFailed<Error>"), (span.Status, span.GetTagItem(ResultfulDiagnostics.ErrorTypeTag)));
        Assert.All(logger.AllText(), t => Assert.DoesNotContain("SENTINEL", t, StringComparison.Ordinal));
        Assert.All(logger.AllText(), t => Assert.DoesNotContain("987654", t, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_declined_payment_logs_the_chain_by_type_and_message_only()
    {
        using var spans = new Spans();
        var logger = new ListLogger();

        var result = await Handle(new Checkout("a@b.c", Card, 1), logger, spans);

        Assert.IsType<CardDeclined>(result.Error);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("Result failed with CardDeclined: the card was declined Context: charging order 7 Causes: GatewayTimeout: the gateway timed out", entry.Text);
        Assert.All(logger.AllText(), t => Assert.DoesNotContain("SENTINEL", t, StringComparison.Ordinal));
        Assert.All(logger.AllText(), t => Assert.DoesNotContain("acme", t, StringComparison.Ordinal));
        Assert.Equal("CardDeclined", Assert.Single(spans.Stopped).GetTagItem(ResultfulDiagnostics.ErrorTypeTag));
    }

    [Fact]
    public async Task A_successful_checkout_logs_nothing_and_leaves_the_span_unset()
    {
        using var spans = new Spans();
        var logger = new ListLogger();

        var result = await Handle(new Checkout("a@b.c", "4000000000000002", 1), logger, spans);

        Assert.Equal(new Receipt(7), result.Value);
        Assert.Empty(logger.Entries);
        var span = Assert.Single(spans.Stopped);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
        Assert.Empty(span.TagObjects);
    }
}
