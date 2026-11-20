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
            IsGeneric: IsGenericOrInGeneric(symbol),
            AccessibilityRank: GetEffectiveAccessibilityRank(symbol),
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
        var isSupportedKind = symbol.TypeKind == TypeKind.Class && symbol.IsAbstract && isPartial && !IsGenericOrInGeneric(symbol);

        if (!isSupportedKind)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.UnsupportedDeclaration,
                tds.Identifier.GetLocation(),
                "[ClosedHierarchy]",
                symbol.Name,
                "a non-generic abstract partial record or class"));
        }

        var enclosing = CollectEnclosingTypes(tds, symbol, diagnostics);
        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        var keyword = symbol.IsRecord ? "record" : "class";
        var hintName = BuildHintName(ns, enclosing, symbol.MetadataName, "ClosedHierarchy");
        var derivesFromError = DerivesFrom(symbol, ErrorMetadataName);

        var closedAncestors = new List<string>();
        for (var cur = symbol.BaseType; cur is not null; cur = cur.BaseType)
        {
            if (HasAttribute(cur, ClosedHierarchyAttr))
            {
                closedAncestors.Add(cur.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
        }

        return new ClosedHierarchyDeclaration(
            FullName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            HintName: hintName,
            Namespace: ns,
            EnclosingTypes: new EquatableArray<EnclosingTypeModel>(enclosing),
            Accessibility: AccessibilityKeyword(symbol.DeclaredAccessibility),
            AccessibilityRank: GetEffectiveAccessibilityRank(symbol),
            Keyword: keyword,
            Name: symbol.Name,
            DerivesFromError: derivesFromError,
            EmitConstructor: derivesFromError && NeedsMessageConstructor(symbol, ct),
            ClosedAncestorFullNames: new EquatableArray<string>(closedAncestors.ToArray()),
            Diagnostics: new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()),
            Position: SourcePosition.From(tds.Identifier.GetLocation()));
    }

    // The `private protected .ctor(string message)` is emitted only when no matching constructor is written, the type
    // has no primary constructor (every other constructor would have to call it, CS8862), and the base can take the
    // message: Resultful.Error itself, another [ClosedHierarchy] (which gets the same constructor), or a base that
    // declares an accessible (string) constructor.
    private static bool NeedsMessageConstructor(INamedTypeSymbol symbol, CancellationToken ct)
    {
        foreach (var ctor in symbol.InstanceConstructors)
        {
            if (!ctor.IsImplicitlyDeclared && IsMessageConstructor(ctor))
            {
                return false;
            }
        }

        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(ct) is TypeDeclarationSyntax { ParameterList: not null })
            {
                return false;
            }
        }

        var baseType = symbol.BaseType;
        if (baseType is null)
        {
            return false;
        }

        if (baseType.ToDisplayString() == ErrorMetadataName || HasAttribute(baseType, ClosedHierarchyAttr))
        {
            return true;
        }

        foreach (var ctor in baseType.InstanceConstructors)
        {
            if (IsMessageConstructor(ctor) && ctor.DeclaredAccessibility != Accessibility.Private)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMessageConstructor(IMethodSymbol ctor) =>
        ctor.Parameters.Length == 1 && ctor.Parameters[0].Type.SpecialType == SpecialType.System_String;

    public static bool HasAttribute(ISymbol symbol, string attributeMetadataName)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == attributeMetadataName)
            {
                return true;
            }
        }

        return false;
    }

    public static ClosedHierarchyModel ResolveClosedHierarchy(
        ClosedHierarchyDeclaration decl,
        EquatableArray<BaseIndexEntry> baseIndex)
    {
        var diagnostics = new List<DiagnosticInfo>(decl.Diagnostics);

        // RFG0005 or RFG0006: the generator can't reopen the type, so it writes nothing for it.
        if (HasError(diagnostics))
        {
            return new ClosedHierarchyModel(
                decl.HintName,
                decl.Namespace,
                decl.EnclosingTypes,
                decl.Accessibility,
                decl.Keyword,
                decl.Name,
                decl.DerivesFromError,
                EmitConstructor: false,
                EmitMembers: false,
                DispatchAccessibility: null,
                HidesDispatch: false,
                HidesCases: false,
                EquatableArray<LeafModel>.Empty,
                new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
        }

        var leaves = ResolveLeavesForBase(decl.FullName, decl.Name, baseIndex, diagnostics);

        // RFG0008 (round 4): Resultful.Text.Json writes a leaf's simple name as its "type", so two leaves that share
        // one can't be read back. Supersedes the prefixing rule in building-the-generator.md; prefixing is kept so the
        // generated parameters stay distinct while the error is reported.
        foreach (var group in leaves.GroupBy(l => l.ShortName, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var items = group.ToList();
            if (items.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.DuplicateLeafSimpleName,
                    decl.Position,
                    decl.Name,
                    group.Key,
                    string.Join(", ", items.Select(i => StripGlobal(i.FullName)))));
            }
        }

        // RFG0003 and RFG0008 leave the case list unusable, but the constructor is still written so the leaves that
        // are declared keep compiling and only the RFG error is reported.
        var emitMembers = !HasError(diagnostics) && leaves.Length > 0;

        var hidesDispatch = false;
        for (var i = 0; i < decl.ClosedAncestorFullNames.Length; i++)
        {
            if (CountConcreteCases(decl.ClosedAncestorFullNames[i], baseIndex) == leaves.Length)
            {
                // Same leaves, so the ancestor's generated Match/Switch/TryMap have the same signatures (CS0108).
                hidesDispatch = true;
            }
        }

        return new ClosedHierarchyModel(
            decl.HintName,
            decl.Namespace,
            decl.EnclosingTypes,
            decl.Accessibility,
            decl.Keyword,
            decl.Name,
            decl.DerivesFromError,
            EmitConstructor: decl.EmitConstructor,
            EmitMembers: emitMembers,
            DispatchAccessibility: DispatchAccessibilityFor(decl.AccessibilityRank, leaves),
            HidesDispatch: hidesDispatch,
            HidesCases: decl.ClosedAncestorFullNames.Length > 0,
            leaves,
            new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
    }

    // Match, Switch and TryMap take a delegate per leaf, so they can be no more accessible than the least accessible
    // leaf (CS0051 otherwise). Null leaves them out: a private or protected leaf can't appear in any of them.
    private static string? DispatchAccessibilityFor(int ownerRank, EquatableArray<LeafModel> leaves)
    {
        var allPublic = true;
        for (var i = 0; i < leaves.Length; i++)
        {
            var rank = leaves[i].AccessibilityRank;
            if (rank == 3)
            {
                continue;
            }

            if (rank != 1)
            {
                return null;
            }

            allPublic = false;
        }

        return allPublic || ownerRank <= 1 ? "public" : "internal";
    }

    // True when a member as accessible as memberRank may mention a type as accessible as typeRank.
    public static bool IsAccessibleEnough(int typeRank, int memberRank) =>
        typeRank == 3 || typeRank == memberRank || memberRank == 0 || (typeRank == 1 && memberRank <= 1);

    private static int CountConcreteCases(string baseFullName, EquatableArray<BaseIndexEntry> entries)
    {
        var count = 0;
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (!entry.IsAbstract && !entry.IsGeneric && GetDepthFromBase(entry, baseFullName) > 0)
            {
                count++;
            }
        }

        return count;
    }

    public static bool HasError(List<DiagnosticInfo> diagnostics) =>
        diagnostics.Any(d => d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error);

    public static string StripGlobal(string fullName) =>
        fullName.StartsWith("global::", StringComparison.Ordinal) ? fullName.Substring("global::".Length) : fullName;

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
                    entry.Position,
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
                        entry.Position,
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
                ShortName: entry.ShortName,
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

    // 3 public, 1 internal (protected internal counts as internal), 2 protected, 0 private or private protected.
    public static int GetAccessibilityRank(Accessibility a) => a switch
    {
        Accessibility.Public => 3,
        Accessibility.ProtectedOrInternal => 1,
        Accessibility.Internal => 1,
        Accessibility.Protected => 2,
        _ => 0,
    };

    // The least accessible of the type and the types it's nested in. Protected and internal together count as private.
    public static int GetEffectiveAccessibilityRank(INamedTypeSymbol symbol)
    {
        var hasInternal = false;
        var hasProtected = false;
        for (ISymbol? cur = symbol; cur is INamedTypeSymbol t; cur = t.ContainingType)
        {
            switch (GetAccessibilityRank(t.DeclaredAccessibility))
            {
                case 0:
                    return 0;
                case 1:
                    hasInternal = true;
                    break;
                case 2:
                    hasProtected = true;
                    break;
            }
        }

        return hasProtected ? (hasInternal ? 0 : 2) : (hasInternal ? 1 : 3);
    }

    public static bool IsGenericOrInGeneric(INamedTypeSymbol symbol)
    {
        for (var cur = symbol; cur is not null; cur = cur.ContainingType)
        {
            if (cur.IsGenericType)
            {
                return true;
            }
        }

        return false;
    }

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
