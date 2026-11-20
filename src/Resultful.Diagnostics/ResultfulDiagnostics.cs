namespace Resultful.Diagnostics;

/// <summary>Names the diagnostics hooks write.</summary>
public static class ResultfulDiagnostics
{
    /// <summary>
    /// The activity tag a failure sets: the error's leaf type name (<c>CardDeclined</c>,
    /// <c>ValidationFailed&lt;Error&gt;</c>).
    /// </summary>
    public const string ErrorTypeTag = "resultful.error.type";
}
