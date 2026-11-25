#if NET11_0_OR_GREATER
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Resultful.Generators.Tests;

// Decision 17 on net11.0 with real C# 15 unions: a union as a shape's error type, nested unions kept as one case, and
// the IErrorUnion members the generator writes on a partial union of errors. Each probe is compiled with the generator,
// loaded next to the real core and run.
public sealed class ErrorUnionTests
{
    // Only members a union shape and a hierarchy shape share, so the two must print the same thing.
    private const string TwinProbe = """
        using System;
        using System.Linq;
        using Resultful;
        using Fixture.Twin;

        public static class Probe
        {
            public static string Run() => string.Join(" / ", Create(), Match(), Switch(), Widen(), WithContext(), Fields(), Zero());

            private static string Create()
            {
                PlaceOrderResult ok = new Order(1);
                PlaceOrderResult failed = new OrderNotFound(2);
                var named = PlaceOrderResult.Fail(new OrderLocked(3));
                return $"{ok.TryGetValue(out Order? o) && o.Id == 1} {failed.TryGetValue(out OrderNotFound? nf) && nf.Id == 2} {named.TryGetValue(out OrderLocked? _)} {named.TryGetValue(out OrderNotFound? _)} {ok.IsDefault}";
            }

            private static string Match()
            {
                static string Name(PlaceOrderResult r) => r.Match(o => "order " + o.Id, locked => "locked " + locked.Id, notFound => "missing " + notFound.Id);
                return string.Join(", ", Name(new Order(1)), Name(new OrderLocked(2)), Name(new OrderNotFound(3)));
            }

            private static string Switch()
            {
                static int Status(PlaceOrderResult r) => r switch
                {
                    Order => 201,
                    OrderLocked => 409,
                    OrderNotFound => 404,
                };
                return string.Join(",", Status(new Order(1)), Status(new OrderLocked(2)), Status(new OrderNotFound(3)));
            }

            private static string Widen()
            {
                PlaceOrderResult failed = new OrderLocked(2);
                Result<Order> wide = failed;
                Result<Order, OrderError> typed = failed;
                PlaceOrderResult back = typed;
                return $"{wide.Error is OrderLocked} {wide.Error.Message} {back.TryGetValue(out OrderLocked? _)}";
            }

            private static string WithContext()
            {
                PlaceOrderResult failed = new OrderNotFound(2);
                var noted = failed.WithContext("placing").WithContext("checkout");
                PlaceOrderResult ok = new Order(1);
                Result<Order> wide = noted;
                return $"{noted.TryGetValue(out OrderNotFound? nf) && nf.Id == 2} {string.Join("|", wide.Error.Context)} {ok.WithContext("x").TryGetValue(out Order? _)}";
            }

            private static string Fields()
            {
                PlaceOrderResult failed = new OrderLocked(4);
                failed.TryGetError(out var error);
                return $"{error!.Message} {error.Cause is null} {error.TryFind(out OrderLocked? l) && l.Id == 4} {error.TryFind(out OrderNotFound? _)} {string.Join("|", OrderError.Cases.Select(t => t.Name))}";
            }

            private static string Zero()
            {
                var zero = default(PlaceOrderResult);
                var result = zero.IsDefault + "";
                try { zero.TryGetValue(out Order? _); result += " no throw"; } catch (InvalidOperationException) { result += " throws"; }
                try { Result<Order> wide = zero; result += " no throw"; } catch (InvalidOperationException) { result += " throws"; }
                try { zero.Match(_ => 0, _ => 0, _ => 0); result += " no throw"; } catch (InvalidOperationException) { result += " throws"; }
                try { zero.WithContext("x"); result += " no throw"; } catch (InvalidOperationException) { result += " throws"; }
                return result;
            }
        }
        """;

    [Fact]
    public void A_union_error_and_its_ClosedHierarchy_twin_give_the_same_output()
    {
        var union = Run([("Twin.cs", FixtureCatalog.TwinUnion), ("Probe.cs", TwinProbe)], "TwinUnion");
        var hierarchy = Run([("Twin.cs", FixtureCatalog.TwinHierarchy), ("Probe.cs", TwinProbe)], "TwinHierarchy");

        Assert.Equal(
            "True True True False False / order 1, locked 2, missing 3 / 201,409,404 / True Order 2 locked True / True placing|checkout True / Order 4 locked True True False OrderLocked|OrderNotFound / True throws throws throws throws",
            union);
        Assert.Equal(hierarchy, union);
    }

    private const string NestedProbe = """
        using System;
        using System.Linq;
        using Resultful;
        using Fixture.Nested;

        public static class Probe
        {
            public static string Run() => string.Join(" / ", Cases(), Match(), Switch(), Widen(), WithContext(), Union(), Zero());

            // The shape's error cases are the union's direct cases; a leaf of the nested union converts in too.
            private static string Cases()
            {
                CheckoutResult declined = new CardDeclined("limit");
                CheckoutResult payment = new PaymentError(new CardExpired());
                CheckoutResult missing = new OrderNotFound(3);
                return $"{declined.TryGetValue(out PaymentError p) && p.Value is CardDeclined} {payment.TryGetValue(out PaymentError _)} {missing.TryGetValue(out OrderNotFound? _)} {missing.TryGetValue(out PaymentError _)}";
            }

            private static string Match()
            {
                static string Name(CheckoutResult r) => r.Match(
                    receipt => "paid " + receipt.Code,
                    notFound => "missing " + notFound.Id,
                    payment => payment switch
                    {
                        CardDeclined d => "declined " + d.Reason,
                        CardExpired => "expired",
                    });
                return string.Join(", ", Name(new Receipt("r1")), Name(new OrderNotFound(2)), Name(new CardDeclined("limit")), Name(new CardExpired()));
            }

            private static string Switch()
            {
                static int Status(CheckoutResult r) => r switch
                {
                    Receipt => 200,
                    OrderNotFound => 404,
                    PaymentError p => p switch
                    {
                        CardDeclined => 402,
                        CardExpired => 410,
                    },
                };
                return string.Join(",", Status(new Receipt("r")), Status(new OrderNotFound(1)), Status(new CardDeclined("x")), Status(new CardExpired()));
            }

            // Widening reaches the innermost leaf through IErrorUnion.Error.
            private static string Widen()
            {
                CheckoutResult failed = new CardDeclined("limit");
                Result<Receipt> wide = failed;
                return $"{wide.Error is CardDeclined} {wide.Error.Message}";
            }

            // WithContext dispatches per direct case and rewraps, so the inner union keeps its type.
            private static string WithContext()
            {
                CheckoutResult failed = new CardExpired();
                var noted = failed.WithContext("charging");
                noted.TryGetError(out var error);
                return $"{error.Value is PaymentError p && p.Value is CardExpired} {string.Join("|", error.Error.Context)} {string.Join("|", error.WithContext("again").Error.Context)}";
            }

            private static string Union()
            {
                var error = new CheckoutError(new PaymentError(new CardDeclined("stolen")) );
                var wrapped = CheckoutError.Wrap(new CardExpired());
                var flat = CheckoutError.Wrap(new OrderNotFound(9));
                string thrown;
                try { CheckoutError.Wrap(new Other()); thrown = "no throw"; } catch (ArgumentException) { thrown = "throws"; }
                return string.Join(" ", error.Message, error.Cause is null, error.TryFind(out CardDeclined? d) && d.Reason == "stolen", Msg(error),
                    string.Join("|", CheckoutError.Cases.Select(t => t.Name)), string.Join("|", PaymentError.Cases.Select(t => t.Name)),
                    wrapped.Value is PaymentError { Value: CardExpired }, flat.Value is OrderNotFound, thrown);
            }

            private static string Msg<T>(T error) where T : IErrorUnion => error.Error.GetType().Name;

            private static string Zero()
            {
                var zero = default(CheckoutError);
                var result = "";
                try { _ = zero.Error; result += "no throw"; } catch (InvalidOperationException) { result += "throws"; }
                try { zero.WithContext("x"); result += " no throw"; } catch (InvalidOperationException) { result += " throws"; }
                try { Result.Fail<int, CheckoutError>(zero); result += " no throw"; } catch (ArgumentException) { result += " rejected"; }
                return result;
            }
        }

        public sealed record Other() : Error("other");
        """;

    [Fact]
    public void A_nested_union_stays_one_case_and_its_leaves_still_convert_unwrap_and_keep_their_type()
    {
        var actual = Run([("Nested.cs", FixtureCatalog.NestedUnion), ("Probe.cs", NestedProbe)], "Nested");
        Assert.Equal(
            "True True True False / paid r1, missing 2, declined limit, expired / 200,404,402,410 / True Card declined: limit / True charging charging|again / Card declined: stolen True True CardDeclined OrderNotFound|CardDeclined|CardExpired CardDeclined|CardExpired True True throws / throws throws rejected",
            actual);
    }

    [Theory]
    [InlineData("missing outer arm", "r switch { Receipt => 1, OrderNotFound => 2 }", "CS8509")]
    [InlineData("missing inner arm", "r switch { Receipt => 1, OrderNotFound => 2, PaymentError p => p switch { CardDeclined => 3 } }", "CS8509")]
    [InlineData("an inner leaf on the outer switch", "r switch { Receipt => 1, OrderNotFound => 2, CardDeclined => 3, CardExpired => 4 }", "CS8121")]
    public void A_union_shape_switch_is_checked_by_the_compiler(string what, string body, string id)
    {
        var consumer = $$"""
            namespace Fixture.Nested
            {
                public static class Uses { public static int Status(CheckoutResult r) => {{body}}; }
            }
            """;

        var output = Generate(("Nested.cs", FixtureCatalog.NestedUnion), ("Uses.cs", consumer));
        Assert.True(output.CompilationDiagnostics.Any(d => d.Id == id), $"{what}: expected {id}, got {string.Join("\n", output.CompilationDiagnostics)}");
    }

    [Fact]
    public void A_bare_core_result_needs_the_union_cast_but_a_shape_does_not()
    {
        // Decision 17 point 1: C# applies one user-defined conversion at a time, so leaf -> union -> Result doesn't chain.
        var consumer = """
            namespace Fixture.Nested
            {
                public static class Uses
                {
                    public static global::Resultful.Result<Receipt, CheckoutError> Bare() => new OrderNotFound(1);
                    public static CheckoutResult Shape() => new CardDeclined("x");
                }
            }
            """;

        var output = Generate(("Nested.cs", FixtureCatalog.NestedUnion), ("Uses.cs", consumer));
        var error = Assert.Single(output.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("CS0029", error.Id);
        Assert.Contains("Bare", error.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).Lines.GetLineFromPosition(error.Location.SourceSpan.Start).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_a_case_changes_Match_so_a_caller_built_against_the_old_union_breaks()
    {
        // A binary break for callers in other assemblies: the generator docs say so.
        static string Errors(string cases) => $$"""
            using Resultful;
            using Resultful.CodeAnalysis;

            namespace Fixture.Break
            {
                public sealed record OrderLocked(int Id) : Error("locked");
                public sealed record OrderNotFound(int Id) : Error("missing");
                public partial union OrderError({{cases}});

                [ResultShape(typeof(OrderError), Success = typeof(int))]
                public readonly partial struct PlaceOrderResult;
            }
            """;

        var v1 = Emit(Generate("FixtureBreak", ("Errors.cs", Errors("OrderLocked"))));
        var v2 = Emit(Generate("FixtureBreak", ("Errors.cs", Errors("OrderLocked, OrderNotFound"))));
        var caller = GeneratorTestHarness.CreateCompilation(
            [("Caller.cs", "public static class Caller { public static int Run() => ((Fixture.Break.PlaceOrderResult)1).Match(v => v, locked => -1); }")],
            [MetadataReference.CreateFromImage(v1)],
            assemblyName: "Caller");

        Assert.Equal(1, Invoke(caller, v1));
        var ex = Assert.Throws<TargetInvocationException>(() => Invoke(caller, v2));
        Assert.IsType<MissingMethodException>(ex.InnerException);
    }

    [Fact]
    public void RFG0001_reports_a_Success_type_that_is_a_union_case()
    {
        var src = """
            using Resultful;
            using Resultful.CodeAnalysis;

            public sealed record OrderNotFound(int Id) : Error("nf");
            public sealed record CardDeclined() : Error("declined");
            public partial union PaymentError(CardDeclined);
            public partial union OrderError(OrderNotFound, PaymentError);

            [ResultShape(typeof(OrderError), Success = typeof(CardDeclined))] public readonly partial struct BadResult;
            """;

        var rfg = Assert.Single(Generate(("Bad.cs", src)).GeneratorDiagnostics);
        Assert.Equal("RFG0001", rfg.Id);
    }

    [Fact]
    public void RFG0008_reports_two_leaves_of_one_union_that_share_a_simple_name()
    {
        var src = """
            using Resultful;

            namespace A { public sealed record Declined() : Error("a"); }
            namespace B { public sealed record Declined() : Error("b"); }
            public partial union Inner(B.Declined);
            public partial union PaymentError(A.Declined, Inner);
            """;

        var output = Generate(("Dup.cs", src));
        var rfg = Assert.Single(output.GeneratorDiagnostics);
        Assert.Equal("RFG0008", rfg.Id);
        Assert.Contains("'PaymentError'", rfg.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(output.GeneratedSources.Keys, k => k.StartsWith("PaymentError", StringComparison.Ordinal));
    }

    [Fact]
    public void A_union_that_is_not_partial_or_holds_a_non_error_gets_no_members()
    {
        var src = """
            using Resultful;

            public sealed record Declined() : Error("d");
            public union Sealed(Declined);
            public partial union Mixed(Declined, string);
            public partial union Handwritten(Declined) : IErrorUnion { public Error Error => (Error)Value!; }
            public partial union Good(Declined);
            """;

        var output = Generate(("Unions.cs", src));
        Assert.Empty(output.GeneratorDiagnostics);
        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(["Good.ErrorUnion.g.cs"], output.GeneratedSources.Keys);
    }

    [Theory]
    [InlineData("Base, Derived")]
    [InlineData("Derived, Base")]
    public void A_case_and_its_subtype_dispatch_most_derived_first_in_either_order(string cases)
    {
        var src = $$"""
            using Resultful;
            using Resultful.CodeAnalysis;

            namespace Fixture.Sub
            {
                public record Base(int Id) : Error("base");
                public sealed record Derived(int Id) : Base(Id);
                public partial union SubError({{cases}});

                [ResultShape(typeof(SubError), Success = typeof(int))]
                public readonly partial struct SubResult;
            }

            public static class Probe
            {
                public static string Run()
                {
                    static string Name(Fixture.Sub.SubResult r) => r.Match(v => "ok", d => "derived " + d.Id, b => "base " + b.Id);
                    var wrapped = Fixture.Sub.SubError.Wrap(new Fixture.Sub.Derived(1));
                    var noted = new Fixture.Sub.SubError(new Fixture.Sub.Derived(2)).WithContext("x");
                    return string.Join(" ", Name(new Fixture.Sub.Derived(3)), Name(new Fixture.Sub.Base(4)), wrapped.Value is Fixture.Sub.Derived,
                        noted.Value is Fixture.Sub.Derived, string.Join("|", Fixture.Sub.SubError.Cases.Select(t => t.Name)));
                }
            }
            """;

        Assert.Equal("derived 3 base 4 True True Derived|Base", Run([("Sub.cs", "using System.Linq;\n" + src)], "Sub" + cases.Length + cases[0]));
    }

    [Fact]
    public void A_ClosedHierarchy_case_lists_its_leaves_so_the_JSON_converter_accepts_the_cases()
    {
        var src = """
            using System.Linq;
            using System.Text.Json;
            using Resultful;
            using Resultful.CodeAnalysis;
            using Resultful.Text.Json;

            namespace Fixture.Mixed
            {
                public sealed record OrderNotFound(int Id) : Error("missing");
                [ClosedHierarchy]
                public abstract partial record PayError(string Message) : Error(Message);
                public sealed record CardDeclined(string Reason) : PayError("declined");
                public sealed record CardExpired() : PayError("expired");
                public partial union OrderError(OrderNotFound, PayError);
            }

            public static class Probe
            {
                public static string Run()
                {
                    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        Converters = { new ErrorUnionJsonConverter<Fixture.Mixed.OrderError>(Fixture.Mixed.OrderError.Cases, Fixture.Mixed.OrderError.Wrap) },
                    }.AddResultful();
                    Result<int, Fixture.Mixed.OrderError> failed = new Fixture.Mixed.OrderError(new Fixture.Mixed.CardDeclined("limit"));
                    var json = JsonSerializer.Serialize(failed, options);
                    var back = JsonSerializer.Deserialize<Result<int, Fixture.Mixed.OrderError>>(json, options);
                    return string.Join(" ", string.Join("|", Fixture.Mixed.OrderError.Cases.Select(t => t.Name)),
                        back.TryGetError(out var e) && e.Value is Fixture.Mixed.CardDeclined { Reason: "limit" },
                        Fixture.Mixed.OrderError.Wrap(new Fixture.Mixed.CardExpired()).Value is Fixture.Mixed.CardExpired);
                }
            }
            """;

        var json = MetadataReference.CreateFromFile(typeof(global::Resultful.Text.Json.ErrorUnionJsonConverter<>).Assembly.Location);
        Assert.Equal("OrderNotFound|CardDeclined|CardExpired True True", Run([("Mixed.cs", src)], "Mixed", [json]));
    }

    [Fact]
    public void A_union_with_RFG0008_gets_no_members_and_a_shape_over_it_does_not_call_them()
    {
        var src = """
            using Resultful;
            using Resultful.CodeAnalysis;

            namespace A { public sealed record Declined() : Error("a"); }
            namespace B { public sealed record Declined() : Error("b"); }
            public partial union PaymentError(A.Declined, B.Declined);

            [ResultShape(typeof(PaymentError), Success = typeof(int))] public readonly partial struct PayResult;
            """;

        var output = Generate(("Dup.cs", src));
        Assert.Equal(["RFG0008"], output.GeneratorDiagnostics.Select(d => d.Id));
        Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var shape = output.OutputCompilation.GetTypeByMetadataName("PayResult")!;
        Assert.Empty(shape.GetMembers("WithContext"));
    }

    [Fact]
    public void RFG0008_is_reported_on_a_shape_over_a_union_the_generator_cannot_reopen()
    {
        var src = """
            using Resultful;
            using Resultful.CodeAnalysis;

            namespace A { public sealed record Declined() : Error("a"); }
            namespace B { public sealed record Declined() : Error("b"); }
            public union PaymentError(A.Declined, B.Declined);

            [ResultShape(typeof(PaymentError), Success = typeof(int))] public readonly partial struct PayResult;
            """;

        var rfg = Assert.Single(Generate(("Dup.cs", src)).GeneratorDiagnostics);
        Assert.Equal("RFG0008", rfg.Id);
        Assert.Contains("'PaymentError'", rfg.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void RFG0009_warns_when_a_partial_union_holds_a_union_it_cannot_extend()
    {
        var src = """
            using Resultful;

            public sealed record Declined() : Error("d");
            public sealed record Missing() : Error("m");
            public union Inner(Declined);
            public partial union Outer(Missing, Inner);
            """;

        var output = Generate(("Nested.cs", src));
        var rfg = Assert.Single(output.GeneratorDiagnostics);
        Assert.Equal(("RFG0009", DiagnosticSeverity.Warning), (rfg.Id, rfg.Severity));
        Assert.Empty(output.GeneratedSources);
    }

    [Fact]
    public void Members_a_partial_union_declares_itself_are_not_generated()
    {
        var src = """
            using Resultful;

            public sealed record Declined() : Error("d");
            public partial union PaymentError(Declined)
            {
                public string Message => "custom";
                public static System.Collections.Generic.IReadOnlyList<System.Type> Cases { get; } = [typeof(Declined)];
            }

            public static class Probe
            {
                public static string Run()
                {
                    var e = new PaymentError(new Declined());
                    return $"{e.Message} {e.Error.Message} {e.WithContext("x").Error.Context[0]} {PaymentError.Cases.Count}";
                }
            }
            """;

        Assert.Equal("custom d x 1", Run([("Custom.cs", src)], "Custom"));
    }

    private static GeneratorRunOutput Generate(params (string FileName, string Source)[] sources) => Generate("FixtureUnion", sources);

    private static GeneratorRunOutput Generate(string assemblyName, params (string FileName, string Source)[] sources) =>
        GeneratorTestHarness.Run(GeneratorTestHarness.CreateCompilation(sources, assemblyName: assemblyName), out _);

    private static byte[] Emit(GeneratorRunOutput output)
    {
        Assert.DoesNotContain(output.GeneratorDiagnostics.Concat(output.CompilationDiagnostics), d => d.Severity >= DiagnosticSeverity.Warning);
        return GeneratorTestHarness.EmitAssemblyBytes(output.OutputCompilation);
    }

    private static int Invoke(Compilation caller, byte[] fixture)
    {
        var context = new AssemblyLoadContext("Break", isCollectible: true);
        context.LoadFromStream(new MemoryStream(fixture));
        var assembly = context.LoadFromStream(new MemoryStream(GeneratorTestHarness.EmitAssemblyBytes(caller)));
        return (int)assembly.GetType("Caller", throwOnError: true)!.GetMethod("Run")!.Invoke(null, null)!;
    }

    private static string Run(IEnumerable<(string FileName, string Source)> sources, string name, IEnumerable<MetadataReference>? references = null)
    {
        var bytes = Emit(GeneratorTestHarness.Run(GeneratorTestHarness.CreateCompilation(sources, references, assemblyName: "Fixture" + name), out _));
        var context = new AssemblyLoadContext(name, isCollectible: true);
        var assembly = context.LoadFromStream(new MemoryStream(bytes));
        return (string)assembly.GetType("Probe", throwOnError: true)!.GetMethod("Run")!.Invoke(null, null)!;
    }
}
#endif
