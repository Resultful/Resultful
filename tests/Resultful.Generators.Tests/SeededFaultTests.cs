using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Resultful.Generators.Tests;

// Each fault rewrites the generated source the way a generator bug would, and a test written for the real output
// must notice: the runtime probe prints something else, or the consumer no longer compiles as it should.
public sealed class SeededFaultTests
{
    private const string Fixtures = "Flat.cs";
    private static readonly string[] ProbeMethods = ["Create", "Switch", "Zero"];

    // Each fault, and the check written for the real output that must catch it.
    public static TheoryData<string, string> Faults() => new()
    {
        { "none", "" },
        { "drop the zero guard", "Zero" },
        { "drop the Obsolete constructor guard", "CS0619" },
        { "swap two Match arms", "Switch" },
        { "return the base for a leaf", "Switch" },
    };

    [Theory]
    [MemberData(nameof(Faults))]
    public void A_seeded_fault_in_the_generated_source_fails_the_check_written_for_it(string fault, string catcher)
    {
        var caught = new List<string>();

        // RuntimeTests' expectations, against the rewritten output.
        var probe = Probe(fault);
        if (probe["Create"] != "True True True False") caught.Add("Create");
        if (probe["Switch"] != "order 1, locked 2, missing 3, switch locked, match locked, OrderLocked|OrderNotFound") caught.Add("Switch");
        if (probe["Zero"] != "True throws throws throws") caught.Add("Zero");

        // Decision 10: `new Shape()` must not compile (CS0619).
        var direct = Compile(fault, ("Direct.cs", "public static class Direct { public static Fixture.Flat.PlaceOrderResult Make() => new Fixture.Flat.PlaceOrderResult(); }"));
        if (!direct.GetDiagnostics(TestContext.Current.CancellationToken).Any(d => d.Id == "CS0619")) caught.Add("CS0619");

        if (catcher.Length == 0)
        {
            Assert.Empty(caught);
        }
        else
        {
            Assert.Contains(catcher, caught);
        }
    }

    private static string Mutate(string fault, string source) => fault switch
    {
        "none" => source,
        "drop the zero guard" => Replace(source, "get => !_inner.IsSuccess && !_inner.IsFailure;", "get => false;"),
        "drop the Obsolete constructor guard" => Replace(source, "[global::System.Obsolete(\"Use Ok, Fail, or an implicit conversion.\", error: true)]", ""),
        "swap two Match arms" => Replace(
            source,
            "global::System.Func<global::Fixture.Flat.OrderLocked, TResult> onOrderLocked, global::System.Func<global::Fixture.Flat.OrderNotFound, TResult> onOrderNotFound",
            "global::System.Func<global::Fixture.Flat.OrderNotFound, TResult> onOrderNotFound, global::System.Func<global::Fixture.Flat.OrderLocked, TResult> onOrderLocked"),
        // The hierarchy's case list names the base where a leaf belongs.
        "return the base for a leaf" => Replace(source, "typeof(global::Fixture.Flat.OrderLocked)", "typeof(global::Fixture.Flat.OrderError)"),
        _ => throw new ArgumentOutOfRangeException(nameof(fault)),
    };

    // Compile checks that each fault changed some file, so an emitter change that moves the text fails loudly.
    private static string Replace(string source, string from, string to) => source.Replace(from, to, StringComparison.Ordinal);

    // The fixture with the generator's output, rewritten, in place of running the generator.
    private static Compilation Compile(string fault, params (string FileName, string Source)[] extra)
    {
        var comp = GeneratorTestHarness.CreateCompilation([(Fixtures, FixtureCatalog.FlatHierarchy), ("Many.cs", FixtureCatalog.ManyErrorsShape), ("Void.cs", FixtureCatalog.ValuelessShape), .. extra]);
        var output = GeneratorTestHarness.Run(comp, out _);
        var options = (CSharpParseOptions)comp.SyntaxTrees[0].Options;
        var changed = output.GeneratedSources.Where(s => Mutate(fault, s.Value) != s.Value).ToList();
        Assert.True(fault == "none" || changed.Count > 0, $"the fault '{fault}' matched no generated source");

        return comp.AddSyntaxTrees(output.GeneratedSources.Select(s =>
            CSharpSyntaxTree.ParseText(Mutate(fault, s.Value), options, path: s.Key, cancellationToken: TestContext.Current.CancellationToken)));
    }

    private static Dictionary<string, string> Probe(string fault)
    {
        var comp = Compile(fault, ("Probe.cs", RuntimeTests.Probe));
        var errors = comp.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            return new Dictionary<string, string> { ["Create"] = "compile error", ["Switch"] = "compile error", ["Zero"] = "compile error" };
        }

        var context = new AssemblyLoadContext("Fault", isCollectible: true);
        var assembly = context.LoadFromStream(new MemoryStream(GeneratorTestHarness.EmitAssemblyBytes(comp)));
        var type = assembly.GetType("Probe", throwOnError: true)!;
        return ProbeMethods.ToDictionary(m => m, m => (string)type.GetMethod(m, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!);
    }
}
