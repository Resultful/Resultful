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

internal static class ShapeAnalyzer
{
    public const string ResultShapeAttr = "Resultful.CodeAnalysis.ResultShapeAttribute";
    private const string UnitFullName = "global::Resultful.Unit";

    public static ResultShapeDeclaration? ExtractResultShapeDeclaration(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ctx.TargetSymbol is not INamedTypeSymbol structType)
        {
            return null;
        }

        if (ctx.TargetNode is not TypeDeclarationSyntax tds)
        {
            return null;
        }

        if (!HierarchyAnalyzer.IsFirstDeclarationWithAttribute(structType, tds, ResultShapeAttr))
        {
            return null;
        }

        var diagnostics = new List<DiagnosticInfo>();
        var isReadOnlyPartialStruct =
            structType.TypeKind == TypeKind.Struct &&
            !structType.IsRecord &&
            structType.IsReadOnly &&
            tds.Modifiers.Any(SyntaxKind.PartialKeyword) &&
            !structType.IsGenericType;

        if (!isReadOnlyPartialStruct)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.UnsupportedDeclaration,
                tds.Identifier.GetLocation(),
                "[ResultShape]",
                structType.Name,
                "a non-generic readonly partial struct"));
        }

        var enclosing = HierarchyAnalyzer.CollectEnclosingTypes(tds, structType, diagnostics);
        var ns = structType.ContainingNamespace.IsGlobalNamespace
            ? null
            : structType.ContainingNamespace.ToDisplayString();
        var hintName = HierarchyAnalyzer.BuildHintName(ns, enclosing, structType.MetadataName, "ResultShape");

        var attr = ctx.Attributes.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ResultShapeAttr);
        if (attr is null || attr.ConstructorArguments.Length < 1 || attr.ConstructorArguments[0].Value is not INamedTypeSymbol errorType)
        {
            return null;
        }

        INamedTypeSymbol? successSymbol = null;
        var manyErrors = false;
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "Success" && named.Value.Value is INamedTypeSymbol s)
            {
                successSymbol = s;
            }
            else if (named.Key == "ManyErrors" && named.Value.Value is bool b)
            {
                manyErrors = b;
            }
        }

        var compilation = ctx.SemanticModel.Compilation;
        var isValueless = successSymbol is null;
        var resolvedSuccess = successSymbol ?? compilation.GetTypeByMetadataName("Resultful.Unit");
        var successFullName = resolvedSuccess is not null
            ? resolvedSuccess.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : UnitFullName;

        var successIsInterface = resolvedSuccess?.TypeKind == TypeKind.Interface;
        var errorFullName = errorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var errorDerivesFromError = HierarchyAnalyzer.DerivesFrom(errorType, HierarchyAnalyzer.ErrorMetadataName);
        var errorIsClosed = errorType.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == HierarchyAnalyzer.ClosedHierarchyAttr);
        var errorIsExternal = !SymbolEqualityComparer.Default.Equals(errorType.ContainingAssembly, compilation.Assembly);

        var externalEntries = errorIsClosed && errorIsExternal
            ? HierarchyAnalyzer.CollectExternalEntries(errorType, ct)
            : EquatableArray<BaseIndexEntry>.Empty;

        var overlaps = new List<string>();
        if (resolvedSuccess is not null)
        {
            CheckStaticOverlaps(compilation, resolvedSuccess, errorType, manyErrors, overlaps);
        }

        return new ResultShapeDeclaration(
            FullName: structType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            HintName: hintName,
            Namespace: ns,
            EnclosingTypes: new EquatableArray<EnclosingTypeModel>(enclosing),
            Accessibility: HierarchyAnalyzer.AccessibilityKeyword(structType.DeclaredAccessibility),
            Name: structType.Name,
            ErrorFullName: errorFullName,
            SuccessFullName: successFullName,
            IsValueless: isValueless,
            ManyErrors: manyErrors,
            SuccessIsInterface: successIsInterface,
            ErrorDerivesFromError: errorDerivesFromError,
            ErrorIsClosed: errorIsClosed,
            ErrorIsExternal: errorIsExternal,
            ShapeAccessibilityRank: HierarchyAnalyzer.GetAccessibilityRank(structType.DeclaredAccessibility),
            ExternalErrorEntries: externalEntries,
            OverlappingErrorCandidates: new EquatableArray<string>(overlaps.ToArray()),
            Diagnostics: new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()),
            Position: SourcePosition.From(tds.Identifier.GetLocation()));
    }

    public static ResultShapeModel ResolveResultShape(
        ResultShapeDeclaration decl,
        EquatableArray<BaseIndexEntry> baseIndex)
    {
        var diagnostics = new List<DiagnosticInfo>(decl.Diagnostics);
        var sourceEntries = decl.ErrorIsExternal ? decl.ExternalErrorEntries : baseIndex;

        var leaves = decl.ErrorIsClosed
            ? HierarchyAnalyzer.ResolveLeavesForBase(decl.ErrorFullName, ShortNameOf(decl.ErrorFullName), sourceEntries, new List<DiagnosticInfo>())
            : EquatableArray<LeafModel>.Empty;

        // RFG0001: static overlaps (TError, Error, object, IReadOnlyList<TError>)
        for (var i = 0; i < decl.OverlappingErrorCandidates.Length; i++)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.SuccessOverlapsError,
                decl.Position?.ToLocation(),
                decl.Name,
                ShortNameOf(decl.SuccessFullName),
                decl.OverlappingErrorCandidates[i]));
        }

        // RFG0001: check Success against each resolved leaf and its base/interface chain
        if (!decl.ManyErrors && decl.OverlappingErrorCandidates.Length == 0 && decl.ErrorIsClosed)
        {
            for (var i = 0; i < sourceEntries.Length; i++)
            {
                var entry = sourceEntries[i];
                if (HierarchyAnalyzer.GetDepthFromBase(entry, decl.ErrorFullName) <= 0 || entry.IsAbstract || entry.IsGeneric)
                {
                    continue;
                }

                if (string.Equals(decl.SuccessFullName, entry.FullName, StringComparison.Ordinal) ||
                    entry.BaseChainFullNames.Contains(decl.SuccessFullName) ||
                    entry.InterfaceFullNames.Contains(decl.SuccessFullName))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.SuccessOverlapsError,
                        decl.Position?.ToLocation(),
                        decl.Name,
                        ShortNameOf(decl.SuccessFullName),
                        entry.ShortName));
                    break;
                }
            }
        }

        // RFG0004: public shape exposing less-accessible leaf
        if (!decl.ManyErrors && decl.ShapeAccessibilityRank >= 3)
        {
            for (var i = 0; i < leaves.Length; i++)
            {
                if (leaves[i].AccessibilityRank < decl.ShapeAccessibilityRank)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.InaccessibleLeaf,
                        decl.Position?.ToLocation(),
                        decl.Name,
                        ShortNameOf(leaves[i].FullName)));
                }
            }
        }

        var hasBlockingError = diagnostics.Any(d => d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error);
        return new ResultShapeModel(
            HintName: decl.HintName,
            Namespace: decl.Namespace,
            EnclosingTypes: decl.EnclosingTypes,
            Accessibility: decl.Accessibility,
            Name: decl.Name,
            ErrorFullName: decl.ErrorFullName,
            SuccessFullName: decl.SuccessFullName,
            IsValueless: decl.IsValueless,
            ManyErrors: decl.ManyErrors,
            SuccessIsInterface: decl.SuccessIsInterface,
            ErrorDerivesFromError: decl.ErrorDerivesFromError,
            ErrorIsClosed: decl.ErrorIsClosed,
            Leaves: hasBlockingError ? EquatableArray<LeafModel>.Empty : leaves,
            Diagnostics: new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
    }

    private static void CheckStaticOverlaps(
        Compilation compilation,
        INamedTypeSymbol successType,
        INamedTypeSymbol errorType,
        bool manyErrors,
        List<string> overlaps)
    {
        if (successType.SpecialType == SpecialType.System_Object)
        {
            overlaps.Add("object");
            return;
        }

        if (manyErrors)
        {
            var iReadOnlyList = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyList`1");
            if (iReadOnlyList is not null)
            {
                var listOfError = iReadOnlyList.Construct(errorType);
                if (AreConvertibleEitherWay(compilation, successType, listOfError))
                {
                    overlaps.Add($"IReadOnlyList<{errorType.Name}>");
                    return;
                }
            }
        }

        if (AreConvertibleEitherWay(compilation, successType, errorType))
        {
            overlaps.Add(errorType.Name);
            return;
        }

        var coreError = compilation.GetTypeByMetadataName(HierarchyAnalyzer.ErrorMetadataName);
        if (coreError is not null &&
            HierarchyAnalyzer.DerivesFrom(errorType, HierarchyAnalyzer.ErrorMetadataName) &&
            AreConvertibleEitherWay(compilation, successType, coreError))
        {
            overlaps.Add("Error");
        }
    }

    private static bool AreConvertibleEitherWay(Compilation compilation, ITypeSymbol a, ITypeSymbol b)
    {
        if (SymbolEqualityComparer.Default.Equals(a, b))
        {
            return true;
        }

        if (compilation is CSharpCompilation cs)
        {
            var c1 = cs.ClassifyConversion(a, b);
            if (c1.IsImplicit && (c1.IsIdentity || c1.IsReference || c1.IsBoxing))
            {
                return true;
            }

            var c2 = cs.ClassifyConversion(b, a);
            if (c2.IsImplicit && (c2.IsIdentity || c2.IsReference || c2.IsBoxing))
            {
                return true;
            }
        }

        return false;
    }

    private static string ShortNameOf(string fullName)
    {
        var idx = fullName.LastIndexOf('.');
        return idx >= 0 ? fullName.Substring(idx + 1) : fullName;
    }
}
