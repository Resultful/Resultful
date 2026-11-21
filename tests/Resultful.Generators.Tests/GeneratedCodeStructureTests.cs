using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Resultful.Generators.Tests;

public sealed class GeneratedCodeStructureTests
{
    public static TheoryData<string, string> AllSingleAssemblyFixtures() => new()
    {
        { "Flat", FixtureCatalog.FlatHierarchy },
        { "DeepFourLevel", FixtureCatalog.DeepFourLevelHierarchy },
        { "ConcreteIntermediate", FixtureCatalog.ConcreteIntermediate },
        { "NestedShape", FixtureCatalog.NestedShape },
        { "InterfaceSuccess", FixtureCatalog.InterfaceSuccess },
        { "ValuelessShape", FixtureCatalog.ValuelessShape },
        { "ManyErrorsShape", FixtureCatalog.ManyErrorsShape },
    };

    [Theory]
    [MemberData(nameof(AllSingleAssemblyFixtures))]
    public void Emitted_files_compile_bare_and_satisfy_all_syntax_and_symbol_rules(string name, string source)
    {
        var comp = GeneratorTestHarness.CreateCompilation([(name + ".cs", source)], assemblyName: "Fixture_" + name);
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.NotEmpty(output.GeneratedSources);
        Assert.DoesNotContain(output.GeneratorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        AssertNoWarningsInGeneratedFiles(output);

        VerifyAllGeneratedSourceRules(output);
    }

    // Consumers build with TreatWarningsAsErrors, so a warning in a .g.cs (CS0108 from a nested hierarchy, say) breaks them.
    private static void AssertNoWarningsInGeneratedFiles(GeneratorRunOutput output)
    {
        var generated = output.RunResult.GeneratedTrees.ToHashSet();
        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity >= DiagnosticSeverity.Warning && d.Location.SourceTree is { } t && generated.Contains(t));
    }

    [Fact]
    public void Cross_assembly_hierarchy_emits_and_compiles_cleanly()
    {
        var domainComp = GeneratorTestHarness.CreateCompilation([("Domain.cs", FixtureCatalog.CrossAssemblyBase)], assemblyName: "ExternalDomain");
        var domainOut = GeneratorTestHarness.Run(domainComp, out _);
        Assert.DoesNotContain(domainOut.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var domainRef = GeneratorTestHarness.EmitToReference((CSharpCompilation)domainOut.OutputCompilation);

        var apiComp = GeneratorTestHarness.CreateCompilation(
            [("Api.cs", FixtureCatalog.CrossAssemblyConsumer)],
            extraReferences: [domainRef],
            assemblyName: "ApiConsumer");
        var apiOut = GeneratorTestHarness.Run(apiComp, out _);

        Assert.Empty(apiOut.GeneratorDiagnostics);
        Assert.DoesNotContain(apiOut.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Fixture.Api.LoadAccountResult.ResultShape.g.cs", apiOut.GeneratedSources.Keys);

        VerifyAllGeneratedSourceRules(apiOut);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void Net11_emitted_union_shapes_switch_exhaustively_flat_and_grouped()
    {
        var consumerSwitch = """
            namespace Fixture.Deep
            {
                public static class ConsumerSwitches
                {
                    public static int Flat(CheckoutResult r) => r switch
                    {
                        Receipt => 200,
                        CardExpired => 402,
                        InsufficientFunds => 402,
                        OrderMissing => 404,
                        RateLimited => 429,
                    };

                    public static int Grouped(CheckoutResult r) => r switch
                    {
                        Receipt => 200,
                        PaymentError => 402,
                        OrderMissing => 404,
                        RateLimited => 429,
                    };
                }
            }
            """;

        var comp = GeneratorTestHarness.CreateCompilation([
            ("Deep.cs", FixtureCatalog.DeepFourLevelHierarchy),
            ("Switch.cs", consumerSwitch),
        ]);
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error || d.Id == "CS8509");
    }

    [Fact]
    public void Net11_references_with_CSharp_14_emit_no_union_members()
    {
        // UnionAttribute exists in net11.0's references, but C# 14 can't declare a union: the gate needs both.
        var comp = GeneratorTestHarness.CreateCompilation([("Flat.cs", FixtureCatalog.FlatHierarchy)], languageVersion: LanguageVersion.CSharp14);
        Assert.NotNull(comp.GetTypeByMetadataName("System.Runtime.CompilerServices.UnionAttribute"));
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
        var shape = output.OutputCompilation.GetTypeByMetadataName("Fixture.Flat.PlaceOrderResult")!;
        Assert.DoesNotContain(shape.GetAttributes(), a => a.AttributeClass?.Name == "UnionAttribute");
        Assert.DoesNotContain(shape.Interfaces, i => i.Name == "IUnion");
        Assert.Empty(shape.GetMembers("HasValue"));
        Assert.DoesNotContain(output.GeneratedSources.Values, v => v.Contains("Union", StringComparison.Ordinal));
    }

    [Fact]
    public void Net11_a_leaf_converts_into_a_union_shape_without_ambiguity()
    {
        // The shape has both a union constructor and an implicit conversion per leaf; neither may make a plain
        // assignment, a return or an argument ambiguous.
        var consumer = """
            namespace Fixture.Flat
            {
                public static class Uses
                {
                    public static PlaceOrderResult Locked() => new OrderLocked(1);
                    public static PlaceOrderResult Value() => new Order(1);
                    public static PlaceOrderResult Base(OrderError e) => e;
                    public static int Take(PlaceOrderResult r) => 0;
                    public static int Call() => Take(new OrderNotFound(2));
                    public static global::Resultful.Result<Order> Widen(PlaceOrderResult r) => r;
                }
            }
            """;

        var comp = GeneratorTestHarness.CreateCompilation([("Flat.cs", FixtureCatalog.FlatHierarchy), ("Uses.cs", consumer)]);
        var output = GeneratorTestHarness.Run(comp, out _);

        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);
    }
#endif

    private static void VerifyAllGeneratedSourceRules(GeneratorRunOutput output)
    {
        var compilation = output.OutputCompilation;
        var hasUnionAttr = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnionAttribute") is not null
            && (int)((CSharpParseOptions)compilation.SyntaxTrees.First().Options).LanguageVersion >= 1500;

        foreach (var (hintName, text) in output.GeneratedSources)
        {
            // Rule: starts with // <auto-generated/>, #nullable enable and the RF0002 pragma (deliberate zero values)
            Assert.StartsWith("// <auto-generated/>\n#nullable enable\n#pragma warning disable RF0002\n", text, StringComparison.Ordinal);

            var tree = output.OutputCompilation.SyntaxTrees.First(t => t.FilePath.EndsWith(hintName, StringComparison.Ordinal));
            var root = tree.GetRoot();
            var model = compilation.GetSemanticModel(tree);

            // Rule: zero using directives
            Assert.Empty(root.DescendantNodes().OfType<UsingDirectiveSyntax>());

            // Rule: every QualifiedNameSyntax outside namespace declarations is rooted in global::
            foreach (var qn in root.DescendantNodes().OfType<QualifiedNameSyntax>())
            {
                if (qn.Ancestors().Any(a => a is BaseNamespaceDeclarationSyntax or QualifiedNameSyntax))
                {
                    continue;
                }

                Assert.True(
                    qn.ToString().StartsWith("global::", StringComparison.Ordinal),
                    $"Unqualified type name '{qn}' in {hintName}");
            }

            // Rule: cases in every generated switch run most-derived first
            foreach (var sw in root.DescendantNodes().OfType<SwitchStatementSyntax>())
            {
                var seenTypes = new List<ITypeSymbol>();
                foreach (var section in sw.Sections)
                {
                    foreach (var label in section.Labels.OfType<CasePatternSwitchLabelSyntax>())
                    {
                        if (label.Pattern is DeclarationPatternSyntax dp)
                        {
                            var caseType = model.GetTypeInfo(dp.Type).Type;
                            Assert.NotNull(caseType);
                            foreach (var prev in seenTypes)
                            {
                                Assert.False(
                                    DerivesFrom(caseType!, prev),
                                    $"Switch arm '{caseType}' derives from earlier arm '{prev}' in {hintName}");
                            }

                            seenTypes.Add(caseType!);
                        }
                    }
                }
            }

            // Symbol rules on generated [ResultShape] structs
            foreach (var structDecl in root.DescendantNodes().OfType<StructDeclarationSyntax>())
            {
                var sym = model.GetDeclaredSymbol(structDecl)!;

                // Exactly one instance field, _inner
                var fields = sym.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic).ToList();
                Assert.Single(fields);
                Assert.Equal("_inner", fields[0].Name);

                // Carries [MustObserve]
                Assert.Contains(sym.GetAttributes(), a => a.AttributeClass?.Name == "MustObserveAttribute");

                // Every forwarding method (not Match over closed leaves) carries AggressiveInlining
                foreach (var method in sym.GetMembers().OfType<IMethodSymbol>())
                {
                    if (method.IsImplicitlyDeclared || method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet)
                    {
                        continue;
                    }

                    if (method.Name == "Match")
                    {
                        continue;
                    }

                    Assert.True(
                        method.MethodImplementationFlags.HasFlag(System.Reflection.MethodImplAttributes.AggressiveInlining),
                        $"Expected AggressiveInlining on {sym.Name}.{method.Name} in {hintName}");
                }

                // .NET 11 vs net10.0 [Union] rule
                var hasUnionOnShape = sym.GetAttributes().Any(a => a.AttributeClass?.Name == "UnionAttribute");
                Assert.Equal(hasUnionAttr, hasUnionOnShape);
            }
        }
    }

    private static bool DerivesFrom(ITypeSymbol candidate, ITypeSymbol potentialBase)
    {
        var cur = candidate.BaseType;
        while (cur is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(cur, potentialBase))
            {
                return true;
            }

            cur = cur.BaseType;
        }

        return false;
    }
}
