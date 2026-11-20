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
    public void Colliding_short_names_and_same_named_shapes_in_different_namespaces_produce_unique_names()
    {
        var extra = """
            using Resultful;
            using Resultful.CodeAnalysis;

            namespace OtherNamespace
            {
                [ClosedHierarchy]
                public abstract partial record RootError(string Message) : Error(Message);
                public sealed record Leaf() : RootError("leaf");

                [ResultShape(typeof(RootError))]
                public readonly partial struct CombinedResult;
            }
            """;

        var comp = GeneratorTestHarness.CreateCompilation([
            ("Colliding.cs", FixtureCatalog.CollidingLeafNames),
            ("Other.cs", extra),
        ]);
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.Empty(output.GeneratorDiagnostics);
        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var collidingFile = output.GeneratedSources.Single(kv => kv.Key == "Fixture.Colliding.RootError.ClosedHierarchy.g.cs").Value;
        Assert.Contains("onOrdersNotFound", collidingFile, StringComparison.Ordinal);
        Assert.Contains("onPaymentsNotFound", collidingFile, StringComparison.Ordinal);
        Assert.Contains("onUniqueError", collidingFile, StringComparison.Ordinal);
    }
}
