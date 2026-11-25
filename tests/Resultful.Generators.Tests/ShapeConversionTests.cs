using System.Text;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Resultful.Generators.Tests;

// Decision 16 items 4, 5 and 7 (SHAPE-INTO, SHAPE-GATHER): conversions into a shape from a shape whose error is a closed
// base nested under its own, from the core value and from Validation, run against the real core and checked to keep the
// value or the same error instances; a shape with the same error type goes through AsResult(). Then the conversions that
// must not compile and the overloads they change, from the compiler's own diagnostics, and how they scale.
public sealed class ShapeConversionTests
{
    private const string Probe = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using Resultful;
        using Fixture.Deep;
        using Fixture.Flat;
        using Fixture.Many;

        public static class Probe
        {
            // A shape with the same types converts through the core value, keeping the value or the error instance.
            public static string SameErrorThroughAsResult()
            {
                var missing = new OrderNotFound(2);
                LoadOrderResult loaded = missing;
                PlaceOrderResult placed = loaded.AsResult();
                LoadOrderResult back = placed.AsResult();
                LoadOrderResult okLoaded = new Order(7);
                PlaceOrderResult ok = okLoaded.AsResult();
                return $"{placed.TryGetError(out var e) && ReferenceEquals(e, missing)} {back.TryGetError(out var b) && ReferenceEquals(b, missing)} {ok.TryGetValue(out Order? o) && o.Id == 7}";
            }

            public static string FromCoreResult()
            {
                var locked = new OrderLocked(4);
                PlaceOrderResult failed = Result.Fail<Order, OrderError>(locked);
                PlaceOrderResult ok = Result.Ok<Order, OrderError>(new Order(5));
                return $"{failed.TryGetError(out var e) && ReferenceEquals(e, locked)} {ok.TryGetValue(out Order? o) && o.Id == 5}";
            }

            // PaymentError is a [ClosedHierarchy] under DomainError under AppError: its shape converts up, on return too.
            public static string FromNestedBase()
            {
                var expired = new CardExpired();
                PayResult paid = expired;
                CheckoutResult checkout = paid;
                DomainResult domain = paid;
                static PayResult Pay(int n) => n > 0 ? new Receipt("r" + n) : new InsufficientFunds();
                static CheckoutResult Checkout(int n) => Pay(n);
                return $"{checkout.TryGetError(out var e) && ReferenceEquals(e, expired)} {domain.TryGetError(out var d) && ReferenceEquals(d, expired)} " +
                    Checkout(1).Match(r => r.Code, _ => "expired", _ => "funds", _ => "missing", _ => "limited") + " " +
                    Checkout(0).Match(r => r.Code, _ => "expired", _ => "funds", _ => "missing", _ => "limited");
            }

            // The zero value converts to the zero value (decision 10: only reading it throws).
            public static string ZeroFromNestedBase()
            {
                CheckoutResult checkout = default(PayResult);
                AppChecks checks = default(DomainChecks);
                string read;
                try { checkout.TryGetValue(out Receipt? _); read = "no throw"; }
                catch (InvalidOperationException) { read = "throws"; }
                return $"{checkout.IsDefault} {checks.IsDefault} {read}";
            }

            // A ManyErrors shape converts from one over a nested base, keeping the list, and from Validation<T, TError>.
            public static string ManyFromNestedBaseAndValidation()
            {
                var missing = new OrderMissing(1);
                var expired = new CardExpired();
                AppChecks app = DomainChecks.Fail(new DomainError[] { missing, expired });
                var errors = new InputError[] { new Required("email"), new TooLong("name") };
                RegisterResult fromValidation = Validation.Fail<Registration, InputError>(errors);
                RegisterResult fromPeer = ReviewResult.Fail(errors).AsValidation();
                RegisterResult ok = Validation.Ok<Registration, InputError>(new Registration("a@b.c"));
                var many = app.TryGetErrors(out var es) && ReferenceEquals(es[0], missing) && ReferenceEquals(es[1], expired);
                return $"{many} {Same(fromValidation, errors)} {Same(fromPeer, errors)} {ok.TryGetValue(out Registration? r) && r.Email == "a@b.c"}";
            }

            // A failed ManyErrors shape needs at least one error.
            public static string ManyFailsWithANonEmptyList()
            {
                var one = RegisterResult.Fail(new InputError[] { new Required("email") });
                string empty;
                try { RegisterResult.Fail(Array.Empty<InputError>()); empty = "no throw"; }
                catch (ArgumentException) { empty = "throws"; }
                return $"{one.TryGetErrors(out var es) && es.Count == 1} {empty}";
            }

            // Combine gathers every error, in order, into the shape.
            public static string CombineGathersEveryError()
            {
                static Validation<string, InputError> Name(string s) => s.Length == 0 ? new Required("name") : s.Length > 5 ? new TooLong("name") : s;
                static Validation<string, InputError> Email(string s) => s.Contains('@') ? s : new Required("email");
                static RegisterResult Register(string name, string email, string other) =>
                    Validation.Combine(Name(name), Email(email), Name(other), (n, e, o) => new Registration(e));

                var failed = Register("", "nope", "toolong");
                var ok = Register("ann", "a@b.c", "bo");
                var arms = failed.Match(r => "ok", es => string.Join("|", es.Select(x => x.Message)));
                return $"{arms} {ok.TryGetValue(out Registration? r) && r.Email == "a@b.c"}";
            }

            // WithContext keeps the ManyErrors shape and notes every error.
            public static string ManyWithContext()
            {
                RegisterResult failed = RegisterResult.Fail(new InputError[] { new Required("email"), new TooLong("name") });
                RegisterResult noted = failed.WithContext("signing up");
                noted.TryGetErrors(out var es);
                return $"{es![0] is Required} {es[1] is TooLong} {string.Join(",", es.Select(e => string.Join("|", e.Context)))}";
            }

            // What the one-way conversion changes for existing code: K(object) and K(Result<Receipt>) now lose to
            // K(CheckoutResult), and ?:, switch expressions and array literals mixing the two have CheckoutResult as type.
            public static string OverloadsAndCommonType()
            {
                PayResult pay = new CardExpired();
                CheckoutResult checkout = new RateLimited();
                var picked = K(pay) + "," + W(pay);
                var either = DateTime.Now.Year > 0 ? pay : checkout;
                var switched = 1 switch { 0 => checkout, _ => pay };
                var array = new[] { pay, checkout };
                return $"{picked} {either.GetType().Name} {switched.TryGetError(out var e) && e is CardExpired} {array.GetType().Name}";
            }

            private static string K(CheckoutResult r) => "shape";
            private static string K(object o) => "object";
            private static string W(CheckoutResult r) => "shape";
            private static string W(Result<Receipt> r) => "result";

            private static bool Same(RegisterResult r, InputError[] expected) =>
                r.TryGetErrors(out var es) && es.Count == expected.Length && es.Select((e, i) => ReferenceEquals(e, expected[i])).All(x => x);
        }
        """;

    private static readonly (string FileName, string Source)[] Fixtures =
    [
        ("Flat.cs", FixtureCatalog.FlatHierarchy),
        ("Many.cs", FixtureCatalog.ManyErrorsShape),
        ("Void.cs", FixtureCatalog.ValuelessShape),
        ("Peers.cs", FixtureCatalog.ShapePeers),
        ("Deep.cs", FixtureCatalog.DeepFourLevelHierarchy + FixtureCatalog.NestedPeers),
    ];

    private static readonly Lazy<System.Reflection.Assembly> Loaded = new(() => RuntimeTests.Compile(
        [.. Fixtures, ("Probe.cs", Probe)],
        "Conversions"));

    [Theory]
    [InlineData("SameErrorThroughAsResult", "True True True")]
    [InlineData("FromCoreResult", "True True")]
    [InlineData("FromNestedBase", "True True r1 funds")]
    [InlineData("ZeroFromNestedBase", "True True throws")]
    [InlineData("ManyFromNestedBaseAndValidation", "True True True True")]
    [InlineData("ManyFailsWithANonEmptyList", "True throws")]
    [InlineData("CombineGathersEveryError", "name required|email required|name too long True")]
    [InlineData("ManyWithContext", "True True signing up,signing up")]
    [InlineData("OverloadsAndCommonType", "shape,shape CheckoutResult True CheckoutResult[]")]
    public void A_conversion_into_a_shape_keeps_the_value_or_the_errors(string probe, string expected)
    {
        var actual = (string)Loaded.Value.GetType("Probe", throwOnError: true)!.GetMethod(probe)!.Invoke(null, null)!;
        Assert.Equal(expected, actual);
    }

    // Each member body must not compile, and only for the reason given. Fixture.Void.CancelError is another hierarchy;
    // LoadOrderResult and ReviewResult have PlaceOrderResult's and RegisterResult's types.
    [Theory]
    [InlineData("the same error type", "PlaceOrderResult M() => default(LoadOrderResult);", "CS0029")]
    [InlineData("the same error type, ManyErrors", "Fixture.Many.RegisterResult M() => default(Fixture.Many.ReviewResult);", "CS0029")]
    [InlineData("another error type", "PlaceOrderResult M() => default(CancelForOrderResult);", "CS0029")]
    [InlineData("another success type", "PlaceOrderResult M() => default(Fixture.Void.CancelOrderResult);", "CS0029")]
    [InlineData("a core result with another error type", "PlaceOrderResult M() => default(Result<Order, Fixture.Void.CancelError>);", "CS0029")]
    [InlineData("a ManyErrors shape into a plain one", "PlaceOrderResult M() => default(Fixture.Many.RegisterResult);", "CS0029")]
    [InlineData("a plain shape into a ManyErrors one", "Fixture.Many.RegisterResult M() => default(PlaceOrderResult);", "CS0029")]
    [InlineData("a nested base's shape back down", "Fixture.Deep.PayResult M() => default(Fixture.Deep.CheckoutResult);", "CS0029")]
    [InlineData("a nested base's ManyErrors shape back down", "Fixture.Deep.DomainChecks M() => default(Fixture.Deep.AppChecks);", "CS0029")]
    [InlineData("no chain through the core result", "PlaceOrderResult M() => (Result<Order, OrderError>)default(CancelForOrderResult);", "CS0030")]
    // Two shapes with the same types have no conversion either way, so no common type without a target type. Shapes
    // never convert both ways (CS0172), since a same-error conversion isn't emitted.
    [InlineData("?: over same-error shapes", "void M(bool b) { var v = b ? default(LoadOrderResult) : default(PlaceOrderResult); }", "CS0173")]
    [InlineData("switch over same-error shapes", "void M(int n) { var v = n switch { 0 => default(LoadOrderResult), _ => default(PlaceOrderResult) }; }", "CS8506")]
    [InlineData("array over same-error shapes", "object M() => new[] { default(LoadOrderResult), default(PlaceOrderResult) };", "CS0826")]
    [InlineData("inference over same-error shapes", "object M() => Pick(default(LoadOrderResult), default(PlaceOrderResult)); static T Pick<T>(T a, T b) => a;", "CS0411")]
    // A PayResult converts to its own core type and to the nested base's shape, neither better: K(Result<Receipt,
    // PaymentError>) alone bound before, and now the call is ambiguous.
    [InlineData("K(CheckoutResult) and K(Result<Receipt, PaymentError>) with a PayResult", "string M() => K(default(Fixture.Deep.PayResult)); static string K(Fixture.Deep.CheckoutResult r) => \"\"; static string K(Result<Fixture.Deep.Receipt, Fixture.Deep.PaymentError> r) => \"\";", "CS0121")]
    public void A_conversion_that_must_not_compile_does_not(string what, string member, string id)
    {
        var errors = CompileErrors(("Uses.cs", Uses(member)));
        Assert.True(errors.Count == 1 && errors[0].Id == id, $"{what}: expected one {id}, got {string.Join("\n", errors)}");
    }

    [Fact]
    public void An_internal_shape_converts_into_a_public_one_only_through_the_core_value()
    {
        // A public operator can't take an internal shape (CS0057), so the public shape has no conversion from it.
        const string Shapes = """
            namespace Fixture.Deep
            {
                [Resultful.CodeAnalysis.ResultShape(typeof(PaymentError), Success = typeof(Receipt))]
                internal readonly partial struct InternalPayResult;
            }
            """;

        Assert.Empty(CompileErrors(("Shapes.cs", Shapes), ("Uses.cs", Uses("Fixture.Deep.CheckoutResult M() => default(Fixture.Deep.InternalPayResult).AsResult().MapError<Fixture.Deep.AppError>(e => e);"))));
        var errors = CompileErrors(("Shapes.cs", Shapes), ("Uses.cs", Uses("Fixture.Deep.CheckoutResult M() => default(Fixture.Deep.InternalPayResult);")));
        Assert.Equal("CS0029", Assert.Single(errors).Id);
    }

    [Fact]
    public void Same_error_shapes_emit_no_conversions_and_nested_bases_stay_linear()
    {
        // 200 shapes with one error type would be 39,800 operators if each converted from the others: none is emitted.
        var same = new StringBuilder("namespace Fixture.Flat {\n");
        for (var i = 0; i < 200; i++)
        {
            same.Append("[Resultful.CodeAnalysis.ResultShape(typeof(OrderError), Success = typeof(Order))] public readonly partial struct Same").Append(i).Append("Result;\n");
        }

        Assert.Equal(0, ShapeConversionCount(GeneratorTestHarness.CreateCompilation([("Flat.cs", FixtureCatalog.FlatHierarchy), ("Same.cs", same.Append('}').ToString())]), out _));

        // 50 closed bases under one root, each with its own shape: the root's shape converts from each, once.
        var nested = new StringBuilder("""
            namespace Fixture.Wide
            {
                [Resultful.CodeAnalysis.ClosedHierarchy] public abstract partial record RootError(string Message) : Resultful.Error(Message);
                public sealed record Out();
                [Resultful.CodeAnalysis.ResultShape(typeof(RootError), Success = typeof(Out))] public readonly partial struct RootResult;

            """);
        for (var i = 0; i < 50; i++)
        {
            nested.Append($"[Resultful.CodeAnalysis.ClosedHierarchy] public abstract partial record Sub{i}(string Message) : RootError(Message);\n");
            nested.Append($"public sealed record Leaf{i}() : Sub{i}(\"leaf {i}\");\n");
            nested.Append($"[Resultful.CodeAnalysis.ResultShape(typeof(Sub{i}), Success = typeof(Out))] public readonly partial struct Sub{i}Result;\n");
        }

        Assert.Equal(50, ShapeConversionCount(GeneratorTestHarness.CreateCompilation([("Wide.cs", nested.Append('}').ToString())]), out var root));
        Assert.Equal(50, root);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void Net11_a_ManyErrors_shape_switches_over_two_arms_and_a_union_error_shape_converts_from_its_cases_shape()
    {
        const string Union = """
            using Resultful;

            public static class UnionProbe
            {
                public static string Run()
                {
                    var declined = new Fixture.Nested.CardDeclined("limit");
                    Fixture.Nested.ChargeResult charged = declined;
                    Fixture.Nested.CheckoutResult checkout = charged;
                    Result<Fixture.Nested.Receipt> wide = checkout;
                    var nested = checkout.TryGetValue(out Fixture.Nested.PaymentError p) && p.Value is Fixture.Nested.CardDeclined d && ReferenceEquals(d, declined);
                    Fixture.Nested.CheckoutResult zero = default(Fixture.Nested.ChargeResult);
                    var many = string.Join(",", Arms(Fixture.Many.RegisterResult.Ok(new Fixture.Many.Registration("a"))),
                        Arms(Fixture.Many.RegisterResult.Fail(new Fixture.Many.InputError[] { new Fixture.Many.Required("x"), new Fixture.Many.TooLong("y") })));
                    return $"{nested} {ReferenceEquals(wide.Error, declined)} {zero.IsDefault} {zero.HasValue} {many}";
                }

                private static string Arms(Fixture.Many.RegisterResult r) => r switch
                {
                    Fixture.Many.Registration reg => "ok " + reg.Email,
                    System.Collections.Generic.IReadOnlyList<Fixture.Many.InputError> errors => "errors " + errors.Count,
                };
            }
            """;

        var assembly = RuntimeTests.Compile([("Many.cs", FixtureCatalog.ManyErrorsShape), ("Nested.cs", FixtureCatalog.NestedUnion + FixtureCatalog.NestedUnionPeers), ("Union.cs", Union)], "ConversionsUnion");
        var actual = (string)assembly.GetType("UnionProbe", throwOnError: true)!.GetMethod("Run")!.Invoke(null, null)!;
        Assert.Equal("True True True False ok a,errors 2", actual);
    }

    [Theory]
    [InlineData("a shape over a leaf of the inner union (not a direct case)", "Fixture.Nested.CheckoutResult M() => default(Fixture.Nested.DeclineResult);", "CS0029")]
    [InlineData("a ManyErrors switch missing an arm", "int M(Fixture.Many.RegisterResult r) => r switch { Fixture.Many.Registration => 1 };", "CS8509")]
    public void Net11_a_union_conversion_that_must_not_compile_does_not(string what, string member, string id)
    {
        var uses = $"namespace Fixture.Nested {{ public static class Uses {{ public static {member} }} }}";
        var output = GeneratorTestHarness.Run(GeneratorTestHarness.CreateCompilation(
            [("Many.cs", FixtureCatalog.ManyErrorsShape), ("Nested.cs", FixtureCatalog.NestedUnion + FixtureCatalog.NestedUnionPeers), ("Uses.cs", uses)]), out _);
        Assert.True(output.CompilationDiagnostics.Any(d => d.Id == id), $"{what}: expected {id}, got {string.Join("\n", output.CompilationDiagnostics)}");
    }
#endif

    // Conversions from a [ResultShape] into another, over every shape of the compilation; root counts RootResult's.
    private static int ShapeConversionCount(Microsoft.CodeAnalysis.CSharp.CSharpCompilation comp, out int root)
    {
        var output = GeneratorTestHarness.Run(comp, out _);
        Assert.DoesNotContain(output.GeneratorDiagnostics.Concat(output.CompilationDiagnostics), d => d.Severity == DiagnosticSeverity.Error);
        static bool IsShape(ITypeSymbol t) => t.GetAttributes().Any(a => a.AttributeClass?.Name == "ResultShapeAttribute");
        var counts = output.OutputCompilation.GetSymbolsWithName(_ => true, SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .Where(IsShape)
            .ToDictionary(t => t.Name, t => t.GetMembers("op_Implicit").OfType<IMethodSymbol>().Count(c => IsShape(c.Parameters[0].Type) && !SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, t)));
        root = counts.GetValueOrDefault("RootResult");
        return counts.Values.Sum();
    }

    private static string Uses(string member) => $$"""
        using Resultful;

        namespace Fixture.Flat
        {
            public static class Uses { public static {{member}} }
        }
        """;

    private static List<Diagnostic> CompileErrors(params (string FileName, string Source)[] extra)
    {
        var output = GeneratorTestHarness.Run(GeneratorTestHarness.CreateCompilation([.. Fixtures, .. extra]), out _);
        Assert.DoesNotContain(output.GeneratorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return output.CompilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    }
}
