namespace Resultful.Generators.Models;

internal readonly record struct EnclosingTypeModel(
    string Accessibility,
    string Keyword,
    string Name,
    bool IsPartial);

// AccessibilityRank is the effective accessibility: the least accessible of the type and its enclosing types.
internal sealed record BaseIndexEntry(
    string FullName,
    string ShortName,
    EquatableArray<string> QualifierSegments,
    string? DirectBaseFullName,
    EquatableArray<string> BaseChainFullNames,
    EquatableArray<string> InterfaceFullNames,
    bool IsAbstract,
    bool IsSealed,
    bool IsGeneric,
    int AccessibilityRank,
    SourcePosition? Position);

internal sealed record LeafModel(
    string FullName,
    string ShortName,
    string ParamSuffix,
    int Depth,
    int AccessibilityRank);

internal sealed record ClosedHierarchyDeclaration(
    string FullName,
    string HintName,
    string? Namespace,
    EquatableArray<EnclosingTypeModel> EnclosingTypes,
    string Accessibility,
    int AccessibilityRank,
    string Keyword,
    string Name,
    bool DerivesFromError,
    bool EmitConstructor,
    EquatableArray<string> ClosedAncestorFullNames,
    EquatableArray<DiagnosticInfo> Diagnostics,
    SourcePosition? Position);

internal sealed record ClosedHierarchyModel(
    string HintName,
    string? Namespace,
    EquatableArray<EnclosingTypeModel> EnclosingTypes,
    string Accessibility,
    string Keyword,
    string Name,
    bool DerivesFromError,
    bool EmitConstructor,
    bool EmitMembers,
    string? DispatchAccessibility,
    bool HidesDispatch,
    bool HidesCases,
    EquatableArray<LeafModel> Leaves,
    EquatableArray<DiagnosticInfo> Diagnostics);

internal sealed record ResultShapeDeclaration(
    string FullName,
    string HintName,
    string? Namespace,
    EquatableArray<EnclosingTypeModel> EnclosingTypes,
    string Accessibility,
    string Name,
    string ErrorFullName,
    string SuccessFullName,
    bool IsValueless,
    bool ManyErrors,
    bool SuccessIsInterface,
    bool ErrorDerivesFromError,
    bool ErrorIsClosed,
    bool ErrorIsExternal,
    int ShapeAccessibilityRank,
    EquatableArray<BaseIndexEntry> ExternalErrorEntries,
    EquatableArray<string> OverlappingErrorCandidates,
    EquatableArray<DiagnosticInfo> Diagnostics,
    SourcePosition? Position);

internal sealed record ResultShapeModel(
    string HintName,
    string? Namespace,
    EquatableArray<EnclosingTypeModel> EnclosingTypes,
    string Accessibility,
    string Name,
    string ErrorFullName,
    string SuccessFullName,
    bool IsValueless,
    bool ManyErrors,
    bool SuccessIsInterface,
    bool ErrorDerivesFromError,
    bool ErrorIsClosed,
    EquatableArray<LeafModel> Leaves,
    EquatableArray<DiagnosticInfo> Diagnostics);

internal sealed record EmitItem(
    ClosedHierarchyModel? Hierarchy,
    ResultShapeModel? Shape,
    bool UnionAvailable);
