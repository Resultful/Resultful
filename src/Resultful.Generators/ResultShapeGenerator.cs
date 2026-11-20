using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Resultful.Generators.Analysis;
using Resultful.Generators.Emission;
using Resultful.Generators.Models;

namespace Resultful.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class ResultShapeGenerator : IIncrementalGenerator
{
    public const string BaseIndexTrackingName = "Resultful.BaseIndex";
    public const string HierarchiesTrackingName = "Resultful.Hierarchies";
    public const string ShapesTrackingName = "Resultful.Shapes";
    public const string UnionAvailableTrackingName = "Resultful.UnionAvailable";
    public const string EmitTrackingName = "Resultful.Emit";

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

        // Single-bool reference gate for .NET 11 [Union]
        var unionAvailable = context.CompilationProvider
            .Select(static (c, _) => c.GetTypeByMetadataName("System.Runtime.CompilerServices.UnionAttribute") is not null)
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
            .Select(static (pair, _) => new EquatableArray<DiagnosticInfo>(
                pair.Left.SelectMany(d => d).Concat(pair.Right.SelectMany(d => d)).ToArray()));

        context.RegisterSourceOutput(diagnostics.Combine(context.CompilationProvider), static (spc, pair) =>
        {
            var (all, compilation) = pair;
            for (var i = 0; i < all.Length; i++)
            {
                spc.ReportDiagnostic(all[i].ToDiagnostic(compilation));
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
