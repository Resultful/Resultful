using Microsoft.CodeAnalysis;
using Xunit;

namespace Resultful.Generators.Tests;

// What decision 16 says the generator writes, one check per member group, on fixtures compiled with the real core.
public sealed class GeneratedMembersTests
{
    private const string ShapeOfHierarchy = "[ResultShape] readonly partial struct";
    private const string Hierarchy = "[ClosedHierarchy] abstract partial record";

    private const string ErrorUnion = "partial union whose cases are all Error";

    // Decision 16 items 3, 4, 5 and 9 (and decision 17 for unions), member by member. Each is either checked below or
    // deferred to the PR that emits it, so deleting a member's check, or a deferral, fails.
    private static readonly (string Declaration, string Member)[] Decision16 =
    [
        (Hierarchy, "private protected .ctor(string message)"),
        (Hierarchy, "TResult Match<TResult>(Func<Leaf, TResult> on<Leaf>, …)"),
        (Hierarchy, "void Switch(Action<Leaf> on<Leaf>, …)"),
        (Hierarchy, "static bool TryMap<TResult>(Error error, Func<Leaf, TResult> on<Leaf>, …, out TResult result)"),
        (Hierarchy, "static IReadOnlyList<Type> Cases { get; }"),
        (ShapeOfHierarchy, "IResultShape<T, TError> (IValidationShape<T, TError> with ManyErrors)"),
        (ShapeOfHierarchy, "[MustObserve]"),
        (ShapeOfHierarchy, "[Obsolete(error: true)] .ctor()"),
        (ShapeOfHierarchy, "static Shape Ok(T value), static Shape Fail(TError error)"),
        (ShapeOfHierarchy, "bool IsDefault"),
        (ShapeOfHierarchy, "bool TryGetValue(out T value), bool TryGetError(out TError error)"),
        (ShapeOfHierarchy, "TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Leaf, TResult> on<Leaf>, …)"),
        (ShapeOfHierarchy, "Result<TOut, TError> Map<TOut>(…), Result<TOut, TError> Bind<TOut>(…)"),
        (ShapeOfHierarchy, "Shape WithContext(string message)"),
        (ShapeOfHierarchy, "Result<T, TError> AsResult()"),
        (ShapeOfHierarchy, "implicit operator Shape(T), implicit operator Shape(Leaf) per leaf"),
        (ShapeOfHierarchy, "implicit operator Shape(Result<T, TError>)"),
        (ShapeOfHierarchy, "implicit operator Shape(OtherShape)"),
        (ShapeOfHierarchy, "implicit operator Result<T, TError>(Shape), implicit operator Result<T>(Shape)"),
        (ShapeOfHierarchy, "[Union], IUnion, bool HasValue, object? Value, public .ctor(Leaf) and bool TryGetValue(out Leaf) per leaf"),
        (ErrorUnion, "IErrorUnion: Error Error { get; }"),
        (ErrorUnion, "string Message, Error? Cause, Union WithContext(string message), bool TryFind<TFound>(out TFound? found)"),
        (ErrorUnion, "static IReadOnlyList<Type> Cases { get; }"),
        (ErrorUnion, "static Union Wrap(Error leaf)"),
    ];

    private static readonly Dictionary<(string Declaration, string Member), string> Deferred = new();

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
        [(ShapeOfHierarchy, "implicit operator Shape(Result<T, TError>)")] = e =>
        {
            Assert.Contains(Conversions(e.Shape), c => c.Parameters[0].Type.ToDisplayString() == "Resultful.Result<Fixture.Flat.Order, Fixture.Flat.OrderError>");
        },
        [(ShapeOfHierarchy, "implicit operator Shape(OtherShape)")] = e =>
        {
            // From each shape with the same success type and kind whose error is a closed base nested under this one's;
            // not from one with the same error type (AsResult() converts those) or another hierarchy.
            INamedTypeSymbol Type(string name) => e.Compilation.GetTypeByMetadataName(name)!;
            static string[] From(INamedTypeSymbol shape) => Conversions(shape)
                .Where(c => SymbolEqualityComparer.Default.Equals(c.ReturnType, shape) &&
                    c.Parameters[0].Type.GetAttributes().Any(a => a.AttributeClass?.Name == "ResultShapeAttribute"))
                .Select(c => c.Parameters[0].Type.ToDisplayString())
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(["Fixture.Deep.DomainResult", "Fixture.Deep.PayResult"], From(Type("Fixture.Deep.CheckoutResult")));
            Assert.Equal(["Fixture.Deep.PayResult"], From(Type("Fixture.Deep.DomainResult")));
            Assert.Empty(From(Type("Fixture.Deep.PayResult")));
            Assert.Equal(["Fixture.Deep.DomainChecks"], From(Type("Fixture.Deep.AppChecks")));
            Assert.Empty(From(e.Shape));
            Assert.Empty(From(Type("Fixture.Flat.LoadOrderResult")));
            Assert.Empty(From(e.ManyShape));
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

    // Error unions need C# 15 and net11.0's IUnion; on net10.0 the rows check that there are none to emit.
    private static readonly Dictionary<(string Declaration, string Member), Action<UnionEmitted?>> UnionChecks = new()
    {
        [(ErrorUnion, "IErrorUnion: Error Error { get; }")] = u =>
        {
            if (u is null) return;
            Assert.Contains(u.Union.Interfaces, i => i.ToDisplayString() == "Resultful.IErrorUnion");
            var error = Assert.Single(u.Union.GetMembers("Error").OfType<IPropertySymbol>());
            Assert.Equal("Resultful.Error", error.Type.ToDisplayString());
            Assert.Null(error.SetMethod);
        },
        [(ErrorUnion, "string Message, Error? Cause, Union WithContext(string message), bool TryFind<TFound>(out TFound? found)")] = u =>
        {
            if (u is null) return;
            Assert.Equal(SpecialType.System_String, Assert.Single(u.Union.GetMembers("Message").OfType<IPropertySymbol>()).Type.SpecialType);
            Assert.Equal("Resultful.Error?", Assert.Single(u.Union.GetMembers("Cause").OfType<IPropertySymbol>()).Type.ToDisplayString());
            var withContext = Assert.Single(u.Union.GetMembers("WithContext").OfType<IMethodSymbol>());
            Assert.Equal(u.Union, withContext.ReturnType, SymbolEqualityComparer.Default);
            Assert.Equal("message", Assert.Single(withContext.Parameters).Name);
            var tryFind = Assert.Single(u.Union.GetMembers("TryFind").OfType<IMethodSymbol>());
            Assert.Equal("TFound", Assert.Single(tryFind.TypeParameters).Name);
            Assert.Equal(RefKind.Out, Assert.Single(tryFind.Parameters).RefKind);
        },
        [(ErrorUnion, "static IReadOnlyList<Type> Cases { get; }")] = u =>
        {
            if (u is null) return;
            var cases = Assert.Single(u.Union.GetMembers("Cases").OfType<IPropertySymbol>());
            Assert.True(cases.IsStatic);
            Assert.Equal("System.Collections.Generic.IReadOnlyList<System.Type>", cases.Type.ToDisplayString());
        },
        [(ErrorUnion, "static Union Wrap(Error leaf)")] = u =>
        {
            if (u is null) return;
            var wrap = Assert.Single(u.Union.GetMembers("Wrap").OfType<IMethodSymbol>());
            Assert.True(wrap.IsStatic);
            Assert.Equal(u.Union, wrap.ReturnType, SymbolEqualityComparer.Default);
            Assert.Equal("Resultful.Error", Assert.Single(wrap.Parameters).Type.ToDisplayString());
        },
    };

    public static TheoryData<string, string> UnionRows()
    {
        var rows = new TheoryData<string, string>();
        foreach (var key in UnionChecks.Keys)
        {
            rows.Add(key.Declaration, key.Member);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(UnionRows))]
    public void Each_error_union_member_is_emitted_as_decision_17_describes(string declaration, string member) =>
        UnionChecks[(declaration, member)](UnionOutput.Value);

    private static readonly Lazy<UnionEmitted?> UnionOutput = new(EmitUnion);

    private sealed record UnionEmitted(INamedTypeSymbol Union);

    private static UnionEmitted? EmitUnion()
    {
#if NET11_0_OR_GREATER
        var output = GeneratorTestHarness.Run(GeneratorTestHarness.CreateCompilation([("Nested.cs", FixtureCatalog.NestedUnion)]), out _);
        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return new UnionEmitted(output.OutputCompilation.GetTypeByMetadataName("Fixture.Nested.CheckoutError")!);
#else
        Assert.Null(GeneratorTestHarness.CreateCompilation([("Empty.cs", "")]).GetTypeByMetadataName("System.Runtime.CompilerServices.IUnion"));
        return null;
#endif
    }

    public static TheoryData<string, string> Rows()
    {
        var rows = new TheoryData<string, string>();
        foreach (var key in Checks.Keys)
        {
            rows.Add(key.Declaration, key.Member);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Each_member_is_emitted_as_decision_16_describes(string declaration, string member) =>
        Checks[(declaration, member)](Output.Value);

    private static readonly Lazy<Emitted> Output = new(Emit);

    [Fact]
    public void Every_member_of_decision_16_is_checked_or_deferred_once()
    {
        var expected = Decision16.Order().ToList();
        var accounted = Checks.Keys.Concat(UnionChecks.Keys).Concat(Deferred.Keys).Order().ToList();
        Assert.Equal(expected, accounted);
    }

    [Fact]
    public void The_core_members_the_emitter_calls_exist()
    {
        // What the emitted code calls on the core: the result structs have no static Ok/Fail, so the builders are on
        // Result and Validation, and the zero value is the one that is neither a success nor a failure.
        var comp = GeneratorTestHarness.CreateCompilation([("Empty.cs", "")]);
        INamedTypeSymbol Type(string name) => comp.GetTypeByMetadataName(name) ?? throw new InvalidOperationException(name + " is not in the core");
        void Has(INamedTypeSymbol type, params string[] members)
        {
            foreach (var member in members)
            {
                Assert.True(type.GetMembers(member).Length > 0, $"{type.ToDisplayString()} has no {member}");
            }
        }

        Has(Type("Resultful.Result`2"), "IsSuccess", "IsFailure", "TryGetValue", "TryGetError", "Match", "Map", "Bind");
        Has(Type("Resultful.Validation`2"), "IsSuccess", "IsFailure", "TryGetValue", "TryGetErrors", "Match", "Map", "MapError");
        Has(Type("Resultful.IResultShape`2"), "AsResult");
        Has(Type("Resultful.IValidationShape`2"), "AsValidation");
        Has(Type("Resultful.Result"), "Ok", "Fail", "AsResult", "WithContext");
        Has(Type("Resultful.Validation"), "Ok", "Fail", "ToResult");
        Has(Type("Resultful.ErrorExtensions"), "WithContext");
        Has(Type("Resultful.Error"), "Causes", "TryFind", "Message", "Cause");
        Has(Type("Resultful.IErrorUnion"), "Error");
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
            ("Peers.cs", FixtureCatalog.ShapePeers),
            ("Deep.cs", FixtureCatalog.DeepFourLevelHierarchy + FixtureCatalog.NestedPeers),
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
