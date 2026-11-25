using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Resultful.Generators.Tests;

public sealed class IncrementalityTests
{
    private const string TwoHierarchiesFileA = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Inc.A
        {
            [ClosedHierarchy]
            public abstract partial record OrderError(string Message) : Error(Message);
            public sealed record OrderNotFound(int Id) : OrderError($"missing {Id}")
            {
                public string Describe() => Message;
            }
            public sealed record Order(int Id);

            [ResultShape(typeof(OrderError), Success = typeof(Order))]
            public readonly partial struct PlaceOrderResult;
        }
        """;

    private const string TwoHierarchiesFileB = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Inc.B
        {
            [ClosedHierarchy]
            public abstract partial record BillingError(string Message) : Error(Message);
            public sealed record CardDeclined() : BillingError("declined");
            public sealed record Invoice(int Id);

            [ResultShape(typeof(BillingError), Success = typeof(Invoice))]
            public readonly partial struct ChargeResult;
        }
        """;

    private const string UnrelatedFile = """
        namespace Inc.Misc
        {
            public static class Helper
            {
                public static int Add(int a, int b) => a + b;
            }
        }
        """;

    [Fact]
    public void Unrelated_edit_keeps_all_tracked_steps_cached_or_unchanged()
    {
        var comp1 = GeneratorTestHarness.CreateCompilation([
            ("A.cs", TwoHierarchiesFileA),
            ("B.cs", TwoHierarchiesFileB),
            ("Misc.cs", UnrelatedFile),
        ]);
        GeneratorTestHarness.Run(comp1, out var driver);

        var ct = TestContext.Current.CancellationToken;
        var oldTree = comp1.SyntaxTrees.First(t => t.FilePath == "Misc.cs");
        var newTree = CSharpSyntaxTree.ParseText(UnrelatedFile + "\n// harmless comment\n", (CSharpParseOptions)oldTree.Options, path: "Misc.cs", cancellationToken: ct);
        var comp2 = comp1.ReplaceSyntaxTree(oldTree, newTree);

        driver = driver.RunGenerators(comp2, ct);
        var result = driver.GetRunResult().Results.Single();

        AssertAllStepsCachedOrUnchanged(result, ResultShapeGenerator.BaseIndexTrackingName);
        AssertAllStepsCachedOrUnchanged(result, ResultShapeGenerator.HierarchiesTrackingName);
        AssertAllStepsCachedOrUnchanged(result, ResultShapeGenerator.ShapesTrackingName);
        AssertAllStepsCachedOrUnchanged(result, ResultShapeGenerator.UnionAvailableTrackingName);
        AssertAllStepsCachedOrUnchanged(result, ResultShapeGenerator.EmitTrackingName);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void Unrelated_edit_keeps_error_union_models_cached_or_unchanged()
    {
        var comp1 = GeneratorTestHarness.CreateCompilation([("Nested.cs", FixtureCatalog.NestedUnion), ("Misc.cs", UnrelatedFile)]);
        GeneratorTestHarness.Run(comp1, out var driver);

        var ct = TestContext.Current.CancellationToken;
        var oldTree = comp1.SyntaxTrees.First(t => t.FilePath == "Misc.cs");
        var newTree = CSharpSyntaxTree.ParseText(UnrelatedFile + "\n// harmless comment\n", (CSharpParseOptions)oldTree.Options, path: "Misc.cs", cancellationToken: ct);
        driver = driver.RunGenerators(comp1.ReplaceSyntaxTree(oldTree, newTree), ct);
        var result = driver.GetRunResult().Results.Single();

        Assert.Equal(2, GetOutputReasons(result, ResultShapeGenerator.ErrorUnionsTrackingName).Count);
        AssertAllStepsCachedOrUnchanged(result, ResultShapeGenerator.ErrorUnionsTrackingName);
        AssertAllStepsCachedOrUnchanged(result, ResultShapeGenerator.ShapesTrackingName);
    }
#endif

    [Fact]
    public void Editing_method_body_in_leaf_file_keeps_Emit_unchanged()
    {
        var comp1 = GeneratorTestHarness.CreateCompilation([
            ("A.cs", TwoHierarchiesFileA),
            ("B.cs", TwoHierarchiesFileB),
        ]);
        GeneratorTestHarness.Run(comp1, out var driver);

        var ct = TestContext.Current.CancellationToken;
        var oldTree = comp1.SyntaxTrees.First(t => t.FilePath == "A.cs");
        var edited = TwoHierarchiesFileA.Replace("public string Describe() => Message;", "public string Describe() => Message + \"!\";", StringComparison.Ordinal);
        var newTree = CSharpSyntaxTree.ParseText(edited, (CSharpParseOptions)oldTree.Options, path: "A.cs", cancellationToken: ct);
        var comp2 = comp1.ReplaceSyntaxTree(oldTree, newTree);

        driver = driver.RunGenerators(comp2, ct);
        var result = driver.GetRunResult().Results.Single();

        AssertAllStepsCachedOrUnchanged(result, ResultShapeGenerator.EmitTrackingName);
    }

    [Fact]
    public void Adding_new_leaf_in_another_file_modifies_only_affected_hierarchy()
    {
        var comp1 = GeneratorTestHarness.CreateCompilation([
            ("A.cs", TwoHierarchiesFileA),
            ("B.cs", TwoHierarchiesFileB),
        ]);
        GeneratorTestHarness.Run(comp1, out var driver);

        var ct = TestContext.Current.CancellationToken;
        var newLeafTree = CSharpSyntaxTree.ParseText(
            """
            namespace Inc.A
            {
                public sealed record OrderCancelled(int Id) : OrderError($"cancelled {Id}");
            }
            """,
            (CSharpParseOptions)comp1.SyntaxTrees[0].Options,
            path: "OrderCancelled.cs",
            cancellationToken: ct);
        var comp2 = comp1.AddSyntaxTrees(newLeafTree);

        driver = driver.RunGenerators(comp2, ct);
        var result = driver.GetRunResult().Results.Single();

        var baseIndexReasons = GetOutputReasons(result, ResultShapeGenerator.BaseIndexTrackingName);
        Assert.Contains(IncrementalStepRunReason.Modified, baseIndexReasons);

        var emitReasons = GetOutputReasons(result, ResultShapeGenerator.EmitTrackingName);
        Assert.Contains(IncrementalStepRunReason.Modified, emitReasons);
        Assert.Contains(emitReasons, r => r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged);
    }

    [Fact]
    public void Cached_model_types_hold_no_Roslyn_symbols_syntax_locations_or_compilations()
    {
        var genAssembly = typeof(ResultShapeGenerator).Assembly;
        var modelTypes = genAssembly.GetTypes()
            .Where(t => t.Namespace == "Resultful.Generators.Models")
            .ToList();

        Assert.NotEmpty(modelTypes);

        var forbidden = new[]
        {
            typeof(ISymbol),
            typeof(SyntaxNode),
            typeof(SyntaxTree),
            typeof(Location),
            typeof(Compilation),
            typeof(SemanticModel),
        };

        foreach (var type in modelTypes)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var fieldType = field.FieldType;
                foreach (var bad in forbidden)
                {
                    Assert.False(
                        bad.IsAssignableFrom(fieldType),
                        $"Model type '{type.FullName}' caches forbidden Roslyn type '{fieldType.FullName}' in field '{field.Name}'.");
                }
            }
        }
    }

    private static List<IncrementalStepRunReason> GetOutputReasons(GeneratorRunResult result, string trackingName)
    {
        Assert.True(result.TrackedSteps.TryGetValue(trackingName, out var steps), $"Missing tracked step '{trackingName}'.");
        return steps.SelectMany(s => s.Outputs).Select(o => o.Reason).ToList();
    }

    private static void AssertAllStepsCachedOrUnchanged(GeneratorRunResult result, string trackingName)
    {
        var reasons = GetOutputReasons(result, trackingName);
        Assert.NotEmpty(reasons);
        Assert.All(reasons, r => Assert.True(
            r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Step '{trackingName}' had unexpected reason {r}."));
    }
}
