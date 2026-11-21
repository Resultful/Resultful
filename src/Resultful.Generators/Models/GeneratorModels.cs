namespace Resultful.Generators.Models;

internal readonly record struct EnclosingTypeModel(
    string Accessibility,
    string Keyword,
    string Name,
    bool IsPartial);

// AccessibilityRank is the effective accessibility: the least accessible of the type and its enclosing types.
// IsClosed: the type carries [ClosedHierarchy]; IsExtendable: the generator can reopen it (no RFG0005 or RFG0006).
// ClosedAncestorFullNames: the [ClosedHierarchy] types in its base chain, nearest first.
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
    SourcePosition? Position,
    bool IsClosed,
    bool IsExtendable,
    EquatableArray<string> ClosedAncestorFullNames);

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
    bool MetadataAncestorHasCases,
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
    EquatableArray<string> ErrorClosedAncestors,
    UnionErrorModel? Union,
    int ShapeAccessibilityRank,
    EquatableArray<BaseIndexEntry> ExternalErrorEntries,
    EquatableArray<string> OverlappingErrorCandidates,
    EquatableArray<DiagnosticInfo> Diagnostics,
    SourcePosition? Position);

internal sealed record ResultShapeModel(
    string FullName,
    int AccessibilityRank,
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
    EquatableArray<string> ErrorClosedAncestors,
    EquatableArray<LeafModel> Leaves,
    UnionErrorModel? Union,
    EquatableArray<DiagnosticInfo> Diagnostics);

// A leaf of a nested union (decision 17): not a case of the outer union, but still converted into it, by WrapOpen, the
// leaf, and Depth closing parentheses: new Outer(new Inner(leaf)).
internal sealed record NestedLeafModel(
    string FullName,
    string ShortName,
    string WrapOpen,
    int Depth,
    int AccessibilityRank,
    int TypeDepth,
    bool IsHierarchy);

// A shape's error union: its direct cases are the shape's leaves (a nested union stays one case); NestedLeaves convert
// in too. ErrorAccess reads the leaf through IErrorUnion (null when the union isn't an error union), and HasWithContext
// says the union has, or gets, WithContext.
internal sealed record UnionErrorModel(
    EquatableArray<LeafModel> Cases,
    EquatableArray<NestedLeafModel> NestedLeaves,
    string? ErrorAccess,
    bool HasWithContext);

// A partial union whose cases are all errors (decision 17), which gets IErrorUnion's members. Leaves are the innermost
// leaves, each with how Wrap builds the union from it; DeclaredMembers are written by hand, so not generated.
internal sealed record ErrorUnionModel(
    string HintName,
    string? Namespace,
    EquatableArray<EnclosingTypeModel> EnclosingTypes,
    string Accessibility,
    string Name,
    EquatableArray<UnionCaseModel> Cases,
    EquatableArray<NestedLeafModel> Leaves,
    EquatableArray<string> DeclaredMembers,
    EquatableArray<DiagnosticInfo> Diagnostics);

internal sealed record UnionCaseModel(string FullName, bool IsUnion);

// A shape of the compilation that others may convert from (decision 16 item 5). ErrorClosedAncestors are
// the [ClosedHierarchy] bases of its error type when that type is closed itself, nearest first.
internal sealed record ShapePeer(
    string FullName,
    string SuccessFullName,
    string ErrorFullName,
    EquatableArray<string> ErrorClosedAncestors,
    bool ManyErrors,
    int AccessibilityRank);


internal sealed record EmitItem(
    ClosedHierarchyModel? Hierarchy,
    ResultShapeModel? Shape,
    bool UnionAvailable,
    bool NullableAttributes,
    EquatableArray<string> Peers);
