using Microsoft.CodeAnalysis;

namespace Resultful.Generators.Diagnostics;

internal static class DiagnosticDescriptors
{
    private const string Category = "Resultful.Generators";

    public static readonly DiagnosticDescriptor SuccessOverlapsError = new(
        id: "RFG0001",
        title: "Result shape Success type overlaps error types",
        messageFormat: "Result shape '{0}' has Success type '{1}' which overlaps error type '{2}'; type patterns cannot distinguish success from failure",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonSealedConcreteHierarchyMember = new(
        id: "RFG0002",
        title: "Concrete type in closed hierarchy is not sealed",
        messageFormat: "Concrete type '{0}' in [ClosedHierarchy] '{1}' is not sealed; seal leaf types so external assemblies cannot derive from them",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OpenGenericDerivedType = new(
        id: "RFG0003",
        title: "Open generic type cannot derive from a closed hierarchy",
        messageFormat: "Open generic type '{0}' cannot derive from [ClosedHierarchy] '{1}' because its cases cannot be enumerated",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InaccessibleLeaf = new(
        id: "RFG0004",
        title: "Public result shape exposes a less-accessible leaf type",
        messageFormat: "Public result shape '{0}' exposes less-accessible error leaf '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonPartialEnclosingType = new(
        id: "RFG0005",
        title: "Enclosing type must be partial",
        messageFormat: "Type '{0}' is nested inside non-partial type '{1}'; mark all enclosing types partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedDeclaration = new(
        id: "RFG0006",
        title: "Attribute placed on an unsupported type declaration",
        messageFormat: "Attribute '{0}' on '{1}' requires {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
