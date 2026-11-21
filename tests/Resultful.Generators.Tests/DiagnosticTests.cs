using Microsoft.CodeAnalysis;
using Xunit;

namespace Resultful.Generators.Tests;

public sealed class DiagnosticTests
{
    [Theory]
    [InlineData("typeof(OrderError)", "OrderError")]
    [InlineData("typeof(OrderNotFound)", "OrderNotFound")]
    [InlineData("typeof(Error)", "Error")]
    [InlineData("typeof(object)", "object")]
    [InlineData("typeof(ITag)", "OrderNotFound")]
    public void RFG0001_reported_when_Success_overlaps_errors(string successTypeOf, string expectedOverlap)
    {
        Assert.NotEmpty(expectedOverlap);
        var src = $$"""
            using Resultful;
            using Resultful.CodeAnalysis;

            namespace Probe
            {
                public interface ITag { }
                [ClosedHierarchy]
                public abstract partial record OrderError(string Message) : Error(Message);
                public sealed record OrderNotFound(int Id) : OrderError("missing"), ITag;

                [ResultShape(typeof(OrderError), Success = {{successTypeOf}})]
                public readonly partial struct BadShape;
            }
            """;

        var comp = GeneratorTestHarness.CreateCompilation([("Test.cs", src)]);
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.Contains(output.GeneratorDiagnostics, d => d.Id == "RFG0001" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GeneratedSources.Keys, k => k.Contains("BadShape", StringComparison.Ordinal));
    }

    [Fact]
    public void RFG0001_reported_for_ManyErrors_when_Success_overlaps_IReadOnlyList_of_TError()
    {
        var src = """
            using System.Collections.Generic;
            using Resultful;
            using Resultful.CodeAnalysis;

            namespace Probe
            {
                [ClosedHierarchy]
                public abstract partial record InputError(string Message) : Error(Message);
                public sealed record Required() : InputError("req");

                [ResultShape(typeof(InputError), Success = typeof(List<InputError>), ManyErrors = true)]
                public readonly partial struct BadManyShape;
            }
            """;

        var comp = GeneratorTestHarness.CreateCompilation([("Test.cs", src)]);
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.Contains(output.GeneratorDiagnostics, d => d.Id == "RFG0001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RFG0002_warns_on_non_sealed_concrete_intermediate_and_still_emits_most_derived_first()
    {
        var comp = GeneratorTestHarness.CreateCompilation([("ConcreteMid.cs", FixtureCatalog.ConcreteIntermediate)]);
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.Contains(output.GeneratorDiagnostics, d => d.Id == "RFG0002" && d.Severity == DiagnosticSeverity.Warning);
        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(output.GeneratedSources.Keys, k => k.Contains("FetchResult", StringComparison.Ordinal));
    }

    [Fact]
    public void RFG0003_reported_on_open_generic_derived_type()
    {
        var src = """
            using Resultful;
            using Resultful.CodeAnalysis;

            namespace Probe
            {
                [ClosedHierarchy]
                public abstract partial record AppError(string Message) : Error(Message);
                public sealed record Wrapped<T>(T Data) : AppError("wrapped");
            }
            """;

        var comp = GeneratorTestHarness.CreateCompilation([("Test.cs", src)]);
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.Contains(output.GeneratorDiagnostics, d => d.Id == "RFG0003" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RFG0004_reported_when_public_shape_exposes_internal_leaf()
    {
        var src = """
            using Resultful;
            using Resultful.CodeAnalysis;

            namespace Probe
            {
                [ClosedHierarchy]
                public abstract partial record OrderError(string Message) : Error(Message);
                internal sealed record SecretError() : OrderError("secret");
                public sealed record Order(int Id);

                [ResultShape(typeof(OrderError), Success = typeof(Order))]
                public readonly partial struct PublicOrderResult;
            }
            """;

        var comp = GeneratorTestHarness.CreateCompilation([("Test.cs", src)]);
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.Contains(output.GeneratorDiagnostics, d => d.Id == "RFG0004" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RFG0005_reported_when_nested_in_non_partial_enclosing_type()
    {
        var src = """
            using Resultful;
            using Resultful.CodeAnalysis;

            namespace Probe
            {
                public class NotPartialOuter
                {
                    [ClosedHierarchy]
                    public abstract partial record InnerError(string Message) : Error(Message);
                    public sealed record Boom() : InnerError("boom");
                }
            }
            """;

        var comp = GeneratorTestHarness.CreateCompilation([("Test.cs", src)]);
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.Contains(output.GeneratorDiagnostics, d => d.Id == "RFG0005" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RFG0006_reported_on_unsupported_declarations()
    {
        var src = """
            using Resultful;
            using Resultful.CodeAnalysis;

            namespace Probe
            {
                [ClosedHierarchy]
                public sealed partial record NotAbstract(string Message) : Error(Message);

                [ResultShape(typeof(NotAbstract))]
                public partial struct NotReadOnlyShape;
            }
            """;

        var comp = GeneratorTestHarness.CreateCompilation([("Test.cs", src)]);
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.Equal(2, output.GeneratorDiagnostics.Count(d => d.Id == "RFG0006" && d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Interface_Success_omits_implicit_operator_without_diagnostic_or_CS0552()
    {
        var comp = GeneratorTestHarness.CreateCompilation([("InterfaceOk.cs", FixtureCatalog.InterfaceSuccess)]);
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RFG0008_reported_on_the_base_when_leaves_share_a_simple_name_and_the_output_still_compiles()
    {
        var comp = GeneratorTestHarness.CreateCompilation([("Colliding.cs", FixtureCatalog.CollidingLeafNames)]);
        var output = GeneratorTestHarness.Run(comp, out _);

        var rfg = Assert.Single(output.GeneratorDiagnostics);
        Assert.Equal("RFG0008", rfg.Id);
        Assert.Equal(DiagnosticSeverity.Error, rfg.Severity);
        Assert.True(rfg.Location.IsInSource);
        Assert.Equal("RootError", rfg.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(rfg.Location.SourceSpan));

        // The hierarchy keeps its constructor, so the leaves compile and RFG0008 is the only error.
        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GeneratedSources.Values, v => v.Contains("Match<", StringComparison.Ordinal) && v.Contains("abstract partial record RootError", StringComparison.Ordinal));
    }

    [Fact]
    public void Same_named_shapes_in_different_namespaces_get_unique_hint_names()
    {
        var a = """
            using Resultful;
            using Resultful.CodeAnalysis;

            namespace One
            {
                [ClosedHierarchy]
                public abstract partial record RootError(string Message) : Error(Message);
                public sealed record Leaf() : RootError("leaf");

                [ResultShape(typeof(RootError))]
                public readonly partial struct CombinedResult;
            }
            """;

        var comp = GeneratorTestHarness.CreateCompilation([("One.cs", a), ("Two.cs", a.Replace("namespace One", "namespace Two", StringComparison.Ordinal))]);
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("One.CombinedResult.ResultShape.g.cs", output.GeneratedSources.Keys);
        Assert.Contains("Two.CombinedResult.ResultShape.g.cs", output.GeneratedSources.Keys);
    }

    [Fact]
    public void Leaf_diagnostics_are_reported_once_by_the_nearest_closed_hierarchy()
    {
        var src = """
            using Resultful;
            using Resultful.CodeAnalysis;

            [ClosedHierarchy] public abstract partial record AppError : Error;
            [ClosedHierarchy] public abstract partial record OrderError : AppError;
            public record Open() : OrderError("open");
            public sealed record Wrapped<T>() : OrderError("wrapped");
            namespace A { public sealed record Gone() : global::OrderError("a"); }
            namespace B { public sealed record Gone() : global::OrderError("b"); }
            """;

        var comp = GeneratorTestHarness.CreateCompilation([("Nested.cs", src)]);
        var output = GeneratorTestHarness.Run(comp, out _);

        // Each once, all from OrderError, none repeated by AppError.
        Assert.Single(output.GeneratorDiagnostics, d => d.Id == "RFG0002");
        Assert.Single(output.GeneratorDiagnostics, d => d.Id == "RFG0003");
        var rfg0008 = Assert.Single(output.GeneratorDiagnostics, d => d.Id == "RFG0008");
        Assert.Contains("'OrderError'", rfg0008.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void A_nested_hierarchy_marks_new_only_what_its_ancestor_really_emits()
    {
        // AppError lists no cases (an open generic under it, RFG0003), so it emits no Cases or Match: OrderError's
        // members hide nothing, and `new` there would be CS0109.
        var src = """
            using Resultful;
            using Resultful.CodeAnalysis;

            [ClosedHierarchy] public abstract partial record AppError : Error;
            public sealed record Wrapped<T>() : AppError("wrapped");
            [ClosedHierarchy] public abstract partial record OrderError : AppError;
            public sealed record Missing() : OrderError("missing");
            """;

        var comp = GeneratorTestHarness.CreateCompilation([("Nested.cs", src)]);
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.Single(output.GeneratorDiagnostics, d => d.Id == "RFG0003");
        var generated = output.RunResult.GeneratedTrees.ToHashSet();
        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Location.SourceTree is { } t && generated.Contains(t));
        var order = output.GeneratedSources["OrderError.ClosedHierarchy.g.cs"];
        Assert.DoesNotContain("static new ", order, StringComparison.Ordinal);
        Assert.DoesNotContain("public new ", order, StringComparison.Ordinal);
        Assert.Contains("Cases", order, StringComparison.Ordinal);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void RFG0007_allows_a_nested_union_of_errors_and_reports_a_nested_case_that_isnt_one()
    {
        var src = """
            using Resultful;
            using Resultful.CodeAnalysis;

            public sealed record OrderNotFound(int Id) : Error("nf");
            public sealed record CardDeclined() : Error("declined");
            public partial union PaymentError(CardDeclined);
            public partial union OrderError(OrderNotFound, PaymentError);
            public partial union BadInner(CardDeclined, string);
            public partial union BadOuter(OrderNotFound, BadInner);

            [ResultShape(typeof(OrderError), Success = typeof(int))] public readonly partial struct GoodResult;
            [ResultShape(typeof(BadOuter), Success = typeof(int))] public readonly partial struct BadResult;
            """;

        var comp = GeneratorTestHarness.CreateCompilation([("Unions.cs", src)]);
        var output = GeneratorTestHarness.Run(comp, out _);

        var rfg = Assert.Single(output.GeneratorDiagnostics);
        Assert.Equal("RFG0007", rfg.Id);
        Assert.Contains("BadResult", rfg.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
#endif
}
