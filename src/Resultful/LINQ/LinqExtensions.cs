using System;
using Resultful.Utils;

namespace Resultful.LINQ
{
    /// <summary><c>Select</c> and <c>SelectMany</c> for options and results, so they work in LINQ query syntax.</summary>
    public static class LinqExtensions
    {

        //Linq Extensions for Option<T>
        /// <summary>Same as <c>Map</c>: if the option has a value, returns an option with the value <paramref name="mapFunc"/> gives for it. None stays None. Used by LINQ <c>select</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The option.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Option<TResult> Select<TSource, TResult>(this Option<TSource> value, Func<TSource, TResult> mapFunc)
            => value.Map(mapFunc);

        /// <summary>Same as <c>Bind</c>: if the option has a value, returns what <paramref name="bindFunc"/> returns for it. None stays None.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The option.</param>
        /// <param name="bindFunc">Returns the next option from the value.</param>
        public static Option<TResult> SelectMany<TSource, TResult>(this Option<TSource> value, Func<TSource, Option<TResult>> bindFunc)
            => value.Bind(bindFunc);

        /// <summary>If the option has a value, binds it with <paramref name="bindFunc"/>, then combines the source value and the bound value with <paramref name="mapFunc"/>. Gives None if either is None. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The option.</param>
        /// <param name="bindFunc">Returns the next option from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Option<TResult> SelectMany<TSource, TCollection, TResult>(this Option<TSource> value, Func<TSource, Option<TCollection>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
            => value.Bind(src => bindFunc.ThrowIfDefault(nameof(bindFunc))(src).Map(elem => mapFunc.ThrowIfDefault(nameof(mapFunc))(src, elem)));

        /*
        public static Option<TSource> Where<TSource>(this Option<TSource> value, Func<TSource, bool> filterFunc)
            => value.Filter(filterFunc.ThrowIfDefault(nameof(filterFunc)));
        */

        //Linq Extensions for Result<T, TError>
        /// <summary>Same as <c>Map</c>: on success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps its error. Used by LINQ <c>select</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Result<TResult, TError> Select<TSource, TError, TResult>(this Result<TSource, TError> value, Func<TSource, TResult> mapFunc)
            => value.Map(mapFunc);

        /// <summary>Same as <c>Bind</c>: on success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps its error.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public static Result<TResult, TError> SelectMany<TSource, TError, TResult>(this Result<TSource, TError> value, Func<TSource, Result<TResult, TError>> bindFunc)
            => value.Bind(bindFunc);

        /// <summary>On success, binds the value with <paramref name="bindFunc"/>, then combines the source value and the bound value with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Result<TResult, TError> SelectMany<TSource, TError, TCollection, TResult>(this Result<TSource, TError> value, Func<TSource, Result<TCollection, TError>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
            => value.Bind(src => bindFunc.ThrowIfDefault(nameof(bindFunc))(src).Map(elem => mapFunc.ThrowIfDefault(nameof(mapFunc))(src, elem)));

        //Linq Extensions for Result<T>
        /// <summary>Same as <c>Map</c>: on success, returns the value <paramref name="mapFunc"/> gives for the value, as a success. A failure keeps its errors. Used by LINQ <c>select</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Result<TResult> Select<TSource, TResult>(this Result<TSource> value, Func<TSource, TResult> mapFunc)
            => value.Map(mapFunc);

        /// <summary>Same as <c>Bind</c>: on success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps its errors.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public static Result<TResult> SelectMany<TSource, TResult>(this Result<TSource> value, Func<TSource, Result<TResult>> bindFunc)
            => value.Bind(bindFunc);

        /// <summary>On success, binds the value with <paramref name="bindFunc"/>, then combines the source value and the bound value with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its errors. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Result<TResult> SelectMany<TSource, TCollection, TResult>(this Result<TSource> value, Func<TSource, Result<TCollection>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
            => value.Bind(src => bindFunc.ThrowIfDefault(nameof(bindFunc))(src).Map(elem => mapFunc.ThrowIfDefault(nameof(mapFunc))(src, elem)));



    }
}
