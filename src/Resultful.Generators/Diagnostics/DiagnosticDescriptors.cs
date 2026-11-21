using Microsoft.CodeAnalysis;

namespace Resultful.Generators.Diagnostics;

// RFG0001-RFG0009 (decision 16 item 10, decision 17), listed in AnalyzerReleases.Unshipped.md.
internal static class DiagnosticDescriptors
{
    private const string Category = "Resultful.Generators";

    public static readonly DiagnosticDescriptor SuccessOverlapsError = new(
        id: "RFG0001",
        title: "A shape's Success type overlaps its error types",
        messageFormat: "Result shape '{0}' has Success type '{1}' which overlaps error type '{2}'; type patterns cannot distinguish success from failure",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonSealedConcreteHierarchyMember = new(
        id: "RFG0002",
        title: "A concrete type inside a [ClosedHierarchy] isn't sealed",
        messageFormat: "Concrete type '{0}' in [ClosedHierarchy] '{1}' is not sealed; seal leaf types so external assemblies cannot derive from them",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OpenGenericDerivedType = new(
        id: "RFG0003",
        title: "An open generic type derives from a [ClosedHierarchy] base",
        messageFormat: "Open generic type '{0}' cannot derive from [ClosedHierarchy] '{1}' because its cases cannot be enumerated",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InaccessibleLeaf = new(
        id: "RFG0004",
        title: "A public shape exposes a less accessible leaf",
        messageFormat: "Result shape '{0}' exposes error leaf '{1}', which is less accessible than the shape",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonPartialEnclosingType = new(
        id: "RFG0005",
        title: "An attributed declaration is nested in a type that isn't partial",
        messageFormat: "Type '{0}' is nested inside non-partial type '{1}'; mark all enclosing types partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedDeclaration = new(
        id: "RFG0006",
        title: "The attribute is on a declaration the generator can't extend",
        messageFormat: "Attribute '{0}' on '{1}' requires {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnionCaseNotError = new(
        id: "RFG0007",
        title: "A case of an error union doesn't derive from Error",
        messageFormat: "Result shape '{0}' uses union '{1}' as its error type, but its case '{2}' doesn't derive from Resultful.Error",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateLeafSimpleName = new(
        id: "RFG0008",
        title: "Two leaves of one hierarchy or union share a simple name, so the JSON \"type\" is ambiguous",
        messageFormat: "'{0}' has more than one leaf named '{1}' ({2}); the JSON \"type\" discriminator would be ambiguous, so rename one",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NestedUnionNotExtendable = new(
        id: "RFG0009",
        title: "A partial union of errors holds a union the generator can't extend",
        messageFormat: "Union '{0}' gets no IErrorUnion members: its case '{1}' is a union that isn't partial and doesn't implement IErrorUnion with WithContext",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
