#nullable enable
using System;
using System.Threading.Tasks;

// Decision 9: opt-in sugar, one `using Resultful.Operators;`. Each operator is a one-line call to a named method.
// Decision 14 and DECISIONS-TAKEN (04c, accepted): no `+ - *` anywhere; `&` belongs to Validation; Result gets
// `|` (OrElse, ValueOr), `||` (lazy OrElse, through true/false) and `>>` (Bind, one type-changing form only).
namespace Resultful.Operators
{
    public static class ResultOperators
    {
        extension<T, TError>(Result<T, TError>)
        {
            public static Result<T, TError> operator |(Result<T, TError> left, Result<T, TError> right) => throw null!;
            public static T operator |(Result<T, TError> left, T fallback) => throw null!;
            public static bool operator true(Result<T, TError> value) => throw null!;
            public static bool operator false(Result<T, TError> value) => throw null!;
        }

        extension<T, TResult, TError>(Result<T, TError>)
        {
            public static Result<TResult, TError> operator >>(Result<T, TError> left, Func<T, Result<TResult, TError>> bind) => throw null!;
        }

        extension<T>(Result<T>)
        {
            public static Result<T> operator |(Result<T> left, Result<T> right) => throw null!;
            public static T operator |(Result<T> left, T fallback) => throw null!;
            public static bool operator true(Result<T> value) => throw null!;
            public static bool operator false(Result<T> value) => throw null!;
        }

        extension<T, TResult>(Result<T>)
        {
            public static Result<TResult> operator >>(Result<T> left, Func<T, Result<TResult>> bind) => throw null!;
        }

        // The same operators on Task<Result<…>> (decision 9).
        extension<T, TError>(Task<Result<T, TError>>)
        {
            public static Task<Result<T, TError>> operator |(Task<Result<T, TError>> left, Result<T, TError> right) => throw null!;
            public static Task<T> operator |(Task<Result<T, TError>> left, T fallback) => throw null!;
        }

        extension<T, TResult, TError>(Task<Result<T, TError>>)
        {
            public static Task<Result<TResult, TError>> operator >>(Task<Result<T, TError>> left, Func<T, Task<Result<TResult, TError>>> bind) => throw null!;
        }

        extension<T>(Task<Result<T>>)
        {
            public static Task<Result<T>> operator |(Task<Result<T>> left, Result<T> right) => throw null!;
            public static Task<T> operator |(Task<Result<T>> left, T fallback) => throw null!;
        }

        extension<T, TResult>(Task<Result<T>>)
        {
            public static Task<Result<TResult>> operator >>(Task<Result<T>> left, Func<T, Task<Result<TResult>>> bind) => throw null!;
        }
    }

    public static class ValidationOperators
    {
        // `&` keeps both values as a tuple, or every error from both sides in order: Validation.Combine(left, right).
        extension<T1, T2, TError>(Validation<T1, TError>)
        {
            public static Validation<(T1, T2), TError> operator &(Validation<T1, TError> left, Validation<T2, TError> right) => throw null!;
        }

        extension<T1, T2>(Validation<T1>)
        {
            public static Validation<(T1, T2)> operator &(Validation<T1> left, Validation<T2> right) => throw null!;
        }
    }
}
