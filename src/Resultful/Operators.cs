using System;
using System.Threading.Tasks;

// Decision 9: opt-in sugar, one `using Resultful.Operators;`. Each operator is a one-line call to a named method, and
// the named method is the API. Decision 14 and DECISIONS-TAKEN (04c): no `+ - *`; `&` belongs to Validation; a result
// gets `|` (OrElse, ValueOr), `||` (a lazy OrElse, through true and false) and `>>` (Bind, one type-changing form).
namespace Resultful.Operators
{
    /// <summary>Operators on <see cref="Result{T, TError}"/>, <see cref="Result{T}"/> and tasks of them, each a call to a named method. Opt in with <c>using Resultful.Operators;</c>.</summary>
    public static class ResultOperators
    {
        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        extension<T, TError>(Result<T, TError>)
        {
            /// <summary><c>left | right</c> is <c>left.OrElse(right)</c>: the first success wins.</summary>
            /// <param name="left">The result to try first.</param>
            /// <param name="right">The result to use when <paramref name="left"/> is a failure.</param>
            public static Result<T, TError> operator |(Result<T, TError> left, Result<T, TError> right) => left.OrElse(right);

            /// <summary><c>left | fallback</c> is <c>left.ValueOr(fallback)</c>: the value, or <paramref name="fallback"/> on a failure.</summary>
            /// <param name="left">The result to unwrap.</param>
            /// <param name="fallback">The value to use when <paramref name="left"/> is a failure.</param>
            public static T operator |(Result<T, TError> left, T fallback) => left.ValueOr(fallback);

            /// <summary><see cref="Result{T, TError}.IsSuccess"/>, so <c>if (result)</c> tests for a success and <c>left || right</c> reads <paramref name="value"/>'s right side only on a failure.</summary>
            /// <param name="value">The result.</param>
            public static bool operator true(Result<T, TError> value) => value.IsSuccess;

            /// <summary><see cref="Result{T, TError}.IsFailure"/>, the other half of <c>operator true</c>.</summary>
            /// <param name="value">The result.</param>
            public static bool operator false(Result<T, TError> value) => value.IsFailure;
        }

        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TResult">The type of the next value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        extension<T, TResult, TError>(Result<T, TError>)
        {
            /// <summary><c>left &gt;&gt; bind</c> is <c>left.Bind(bind)</c>. A lambda on the right needs parentheses.</summary>
            /// <param name="left">The result.</param>
            /// <param name="bind">Returns the next result from the value; called only on a success.</param>
            public static Result<TResult, TError> operator >>(Result<T, TError> left, Func<T, Result<TResult, TError>> bind) => left.Bind(bind);
        }

        /// <typeparam name="T">The type of the success value.</typeparam>
        extension<T>(Result<T>)
        {
            /// <summary><c>left | right</c> is <c>left.OrElse(right)</c>: the first success wins.</summary>
            /// <param name="left">The result to try first.</param>
            /// <param name="right">The result to use when <paramref name="left"/> is a failure.</param>
            public static Result<T> operator |(Result<T> left, Result<T> right) => left.OrElse(right);

            /// <summary><c>left | fallback</c> is <c>left.ValueOr(fallback)</c>: the value, or <paramref name="fallback"/> on a failure.</summary>
            /// <param name="left">The result to unwrap.</param>
            /// <param name="fallback">The value to use when <paramref name="left"/> is a failure.</param>
            public static T operator |(Result<T> left, T fallback) => left.ValueOr(fallback);

            /// <summary><see cref="Result{T}.IsSuccess"/>, so <c>if (result)</c> tests for a success and <c>left || right</c> reads the right side only on a failure.</summary>
            /// <param name="value">The result.</param>
            public static bool operator true(Result<T> value) => value.IsSuccess;

            /// <summary><see cref="Result{T}.IsFailure"/>, the other half of <c>operator true</c>.</summary>
            /// <param name="value">The result.</param>
            public static bool operator false(Result<T> value) => value.IsFailure;
        }

        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TResult">The type of the next value.</typeparam>
        extension<T, TResult>(Result<T>)
        {
            /// <summary><c>left &gt;&gt; bind</c> is <c>left.Bind(bind)</c>. A lambda on the right needs parentheses.</summary>
            /// <param name="left">The result.</param>
            /// <param name="bind">Returns the next result from the value; called only on a success.</param>
            public static Result<TResult> operator >>(Result<T> left, Func<T, Result<TResult>> bind) => left.Bind(bind);
        }

        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        extension<T, TError>(Task<Result<T, TError>>)
        {
            /// <summary><c>left | right</c> is <c>left.OrElse(right)</c>, once <paramref name="left"/> completes.</summary>
            /// <param name="left">The task that produces the result to try first.</param>
            /// <param name="right">The result to use when the awaited result is a failure.</param>
            public static Task<Result<T, TError>> operator |(Task<Result<T, TError>> left, Result<T, TError> right) => left.OrElse(right);

            /// <summary><c>left | fallback</c> is <c>left.ValueOr(fallback)</c>, once <paramref name="left"/> completes.</summary>
            /// <param name="left">The task that produces the result to unwrap.</param>
            /// <param name="fallback">The value to use when the awaited result is a failure.</param>
            public static Task<T> operator |(Task<Result<T, TError>> left, T fallback) => left.ValueOr(fallback);
        }

        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TResult">The type of the next value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        extension<T, TResult, TError>(Task<Result<T, TError>>)
        {
            /// <summary><c>left &gt;&gt; bind</c> is <c>left.BindAsync(bind)</c>.</summary>
            /// <param name="left">The task that produces the result.</param>
            /// <param name="bind">Returns the next result from the value; called only on a success.</param>
            public static Task<Result<TResult, TError>> operator >>(Task<Result<T, TError>> left, Func<T, Task<Result<TResult, TError>>> bind) => left.BindAsync(bind);
        }

        /// <typeparam name="T">The type of the success value.</typeparam>
        extension<T>(Task<Result<T>>)
        {
            /// <summary><c>left | right</c> is <c>left.OrElse(right)</c>, once <paramref name="left"/> completes.</summary>
            /// <param name="left">The task that produces the result to try first.</param>
            /// <param name="right">The result to use when the awaited result is a failure.</param>
            public static Task<Result<T>> operator |(Task<Result<T>> left, Result<T> right) => left.OrElse(right);

            /// <summary><c>left | fallback</c> is <c>left.ValueOr(fallback)</c>, once <paramref name="left"/> completes.</summary>
            /// <param name="left">The task that produces the result to unwrap.</param>
            /// <param name="fallback">The value to use when the awaited result is a failure.</param>
            public static Task<T> operator |(Task<Result<T>> left, T fallback) => left.ValueOr(fallback);
        }

        /// <typeparam name="T">The type of the success value.</typeparam>
        /// <typeparam name="TResult">The type of the next value.</typeparam>
        extension<T, TResult>(Task<Result<T>>)
        {
            /// <summary><c>left &gt;&gt; bind</c> is <c>left.BindAsync(bind)</c>.</summary>
            /// <param name="left">The task that produces the result.</param>
            /// <param name="bind">Returns the next result from the value; called only on a success.</param>
            public static Task<Result<TResult>> operator >>(Task<Result<T>> left, Func<T, Task<Result<TResult>>> bind) => left.BindAsync(bind);
        }
    }

    /// <summary>Operators on <see cref="Validation{T, TError}"/> and <see cref="Validation{T}"/>, each a call to a named method. Opt in with <c>using Resultful.Operators;</c>.</summary>
    public static class ValidationOperators
    {
        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="TError">The type of each error.</typeparam>
        extension<T1, T2, TError>(Validation<T1, TError>)
        {
            /// <summary><c>left &amp; right</c> is <c>Validation.Combine(left, right)</c>: both values as a tuple, or every error from both sides, in order.</summary>
            /// <param name="left">The first validation.</param>
            /// <param name="right">The second validation.</param>
            public static Validation<(T1, T2), TError> operator &(Validation<T1, TError> left, Validation<T2, TError> right) => Validation.Combine(left, right);
        }

        /// <typeparam name="T1">The type of the first value.</typeparam>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        extension<T1, T2>(Validation<T1>)
        {
            /// <summary><c>left &amp; right</c> is <c>Validation.Combine(left, right)</c>: both values as a tuple, or every error from both sides, in order.</summary>
            /// <param name="left">The first validation.</param>
            /// <param name="right">The second validation.</param>
            public static Validation<(T1, T2)> operator &(Validation<T1> left, Validation<T2> right) => Validation.Combine(left, right);
        }
    }
}
