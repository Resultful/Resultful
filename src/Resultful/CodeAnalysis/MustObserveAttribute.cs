using System;

namespace Resultful.CodeAnalysis
{
    // Decision 15: rules read attributes, matched by full name, so a user's own types get the same checks as the core's.
    /// <summary>
    /// A value of this type, or returned by this method, must not be thrown away. <c>Resultful.Analyzers</c> reports a
    /// call whose result is ignored (RF0001); only an explicit discard (<c>_ = …</c>) passes.
    /// </summary>
    /// <remarks>
    /// The analyzers match this attribute by its full name, <c>Resultful.CodeAnalysis.MustObserveAttribute</c>, not by
    /// assembly. A library that doesn't reference Resultful can declare its own <c>internal</c> copy with that name.
    /// It isn't inherited: put it on each type that needs it.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method, Inherited = false)]
    public sealed class MustObserveAttribute : Attribute
    {
    }
}
