using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Resultful.Generators.Diagnostics;
using Resultful.Generators.Models;

namespace Resultful.Generators.Analysis;

// Decision 17: C# 15 unions of Error records. Everything is read through symbols (a union is a struct that implements
// IUnion, with one public constructor per case), so the build against Roslyn 4.14 understands unions from a 5.x
// compiler. A nested union stays one case; its leaves are reached by walking it.
internal static class UnionAnalyzer
{
    public const string IUnionName = "System.Runtime.CompilerServices.IUnion";
    public const string IErrorUnionName = "Resultful.IErrorUnion";
    private const int MaxDepth = 8;

    public static bool IsUnion(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Struct &&
        type.AllInterfaces.Any(i => i.ToDisplayString() == IUnionName);

    public static IEnumerable<ITypeSymbol> CaseTypes(INamedTypeSymbol union) =>
        union.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 1)
            .Select(c => c.Parameters[0].Type);

    // A case is an Error, or a nested union whose own cases all are (RFG0007).
    public static bool IsErrorCase(ITypeSymbol caseType, int depth = 0) =>
        caseType is INamedTypeSymbol named &&
        (HierarchyAnalyzer.DerivesFrom(named, HierarchyAnalyzer.ErrorMetadataName) ||
         (depth < MaxDepth && IsUnion(named) && CaseTypes(named).All(c => IsErrorCase(c, depth + 1))));

    // The generator can reopen the union: declared in source, non-generic, every declaration and every enclosing type
    // partial, and not implementing IErrorUnion itself.
    public static bool CanReopen(INamedTypeSymbol union)
    {
        if (!IsUnion(union) || ImplementsIErrorUnion(union) ||
            HierarchyAnalyzer.IsGenericOrInGeneric(union) || union.DeclaringSyntaxReferences.Length == 0)
        {
            return false;
        }

        foreach (var reference in union.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not TypeDeclarationSyntax declaration || !IsPartialAll(declaration))
            {
                return false;
            }
        }

        return true;
    }

    // The generator writes IErrorUnion's members on a union it can reopen whose cases it can dispatch on and whose
    // leaves have distinct simple names (no RFG0008 or RFG0009).
    public static bool GetsErrorUnionMembers(INamedTypeSymbol union, int depth = 0) =>
        depth <= MaxDepth &&
        CanReopen(union) &&
        CaseTypes(union).All(c => IsDispatchableCase(c, depth + 1)) &&
        Collisions(union).Count == 0;

    // Leaves of the union, at any depth, that share a simple name (RFG0008).
    public static List<IGrouping<string, NestedLeafModel>> Collisions(INamedTypeSymbol union) =>
        Distinct(AllLeaves(union)).GroupBy(l => l.ShortName, StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();

    // The cases most-derived first, so no switch arm is subsumed by an earlier one (CS8120, CS8510); otherwise in
    // declaration order.
    public static List<INamedTypeSymbol> SortedCases(INamedTypeSymbol union) =>
        CaseTypes(union).OfType<INamedTypeSymbol>().Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .OrderByDescending(TypeDepth).ToList();

    public static int TypeDepth(ITypeSymbol type)
    {
        var depth = 0;
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            depth++;
        }

        return depth;
    }

    // True when the union implements IErrorUnion and has WithContext, written by hand or by this generator.
    public static bool IsErrorUnion(INamedTypeSymbol union) =>
        GetsErrorUnionMembers(union) || (ImplementsIErrorUnion(union) && HasOwnWithContext(union));

    // The expression a shape uses to read the leaf from its union error `e`, or null when it has none.
    public static string? ErrorAccess(INamedTypeSymbol union)
    {
        if (GetsErrorUnionMembers(union))
        {
            return "e.Error";
        }

        if (!ImplementsIErrorUnion(union))
        {
            return null;
        }

        var publicError = union.GetMembers("Error").OfType<IPropertySymbol>().Any(p =>
            !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public &&
            p.Type.ToDisplayString() == HierarchyAnalyzer.ErrorMetadataName);
        return publicError ? "e.Error" : "((global::Resultful.IErrorUnion)e).Error";
    }

    public static bool HasWithContext(INamedTypeSymbol union) =>
        GetsErrorUnionMembers(union) || HasOwnWithContext(union);

    // The innermost leaves under the union's nested-union cases, outer case first, each with how to build the outer
    // union from it. Direct cases aren't included.
    public static List<NestedLeafModel> NestedLeaves(INamedTypeSymbol union)
    {
        var result = new List<NestedLeafModel>();
        var outer = Full(union);
        foreach (var c in SortedCases(union))
        {
            if (c is INamedTypeSymbol inner && IsUnion(inner))
            {
                Walk(inner, $"new {outer}(new {Full(inner)}(", 2, 1, result);
            }
        }

        return result;
    }

    // Every innermost leaf of the union, each with how to build the union from it.
    public static List<NestedLeafModel> AllLeaves(INamedTypeSymbol union)
    {
        var result = new List<NestedLeafModel>();
        Walk(union, $"new {Full(union)}(", 1, 0, result);
        return result;
    }

    public static IEnumerable<ITypeSymbol> LeafSymbols(INamedTypeSymbol union, int level) =>
        level > MaxDepth
            ? Enumerable.Empty<ITypeSymbol>()
            : CaseTypes(union).SelectMany(c => c is INamedTypeSymbol inner && IsUnion(inner) ? LeafSymbols(inner, level + 1) : new[] { c });

    public static ErrorUnionModel? ExtractErrorUnion(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ctx.Node is not TypeDeclarationSyntax tds ||
            ctx.SemanticModel.GetDeclaredSymbol(tds, ct) is not INamedTypeSymbol union ||
            !HierarchyAnalyzer.IsFirstDeclaration(union, tds) ||
            ctx.SemanticModel.Compilation.GetTypeByMetadataName(IErrorUnionName) is null ||
            !CanReopen(union) ||
            !CaseTypes(union).All(c => IsErrorCase(c)))
        {
            return null;
        }

        var diagnostics = new List<DiagnosticInfo>();

        // RFG0009: a nested union the generated Error and WithContext can't dispatch on (not partial, say).
        foreach (var c in SortedCases(union).Where(c => IsUnion(c) && !IsDispatchableCase(c, 1)))
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.NestedUnionNotExtendable,
                tds.Identifier.GetLocation(),
                union.Name,
                c.Name));
        }

        // RFG0008: Resultful.Text.Json writes a leaf's simple name as its "type", so it must be unique in the union.
        foreach (var group in Collisions(union))
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.DuplicateLeafSimpleName,
                tds.Identifier.GetLocation(),
                union.Name,
                group.Key,
                string.Join(", ", group.Select(l => HierarchyAnalyzer.StripGlobal(l.FullName)))));
        }

        var enclosing = HierarchyAnalyzer.CollectEnclosingTypes(tds, union, new List<DiagnosticInfo>());
        var ns = union.ContainingNamespace.IsGlobalNamespace ? null : union.ContainingNamespace.ToDisplayString();
        var cases = SortedCases(union).Select(c => new UnionCaseModel(Full(c), IsUnion(c))).ToArray();
        var leaves = Distinct(AllLeaves(union)).OrderByDescending(l => l.TypeDepth).ToArray();

        // Members the union declares itself are left to it.
        var declared = union.GetMembers().Where(m => !m.IsImplicitlyDeclared).Select(m => m.Name)
            .Where(GeneratedMembers.Contains).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();

        return new ErrorUnionModel(
            HintName: HierarchyAnalyzer.BuildHintName(ns, enclosing, union.MetadataName, "ErrorUnion"),
            Namespace: ns,
            EnclosingTypes: new EquatableArray<EnclosingTypeModel>(enclosing),
            Accessibility: HierarchyAnalyzer.AccessibilityKeyword(union.DeclaredAccessibility),
            Name: union.Name,
            Cases: new EquatableArray<UnionCaseModel>(cases),
            Leaves: new EquatableArray<NestedLeafModel>(leaves),
            DeclaredMembers: new EquatableArray<string>(declared),
            Diagnostics: new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
    }

    private static readonly HashSet<string> GeneratedMembers = new(StringComparer.Ordinal)
    {
        "Cases", "Error", "Message", "Cause", "TryFind", "WithContext", "Wrap",
    };

    // The first of each leaf type: a leaf reached twice converts by its first path.
    public static List<NestedLeafModel> Distinct(List<NestedLeafModel> leaves)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return leaves.Where(l => seen.Add(l.FullName)).ToList();
    }

    public static string Full(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static void Walk(INamedTypeSymbol union, string open, int depth, int level, List<NestedLeafModel> result)
    {
        if (level > MaxDepth)
        {
            return;
        }

        foreach (var c in SortedCases(union))
        {
            if (c is INamedTypeSymbol inner && IsUnion(inner))
            {
                Walk(inner, $"{open}new {Full(inner)}(", depth + 1, level + 1, result);
            }
            else if (c is INamedTypeSymbol leaf)
            {
                // A [ClosedHierarchy] base stands for its leaves: Cases lists them through its generated Cases.
                result.Add(new NestedLeafModel(
                    Full(leaf),
                    leaf.Name,
                    open,
                    depth,
                    HierarchyAnalyzer.GetEffectiveAccessibilityRank(leaf),
                    TypeDepth(leaf),
                    HierarchyAnalyzer.HasAttribute(leaf, HierarchyAnalyzer.ClosedHierarchyAttr)));
            }
        }
    }

    // A case the generated Error and WithContext can handle: an Error, or a union that has or gets both.
    private static bool IsDispatchableCase(ITypeSymbol caseType, int depth) =>
        caseType is INamedTypeSymbol named &&
        (HierarchyAnalyzer.DerivesFrom(named, HierarchyAnalyzer.ErrorMetadataName) ||
         (IsUnion(named) && ((ImplementsIErrorUnion(named) && HasOwnWithContext(named)) || GetsErrorUnionMembers(named, depth))));

    private static bool ImplementsIErrorUnion(INamedTypeSymbol type) =>
        type.AllInterfaces.Any(i => i.ToDisplayString() == IErrorUnionName);

    private static bool HasOwnWithContext(INamedTypeSymbol union) =>
        union.GetMembers("WithContext").OfType<IMethodSymbol>().Any(m =>
            !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && m.Parameters.Length == 1 &&
            m.Parameters[0].Type.SpecialType == SpecialType.System_String &&
            SymbolEqualityComparer.Default.Equals(m.ReturnType, union));

    private static bool IsPartialAll(TypeDeclarationSyntax declaration)
    {
        for (SyntaxNode? node = declaration; node is TypeDeclarationSyntax t; node = t.Parent)
        {
            if (!t.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return false;
            }
        }

        return true;
    }
}
