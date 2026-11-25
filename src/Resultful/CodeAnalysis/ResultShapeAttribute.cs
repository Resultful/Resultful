using System;

namespace Resultful.CodeAnalysis
{
    // Decision 16: a named result type for one operation, written by Resultful.Generators.
    /// <summary>
    /// Makes this <c>partial struct</c> a result shape: a named wrapper over one core result, with a case for each
    /// error that <see cref="ErrorType"/> can hold. <c>Resultful.Generators</c> writes its members.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ErrorType"/> is a <see cref="ClosedHierarchyAttribute"/> base or, on .NET 11, an error union. The
    /// shape wraps a <see cref="Result{T, TError}"/>, or a <c>Validation&lt;T, TError&gt;</c> when
    /// <see cref="ManyErrors"/> is <c>true</c>.
    /// </para>
    /// <para>
    /// The generator and the analyzers match this attribute by its full name,
    /// <c>Resultful.CodeAnalysis.ResultShapeAttribute</c>, not by assembly. It isn't inherited.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Struct, Inherited = false)]
    public sealed class ResultShapeAttribute : Attribute
    {
        /// <summary>Declares a shape whose failures are <paramref name="errorType"/>.</summary>
        /// <param name="errorType">The error base or error union the shape's failures hold.</param>
        /// <exception cref="ArgumentNullException"><paramref name="errorType"/> is <c>null</c>.</exception>
        public ResultShapeAttribute(Type errorType)
        {
            ErrorType = errorType ?? throw new ArgumentNullException(nameof(errorType));
        }

        /// <summary>The error base or error union the shape's failures hold.</summary>
        public Type ErrorType { get; }

        /// <summary>
        /// The type of the success value, or <c>null</c> (the default) for an operation with no value, whose success
        /// holds <see cref="Unit"/>.
        /// </summary>
        public Type? Success { get; set; }

        /// <summary>
        /// <c>true</c> to gather every failure, so the shape wraps a <c>Validation&lt;T, TError&gt;</c>; <c>false</c>
        /// (the default) for the first failure only, so it wraps a <see cref="Result{T, TError}"/>.
        /// </summary>
        public bool ManyErrors { get; set; }
    }
}
