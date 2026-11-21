using System;
using System.Runtime.CompilerServices;
using Resultful.Utils;

namespace Resultful
{
    // Decision 22 item 3: the step for a condition, in place of `where`, which has nowhere to put an error:
    // `from _ in Result.Ensure(total > 0, new EmptyOrder())`.
    public static partial class Result
    {
        /// <summary>Returns a success if <paramref name="condition"/> is <c>true</c>, otherwise a failure holding <paramref name="error"/>. The step for a condition in a LINQ query: <c>from _ in Result.Ensure(total &gt; 0, new EmptyOrder())</c>.</summary>
        /// <param name="condition">The condition that must hold.</param>
        /// <param name="error">The error when it doesn't, including any record derived from <see cref="Error"/>.</param>
        /// <remarks>On C# 13 and later, preferred over <see cref="Ensure{TError}(bool, TError)"/> for an error derived from <see cref="Error"/>, so <c>Ensure(c, new Leaf())</c> gives <see cref="Result{T}"/> and joins its query. Older C# ignores the priority and picks the generic form. On older C#, or in a typed or shape query, write the type argument: <c>Result.Ensure&lt;ShopError&gt;(c, new NotFound(id))</c>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <c>null</c>, whatever <paramref name="condition"/> is.</exception>
        [OverloadResolutionPriority(1)]
        public static Result<Unit> Ensure(bool condition, Error error)
        {
            error.ThrowIfDefault(nameof(error));
            return condition ? Ok() : error.Fail<Unit>();
        }

        /// <summary>Returns a success if <paramref name="condition"/> is <c>true</c>, otherwise a failure holding <paramref name="error"/>.</summary>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <param name="condition">The condition that must hold.</param>
        /// <param name="error">The error when it doesn't.</param>
        /// <remarks>The form for a typed result or a shape's query: <c>Result.Ensure&lt;ShopError&gt;(c, new NotFound(id))</c>. Name the type argument: without it, C# 13 and later pick <see cref="Ensure(bool, Error)"/> for an error derived from <see cref="Error"/>, and a string picks the message form.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <c>null</c>, whatever <paramref name="condition"/> is.</exception>
        public static Result<Unit, TError> Ensure<TError>(bool condition, TError error)
        {
            error.ThrowIfNoError(nameof(error));
            return condition ? Ok<TError>() : error.Fail<TError>();
        }

        /// <summary>Returns a success if <paramref name="condition"/> is <c>true</c>, otherwise a failure holding a <see cref="MessageError"/> whose message is the condition's source text: <c>Result.Ensure(total &gt; 0)</c> fails with <c>"total &gt; 0"</c>.</summary>
        /// <param name="condition">The condition that must hold.</param>
        /// <param name="message">Left out, the compiler passes the source text of <paramref name="condition"/>: the code, never the values in it, so the message holds no personal data.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>, whatever <paramref name="condition"/> is.</exception>
        [OverloadResolutionPriority(1)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("ApiDesign", "RS0027:API with optional parameter(s) should have the most parameters amongst its public overloads", Justification = "CallerArgumentExpression needs an optional parameter; the other Ensure overloads take the same number of parameters and none is optional.")]
        public static Result<Unit> Ensure(bool condition, [CallerArgumentExpression(nameof(condition))] string message = "")
        {
            message.ThrowIfDefault(nameof(message));
            return condition ? Ok() : Error.From(message).Fail<Unit>();
        }
    }
}
