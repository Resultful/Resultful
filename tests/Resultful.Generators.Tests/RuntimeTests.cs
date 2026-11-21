using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Resultful.Generators.Tests;

// Generated shapes run against the real core: the fixture and a probe are compiled with the generator, loaded next to
// the Resultful.dll this project references, and each probe method returns what it saw. TryGetValue names its type:
// on net11.0 a union shape also has one TryGetValue per case, so `out var` is ambiguous (CS0121).
public sealed class RuntimeTests
{
    internal const string Probe = """
        using System;
        using System.Linq;
        using Resultful;
        using Fixture.Flat;
        using Fixture.Many;
        using Fixture.Void;

        public static class Probe
        {
            public static string Create()
            {
                PlaceOrderResult ok = new Order(1);
                PlaceOrderResult failed = new OrderNotFound(2);
                PlaceOrderResult named = PlaceOrderResult.Fail(new OrderLocked(3));
                return $"{ok.TryGetValue(out Order? o) && o.Id == 1} {failed.TryGetError(out var e) && e is OrderNotFound} {named.TryGetError(out var n) && n is OrderLocked} {ok.IsDefault}";
            }

            public static string Switch()
            {
                static string Name(PlaceOrderResult r) => r.Match(o => "order " + o.Id, locked => "locked " + locked.Id, notFound => "missing " + notFound.Id);
                string hierarchy = "";
                OrderError error = new OrderLocked(4);
                error.Switch(locked => hierarchy = "switch locked", notFound => hierarchy = "switch missing");
                return string.Join(", ", Name(new Order(1)), Name(new OrderLocked(2)), Name(new OrderNotFound(3)), hierarchy,
                    error.Match(l => "match locked", nf => "match missing"), string.Join("|", OrderError.Cases.Select(t => t.Name)));
            }

            public static string TryMap()
            {
                Error outer = new Fault("outer") { Cause = new OrderNotFound(7) };
                var mapped = OrderError.TryMap(outer, l => 409, nf => 404, out var status);
                var unmapped = OrderError.TryMap(new Fault("other"), l => 409, nf => 404, out _);
                return $"{mapped} {status} {unmapped}";
            }

            public static string Widen()
            {
                PlaceOrderResult failed = new OrderNotFound(2);
                Result<Order> wide = failed;
                Result<Order, OrderError> typed = failed;
                PlaceOrderResult back = typed.Map(o => o);
                PlaceOrderResult ok = new Order(5);
                Result<Order> wideOk = ok;
                return $"{wide.IsFailure && wide.Error is OrderNotFound} {typed.IsFailure} {back.TryGetError(out var e) && e is OrderNotFound} {wideOk.Value.Id}";
            }

            public static string WithContext()
            {
                PlaceOrderResult failed = new OrderNotFound(2);
                PlaceOrderResult noted = failed.WithContext("placing the order");
                return $"{noted.TryGetError(out var e) && e is OrderNotFound} {string.Join("|", e!.Context)}";
            }

            public static string Valueless()
            {
                var done = CancelOrderResult.Ok();
                CancelOrderResult shipped = new AlreadyShipped();
                Result<Unit> wide = shipped;
                return $"{done.TryGetValue(out Unit? u) && u == Unit.Value} {wide.Error is AlreadyShipped}";
            }

            public static string Many()
            {
                var failed = RegisterResult.Fail(new InputError[] { new Required("email"), new TooLong("name") });
                Result<Registration> wide = failed;
                RegisterResult ok = new Registration("a@b.c");
                return $"{failed.TryGetErrors(out var es) && es.Count == 2} {wide.Error.GetType().Name} {ok.TryGetValue(out Registration? r) && r.Email == "a@b.c"}";
            }

            public static string Zero()
            {
                var zero = default(PlaceOrderResult);
                var result = zero.IsDefault + "";
                try { zero.TryGetValue(out Order? _); result += " no throw"; }
                catch (InvalidOperationException) { result += " throws"; }
                try { Result<Order> wide = zero; _ = wide.IsSuccess; result += " no throw"; }
                catch (InvalidOperationException) { result += " throws"; }
                try { zero.Match(_ => 0, _ => 0, _ => 0); result += " no throw"; }
                catch (InvalidOperationException) { result += " throws"; }
                return result;
            }
        }

        public sealed record Fault(string Message) : Error(Message);
        """;

    private static readonly Lazy<Assembly> Loaded = new(Load);

    [Theory]
    [InlineData("Create", "True True True False")]
    [InlineData("Switch", "order 1, locked 2, missing 3, switch locked, match locked, OrderLocked|OrderNotFound")]
    [InlineData("TryMap", "True 404 False")]
    [InlineData("Widen", "True True True 5")]
    [InlineData("WithContext", "True placing the order")]
    [InlineData("Valueless", "True True")]
    [InlineData("Many", "True ValidationFailed`1 True")]
    [InlineData("Zero", "True throws throws throws")]
    public void A_generated_shape_behaves_as_decision_16_says(string probe, string expected)
    {
        var actual = (string)Loaded.Value.GetType("Probe", throwOnError: true)!.GetMethod(probe)!.Invoke(null, null)!;
        Assert.Equal(expected, actual);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void Net11_a_union_shape_switches_with_no_discard_arm_at_run_time()
    {
        const string Switch = """
            using Fixture.Flat;

            public static class UnionProbe
            {
                public static string Run() => string.Join(", ", Status(new Order(1)), Status(new OrderLocked(2)), Status(new OrderNotFound(3)), Zero());

                // Decision 10: HasValue is the check that doesn't throw; Value on the zero value throws.
                private static string Zero()
                {
                    var zero = default(PlaceOrderResult);
                    try { _ = zero.Value; return zero.HasValue + " no throw"; }
                    catch (System.InvalidOperationException) { return zero.HasValue + " throws"; }
                }

                private static int Status(PlaceOrderResult r) => r switch
                {
                    Order => 201,
                    OrderLocked => 409,
                    OrderNotFound => 404,
                };
            }
            """;

        var assembly = Compile([("Flat.cs", FixtureCatalog.FlatHierarchy), ("UnionProbe.cs", Switch)], "UnionRuntime");
        var actual = (string)assembly.GetType("UnionProbe", throwOnError: true)!.GetMethod("Run")!.Invoke(null, null)!;
        Assert.Equal("201, 409, 404, False throws", actual);
    }
#endif

    private static Assembly Load() => Compile(
        [
            ("Flat.cs", FixtureCatalog.FlatHierarchy),
            ("Many.cs", FixtureCatalog.ManyErrorsShape),
            ("Void.cs", FixtureCatalog.ValuelessShape),
            ("Probe.cs", Probe),
        ],
        "Runtime");

    private static Assembly Compile(IEnumerable<(string FileName, string Source)> sources, string name)
    {
        var comp = GeneratorTestHarness.CreateCompilation(sources, assemblyName: "Fixture" + name);
        var output = GeneratorTestHarness.Run(comp, out _);
        var problems = output.GeneratorDiagnostics.Concat(output.CompilationDiagnostics).Where(d => d.Severity >= DiagnosticSeverity.Warning).ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        // The core resolves from the default context, where this test already loaded it.
        var context = new AssemblyLoadContext(name, isCollectible: true);
        using var stream = new MemoryStream(GeneratorTestHarness.EmitAssemblyBytes(output.OutputCompilation));
        return context.LoadFromStream(stream);
    }
}
