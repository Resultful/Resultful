using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Resultful.Diagnostics.Tests;

// Spans are captured with an ActivityListener, as an exporter sees them. Activity.Current is AsyncLocal, and each xunit
// test runs in its own execution context, so the tests don't share it.
public sealed class ActivityTests
{
    const string Sentinel = "SENTINEL-4111111111111111";

    static object? Tag(Activity activity) => activity.GetTagItem(ResultfulDiagnostics.ErrorTypeTag);

    [Fact]
    public void Nothing_is_recorded_unless_a_call_asks()
    {
        using var spans = new Spans();

        using (spans.Start("charge"))
        {
            var failed = Result.Fail<int>(new CardDeclined(Sentinel)).Map(x => x + 1).Bind(Result.Ok);
            Assert.True(failed.IsFailure);
        }

        var span = Assert.Single(spans.Stopped);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
        Assert.Empty(span.TagObjects);
    }

    [Fact]
    public void An_opted_in_failure_sets_the_status_and_the_leaf_type_tag_and_nothing_else()
    {
        using var spans = new Spans();
        var failed = Result.Fail<int>(new CardDeclined(Sentinel).WithContext("charging order 7"));
        Result<int> returned;

        using (var activity = spans.Start("charge"))
        {
            returned = failed.RecordFailure(activity);
        }

        var span = Assert.Single(spans.Stopped);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Null(span.StatusDescription);
        var tag = Assert.Single(span.TagObjects);
        Assert.Equal(("resultful.error.type", (object?)"CardDeclined"), (tag.Key, tag.Value));
        Assert.Empty(span.Events);
        Assert.Empty(span.Baggage);
        Assert.Equal(failed, returned);
    }

    [Fact]
    public void A_leaf_sentinel_and_the_message_never_reach_a_span()
    {
        using var spans = new Spans();

        using (var a = spans.Start("a")) Result.Fail<int>(new CardDeclined(Sentinel) { Cause = new GatewayTimeout(Sentinel) }).RecordFailure(a);
        using (var b = spans.Start("b")) Result.Fail<int, Wrapper<string>>(new Wrapper<string>(Sentinel)).RecordFailure(b);
        using (var c = spans.Start("c")) Result.Fail<int, PaymentError>(new PaymentError(new CardDeclined(Sentinel))).RecordFailure(c);

        Assert.Equal(3, spans.Stopped.Count);
        var text = spans.Stopped.SelectMany(s => s.TagObjects.Select(t => t.Key + "=" + t.Value).Append(s.StatusDescription ?? "").Concat(s.Events.Select(e => e.Name)));
        Assert.All(text, t => Assert.DoesNotContain("SENTINEL", t, StringComparison.Ordinal));
        Assert.All(text, t => Assert.DoesNotContain("declined", t, StringComparison.Ordinal));
        Assert.Equal(["CardDeclined", "Wrapper<String>", "CardDeclined"], spans.Stopped.Select(Tag));
    }

    [Fact]
    public void The_package_never_starts_an_activity_of_its_own()
    {
        using var spans = new Spans();

        var activity = spans.Start("charge");
        using (activity)
        {
            Result.Fail<int>(new CardDeclined("x")).RecordFailure(activity).RecordFailure(null);
            Result.Fail<int, PaymentError>(new PaymentError(new CardDeclined("x"))).RecordFailure(null);
            Result.Fail<int>(new CardDeclined("x")).LogFailure(new ListLogger(), LogLevel.Error);
            Assert.Same(activity, Activity.Current);
        }

        // Anything started inside the block, from any source, would be a child of the app's activity.
        Assert.Single(spans.Stopped);
        Assert.DoesNotContain(spans.Started, a => a.Parent == activity);
    }

    [Fact]
    public void A_null_activity_means_Activity_Current()
    {
        using var spans = new Spans();
        using var activity = spans.Start("charge");

        Result.Fail<int, CardDeclined>(new CardDeclined("4111")).RecordFailure(null);

        Assert.Same(activity, Activity.Current);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("CardDeclined", Tag(activity));
    }

    [Fact]
    public void With_no_current_activity_nothing_happens_and_nothing_throws()
    {
        Assert.Null(Activity.Current);

        var plain = Result.Fail<int>(new CardDeclined("4111")).RecordFailure(null);
        var typed = Result.Fail<int, CardDeclined>(new CardDeclined("4111")).RecordFailure(null);
        var union = Result.Fail<int, PaymentError>(new PaymentError(new CardDeclined("4111"))).RecordFailure(null);

        Assert.True(plain.IsFailure && typed.IsFailure && union.IsFailure);
    }

    [Fact]
    public void Inside_nested_activities_only_the_innermost_is_marked()
    {
        using var spans = new Spans();

        using (var parent = spans.Start("checkout"))
        {
            using (var child = spans.Start("charge"))
            {
                Assert.Same(parent, child.Parent);
                Result.Fail<int>(new CardDeclined("x")).RecordFailure(null);
            }
        }

        var (charge, checkout) = (spans.Stopped[0], spans.Stopped[1]);
        Assert.Equal(("charge", ActivityStatusCode.Error, (object?)"CardDeclined"), (charge.OperationName, charge.Status, Tag(charge)));
        Assert.Equal(("checkout", ActivityStatusCode.Unset), (checkout.OperationName, checkout.Status));
        Assert.Empty(checkout.TagObjects);
    }

    [Fact]
    public void An_explicit_activity_is_marked_even_when_another_is_current()
    {
        using var spans = new Spans();

        using (var parent = spans.Start("checkout"))
        {
            using (spans.Start("charge"))
            {
                Result.Fail<int>(new GatewayTimeout("gw")).RecordFailure(parent);
            }
        }

        var (charge, checkout) = (spans.Stopped[0], spans.Stopped[1]);
        Assert.Equal(ActivityStatusCode.Unset, charge.Status);
        Assert.Equal((ActivityStatusCode.Error, (object?)"GatewayTimeout"), (checkout.Status, Tag(checkout)));
    }

    [Fact]
    public void After_the_inner_activity_stops_the_outer_one_is_current_again()
    {
        using var spans = new Spans();

        using (spans.Start("checkout"))
        {
            using (spans.Start("charge"))
            {
            }

            Result.Fail<int>(new GatewayTimeout("gw")).RecordFailure(null);
        }

        var (charge, checkout) = (spans.Stopped[0], spans.Stopped[1]);
        Assert.Equal(ActivityStatusCode.Unset, charge.Status);
        Assert.Equal(ActivityStatusCode.Error, checkout.Status);
    }

    [Fact]
    public async Task Activity_Current_flows_across_awaits()
    {
        using var spans = new Spans();

        using (spans.Start("charge"))
        {
            await Task.Yield();
            (await Task.Run(() => Result.Fail<int>(new CardDeclined("x")), TestContext.Current.CancellationToken)).RecordFailure(null);
        }

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(spans.Stopped).Status);
    }

    [Fact]
    public void A_second_failure_overwrites_the_tag_with_the_latest_leaf()
    {
        using var spans = new Spans();
        using var activity = spans.Start("charge");

        Result.Fail<int>(new CardDeclined("x")).RecordFailure(activity);
        Result.Fail<int>(new GatewayTimeout("gw")).RecordFailure(activity);

        Assert.Equal("GatewayTimeout", Tag(activity));
        Assert.Single(activity.TagObjects);
    }

    [Fact]
    public void A_success_and_the_zero_value_change_nothing()
    {
        using var spans = new Spans();
        using var activity = spans.Start("charge");

        Assert.Equal(1, Result.Ok(1).RecordFailure(activity).Value);
        default(Result<int>).RecordFailure(activity);
        Result.Ok<int, CardDeclined>(1).RecordFailure(activity);
        default(Result<int, CardDeclined>).RecordFailure(null);
        Result.Ok<int, PaymentError>(1).RecordFailure(activity);
        default(Result<int, PaymentError>).RecordFailure(null);

        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Empty(activity.TagObjects);
    }

    [Fact]
    public void A_validation_failure_carried_in_a_result_is_tagged_with_its_closed_type()
    {
        using var spans = new Spans();
        using var activity = spans.Start("sign-up");

        Validation.Fail<int>(new CardDeclined("4111")).ToResult().RecordFailure(activity);

        Assert.Equal("ValidationFailed<Error>", Tag(activity));
    }

    [Fact]
    public void A_failed_validation_is_tagged_as_one_ValidationFailed_and_returned_unchanged()
    {
        using var spans = new Spans();
        var failed = Validation.Fail<int>([new CardDeclined(Sentinel), new GatewayTimeout("gw")]);
        Validation<int> returned;

        using (var activity = spans.Start("sign-up"))
        {
            returned = failed.RecordFailure(activity);
        }

        var span = Assert.Single(spans.Stopped);
        Assert.Equal((ActivityStatusCode.Error, (string?)null, (object?)"ValidationFailed<Error>"), (span.Status, span.StatusDescription, Tag(span)));
        Assert.Single(span.TagObjects);
        Assert.Equal(failed, returned);
    }

    [Fact]
    public void A_validation_records_on_Activity_Current_and_a_success_or_zero_value_changes_nothing()
    {
        using var spans = new Spans();
        using var activity = spans.Start("sign-up");

        Validation.Ok(1).RecordFailure(activity);
        default(Validation<int>).RecordFailure(null);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);

        Validation.Fail<int>(new CardDeclined("x")).RecordFailure(null);
        Assert.Equal("ValidationFailed<Error>", Tag(activity));
    }

    [Fact]
    public void A_validation_with_no_current_activity_changes_nothing_and_does_not_throw()
    {
        Assert.Null(Activity.Current);
        Assert.True(Validation.Fail<int>(new CardDeclined("x")).RecordFailure(null).IsFailure);
    }

    [Fact]
    public void An_error_union_is_tagged_with_its_leaf_and_returned_unchanged()
    {
        using var spans = new Spans();
        using var activity = spans.Start("charge");
        var failed = Result.Fail<int, PaymentError>(new PaymentError(new GatewayTimeout("gw")));

        var returned = failed.RecordFailure(activity);

        Assert.Equal((ActivityStatusCode.Error, (object?)"GatewayTimeout"), (activity.Status, Tag(activity)));
        Assert.Equal(failed, returned);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void A_csharp_union_is_tagged_with_its_leaf()
    {
        using var spans = new Spans();
        using var activity = spans.Start("charge");
        Result<int, ChargeError> failed = (ChargeError)new GatewayTimeout("gw");

        failed.RecordFailure(null);

        Assert.Equal("GatewayTimeout", Tag(activity));
    }
#endif

    [Fact]
    public void The_tag_name_is_fixed()
    {
        Assert.Equal("resultful.error.type", ResultfulDiagnostics.ErrorTypeTag);
    }
}
