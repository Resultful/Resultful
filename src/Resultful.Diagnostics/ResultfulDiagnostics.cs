namespace Resultful.Diagnostics;

/// <summary>Names the diagnostics hooks write.</summary>
public static class ResultfulDiagnostics
{
    /// <summary>
    /// The activity tag a failure sets: the error's leaf type name (<c>CardDeclined</c>,
    /// <c>ValidationFailed&lt;Error&gt;</c>).
    /// </summary>
    /// <remarks>
    /// Not OpenTelemetry's <c>error.type</c>: ASP.NET Core and HttpClient instrumentation set <c>error.type</c> on the
    /// HTTP span themselves (to a status code or an exception type), so a Resultful value there would overwrite theirs
    /// or be overwritten. The <c>resultful.</c> prefix keeps the two apart.
    /// </remarks>
    public const string ErrorTypeTag = "resultful.error.type";
}
