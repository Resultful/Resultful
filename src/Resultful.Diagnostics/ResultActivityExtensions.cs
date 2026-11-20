using System.Diagnostics;

namespace Resultful.Diagnostics;

/// <summary>
/// Records a failure on an <see cref="Activity"/> when a call asks for it: the status becomes
/// <see cref="ActivityStatusCode.Error"/> and <see cref="ResultfulDiagnostics.ErrorTypeTag"/> is set to the error's
/// leaf type name. The message and the leaf's properties are not recorded. A success, the zero value and a missing
/// activity change nothing. Each returns the result unchanged.
/// </summary>
/// <remarks>A result whose error is a union (decision 17) widens first: <c>result.AsResult().RecordFailure(…)</c>.</remarks>
public static class ResultActivityExtensions
{
    /// <summary>Records the result's failure on <paramref name="activity"/>, or on <see cref="Activity.Current"/> when it is null.</summary>
    /// <returns>The same result.</returns>
    public static Result<T> RecordFailure<T>(this Result<T> result, Activity? activity)
    {
        if (result.IsFailure) Record(activity ?? Activity.Current, result.Error);
        return result;
    }

    /// <summary>Records the result's failure on <paramref name="activity"/>, or on <see cref="Activity.Current"/> when it is null.</summary>
    /// <returns>The same result.</returns>
    public static Result<T, TError> RecordFailure<T, TError>(this Result<T, TError> result, Activity? activity)
        where TError : Error
    {
        if (result.IsFailure) Record(activity ?? Activity.Current, result.Error);
        return result;
    }

    private static void Record(Activity? activity, Error error)
    {
        if (activity is null) return;
        activity.SetStatus(ActivityStatusCode.Error);
        activity.SetTag(ResultfulDiagnostics.ErrorTypeTag, ErrorDescription.TypeName(error.GetType()));
    }
}
