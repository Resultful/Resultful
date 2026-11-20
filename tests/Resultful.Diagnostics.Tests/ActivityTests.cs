using System.Diagnostics;

namespace Resultful.Diagnostics.Tests;

// Activity.Current is AsyncLocal, and each xunit test runs in its own execution context, so the tests don't share it.
public sealed class ActivityTests
{
    [Fact]
    public void A_failure_sets_the_status_and_the_leaf_type_tag_and_nothing_else()
    {
        using var activity = new Activity("charge").Start();
        var failed = Result.Fail<int>(new CardDeclined("4111111111111111"));

        var returned = failed.RecordFailure(activity);

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Null(activity.StatusDescription);
        var tag = Assert.Single(activity.TagObjects);
        Assert.Equal((ResultfulDiagnostics.ErrorTypeTag, (object?)"CardDeclined"), (tag.Key, tag.Value));
        Assert.Equal(failed, returned);
    }

    [Fact]
    public void A_null_activity_means_Activity_Current()
    {
        using var activity = new Activity("charge").Start();

        Result.Fail<int, CardDeclined>(new CardDeclined("4111")).RecordFailure(null);

        Assert.Same(activity, Activity.Current);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("CardDeclined", activity.GetTagItem(ResultfulDiagnostics.ErrorTypeTag));
    }

    [Fact]
    public void With_no_activity_nothing_happens()
    {
        Assert.Null(Activity.Current);

        var returned = Result.Fail<int>(new CardDeclined("4111")).RecordFailure(null);

        Assert.True(returned.IsFailure);
    }

    [Fact]
    public void A_success_and_the_zero_value_change_nothing()
    {
        using var activity = new Activity("charge").Start();

        Result.Ok(1).RecordFailure(activity);
        default(Result<int>).RecordFailure(activity);
        Result.Ok<int, CardDeclined>(1).RecordFailure(activity);
        default(Result<int, CardDeclined>).RecordFailure(activity);

        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Empty(activity.TagObjects);
    }

    [Fact]
    public void A_validation_failure_carried_in_a_result_is_tagged_with_its_closed_type()
    {
        using var activity = new Activity("sign-up").Start();

        Validation.Fail<int>(new CardDeclined("4111")).ToResult().RecordFailure(activity);

        Assert.Equal("ValidationFailed<Error>", activity.GetTagItem(ResultfulDiagnostics.ErrorTypeTag));
    }

    [Fact]
    public void The_tag_name_is_fixed()
    {
        Assert.Equal("resultful.error.type", ResultfulDiagnostics.ErrorTypeTag);
    }
}
