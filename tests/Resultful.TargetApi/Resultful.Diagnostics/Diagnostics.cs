#nullable enable
using System.Diagnostics;
using Microsoft.Extensions.Logging;

// M5 (21). Opt-in hooks: nothing is logged or tagged unless a call asks for it (decision 11).
namespace Resultful.Diagnostics
{
    public static class ResultfulDiagnostics
    {
        // The tag a failure sets on an activity: the error's leaf type name.
        public const string ErrorTypeTag = "resultful.error.type";
    }

    public static class ResultLoggingExtensions
    {
        // Logs a failure's Message, Context and leaf type name (never the leaf's properties) and returns the result.
        public static Result<T> LogFailure<T>(this Result<T> result, ILogger logger, LogLevel level) => throw null!;
        public static Result<T, TError> LogFailure<T, TError>(this Result<T, TError> result, ILogger logger, LogLevel level) where TError : Error => throw null!;
        public static Validation<T> LogFailure<T>(this Validation<T> result, ILogger logger, LogLevel level) => throw null!;
    }

    public static class ResultActivityExtensions
    {
        // On a failure, sets the activity's status to Error and the error type tag. A null activity means Activity.Current.
        public static Result<T> RecordFailure<T>(this Result<T> result, Activity? activity) => throw null!;
        public static Result<T, TError> RecordFailure<T, TError>(this Result<T, TError> result, Activity? activity) where TError : Error => throw null!;
    }
}
