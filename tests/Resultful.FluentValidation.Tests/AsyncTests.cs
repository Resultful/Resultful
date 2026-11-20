using FluentValidation;

namespace Resultful.FluentValidation.Tests;

public sealed class AsyncTests
{
    [Fact]
    public async Task Async_rules_run_and_their_failures_become_errors()
    {
        var validator = new InlineValidator<SignUp>();
        validator.RuleFor(s => s.Email).MustAsync(async (email, ct) => { await Task.Yield(); return email.Contains('@', StringComparison.Ordinal); }).WithErrorCode("EMAIL");

        var bad = await validator.ValidateToResultAsync(new SignUp("ann", "nope"), TestContext.Current.CancellationToken);
        var good = await validator.ValidateToResultAsync(new SignUp("ann", "a@b.c"), TestContext.Current.CancellationToken);

        Assert.Equal("EMAIL", Assert.IsType<FluentValidationError>(Assert.Single(bad.Errors)).ErrorCode);
        Assert.Equal("ann", good.Value.Name);
    }

    [Fact]
    public async Task The_token_reaches_the_rules()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var validator = new InlineValidator<SignUp>();
        validator.RuleFor(s => s.Name).MustAsync((_, ct) => { seen = ct; return Task.FromResult(true); });

        await validator.ValidateToResultAsync(new SignUp("a", "b"), cts.Token);

        Assert.Equal(cts.Token, seen);
    }

    [Fact]
    public async Task A_cancelled_token_propagates_as_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var validator = new InlineValidator<SignUp>();
        validator.RuleFor(s => s.Name).MustAsync(async (_, ct) => { await cts.CancelAsync(); ct.ThrowIfCancellationRequested(); return true; });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validator.ValidateToResultAsync(new SignUp("a", "b"), cts.Token));
    }

    [Fact]
    public async Task An_async_rule_that_throws_propagates()
    {
        var validator = new InlineValidator<SignUp>();
        validator.RuleFor(s => s.Name).MustAsync(async (_, _) => { await Task.Yield(); throw new InvalidOperationException("boom"); });

        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateToResultAsync(new SignUp("a", "b"), TestContext.Current.CancellationToken));
    }
}
