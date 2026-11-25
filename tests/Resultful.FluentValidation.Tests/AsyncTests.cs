using FluentValidation;

namespace Resultful.FluentValidation.Tests;

public sealed class AsyncTests
{
    static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Async_rules_run_and_their_failures_become_errors()
    {
        var validator = new InlineValidator<SignUp>();
        validator.RuleFor(s => s.Email).MustAsync(async (email, ct) => { await Task.Yield(); return email.Contains('@', StringComparison.Ordinal); }).WithErrorCode("EMAIL");
        var signUp = new SignUp("ann", "a@b.c");

        var bad = await validator.ValidateToResultAsync(new SignUp("ann", "nope"), Token);
        var good = await validator.ValidateToResultAsync(signUp, Token);

        var error = Assert.IsType<FluentValidationError>(Assert.Single(bad.Errors));
        Assert.Equal(("Email", "EMAIL"), (error.PropertyName, error.ErrorCode));
        Assert.Same(signUp, good.Value);
    }

    [Fact]
    public async Task Sync_and_async_rules_both_run_on_the_async_path_in_rule_order()
    {
        var validator = new InlineValidator<SignUp>();
        validator.RuleFor(s => s.Name).NotEmpty();
        validator.RuleFor(s => s.Email).MustAsync(async (_, _) => { await Task.Yield(); return false; }).WithErrorCode("TAKEN");
        validator.RuleForEach(s => s.Lines).SetValidator(new LineValidator()).When(s => s.Lines is not null);

        var bad = await validator.ValidateToResultAsync(new SignUp("", "a@b.c", Lines: [new Line("", 1)]), Token);

        Assert.Equal(["Name", "Email", "Lines[0].Sku"], bad.Errors.Cast<FluentValidationError>().Select(e => e.PropertyName));
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
    public async Task A_token_cancelled_during_validation_propagates_as_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var validator = new InlineValidator<SignUp>();
        validator.RuleFor(s => s.Name).MustAsync(async (_, ct) => { await cts.CancelAsync(); ct.ThrowIfCancellationRequested(); return true; });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validator.ValidateToResultAsync(new SignUp("a", "b"), cts.Token));
    }

    [Fact]
    public async Task A_token_cancelled_before_the_call_propagates_as_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var validator = new InlineValidator<SignUp>();
        validator.RuleFor(s => s.Name).MustAsync((_, ct) => { ct.ThrowIfCancellationRequested(); return Task.FromResult(true); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validator.ValidateToResultAsync(new SignUp("a", "b"), cts.Token));
    }

    [Fact]
    public async Task An_async_rule_that_throws_propagates()
    {
        var validator = new InlineValidator<SignUp>();
        validator.RuleFor(s => s.Name).MustAsync(async (_, _) => { await Task.Yield(); throw new InvalidOperationException("boom"); });

        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateToResultAsync(new SignUp("a", "b"), Token));
    }

    // The call a caller writes when it has no token; outside a test method, so xUnit1051 doesn't ask for one.
    static Task<Validation<SignUp>> WithoutToken(IValidator<SignUp> validator, SignUp signUp) => validator.ValidateToResultAsync(signUp);

    [Fact]
    public async Task The_token_is_optional()
    {
        var validator = new InlineValidator<SignUp>();
        CancellationToken seen = new(canceled: true);
        validator.RuleFor(s => s.Name).MustAsync((_, ct) => { seen = ct; return Task.FromResult(true); });

        var good = await WithoutToken(validator, new SignUp("a", "b"));

        Assert.True(good.IsSuccess);
        Assert.Equal(CancellationToken.None, seen);
    }

    [Fact]
    public async Task A_null_validator_throws_before_anything_runs()
    {
        await Assert.ThrowsAsync<ArgumentNullException>("validator", () => ((IValidator<SignUp>)null!).ValidateToResultAsync(new SignUp("a", "b"), Token));
    }

    [Fact]
    public void The_async_path_keeps_the_ui_culture()
    {
        var item = new SignUp("", "a@b.c");
        var turkishRaw = ValidatorExtensionsTests.Under("tr-TR", () => new SignUpValidator().Validate(item)).Errors[0].ErrorMessage;

        var turkish = ValidatorExtensionsTests.Under("tr-TR", () => new SignUpValidator().ValidateToResultAsync(item, CancellationToken.None).GetAwaiter().GetResult()).Errors[0];

        Assert.Equal(turkishRaw, turkish.Message);
    }
}
