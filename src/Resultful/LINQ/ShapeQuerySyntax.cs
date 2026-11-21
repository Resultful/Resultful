using System;
using System.Threading.Tasks;
using Resultful.Utils;

namespace Resultful.LINQ
{
    // Decision 22 item 3: a generated shape anywhere in a query. A shape's own Select and SelectMany cover a shape source
    // with a core-result step; these cover a shape as a later step, and a shape source followed by an async step. C# infers
    // TNext only from a type the step returns or implements, so the step is read through IResultShape<TNext, TError>: a
    // struct shape is boxed once per step. They live in the core, not the generator, to keep the shape's own surface small.
    /// <summary><c>SelectMany</c> for generated shapes (<see cref="IResultShape{T, TError}"/>) as any step of a LINQ query, sync or before an async step.</summary>
    /// <remarks>A query gives the core <see cref="Result{T, TError}"/> (or a task of one), which converts back to a shape with the same error where it is returned or assigned. Each shape read through the interface is boxed: one allocation per step.</remarks>
    public static class ShapeLinqExtensions
    {
        /// <summary>On success, binds the value to the shape <paramref name="bindFunc"/> returns and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TNext">The type of the intermediate value.</typeparam>
        /// <typeparam name="TOut">The type of the new value.</typeparam>
        /// <param name="value">The shape. A struct shape is boxed.</param>
        /// <param name="bindFunc">Returns the next shape from the value; it is boxed.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Result<TOut, TError> SelectMany<TSource, TError, TNext, TOut>(this IResultShape<TSource, TError> value, Func<TSource, IResultShape<TNext, TError>> bindFunc, Func<TSource, TNext, TOut> mapFunc)
        {
            value.ThrowIfDefault(nameof(value));
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            return value.AsResult().SelectMany(bindFunc, mapFunc);
        }

        /// <summary>On success, binds the value to the shape <paramref name="bindFunc"/> returns and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TNext">The type of the intermediate value.</typeparam>
        /// <typeparam name="TOut">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="bindFunc">Returns the next shape from the value; it is boxed.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Result<TOut, TError> SelectMany<TSource, TError, TNext, TOut>(this Result<TSource, TError> value, Func<TSource, IResultShape<TNext, TError>> bindFunc, Func<TSource, TNext, TOut> mapFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return value.SelectMany(src => bindFunc(src).AsResult(), mapFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then, on success, binds the value to the shape <paramref name="bindFunc"/> returns and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TNext">The type of the intermediate value.</typeparam>
        /// <typeparam name="TOut">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next shape from the value; it is boxed.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TOut, TError>> SelectMany<TSource, TError, TNext, TOut>(this Task<Result<TSource, TError>> value, Func<TSource, IResultShape<TNext, TError>> bindFunc, Func<TSource, TNext, TOut> mapFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            return value.SelectMany(src => bindFunc(src).AsResult(), mapFunc);
        }

        /// <summary>On success, awaits the result <paramref name="bindFunc"/> returns for the shape's value and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TNext">The type of the intermediate value.</typeparam>
        /// <typeparam name="TOut">The type of the new value.</typeparam>
        /// <param name="value">The shape. A struct shape is boxed.</param>
        /// <param name="bindFunc">Returns the task of the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TOut, TError>> SelectMany<TSource, TError, TNext, TOut>(this IResultShape<TSource, TError> value, Func<TSource, Task<Result<TNext, TError>>> bindFunc, Func<TSource, TNext, TOut> mapFunc)
        {
            value.ThrowIfDefault(nameof(value));
            return value.AsResult().SelectMany(bindFunc, mapFunc);
        }
#if NET

        /// <summary>On success, awaits the result <paramref name="bindFunc"/> returns for the shape's value and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TNext">The type of the intermediate value.</typeparam>
        /// <typeparam name="TOut">The type of the new value.</typeparam>
        /// <param name="value">The shape. A struct shape is boxed.</param>
        /// <param name="bindFunc">Returns the ValueTask of the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TOut, TError>> SelectMany<TSource, TError, TNext, TOut>(this IResultShape<TSource, TError> value, Func<TSource, ValueTask<Result<TNext, TError>>> bindFunc, Func<TSource, TNext, TOut> mapFunc)
        {
            value.ThrowIfDefault(nameof(value));
            return value.AsResult().SelectMany(bindFunc, mapFunc);
        }
#endif
    }
}
