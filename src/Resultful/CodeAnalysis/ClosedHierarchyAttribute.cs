using System;

namespace Resultful.CodeAnalysis
{
    // Decision 15: a user closes their own error base, so a switch over it can be exhaustive.
    /// <summary>
    /// Every type that derives from this class is declared in the same assembly. A <c>switch</c> over the base is
    /// exhaustive when every derived type is matched, and a nullable input also needs a <c>null</c> arm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Resultful.Analyzers</c> suppresses the compiler's CS8509 for such a <c>switch</c>, and reports a type in another
    /// assembly that derives from the base. <c>Resultful.Generators</c> writes a <c>private protected</c> constructor and
    /// <c>Match</c> for a base declared <c>partial</c>.
    /// </para>
    /// <para>
    /// Both match this attribute by its full name, <c>Resultful.CodeAnalysis.ClosedHierarchyAttribute</c>, not by
    /// assembly, so a library can declare its own <c>internal</c> copy. It isn't inherited: an intermediate base that
    /// closes its own cases carries it too.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ClosedHierarchyAttribute : Attribute
    {
    }
}
