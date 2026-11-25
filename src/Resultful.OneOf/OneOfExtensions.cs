using System;
using System.Threading.Tasks;
using OneOf;
using OneOfNone = OneOf.Types.None;

namespace Resultful
{
    /// <summary>
    /// Conversions between Resultful's types and OneOf unions. They live in this package so the core references nothing
    /// (decision 1); referencing <c>Resultful.OneOf</c> brings back the 0.2 <c>ToOneOf</c> calls unchanged.
    /// </summary>
    /// <remarks>A result's zero value (<c>default</c>) has no case, so <c>ToOneOf</c> throws <see cref="InvalidOperationException"/> on it, as every other member that reads it does.</remarks>
    public static class OneOfExtensions
    {
        //Resultful -> OneOf
        /// <summary>Returns the result as a <c>OneOf</c>: <c>T0</c> is the success value, <c>T1</c> is the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        public static OneOf<T, TError> ToOneOf<T, TError>(this Result<T, TError> value)
            => value.Match(OneOf<T, TError>.FromT0, OneOf<T, TError>.FromT1);

        /// <summary>Returns the result as a <c>OneOf</c>: <c>T0</c> is the success value, <c>T1</c> is the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The result.</param>
        public static OneOf<T, Error> ToOneOf<T>(this Result<T> value)
            => value.Match(OneOf<T, Error>.FromT0, OneOf<T, Error>.FromT1);

        /// <summary>Returns the result as a <c>OneOf</c>: <c>T0</c> is the <see cref="Unit"/> of a success, <c>T1</c> is the error.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The result.</param>
        public static OneOf<Unit, TError> ToOneOf<TError>(this VoidResult<TError> value)
            => value.Match(OneOf<Unit, TError>.FromT0, OneOf<Unit, TError>.FromT1);

        /// <summary>Returns the result as a <c>OneOf</c>: <c>T0</c> is the <see cref="Unit"/> of a success, <c>T1</c> is the error.</summary>
        /// <param name="value">The result.</param>
        public static OneOf<Unit, Error> ToOneOf(this VoidResult value)
            => value.Match(OneOf<Unit, Error>.FromT0, OneOf<Unit, Error>.FromT1);

        /// <summary>Returns the option as a <c>OneOf</c>: <c>T0</c> is the value, <c>T1</c> is OneOf's <c>None</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The option.</param>
        public static OneOf<T, OneOfNone> ToOneOf<T>(this Option<T> value)
            => value.Match(OneOf<T, OneOfNone>.FromT0, _ => OneOf<T, OneOfNone>.FromT1(default));

        //Task<Resultful> -> Task<OneOf>
        /// <summary>Awaits <paramref name="value"/>, then returns the result as a <c>OneOf</c>: <c>T0</c> is the success value, <c>T1</c> is the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        public static async Task<OneOf<T, TError>> ToOneOf<T, TError>(this Task<Result<T, TError>> value)
            => (await NotNull(value).ConfigureAwait(false)).ToOneOf();

        /// <summary>Awaits <paramref name="value"/>, then returns the result as a <c>OneOf</c>: <c>T0</c> is the success value, <c>T1</c> is the error.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        public static async Task<OneOf<T, Error>> ToOneOf<T>(this Task<Result<T>> value)
            => (await NotNull(value).ConfigureAwait(false)).ToOneOf();

        /// <summary>Awaits <paramref name="value"/>, then returns the result as a <c>OneOf</c>: <c>T0</c> is the <see cref="Unit"/> of a success, <c>T1</c> is the error.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        public static async Task<OneOf<Unit, TError>> ToOneOf<TError>(this Task<VoidResult<TError>> value)
            => (await NotNull(value).ConfigureAwait(false)).ToOneOf();

        /// <summary>Awaits <paramref name="value"/>, then returns the result as a <c>OneOf</c>: <c>T0</c> is the <see cref="Unit"/> of a success, <c>T1</c> is the error.</summary>
        /// <param name="value">The task that produces the result.</param>
        public static async Task<OneOf<Unit, Error>> ToOneOf(this Task<VoidResult> value)
            => (await NotNull(value).ConfigureAwait(false)).ToOneOf();

        /// <summary>Awaits <paramref name="value"/>, then returns the option as a <c>OneOf</c>: <c>T0</c> is the value, <c>T1</c> is OneOf's <c>None</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the option.</param>
        public static async Task<OneOf<T, OneOfNone>> ToOneOf<T>(this Task<Option<T>> value)
            => (await NotNull(value).ConfigureAwait(false)).ToOneOf();

        //OneOf -> Resultful
        /// <summary>Returns a success with <c>T0</c>, or a failure with <c>T1</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The union.</param>
        public static Result<T, TError> ToResult<T, TError>(this OneOf<T, TError> value)
            => value.Match(Result.Ok<T, TError>, Result.Fail<T, TError>);

        /// <summary>Returns a success with <c>T0</c>, or a failure with the <see cref="Error"/> in <c>T1</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The union.</param>
        /// <exception cref="ArgumentNullException">The union holds a <c>null</c> error.</exception>
        public static Result<T> ToResult<T>(this OneOf<T, Error> value)
            => value.Match(Result.Ok, Result.Fail<T>);

        /// <summary>Returns a success for <c>T0</c>, or a failure with <c>T1</c>.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The union.</param>
        public static VoidResult<TError> ToVoidResult<TError>(this OneOf<Unit, TError> value)
            => value.Match(_ => Result.Ok<TError>(), Result.Fail<TError>);

        /// <summary>Returns a success for <c>T0</c>, or a failure with the <see cref="Error"/> in <c>T1</c>.</summary>
        /// <param name="value">The union.</param>
        /// <exception cref="ArgumentNullException">The union holds a <c>null</c> error.</exception>
        public static VoidResult ToVoidResult(this OneOf<Unit, Error> value)
            => value.Match(_ => Result.Ok(), Result.Fail);

        /// <summary>Returns an option with <c>T0</c>, or <see cref="Option{T}.None"/> for OneOf's <c>None</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The union.</param>
        public static Option<T> ToOption<T>(this OneOf<T, OneOfNone> value)
            => value.Match(Option.Some, _ => Option<T>.None);

        //Task<OneOf> -> Task<Resultful>. Without these, `await task.ToOption()` would bind core's ToOption<T>(this Task<T>).
        /// <summary>Awaits <paramref name="value"/>, then returns a success with <c>T0</c>, or a failure with <c>T1</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the union.</param>
        public static async Task<Result<T, TError>> ToResult<T, TError>(this Task<OneOf<T, TError>> value)
            => (await NotNull(value).ConfigureAwait(false)).ToResult();

        /// <summary>Awaits <paramref name="value"/>, then returns a success with <c>T0</c>, or a failure with the <see cref="Error"/> in <c>T1</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the union.</param>
        public static async Task<Result<T>> ToResult<T>(this Task<OneOf<T, Error>> value)
            => (await NotNull(value).ConfigureAwait(false)).ToResult();

        /// <summary>Awaits <paramref name="value"/>, then returns a success for <c>T0</c>, or a failure with <c>T1</c>.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="value">The task that produces the union.</param>
        public static async Task<VoidResult<TError>> ToVoidResult<TError>(this Task<OneOf<Unit, TError>> value)
            => (await NotNull(value).ConfigureAwait(false)).ToVoidResult();

        /// <summary>Awaits <paramref name="value"/>, then returns a success for <c>T0</c>, or a failure with the <see cref="Error"/> in <c>T1</c>.</summary>
        /// <param name="value">The task that produces the union.</param>
        public static async Task<VoidResult> ToVoidResult(this Task<OneOf<Unit, Error>> value)
            => (await NotNull(value).ConfigureAwait(false)).ToVoidResult();

        /// <summary>Awaits <paramref name="value"/>, then returns an option with <c>T0</c>, or <see cref="Option{T}.None"/> for OneOf's <c>None</c>.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The task that produces the union.</param>
        public static async Task<Option<T>> ToOption<T>(this Task<OneOf<T, OneOfNone>> value)
            => (await NotNull(value).ConfigureAwait(false)).ToOption();

        //None <-> None
#pragma warning disable IDE0060 // The value carries no information: every None is the same.
        /// <summary>Returns Resultful's <see cref="None"/> for OneOf's.</summary>
        /// <param name="value">OneOf's none.</param>
        public static None ToResultful(this OneOfNone value)
            => default;

        /// <summary>Returns OneOf's <c>None</c> for Resultful's.</summary>
        /// <param name="value">Resultful's none.</param>
        public static OneOfNone ToOneOf(this None value)
            => default;
#pragma warning restore IDE0060

        static Task<T> NotNull<T>(Task<T> value)
            => value ?? throw new ArgumentNullException(nameof(value));
    }
}
