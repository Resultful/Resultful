using Microsoft.CodeAnalysis;
using Xunit;

namespace Resultful.Generators.Tests;

// The emitted members against the target's tests/Resultful.TargetApi/Resultful.Generators/generated-members.tsv. Every
// row is either checked here or deferred to the PR that owns it; a new or reworded row fails until it is one of the two.
public sealed class GeneratedMembersTests
{
    private const string ShapeOfHierarchy = "[ResultShape] readonly partial struct";
    private const string Hierarchy = "[ClosedHierarchy] abstract partial record";
    private const string ErrorUnion = "partial union whose cases are all Error";

    // Rows another PR emits. Keyed by declaration and member, as written in the tsv.
    private static readonly Dictionary<(string Declaration, string Member), string> Deferred = new()
    {
        [(ErrorUnion, "IErrorUnion: Error Error { get; }")] = "13b (decision 17: error unions)",
        [(ErrorUnion, "string Message, Error? Cause, Union WithContext(string message), bool TryFind<TFound>(out TFound? found)")] = "13b (decision 17: error unions)",
        [(ErrorUnion, "static IReadOnlyList<Type> Cases { get; }")] = "13b (decision 17: error unions)",

        // Half deferred: the conversion from Result<T, TError> is checked below; from another shape is 15's.
        [(ShapeOfHierarchy, "implicit operator Shape(Result<T, TError>), implicit operator Shape(OtherShape)")] = "15 (shape-conversions: the OtherShape half)",
    };

    private static readonly Dictionary<(string Declaration, string Member), Action<Emitted>> Checks = new()
    {
        [(Hierarchy, "private protected .ctor(string message)")] = e =>
        {
            static bool IsMessage(IMethodSymbol c) => c.Parameters.Length == 1 && c.Parameters[0].Type.SpecialType == SpecialType.System_String;
            var ctor = Assert.Single(e.BareHierarchy.InstanceConstructors, c => IsMessage(c) && IsGenerated(e, c));
            Assert.Equal(Accessibility.ProtectedAndInternal, ctor.DeclaredAccessibility);
            Assert.Equal("message", ctor.Parameters[0].Name);

            // Only when no matching constructor is written: OrderError(string Message) has its own.
            Assert.DoesNotContain(e.Hierarchy.InstanceConstructors, c => IsMessage(c) && IsGenerated(e, c));
        },
        [(Hierarchy, "TResult Match<TResult>(Func<Leaf, TResult> on<Leaf>, …)")] = e =>
        {
            var match = Single(e, e.Hierarchy, "Match");
            Assert.Equal("TResult", Assert.Single(match.TypeParameters).Name);
            Assert.Equal(e.LeafParameterNames, match.Parameters.Select(p => p.Name));
        },
        [(Hierarchy, "void Switch(Action<Leaf> on<Leaf>, …)")] = e =>
        {
            var sw = Single(e, e.Hierarchy, "Switch");
            Assert.True(sw.ReturnsVoid);
            Assert.Equal(e.LeafParameterNames, sw.Parameters.Select(p => p.Name));
        },
        [(Hierarchy, "static bool TryMap<TResult>(Error error, Func<Leaf, TResult> on<Leaf>, …, out TResult result)")] = e =>
        {
            var tryMap = Single(e, e.Hierarchy, "TryMap");
            Assert.True(tryMap.IsStatic);
            Assert.Equal(SpecialType.System_Boolean, tryMap.ReturnType.SpecialType);
            Assert.Equal(["error", .. e.LeafParameterNames, "result"], tryMap.Parameters.Select(p => p.Name));
            Assert.Equal(RefKind.Out, tryMap.Parameters[^1].RefKind);
            Assert.Equal("global::Resultful.Error", tryMap.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        },
        [(Hierarchy, "static IReadOnlyList<Type> Cases { get; }")] = e =>
        {
            var cases = Assert.Single(e.Hierarchy.GetMembers("Cases").OfType<IPropertySymbol>());
            Assert.True(cases.IsStatic);
            Assert.Null(cases.SetMethod);
            Assert.Equal("global::System.Collections.Generic.IReadOnlyList<global::System.Type>", cases.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        },
        [(ShapeOfHierarchy, "IResultShape<T, TError> (IValidationShape<T, TError> with ManyErrors)")] = e =>
        {
            Assert.Contains(e.Shape.Interfaces, i => i.ToDisplayString() == "Resultful.IResultShape<Fixture.Flat.Order, Fixture.Flat.OrderError>");
            Assert.Contains(e.ManyShape.Interfaces, i => i.ToDisplayString() == "Resultful.IValidationShape<Fixture.Many.Registration, Fixture.Many.InputError>");
            var field = Assert.Single(e.Shape.GetMembers().OfType<IFieldSymbol>(), f => !f.IsStatic);
            Assert.Equal("_inner", field.Name);
        },
        [(ShapeOfHierarchy, "[MustObserve]")] = e =>
            Assert.Contains(e.Shape.GetAttributes(), a => a.AttributeClass?.ToDisplayString() == "Resultful.CodeAnalysis.MustObserveAttribute"),
        [(ShapeOfHierarchy, "[Obsolete(error: true)] .ctor()")] = e =>
        {
            var ctor = Assert.Single(e.Shape.InstanceConstructors, c => c.Parameters.Length == 0 && !c.IsImplicitlyDeclared);
            var obsolete = Assert.Single(ctor.GetAttributes(), a => a.AttributeClass?.Name == "ObsoleteAttribute");
            Assert.Equal(true, obsolete.ConstructorArguments[1].Value);
        },
        [(ShapeOfHierarchy, "static Shape Ok(T value), static Shape Fail(TError error)")] = e =>
        {
            var ok = Single(e, e.Shape, "Ok");
            Assert.True(ok.IsStatic);
            Assert.Equal("Fixture.Flat.Order", Assert.Single(ok.Parameters).Type.ToDisplayString());
            var fail = Single(e, e.Shape, "Fail");
            Assert.Equal("Fixture.Flat.OrderError", Assert.Single(fail.Parameters).Type.ToDisplayString());

            // Ok() with Unit when there is no Success.
            Assert.Empty(Single(e, e.ValuelessShape, "Ok").Parameters);
        },
        [(ShapeOfHierarchy, "bool IsDefault")] = e =>
        {
            var isDefault = Assert.Single(e.Shape.GetMembers("IsDefault").OfType<IPropertySymbol>());
            Assert.Equal(SpecialType.System_Boolean, isDefault.Type.SpecialType);
            Assert.Single(e.ManyShape.GetMembers("IsDefault").OfType<IPropertySymbol>());
        },
        [(ShapeOfHierarchy, "bool TryGetValue(out T value), bool TryGetError(out TError error)")] = e =>
        {
            Assert.Contains(e.Shape.GetMembers("TryGetValue").OfType<IMethodSymbol>(), m => m.Parameters is [{ Name: "value", RefKind: RefKind.Out }]);
            Assert.Contains(e.Shape.GetMembers("TryGetError").OfType<IMethodSymbol>(), m => m.Parameters is [{ Name: "error", RefKind: RefKind.Out }]);

            // TryGetErrors(out IReadOnlyList<TError>) with ManyErrors.
            Assert.Contains(e.ManyShape.GetMembers("TryGetErrors").OfType<IMethodSymbol>(), m => m.Parameters is [{ RefKind: RefKind.Out }]);
        },
        [(ShapeOfHierarchy, "TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Leaf, TResult> on<Leaf>, …)")] = e =>
        {
            var match = Single(e, e.Shape, "Match");
            Assert.Equal("TResult", Assert.Single(match.TypeParameters).Name);
            Assert.Equal(["onSuccess", .. e.LeafParameterNames], match.Parameters.Select(p => p.Name));
        },
        [(ShapeOfHierarchy, "Result<TOut, TError> Map<TOut>(…), Result<TOut, TError> Bind<TOut>(…)")] = e =>
        {
            Assert.Equal("Resultful.Result<TOut, Fixture.Flat.OrderError>", Single(e, e.Shape, "Map").ReturnType.ToDisplayString());
            Assert.Equal("Resultful.Result<TOut, Fixture.Flat.OrderError>", Single(e, e.Shape, "Bind").ReturnType.ToDisplayString());
        },
        [(ShapeOfHierarchy, "Shape WithContext(string message)")] = e =>
        {
            Assert.Equal(e.Shape, Single(e, e.Shape, "WithContext").ReturnType, SymbolEqualityComparer.Default);
            Assert.Equal(e.ManyShape, Single(e, e.ManyShape, "WithContext").ReturnType, SymbolEqualityComparer.Default);
        },
        [(ShapeOfHierarchy, "Result<T, TError> AsResult()")] = e =>
            Assert.Equal("Resultful.Result<Fixture.Flat.Order, Fixture.Flat.OrderError>", Single(e, e.Shape, "AsResult").ReturnType.ToDisplayString()),
        [(ShapeOfHierarchy, "implicit operator Shape(T), implicit operator Shape(Leaf) per leaf")] = e =>
        {
            var from = Conversions(e.Shape).Where(c => SymbolEqualityComparer.Default.Equals(c.ReturnType, e.Shape)).Select(c => c.Parameters[0].Type.ToDisplayString()).ToHashSet();
            Assert.Contains("Fixture.Flat.Order", from);
            Assert.Contains("Fixture.Flat.OrderNotFound", from);
            Assert.Contains("Fixture.Flat.OrderLocked", from);
        },
        [(ShapeOfHierarchy, "implicit operator Shape(Result<T, TError>), implicit operator Shape(OtherShape)")] = e =>
        {
            // From the core value. From another shape is decision 16 item 5, built in 15 (shape-conversions).
            Assert.Contains(Conversions(e.Shape), c => c.Parameters[0].Type.ToDisplayString() == "Resultful.Result<Fixture.Flat.Order, Fixture.Flat.OrderError>");
        },
        [(ShapeOfHierarchy, "implicit operator Result<T, TError>(Shape), implicit operator Result<T>(Shape)")] = e =>
        {
            var to = Conversions(e.Shape).Where(c => SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, e.Shape)).Select(c => c.ReturnType.ToDisplayString()).ToHashSet();
            Assert.Contains("Resultful.Result<Fixture.Flat.Order, Fixture.Flat.OrderError>", to);
            Assert.Contains("Resultful.Result<Fixture.Flat.Order>", to);
            Assert.Contains(Conversions(e.ManyShape), c => c.ReturnType.ToDisplayString() == "Resultful.Result<Fixture.Many.Registration>");
        },
        [(ShapeOfHierarchy, "[Union], IUnion, bool HasValue, object? Value, public .ctor(Leaf) and bool TryGetValue(out Leaf) per leaf")] = e =>
        {
            var union = e.Shape.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.UnionAttribute");
            if (!e.UnionAvailable)
            {
                Assert.False(union);
                Assert.Empty(e.Shape.GetMembers("HasValue"));
                return;
            }

            Assert.True(union);
            Assert.Contains(e.Shape.Interfaces, i => i.ToDisplayString() == "System.Runtime.CompilerServices.IUnion");
            Assert.Single(e.Shape.GetMembers("HasValue").OfType<IPropertySymbol>());
            Assert.Single(e.Shape.GetMembers("Value").OfType<IPropertySymbol>());
            foreach (var leaf in new[] { "Fixture.Flat.OrderNotFound", "Fixture.Flat.OrderLocked" })
            {
                Assert.Single(e.Shape.InstanceConstructors, c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 1 && c.Parameters[0].Type.ToDisplayString() == leaf);
                Assert.Single(e.Shape.GetMembers("TryGetValue").OfType<IMethodSymbol>(), m => m.Parameters[0].Type.ToDisplayString() == leaf);
            }
        },
    };

    [Fact]
    public void Every_row_of_generated_members_tsv_is_emitted_or_deferred_to_a_named_PR()
    {
        var path = Path.Combine(GeneratorTestHarness.RepoRootDir, "tests", "Resultful.TargetApi", "Resultful.Generators", "generated-members.tsv");
        var rows = File.ReadAllLines(path)
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Skip(1)
            .Select(l => l.Split('\t'))
            .Select(c => (Declaration: c[0], Member: c[1]))
            .ToList();
        Assert.NotEmpty(rows);

        var emitted = Emit();
        var unknown = rows.Where(r => !Checks.ContainsKey(r) && !Deferred.ContainsKey(r)).ToList();
        TestContext.Current.TestOutputHelper?.WriteLine("deferred:\n" + string.Join("\n", Deferred.Select(d => $"{d.Key.Declaration} | {d.Key.Member} -> {d.Value}")));
        Assert.True(unknown.Count == 0, "rows neither checked nor deferred:\n" + string.Join("\n", unknown));

        var failures = new List<string>();
        foreach (var row in rows.Where(Checks.ContainsKey))
        {
            try
            {
                Checks[row](emitted);
            }
            catch (Exception ex) when (ex is Xunit.Sdk.XunitException or InvalidOperationException)
            {
                failures.Add($"{row.Declaration} | {row.Member}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));

        // Checks and deferrals for rows the tsv no longer has are stale.
        var stale = Checks.Keys.Concat(Deferred.Keys).Where(k => !rows.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "checked or deferred rows missing from the tsv:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void The_core_members_the_emitter_calls_exist_in_the_target_API()
    {
        // The handover notes' list (stack-plan GOAL.md, 13a): IsDefault, TryGetValue, TryGetError, TryGetErrors,
        // AsResult(), IResultShape<T, TError>, IValidationShape<T, TError>; plus the builders and WithContext the
        // emitter calls, since the target's result structs have no static Ok/Fail.
        var comp = GeneratorTestHarness.CreateCompilation([("Empty.cs", "")]);
        INamedTypeSymbol Type(string name) => comp.GetTypeByMetadataName(name) ?? throw new InvalidOperationException(name + " is not in the target API");
        void Has(INamedTypeSymbol type, params string[] members)
        {
            foreach (var member in members)
            {
                Assert.True(type.GetMembers(member).Length > 0, $"{type.ToDisplayString()} has no {member}");
            }
        }

        Has(Type("Resultful.Result`2"), "IsDefault", "TryGetValue", "TryGetError", "Match", "Map", "Bind");
        Has(Type("Resultful.Validation`2"), "IsDefault", "TryGetValue", "TryGetErrors", "Match", "Map", "MapError");
        Has(Type("Resultful.IResultShape`2"), "AsResult");
        Has(Type("Resultful.IValidationShape`2"), "AsValidation");
        Has(Type("Resultful.Result"), "Ok", "Fail", "AsResult", "WithContext");
        Has(Type("Resultful.Validation"), "Ok", "Fail", "ToResult");
        Has(Type("Resultful.ErrorExtensions"), "WithContext");
        Has(Type("Resultful.Error"), "Causes");
        Has(Type("Resultful.Unit"), "Value");
        Assert.NotNull(Type("Resultful.CodeAnalysis.MustObserveAttribute"));
    }

    private sealed record Emitted(
        Compilation Compilation,
        HashSet<SyntaxTree> GeneratedTrees,
        INamedTypeSymbol Hierarchy,
        INamedTypeSymbol BareHierarchy,
        INamedTypeSymbol Shape,
        INamedTypeSymbol ManyShape,
        INamedTypeSymbol ValuelessShape,
        string[] LeafParameterNames,
        bool UnionAvailable);

    private static Emitted Emit()
    {
        var comp = GeneratorTestHarness.CreateCompilation([
            ("Flat.cs", FixtureCatalog.FlatHierarchy),
            ("Many.cs", FixtureCatalog.ManyErrorsShape),
            ("Void.cs", FixtureCatalog.ValuelessShape),
            ("Bare.cs", "namespace Fixture.Bare { [Resultful.CodeAnalysis.ClosedHierarchy] public abstract partial record BareError : Resultful.Error; public sealed record Boom() : BareError(\"boom\"); }"),
        ]);
        var output = GeneratorTestHarness.Run(comp, out _);
        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var c = output.OutputCompilation;
        INamedTypeSymbol Type(string name) => c.GetTypeByMetadataName(name) ?? throw new InvalidOperationException(name);
        return new Emitted(
            c,
            output.RunResult.GeneratedTrees.ToHashSet(),
            Type("Fixture.Flat.OrderError"),
            Type("Fixture.Bare.BareError"),
            Type("Fixture.Flat.PlaceOrderResult"),
            Type("Fixture.Many.RegisterResult"),
            Type("Fixture.Void.CancelOrderResult"),
            ["onOrderLocked", "onOrderNotFound"],
            c.GetTypeByMetadataName("System.Runtime.CompilerServices.UnionAttribute") is not null);
    }

    private static bool IsGenerated(Emitted e, ISymbol symbol) =>
        symbol.DeclaringSyntaxReferences.Any(r => e.GeneratedTrees.Contains(r.SyntaxTree));

    // The one generated method of that name.
    private static IMethodSymbol Single(Emitted e, INamedTypeSymbol type, string name) =>
        Assert.Single(type.GetMembers(name).OfType<IMethodSymbol>(), m => IsGenerated(e, m));

    private static IEnumerable<IMethodSymbol> Conversions(INamedTypeSymbol type) =>
        type.GetMembers("op_Implicit").OfType<IMethodSymbol>();
}
