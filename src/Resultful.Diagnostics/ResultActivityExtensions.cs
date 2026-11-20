using System.Diagnostics;

namespace Resultful.Diagnostics;

/// <summary>
/// Records a failure on an <see cref="Activity"/> when a call asks for it: the status becomes
/// <see cref="ActivityStatusCode.Error"/> and <see cref="ResultfulDiagnostics.ErrorTypeTag"/> is set to the error's
/// leaf type name. The message and the leaf's properties are not recorded. A success, the zero value and a missing
/// activity change nothing. Each returns the result unchanged.
/// </summary>
/// <remarks>
/// <para>
/// A null activity falls back to <see cref="Activity.Current"/>. In ASP.NET Core that is often the HTTP server span,
/// and OpenTelemetry's HTTP conventions say a server span is not an error for a 4xx response. So for an expected
/// failure (a validation failure, a not-found), start and pass your own activity rather than marking the request's
/// span as failed.
/// </para>
/// <para>A result whose error is a union (decision 17) widens first: <c>result.AsResult().RecordFailure(…)</c>.</para>
/// </remarks>
public static class ResultActivityExtensions
{
    /// <summary>Records the result's failure on <paramref name="activity"/>, or on <see cref="Activity.Current"/> when it is null.</summary>
    /// <param name="result">The result.</param>
    /// <param name="activity">The activity to mark. Null means <see cref="Activity.Current"/>, which may be the HTTP server span: pass your own for an expected failure.</param>
    /// <returns>The same result.</returns>
    public static Result<T> RecordFailure<T>(this Result<T> result, Activity? activity)
    {
        if (result.IsFailure) Record(activity ?? Activity.Current, result.Error);
        return result;
    }

    /// <summary>Records the result's failure on <paramref name="activity"/>, or on <see cref="Activity.Current"/> when it is null.</summary>
    /// <param name="result">The result.</param>
    /// <param name="activity">The activity to mark. Null means <see cref="Activity.Current"/>, which may be the HTTP server span: pass your own for an expected failure.</param>
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
