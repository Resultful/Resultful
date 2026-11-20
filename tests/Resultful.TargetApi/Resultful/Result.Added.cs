#nullable enable
using System;
using System.Threading.Tasks;

namespace Resultful
{
    public static partial class Result
    {
        // Decision 14: Fail<T>(string) keeps a message-only failure short; it builds a MessageError.
        // Fail(null!) throws ArgumentNullException (ZERO-NULL-ERR).
        public static Result<Unit> Fail(string message) => throw null!;
        public static Result<T> Fail<T>(string message) => throw null!;

        // Decision 14: widening to the general error, named because C# can't express it as a generic conversion.
        public static Result<T> AsResult<T, TError>(this Result<T, TError> result) where TError : Error => throw null!;

        // A note on the error that keeps both the error's type and the result's (decision 14, decision 16 item 4).
        public static Result<T, TError> WithContext<T, TError>(this Result<T, TError> result, string message) where TError : Error => throw null!;

        // The opt-in way to turn an exception into a failure (19). Nothing else in the core catches.
        // OperationCanceledException is never caught: cancellation propagates.
        public static Result<T> Try<T>(Func<T> func, Func<Exception, Error> onException) => throw null!;
        public static Task<Result<T>> TryAsync<T>(Func<Task<T>> func, Func<Exception, Error> onException) => throw null!;
    }
}
