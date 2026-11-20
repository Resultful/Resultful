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

internal static class HierarchyAnalyzer
{
    public const string ClosedHierarchyAttr = "Resultful.CodeAnalysis.ClosedHierarchyAttribute";
    public const string ErrorMetadataName = "Resultful.Error";

    public static BaseIndexEntry? ExtractBaseIndexEntry(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ctx.Node is not TypeDeclarationSyntax tds || tds.BaseList is null || tds.BaseList.Types.Count == 0)
        {
            return null;
        }

        if (ctx.SemanticModel.GetDeclaredSymbol(tds, ct) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        // Only emit one entry per type when a partial type spans multiple files.
        if (!IsFirstDeclaration(symbol, tds))
        {
            return null;
        }

        return CreateBaseIndexEntry(symbol, tds.Identifier.GetLocation());
    }

    public static BaseIndexEntry CreateBaseIndexEntry(INamedTypeSymbol symbol, Location? location)
    {
        var fullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var baseChain = new List<string>();
        var cur = symbol.BaseType;
        while (cur is not null && cur.SpecialType != SpecialType.System_Object)
        {
            baseChain.Add(cur.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            cur = cur.BaseType;
        }

        var interfaces = symbol.AllInterfaces
            .Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        var segments = BuildQualifierSegments(symbol);

        return new BaseIndexEntry(
            FullName: fullName,
            ShortName: symbol.Name,
            QualifierSegments: new EquatableArray<string>(segments),
            DirectBaseFullName: symbol.BaseType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            BaseChainFullNames: new EquatableArray<string>(baseChain.ToArray()),
            InterfaceFullNames: new EquatableArray<string>(interfaces),
            IsAbstract: symbol.IsAbstract,
            IsSealed: symbol.IsSealed,
            IsGeneric: symbol.IsGenericType,
            AccessibilityRank: GetAccessibilityRank(symbol.DeclaredAccessibility),
            Position: SourcePosition.From(location));
    }

    public static ClosedHierarchyDeclaration? ExtractClosedHierarchyDeclaration(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol)
        {
            return null;
        }

        if (ctx.TargetNode is not TypeDeclarationSyntax tds)
        {
            return null;
        }

        // Ensure one model per partial type.
        if (!IsFirstDeclarationWithAttribute(symbol, tds, ClosedHierarchyAttr))
        {
            return null;
        }

        var diagnostics = new List<DiagnosticInfo>();
        var isPartial = tds.Modifiers.Any(SyntaxKind.PartialKeyword);
        var isSupportedKind = symbol.TypeKind == TypeKind.Class && symbol.IsAbstract && isPartial;

        if (!isSupportedKind)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.UnsupportedDeclaration,
                tds.Identifier.GetLocation(),
                "[ClosedHierarchy]",
                symbol.Name,
                "an abstract partial record or class"));
        }

        var enclosing = CollectEnclosingTypes(tds, symbol, diagnostics);
        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        var keyword = symbol.IsRecord ? "record" : "class";
        var hintName = BuildHintName(ns, enclosing, symbol.MetadataName, "ClosedHierarchy");

        return new ClosedHierarchyDeclaration(
            FullName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            HintName: hintName,
            Namespace: ns,
            EnclosingTypes: new EquatableArray<EnclosingTypeModel>(enclosing),
            Accessibility: AccessibilityKeyword(symbol.DeclaredAccessibility),
            Keyword: keyword,
            Name: symbol.Name,
            DerivesFromError: DerivesFrom(symbol, ErrorMetadataName),
            Diagnostics: new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
    }

    public static ClosedHierarchyModel ResolveClosedHierarchy(
        ClosedHierarchyDeclaration decl,
        EquatableArray<BaseIndexEntry> baseIndex)
    {
        var diagnostics = new List<DiagnosticInfo>(decl.Diagnostics);
        var hasBlockingError = diagnostics.Any(d => d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error);

        if (hasBlockingError)
        {
            return new ClosedHierarchyModel(
                decl.HintName,
                decl.Namespace,
                decl.EnclosingTypes,
                decl.Accessibility,
                decl.Keyword,
                decl.Name,
                decl.DerivesFromError,
                EquatableArray<LeafModel>.Empty,
                new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
        }

        var leaves = ResolveLeavesForBase(decl.FullName, decl.Name, baseIndex, diagnostics);

        return new ClosedHierarchyModel(
            decl.HintName,
            decl.Namespace,
            decl.EnclosingTypes,
            decl.Accessibility,
            decl.Keyword,
            decl.Name,
            decl.DerivesFromError,
            leaves,
            new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
    }

    public static EquatableArray<LeafModel> ResolveLeavesForBase(
        string baseFullName,
        string baseShortName,
        EquatableArray<BaseIndexEntry> entries,
        List<DiagnosticInfo> diagnostics)
    {
        var derived = new List<(BaseIndexEntry Entry, int Depth)>();

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var depth = GetDepthFromBase(entry, baseFullName);
            if (depth <= 0)
            {
                continue;
            }

            if (entry.IsGeneric)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.OpenGenericDerivedType,
                    entry.Position?.ToLocation(),
                    entry.ShortName,
                    baseShortName));
                continue;
            }

            if (!entry.IsAbstract)
            {
                if (!entry.IsSealed)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.NonSealedConcreteHierarchyMember,
                        entry.Position?.ToLocation(),
                        entry.ShortName,
                        baseShortName));
                }

                derived.Add((entry, depth));
            }
        }

        // Most-derived first (depth descending), deterministic tie-break by ordinal FQN.
        derived.Sort((a, b) =>
        {
            var cmp = b.Depth.CompareTo(a.Depth);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Entry.FullName, b.Entry.FullName);
        });

        var suffixMap = ComputeUniqueParamSuffixes(derived.Select(d => d.Entry).ToList());
        var leaves = new LeafModel[derived.Count];
        for (var i = 0; i < derived.Count; i++)
        {
            var (entry, depth) = derived[i];
            leaves[i] = new LeafModel(
                FullName: entry.FullName,
                ParamSuffix: suffixMap[entry.FullName],
                Depth: depth,
                AccessibilityRank: entry.AccessibilityRank);
        }

        return new EquatableArray<LeafModel>(leaves);
    }

    public static EquatableArray<BaseIndexEntry> CollectExternalEntries(
        INamedTypeSymbol baseType,
        CancellationToken ct)
    {
        var results = new List<BaseIndexEntry>();
        WalkNamespace(baseType.ContainingAssembly.GlobalNamespace, baseType, results, ct);
        results.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        return new EquatableArray<BaseIndexEntry>(results.ToArray());
    }

    private static void WalkNamespace(
        INamespaceSymbol ns,
        INamedTypeSymbol targetBase,
        List<BaseIndexEntry> results,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var type in ns.GetTypeMembers())
        {
            WalkType(type, targetBase, results, ct);
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            WalkNamespace(child, targetBase, results, ct);
        }
    }

    private static void WalkType(
        INamedTypeSymbol type,
        INamedTypeSymbol targetBase,
        List<BaseIndexEntry> results,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (InheritsFrom(type, targetBase))
        {
            results.Add(CreateBaseIndexEntry(type, location: null));
        }

        foreach (var nested in type.GetTypeMembers())
        {
            WalkType(nested, targetBase, results, ct);
        }
    }

    private static bool InheritsFrom(INamedTypeSymbol candidate, INamedTypeSymbol targetBase)
    {
        var cur = candidate.BaseType;
        while (cur is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(cur, targetBase))
            {
                return true;
            }

            cur = cur.BaseType;
        }

        return false;
    }

    public static int GetDepthFromBase(BaseIndexEntry entry, string baseFullName)
    {
        for (var i = 0; i < entry.BaseChainFullNames.Length; i++)
        {
            if (string.Equals(entry.BaseChainFullNames[i], baseFullName, StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static Dictionary<string, string> ComputeUniqueParamSuffixes(List<BaseIndexEntry> entries)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var groups = entries.GroupBy(e => e.ShortName, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var items = group.ToList();
            if (items.Count == 1)
            {
                result[items[0].FullName] = items[0].ShortName;
                continue;
            }

            var prefixLen = 1;
            while (true)
            {
                var candidates = new Dictionary<string, string>(StringComparer.Ordinal);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var collision = false;
                var anyHasMore = false;

                foreach (var item in items)
                {
                    var take = Math.Min(prefixLen, item.QualifierSegments.Length);
                    if (prefixLen < item.QualifierSegments.Length)
                    {
                        anyHasMore = true;
                    }

                    var prefix = string.Concat(
                        item.QualifierSegments.Skip(item.QualifierSegments.Length - take));
                    var name = prefix + item.ShortName;
                    candidates[item.FullName] = name;
                    if (!seen.Add(name))
                    {
                        collision = true;
                    }
                }

                if (!collision || !anyHasMore)
                {
                    foreach (var kv in candidates)
                    {
                        result[kv.Key] = kv.Value;
                    }

                    break;
                }

                prefixLen++;
            }
        }

        return result;
    }

    private static string[] BuildQualifierSegments(INamedTypeSymbol symbol)
    {
        var segments = new List<string>();
        var ns = symbol.ContainingNamespace;
        if (ns is { IsGlobalNamespace: false })
        {
            segments.AddRange(ns.ToDisplayString().Split('.'));
        }

        var enclosing = new List<string>();
        var parent = symbol.ContainingType;
        while (parent is not null)
        {
            enclosing.Add(parent.Name);
            parent = parent.ContainingType;
        }

        enclosing.Reverse();
        segments.AddRange(enclosing);
        return segments.ToArray();
    }

    public static EnclosingTypeModel[] CollectEnclosingTypes(
        TypeDeclarationSyntax tds,
        INamedTypeSymbol symbol,
        List<DiagnosticInfo> diagnostics)
    {
        var list = new List<EnclosingTypeModel>();
        var parentSyntax = tds.Parent as TypeDeclarationSyntax;
        var parentSymbol = symbol.ContainingType;

        while (parentSyntax is not null && parentSymbol is not null)
        {
            var isPartial = parentSyntax.Modifiers.Any(SyntaxKind.PartialKeyword);
            if (!isPartial)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.NonPartialEnclosingType,
                    parentSyntax.Identifier.GetLocation(),
                    symbol.Name,
                    parentSymbol.Name));
            }

            var kw = parentSymbol.IsRecord
                ? (parentSymbol.TypeKind == TypeKind.Struct ? "record struct" : "record")
                : (parentSymbol.TypeKind == TypeKind.Struct ? "struct" : "class");

            list.Add(new EnclosingTypeModel(
                AccessibilityKeyword(parentSymbol.DeclaredAccessibility),
                kw,
                parentSymbol.Name,
                isPartial));

            parentSyntax = parentSyntax.Parent as TypeDeclarationSyntax;
            parentSymbol = parentSymbol.ContainingType;
        }

        list.Reverse();
        return list.ToArray();
    }

    public static string BuildHintName(
        string? ns,
        EnclosingTypeModel[] enclosing,
        string metadataName,
        string kind)
    {
        var enclosingPart = enclosing.Length == 0
            ? metadataName
            : string.Join("+", enclosing.Select(e => e.Name)) + "+" + metadataName;

        var prefix = string.IsNullOrEmpty(ns) ? enclosingPart : $"{ns}.{enclosingPart}";
        return $"{prefix}.{kind}.g.cs";
    }

    public static bool DerivesFrom(INamedTypeSymbol candidate, string metadataName)
    {
        if (candidate.ToDisplayString() == metadataName)
        {
            return true;
        }

        var cur = candidate.BaseType;
        while (cur is not null)
        {
            if (cur.ToDisplayString() == metadataName)
            {
                return true;
            }

            cur = cur.BaseType;
        }

        return false;
    }

    public static string AccessibilityKeyword(Accessibility a) => a switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.Private => "private",
        _ => "internal",
    };

    public static int GetAccessibilityRank(Accessibility a) => a switch
    {
        Accessibility.Public => 3,
        Accessibility.ProtectedOrInternal => 2,
        Accessibility.Protected => 2,
        Accessibility.Internal => 1,
        Accessibility.ProtectedAndInternal => 1,
        _ => 0,
    };

    public static bool IsFirstDeclaration(INamedTypeSymbol symbol, TypeDeclarationSyntax current)
    {
        var first = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        return first is not null &&
               first.SyntaxTree == current.SyntaxTree &&
               first.Span == current.Span;
    }

    public static bool IsFirstDeclarationWithAttribute(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax current,
        string attrMetadataName)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == attrMetadataName &&
                attr.ApplicationSyntaxReference is { } attrRef)
            {
                var ownerNode = attrRef.GetSyntax().Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                if (ownerNode is not null)
                {
                    return ownerNode.SyntaxTree == current.SyntaxTree && ownerNode.Span == current.Span;
                }
            }
        }

        return IsFirstDeclaration(symbol, current);
    }
}
