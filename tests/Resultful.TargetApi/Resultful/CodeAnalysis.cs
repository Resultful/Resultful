#nullable enable
using System;

// Decisions 15 and 16: attributes read by Resultful.Analyzers and Resultful.Generators, matched by full name.
namespace Resultful.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method, Inherited = false)]
    public sealed class MustObserveAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ClosedHierarchyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Struct, Inherited = false)]
    public sealed class ResultShapeAttribute : Attribute
    {
        public ResultShapeAttribute(Type errorType) => throw null!;
        public Type ErrorType => throw null!;
        public Type? Success { get => throw null!; set => throw null!; }
        public bool ManyErrors { get => throw null!; set => throw null!; }
    }
}
