using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Resultful.Generators.Analysis;
using Resultful.Generators.Emission;
using Resultful.Generators.Models;

namespace Resultful.Generators;

/// <summary>Writes the members of <c>[ClosedHierarchy]</c> records and <c>[ResultShape]</c> structs (decision 16).</summary>
[Generator(LanguageNames.CSharp)]
public sealed class ResultShapeGenerator : IIncrementalGenerator
{
    /// <summary>The tracking name of the index of declarations with a base list.</summary>
    public const string BaseIndexTrackingName = "Resultful.BaseIndex";
    /// <summary>The tracking name of the closed hierarchy models.</summary>
    public const string HierarchiesTrackingName = "Resultful.Hierarchies";
    /// <summary>The tracking name of the result shape models.</summary>
    public const string ShapesTrackingName = "Resultful.Shapes";
    /// <summary>The tracking name of the check for <c>[Union]</c> support.</summary>
    public const string UnionAvailableTrackingName = "Resultful.UnionAvailable";
    /// <summary>The tracking name of the error union models.</summary>
    public const string ErrorUnionsTrackingName = "Resultful.ErrorUnions";
    /// <summary>The tracking name of the emitted sources.</summary>
    public const string EmitTrackingName = "Resultful.Emit";

    // LanguageVersion.CSharp15's value in Roslyn 5.x; the 4.14 API this build compiles against has no name for it.
    private const int CSharp15 = 1500;

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Stage A: syntax-filtered index of types declaring a BaseList
        var baseIndex = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax { BaseList.Types.Count: > 0 },
                transform: static (ctx, ct) => HierarchyAnalyzer.ExtractBaseIndexEntry(ctx, ct))
            .Where(static e => e is not null)
            .Collect()
            .Select(static (arr, _) => new EquatableArray<BaseIndexEntry>(
                arr.Where(e => e is not null).OrderBy(e => e!.FullName, StringComparer.Ordinal).ToArray()!))
            .WithTrackingName(BaseIndexTrackingName);

        // Single-bool gate for .NET 11 [Union], decided at run time from the compilation (Known traps): the attribute
        // exists and the language version allows unions. C# 15 is compared by number, since Roslyn 4.14 has no CSharp15.
        var unionAvailable = context.CompilationProvider
            .Select(static (c, _) => c.GetTypeByMetadataName("System.Runtime.CompilerServices.UnionAttribute") is not null)
            .Combine(context.ParseOptionsProvider.Select(static (po, _) =>
                po is CSharpParseOptions cs && (int)cs.LanguageVersion >= CSharp15))
            .Select(static (pair, _) => pair.Left && pair.Right)
            .WithTrackingName(UnionAvailableTrackingName);

        // Stage B1: [ClosedHierarchy] declarations combined with Stage A index
        var hierarchies = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HierarchyAnalyzer.ClosedHierarchyAttr,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, ct) => HierarchyAnalyzer.ExtractClosedHierarchyDeclaration(ctx, ct))
            .Where(static d => d is not null)
            .Combine(baseIndex)
            .Select(static (pair, _) => HierarchyAnalyzer.ResolveClosedHierarchy(pair.Left!, pair.Right))
            .WithTrackingName(HierarchiesTrackingName);

        // Stage B2: [ResultShape] declarations combined with Stage A index
        var shapes = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ShapeAnalyzer.ResultShapeAttr,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, ct) => ShapeAnalyzer.ExtractResultShapeDeclaration(ctx, ct))
            .Where(static d => d is not null)
            .Combine(baseIndex)
            .Select(static (pair, _) => ShapeAnalyzer.ResolveResultShape(pair.Left!, pair.Right))
            .WithTrackingName(ShapesTrackingName);

        // Stage B3 (decision 17): partial unions whose cases are all errors. A union isn't attributed, so the syntax
        // filter keeps partial type declarations that aren't a class, struct, record or interface (a 4.14 build has no
        // UnionDeclarationSyntax) and the symbol decides.
        var errorUnions = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is TypeDeclarationSyntax tds and not (ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax or InterfaceDeclarationSyntax) &&
                    tds.Modifiers.Any(SyntaxKind.PartialKeyword),
                transform: static (ctx, ct) => UnionAnalyzer.ExtractErrorUnion(ctx, ct))
            .Where(static u => u is not null)
            .Select(static (u, _) => u!)
            .WithTrackingName(ErrorUnionsTrackingName);

        context.RegisterSourceOutput(errorUnions, static (spc, u) =>
        {
            if (u.Diagnostics.Length == 0)
            {
                spc.AddSource(u.HintName, ErrorUnionEmitter.Emit(u));
            }
        });

        // Final emit models
        var hierarchyEmits = hierarchies
            .Select(static (h, _) => new EmitItem(h, null, false))
            .WithTrackingName(EmitTrackingName);

        var shapeEmits = shapes
            .Combine(unionAvailable)
            .Select(static (pair, _) => new EmitItem(null, pair.Left, pair.Right))
            .WithTrackingName(EmitTrackingName);

        context.RegisterSourceOutput(hierarchyEmits, static (spc, item) =>
        {
            var h = item.Hierarchy!;
            if (h.EmitConstructor || h.EmitMembers)
            {
                spc.AddSource(h.HintName, ClosedHierarchyEmitter.Emit(h));
            }
        });

        context.RegisterSourceOutput(shapeEmits, static (spc, item) =>
        {
            var s = item.Shape!;
            if (!HasError(s.Diagnostics))
            {
                spc.AddSource(s.HintName, ResultShapeEmitter.Emit(s, item.UnionAvailable));
            }
        });

        // Diagnostics are reported apart from the sources: their cached positions are turned back into source
        // locations against the current compilation. Only this step sees every edit, and it does nothing when there
        // are no diagnostics; the emitted files stay cached as before.
        var diagnostics = hierarchies
            .Select(static (h, _) => h.Diagnostics)
            .Collect()
            .Combine(shapes.Select(static (s, _) => s.Diagnostics).Collect())
            .Combine(errorUnions.Select(static (u, _) => u.Diagnostics).Collect())
            .Select(static (pair, _) => new EquatableArray<DiagnosticInfo>(
                pair.Left.Left.SelectMany(d => d).Concat(pair.Left.Right.SelectMany(d => d)).Concat(pair.Right.SelectMany(d => d)).ToArray()));

        context.RegisterSourceOutput(diagnostics.Combine(context.CompilationProvider), static (spc, pair) =>
        {
            var (all, compilation) = pair;
            if (all.Length == 0)
            {
                return;
            }

            // One lookup per call, not a scan of every tree per diagnostic.
            var trees = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (!trees.ContainsKey(tree.FilePath))
                {
                    trees.Add(tree.FilePath, tree);
                }
            }

            for (var i = 0; i < all.Length; i++)
            {
                spc.ReportDiagnostic(all[i].ToDiagnostic(trees));
            }
        });
    }

    private static bool HasError(EquatableArray<DiagnosticInfo> diagnostics)
    {
        for (var i = 0; i < diagnostics.Length; i++)
        {
            if (diagnostics[i].Descriptor.DefaultSeverity == DiagnosticSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }
}
