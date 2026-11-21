using System;
using System.Threading.Tasks;
using Resultful.Utils;

namespace Resultful.LINQ
{
    // Decision 22 item 3: query syntax with async steps. Once one step is a Task or a ValueTask the query is a
    // Task<Result<...>>: every form returns Task, so Task and ValueTask steps mix in one query and CA2012 has no ValueTask
    // to flag (query translation hides the await). Each step starts only after the one before it succeeded: a failure
    // ends the query and later steps are never called. Nothing here catches: an exception a step throws, or a faulted or
    // cancelled task, propagates unchanged. ValueTask steps are on net10.0 and later only (netstandard2.0 has no ValueTask
    // without a package, decision 1).
    /// <summary><c>Select</c> and <c>SelectMany</c> over tasks of results, mixed with sync steps, so any step of a LINQ query can be async. The query is a <see cref="Task{TResult}"/>; on net10.0 and later a step can also be a <c>ValueTask</c>.</summary>
    /// <remarks>A failure ends the query: later steps aren't called. Exceptions and cancellation propagate unchanged; nothing is caught. A <c>Select</c> or <c>SelectMany</c> for tasks declared in a namespace enclosing the caller's is found first and takes precedence.</remarks>
    public static class AsyncLinqExtensions
    {
        //Result<T>
        /// <summary>Awaits <paramref name="value"/>, then, on success, maps the value with <paramref name="mapFunc"/>. A failure keeps its error. Used by LINQ <c>select</c> and <c>let</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Task<Result<TResult>> Select<TSource, TResult>(this Task<Result<TSource>> value, Func<TSource, TResult> mapFunc)
        {
            value.ThrowIfDefault(nameof(value));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, mapFunc);

            static async Task<Result<TResult>> Core(Task<Result<TSource>> value, Func<TSource, TResult> mapFunc)
                => (await value.ConfigureAwait(false)).Map(mapFunc);
        }

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns for the value, awaited. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="bindFunc">Returns the task of the next result from the value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TResult>(this Result<TSource> value, Func<TSource, Task<Result<TResult>>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>On success, binds the value with <paramref name="bindFunc"/>, awaits it, and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="bindFunc">Returns the task of the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TCollection, TResult>(this Result<TSource> value, Func<TSource, Task<Result<TCollection>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return StepAsync(value, bindFunc, mapFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then, on success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TResult>(this Task<Result<TSource>> value, Func<TSource, Result<TResult>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>Awaits <paramref name="value"/>, then, on success, binds the value with <paramref name="bindFunc"/> and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TCollection, TResult>(this Task<Result<TSource>> value, Func<TSource, Result<TCollection>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            value.ThrowIfDefault(nameof(value));
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, bindFunc, mapFunc);

            static async Task<Result<TResult>> Core(Task<Result<TSource>> value, Func<TSource, Result<TCollection>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
                => Step(await value.ConfigureAwait(false), bindFunc, mapFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then, on success, returns what <paramref name="bindFunc"/> returns for the value, awaited. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the task of the next result from the value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TResult>(this Task<Result<TSource>> value, Func<TSource, Task<Result<TResult>>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>Awaits <paramref name="value"/>, then, on success, binds the value with <paramref name="bindFunc"/>, awaits it, and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the task of the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TCollection, TResult>(this Task<Result<TSource>> value, Func<TSource, Task<Result<TCollection>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            value.ThrowIfDefault(nameof(value));
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, bindFunc, mapFunc);

            static async Task<Result<TResult>> Core(Task<Result<TSource>> value, Func<TSource, Task<Result<TCollection>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
                => await StepAsync(await value.ConfigureAwait(false), bindFunc, mapFunc).ConfigureAwait(false);
        }

        //Result<T, TError>
        /// <summary>Awaits <paramref name="value"/>, then, on success, maps the value with <paramref name="mapFunc"/>. A failure keeps its error. Used by LINQ <c>select</c> and <c>let</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Task<Result<TResult, TError>> Select<TSource, TError, TResult>(this Task<Result<TSource, TError>> value, Func<TSource, TResult> mapFunc)
        {
            value.ThrowIfDefault(nameof(value));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, mapFunc);

            static async Task<Result<TResult, TError>> Core(Task<Result<TSource, TError>> value, Func<TSource, TResult> mapFunc)
                => (await value.ConfigureAwait(false)).Map(mapFunc);
        }

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns for the value, awaited. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="bindFunc">Returns the task of the next result from the value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TResult>(this Result<TSource, TError> value, Func<TSource, Task<Result<TResult, TError>>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>On success, binds the value with <paramref name="bindFunc"/>, awaits it, and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="bindFunc">Returns the task of the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TCollection, TResult>(this Result<TSource, TError> value, Func<TSource, Task<Result<TCollection, TError>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return StepAsync(value, bindFunc, mapFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then, on success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TResult>(this Task<Result<TSource, TError>> value, Func<TSource, Result<TResult, TError>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>Awaits <paramref name="value"/>, then, on success, binds the value with <paramref name="bindFunc"/> and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TCollection, TResult>(this Task<Result<TSource, TError>> value, Func<TSource, Result<TCollection, TError>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            value.ThrowIfDefault(nameof(value));
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, bindFunc, mapFunc);

            static async Task<Result<TResult, TError>> Core(Task<Result<TSource, TError>> value, Func<TSource, Result<TCollection, TError>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
                => Step(await value.ConfigureAwait(false), bindFunc, mapFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then, on success, returns what <paramref name="bindFunc"/> returns for the value, awaited. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the task of the next result from the value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TResult>(this Task<Result<TSource, TError>> value, Func<TSource, Task<Result<TResult, TError>>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>Awaits <paramref name="value"/>, then, on success, binds the value with <paramref name="bindFunc"/>, awaits it, and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the task of the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TCollection, TResult>(this Task<Result<TSource, TError>> value, Func<TSource, Task<Result<TCollection, TError>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            value.ThrowIfDefault(nameof(value));
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, bindFunc, mapFunc);

            static async Task<Result<TResult, TError>> Core(Task<Result<TSource, TError>> value, Func<TSource, Task<Result<TCollection, TError>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
                => await StepAsync(await value.ConfigureAwait(false), bindFunc, mapFunc).ConfigureAwait(false);
        }

        private static Result<TResult> Step<TSource, TCollection, TResult>(Result<TSource> source, Func<TSource, Result<TCollection>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            if (!source.TryGetValue(out var value))
                return source.Error.Fail<TResult>();
            var next = bindFunc(value);
            return next.TryGetValue(out var item) ? mapFunc(value, item).Ok() : next.Error.Fail<TResult>();
        }

        private static Task<Result<TResult>> StepAsync<TSource, TCollection, TResult>(Result<TSource> source, Func<TSource, Task<Result<TCollection>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            return source.TryGetValue(out var value)
                ? Core(value, bindFunc, mapFunc)
                : Task.FromResult(source.Error.Fail<TResult>());

            static async Task<Result<TResult>> Core(TSource value, Func<TSource, Task<Result<TCollection>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
            {
                var next = await bindFunc(value).ConfigureAwait(false);
                return next.TryGetValue(out var item) ? mapFunc(value, item).Ok() : next.Error.Fail<TResult>();
            }
        }

        private static Result<TResult, TError> Step<TSource, TError, TCollection, TResult>(Result<TSource, TError> source, Func<TSource, Result<TCollection, TError>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            if (!source.TryGetValue(out var value))
                return source.Error.Fail<TResult, TError>();
            var next = bindFunc(value);
            return next.TryGetValue(out var item) ? mapFunc(value, item).Ok<TResult, TError>() : next.Error.Fail<TResult, TError>();
        }

        private static Task<Result<TResult, TError>> StepAsync<TSource, TError, TCollection, TResult>(Result<TSource, TError> source, Func<TSource, Task<Result<TCollection, TError>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            return source.TryGetValue(out var value)
                ? Core(value, bindFunc, mapFunc)
                : Task.FromResult(source.Error.Fail<TResult, TError>());

            static async Task<Result<TResult, TError>> Core(TSource value, Func<TSource, Task<Result<TCollection, TError>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
            {
                var next = await bindFunc(value).ConfigureAwait(false);
                return next.TryGetValue(out var item) ? mapFunc(value, item).Ok<TResult, TError>() : next.Error.Fail<TResult, TError>();
            }
        }

#if NET

        //ValueTask steps, Result<T>
        /// <summary>Awaits <paramref name="value"/>, then, on success, maps the value with <paramref name="mapFunc"/>. A failure keeps its error. Used by LINQ <c>select</c> and <c>let</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The ValueTask that produces the result.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Task<Result<TResult>> Select<TSource, TResult>(this ValueTask<Result<TSource>> value, Func<TSource, TResult> mapFunc)
        {
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, mapFunc);

            static async Task<Result<TResult>> Core(ValueTask<Result<TSource>> value, Func<TSource, TResult> mapFunc)
                => (await value.ConfigureAwait(false)).Map(mapFunc);
        }

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns for the value, awaited. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="bindFunc">Returns the ValueTask of the next result from the value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TResult>(this Result<TSource> value, Func<TSource, ValueTask<Result<TResult>>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>On success, binds the value with <paramref name="bindFunc"/>, awaits it, and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="bindFunc">Returns the ValueTask of the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TCollection, TResult>(this Result<TSource> value, Func<TSource, ValueTask<Result<TCollection>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return StepValueAsync(value, bindFunc, mapFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then, on success, returns what <paramref name="bindFunc"/> returns for the value, awaited. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the ValueTask of the next result from the value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TResult>(this Task<Result<TSource>> value, Func<TSource, ValueTask<Result<TResult>>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>Awaits <paramref name="value"/>, then, on success, binds the value with <paramref name="bindFunc"/>, awaits it, and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the ValueTask of the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TCollection, TResult>(this Task<Result<TSource>> value, Func<TSource, ValueTask<Result<TCollection>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            value.ThrowIfDefault(nameof(value));
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, bindFunc, mapFunc);

            static async Task<Result<TResult>> Core(Task<Result<TSource>> value, Func<TSource, ValueTask<Result<TCollection>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
                => await StepValueAsync(await value.ConfigureAwait(false), bindFunc, mapFunc).ConfigureAwait(false);
        }

        /// <summary>Awaits <paramref name="value"/>, then, on success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The ValueTask that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TResult>(this ValueTask<Result<TSource>> value, Func<TSource, Result<TResult>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>Awaits <paramref name="value"/>, then, on success, binds the value with <paramref name="bindFunc"/> and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The ValueTask that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TCollection, TResult>(this ValueTask<Result<TSource>> value, Func<TSource, Result<TCollection>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, bindFunc, mapFunc);

            static async Task<Result<TResult>> Core(ValueTask<Result<TSource>> value, Func<TSource, Result<TCollection>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
                => Step(await value.ConfigureAwait(false), bindFunc, mapFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then, on success, returns what <paramref name="bindFunc"/> returns for the value, awaited. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The ValueTask that produces the result.</param>
        /// <param name="bindFunc">Returns the task of the next result from the value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TResult>(this ValueTask<Result<TSource>> value, Func<TSource, Task<Result<TResult>>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>Awaits <paramref name="value"/>, then, on success, binds the value with <paramref name="bindFunc"/>, awaits it, and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The ValueTask that produces the result.</param>
        /// <param name="bindFunc">Returns the task of the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TCollection, TResult>(this ValueTask<Result<TSource>> value, Func<TSource, Task<Result<TCollection>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, bindFunc, mapFunc);

            static async Task<Result<TResult>> Core(ValueTask<Result<TSource>> value, Func<TSource, Task<Result<TCollection>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
                => await StepAsync(await value.ConfigureAwait(false), bindFunc, mapFunc).ConfigureAwait(false);
        }

        /// <summary>Awaits <paramref name="value"/>, then, on success, returns what <paramref name="bindFunc"/> returns for the value, awaited. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The ValueTask that produces the result.</param>
        /// <param name="bindFunc">Returns the ValueTask of the next result from the value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TResult>(this ValueTask<Result<TSource>> value, Func<TSource, ValueTask<Result<TResult>>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>Awaits <paramref name="value"/>, then, on success, binds the value with <paramref name="bindFunc"/>, awaits it, and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The ValueTask that produces the result.</param>
        /// <param name="bindFunc">Returns the ValueTask of the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult>> SelectMany<TSource, TCollection, TResult>(this ValueTask<Result<TSource>> value, Func<TSource, ValueTask<Result<TCollection>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, bindFunc, mapFunc);

            static async Task<Result<TResult>> Core(ValueTask<Result<TSource>> value, Func<TSource, ValueTask<Result<TCollection>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
                => await StepValueAsync(await value.ConfigureAwait(false), bindFunc, mapFunc).ConfigureAwait(false);
        }

        //ValueTask steps, Result<T, TError>
        /// <summary>Awaits <paramref name="value"/>, then, on success, maps the value with <paramref name="mapFunc"/>. A failure keeps its error. Used by LINQ <c>select</c> and <c>let</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The ValueTask that produces the result.</param>
        /// <param name="mapFunc">Maps the value.</param>
        public static Task<Result<TResult, TError>> Select<TSource, TError, TResult>(this ValueTask<Result<TSource, TError>> value, Func<TSource, TResult> mapFunc)
        {
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, mapFunc);

            static async Task<Result<TResult, TError>> Core(ValueTask<Result<TSource, TError>> value, Func<TSource, TResult> mapFunc)
                => (await value.ConfigureAwait(false)).Map(mapFunc);
        }

        /// <summary>On success, returns what <paramref name="bindFunc"/> returns for the value, awaited. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="bindFunc">Returns the ValueTask of the next result from the value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TResult>(this Result<TSource, TError> value, Func<TSource, ValueTask<Result<TResult, TError>>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>On success, binds the value with <paramref name="bindFunc"/>, awaits it, and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The result.</param>
        /// <param name="bindFunc">Returns the ValueTask of the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TCollection, TResult>(this Result<TSource, TError> value, Func<TSource, ValueTask<Result<TCollection, TError>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return StepValueAsync(value, bindFunc, mapFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then, on success, returns what <paramref name="bindFunc"/> returns for the value, awaited. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the ValueTask of the next result from the value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TResult>(this Task<Result<TSource, TError>> value, Func<TSource, ValueTask<Result<TResult, TError>>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>Awaits <paramref name="value"/>, then, on success, binds the value with <paramref name="bindFunc"/>, awaits it, and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The task that produces the result.</param>
        /// <param name="bindFunc">Returns the ValueTask of the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TCollection, TResult>(this Task<Result<TSource, TError>> value, Func<TSource, ValueTask<Result<TCollection, TError>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            value.ThrowIfDefault(nameof(value));
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, bindFunc, mapFunc);

            static async Task<Result<TResult, TError>> Core(Task<Result<TSource, TError>> value, Func<TSource, ValueTask<Result<TCollection, TError>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
                => await StepValueAsync(await value.ConfigureAwait(false), bindFunc, mapFunc).ConfigureAwait(false);
        }

        /// <summary>Awaits <paramref name="value"/>, then, on success, returns what <paramref name="bindFunc"/> returns for the value. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The ValueTask that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TResult>(this ValueTask<Result<TSource, TError>> value, Func<TSource, Result<TResult, TError>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>Awaits <paramref name="value"/>, then, on success, binds the value with <paramref name="bindFunc"/> and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The ValueTask that produces the result.</param>
        /// <param name="bindFunc">Returns the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TCollection, TResult>(this ValueTask<Result<TSource, TError>> value, Func<TSource, Result<TCollection, TError>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, bindFunc, mapFunc);

            static async Task<Result<TResult, TError>> Core(ValueTask<Result<TSource, TError>> value, Func<TSource, Result<TCollection, TError>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
                => Step(await value.ConfigureAwait(false), bindFunc, mapFunc);
        }

        /// <summary>Awaits <paramref name="value"/>, then, on success, returns what <paramref name="bindFunc"/> returns for the value, awaited. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The ValueTask that produces the result.</param>
        /// <param name="bindFunc">Returns the task of the next result from the value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TResult>(this ValueTask<Result<TSource, TError>> value, Func<TSource, Task<Result<TResult, TError>>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>Awaits <paramref name="value"/>, then, on success, binds the value with <paramref name="bindFunc"/>, awaits it, and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The ValueTask that produces the result.</param>
        /// <param name="bindFunc">Returns the task of the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TCollection, TResult>(this ValueTask<Result<TSource, TError>> value, Func<TSource, Task<Result<TCollection, TError>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, bindFunc, mapFunc);

            static async Task<Result<TResult, TError>> Core(ValueTask<Result<TSource, TError>> value, Func<TSource, Task<Result<TCollection, TError>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
                => await StepAsync(await value.ConfigureAwait(false), bindFunc, mapFunc).ConfigureAwait(false);
        }

        /// <summary>Awaits <paramref name="value"/>, then, on success, returns what <paramref name="bindFunc"/> returns for the value, awaited. A failure keeps its error and <paramref name="bindFunc"/> isn't called.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The ValueTask that produces the result.</param>
        /// <param name="bindFunc">Returns the ValueTask of the next result from the value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TResult>(this ValueTask<Result<TSource, TError>> value, Func<TSource, ValueTask<Result<TResult, TError>>> bindFunc)
            => value.SelectMany(bindFunc, static (_, r) => r);

        /// <summary>Awaits <paramref name="value"/>, then, on success, binds the value with <paramref name="bindFunc"/>, awaits it, and combines both values with <paramref name="mapFunc"/>. Stops at the first failure, which keeps its error. Used by LINQ queries with more than one <c>from</c>.</summary>
        /// <typeparam name="TSource">The type of the source value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <typeparam name="TCollection">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the new value.</typeparam>
        /// <param name="value">The ValueTask that produces the result.</param>
        /// <param name="bindFunc">Returns the ValueTask of the next result from the value.</param>
        /// <param name="mapFunc">Combines the source value and the bound value.</param>
        public static Task<Result<TResult, TError>> SelectMany<TSource, TError, TCollection, TResult>(this ValueTask<Result<TSource, TError>> value, Func<TSource, ValueTask<Result<TCollection, TError>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            bindFunc.ThrowIfDefault(nameof(bindFunc));
            mapFunc.ThrowIfDefault(nameof(mapFunc));
            return Core(value, bindFunc, mapFunc);

            static async Task<Result<TResult, TError>> Core(ValueTask<Result<TSource, TError>> value, Func<TSource, ValueTask<Result<TCollection, TError>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
                => await StepValueAsync(await value.ConfigureAwait(false), bindFunc, mapFunc).ConfigureAwait(false);
        }

        private static Task<Result<TResult>> StepValueAsync<TSource, TCollection, TResult>(Result<TSource> source, Func<TSource, ValueTask<Result<TCollection>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            return source.TryGetValue(out var value)
                ? Core(value, bindFunc, mapFunc)
                : Task.FromResult(source.Error.Fail<TResult>());

            static async Task<Result<TResult>> Core(TSource value, Func<TSource, ValueTask<Result<TCollection>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
            {
                var next = await bindFunc(value).ConfigureAwait(false);
                return next.TryGetValue(out var item) ? mapFunc(value, item).Ok() : next.Error.Fail<TResult>();
            }
        }

        private static Task<Result<TResult, TError>> StepValueAsync<TSource, TError, TCollection, TResult>(Result<TSource, TError> source, Func<TSource, ValueTask<Result<TCollection, TError>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
        {
            return source.TryGetValue(out var value)
                ? Core(value, bindFunc, mapFunc)
                : Task.FromResult(source.Error.Fail<TResult, TError>());

            static async Task<Result<TResult, TError>> Core(TSource value, Func<TSource, ValueTask<Result<TCollection, TError>>> bindFunc, Func<TSource, TCollection, TResult> mapFunc)
            {
                var next = await bindFunc(value).ConfigureAwait(false);
                return next.TryGetValue(out var item) ? mapFunc(value, item).Ok<TResult, TError>() : next.Error.Fail<TResult, TError>();
            }
        }
#endif
    }
}
