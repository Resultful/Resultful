using System.Text;
using CsCheck;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Resultful.Generators.Tests;

// building-the-generator.md: the generator either compiles cleanly or reports an RFG diagnostic, and never throws
// (CS8785 drops all of its output). Random hierarchies cover depth, nested [ClosedHierarchy] bases, concrete
// intermediates (RFG0002), open generics (RFG0003), internal leaves (RFG0004), non-partial enclosing types (RFG0005),
// unsupported declarations (RFG0006), shared simple names across namespaces (RFG0008) and every kind of Success
// (RFG0001). Determinism: the same sources in any order give byte-identical output.
//
// Iterations: RESULTFUL_FUZZ_ITER (default 150); a failure prints its CsCheck seed, and CsCheck_Seed replays it.
public sealed class FuzzTests
{
    private static readonly long Iterations =
        long.TryParse(Environment.GetEnvironmentVariable("RESULTFUL_FUZZ_ITER"), out var n) && n > 0 ? n : 150;

    [Fact]
    public void Random_hierarchies_compile_cleanly_or_report_an_RFG_diagnostic_never_CS8785()
    {
        var clean = 0;
        var total = 0;
        Plans.Sample(
            plan =>
            {
                var comp = GeneratorTestHarness.CreateCompilation(plan.Files(), assemblyName: "Fuzz");
                var output = GeneratorTestHarness.Run(comp, out _);
                var all = output.GeneratorDiagnostics.Concat(output.CompilationDiagnostics).ToList();

                var crashes = all.Where(d => d.Id is "CS8784" or "CS8785" || d.Id.StartsWith("AD", StringComparison.Ordinal)).ToList();
                if (crashes.Count > 0)
                {
                    throw new InvalidOperationException("the generator failed:\n" + string.Join("\n", crashes));
                }

                foreach (var d in output.GeneratorDiagnostics)
                {
                    if (!d.Id.StartsWith("RFG", StringComparison.Ordinal) || !d.Location.IsInSource)
                    {
                        throw new InvalidOperationException("a generator diagnostic that isn't an RFG one in source: " + d);
                    }
                }

                // Whatever the user wrote, the generated files themselves have no diagnostic of any severity.
                var generated = output.RunResult.GeneratedTrees.ToHashSet();
                var inGenerated = output.CompilationDiagnostics
                    .Where(d => d.Severity != DiagnosticSeverity.Hidden && d.Location.SourceTree is { } t && generated.Contains(t))
                    .ToList();
                if (inGenerated.Count > 0)
                {
                    throw new InvalidOperationException("diagnostics in generated files:\n" + string.Join("\n", inGenerated));
                }

                Interlocked.Increment(ref total);
                if (output.GeneratorDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                {
                    return;
                }

                // No RFG error: the whole compilation is clean.
                Interlocked.Increment(ref clean);
                var problems = output.CompilationDiagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .ToList();
                if (problems.Count > 0)
                {
                    throw new InvalidOperationException("no RFG diagnostic, but the output doesn't compile cleanly:\n" + string.Join("\n", problems));
                }
            },
            iter: Iterations,
            threads: 1,
            print: p => p.Source());

        // A fuzz where most plans stop at an RFG error checks little: about half are clean (measured 39-47%), 30% is the floor.
        TestContext.Current.TestOutputHelper?.WriteLine($"{clean} of {total} plans compiled without an RFG error");
        Assert.True(clean * 10 >= total * 3, $"only {clean} of {total} plans compiled without an RFG error; rebalance the generators");
    }

    [Fact]
    public void Shuffled_syntax_trees_give_byte_identical_output()
    {
        Gen.Select(Plans, Gen.Int[0, int.MaxValue]).Sample(
            t =>
            {
                var (plan, seed) = t;
                var files = plan.Files().ToList();
                var shuffled = new List<(string Name, string Source)>(files);
                new Random(seed).Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(shuffled));

                var first = Generate(files);
                var second = Generate(shuffled);
                Assert.Equal(first.Keys.Order(StringComparer.Ordinal), second.Keys.Order(StringComparer.Ordinal));
                foreach (var (hint, bytes) in first)
                {
                    Assert.True(bytes.AsSpan().SequenceEqual(second[hint]), $"{hint} differs when the syntax trees are shuffled");
                }

                // The diagnostics are the same set, wherever the trees are.
                Assert.Equal(first.Diagnostics, second.Diagnostics);
            },
            iter: Math.Max(10, Iterations / 3),
            threads: 1,
            print: t => t.Item1.Source());
    }

    [Fact]
    public void A_fixed_set_of_fixtures_gives_identical_bytes_in_reverse_order()
    {
        (string, string)[] files =
        [
            ("Flat.cs", FixtureCatalog.FlatHierarchy),
            ("Deep.cs", FixtureCatalog.DeepFourLevelHierarchy),
            ("Mid.cs", FixtureCatalog.ConcreteIntermediate),
            ("Colliding.cs", FixtureCatalog.CollidingLeafNames),
            ("Nested.cs", FixtureCatalog.NestedShape),
            ("Interface.cs", FixtureCatalog.InterfaceSuccess),
            ("Void.cs", FixtureCatalog.ValuelessShape),
            ("Many.cs", FixtureCatalog.ManyErrorsShape),
        ];

        var forward = Generate(files);
        var reverse = Generate(files.Reverse().ToArray());
        Assert.NotEmpty(forward);
        Assert.Equal(forward.Keys.Order(StringComparer.Ordinal), reverse.Keys.Order(StringComparer.Ordinal));
        foreach (var (hint, bytes) in forward)
        {
            Assert.Equal(bytes, reverse[hint]);
        }
    }

    private sealed class Generated : Dictionary<string, byte[]>
    {
        public Generated()
            : base(StringComparer.Ordinal)
        {
        }

        public List<string> Diagnostics { get; } = [];
    }

    private static Generated Generate(IEnumerable<(string Name, string Source)> files)
    {
        var comp = GeneratorTestHarness.CreateCompilation(files, assemblyName: "Determinism");
        var output = GeneratorTestHarness.Run(comp, out _);
        var result = new Generated();
        foreach (var source in output.RunResult.Results.SelectMany(r => r.GeneratedSources))
        {
            result[source.HintName] = Encoding.UTF8.GetBytes(source.SourceText.ToString());
        }

        result.Diagnostics.AddRange(output.GeneratorDiagnostics.Select(d => d.ToString()).Order(StringComparer.Ordinal));
        return result;
    }

    // ---- the random plan ----

    private enum NodeKind { SealedLeaf, ClosedBase, PlainBase, OpenLeaf, GenericLeaf, InternalLeaf }

    private sealed record NodeSpec(int Parent, NodeKind Kind, int Name, int Namespace);

    private sealed record HierarchySpec(int Nesting, bool RootUnsupported, NodeSpec[] Nodes);

    private sealed record ShapeSpec(int Hierarchy, int Success, bool Many, bool Public, bool Readonly);

    // Weights keep about half the plans free of RFG errors (asserted above). OpenLeaf is a concrete intermediate
    // (RFG0002, a warning) and becomes a base, so most-derived-first ordering is exercised; InternalLeaf stays sealed,
    // since a public type can't derive from it.
    private static readonly Gen<NodeKind> Kinds = Gen.Int[0, 59].Select(i => i switch
    {
        <= 29 => NodeKind.SealedLeaf,
        <= 39 => NodeKind.ClosedBase,
        <= 45 => NodeKind.PlainBase,
        <= 56 => NodeKind.OpenLeaf,
        57 => NodeKind.GenericLeaf,
        _ => NodeKind.InternalLeaf,
    });

    // Namespace: mostly the global one, where a repeated name is renamed; Alpha and Beta give RFG0008 collisions.
    private static readonly Gen<NodeSpec> Nodes = Gen.Select(Gen.Int[0, 100], Kinds, Gen.Int[0, 11], Gen.Int[0, 15].Select(i => i <= 13 ? 0 : i - 13), (p, k, n, ns) => new NodeSpec(p, k, n, ns));

    // Nesting: 0 none (mostly), 1 in a partial class, 2 in a non-partial class (RFG0005).
    private static readonly Gen<HierarchySpec> Hierarchies = Gen.Select(
        Gen.Int[0, 19].Select(i => i <= 13 ? 0 : i <= 18 ? 1 : 2),
        Gen.Int[0, 29].Select(i => i == 0),
        Nodes.Array[1, 8],
        (nesting, unsupported, nodes) => new HierarchySpec(nesting, unsupported, nodes));

    // Success: 5 a leaf, 6 object, 7 Error give RFG0001; each is 1 in 30.
    private static readonly Gen<ShapeSpec> Shapes = Gen.Select(
        Gen.Int[0, 100],
        Gen.Int[0, 29].Select(i => i switch { <= 2 => 0, <= 6 => 1, <= 9 => 2, <= 13 => 3, <= 16 => 4, 17 => 5, 18 => 6, 19 => 7, <= 22 => 8, _ => 9 }),
        Gen.Select(Gen.Int[0, 4].Select(i => i == 0), Gen.Int[0, 3].Select(i => i != 0), Gen.Int[0, 29].Select(i => i != 0), (m, p, r) => (m, p, r)),
        (h, s, f) => new ShapeSpec(h, s, f.m, f.p, f.r));

    private static readonly Gen<Plan> Plans = Gen.Select(Hierarchies.Array[1, 3], Shapes.Array[0, 4], (h, s) => new Plan(h, s));

    private sealed record Plan(HierarchySpec[] Hierarchies, ShapeSpec[] Shapes)
    {
        private static readonly string[] Names = ["NotFound", "Locked", "Gone", "Expired", "Declined", "Missing", "Timeout", "Refused", "Conflict", "Invalid", "Stale", "Busy"];
        private static readonly string[] Namespaces = ["", "Alpha", "Beta"];
        private const string Usings = "using Resultful;\nusing Resultful.CodeAnalysis;\n\n";

        public string Source() => string.Join("\n", Files().Select(f => $"// ==== {f.Name}\n{f.Source}"));

        public IEnumerable<(string Name, string Source)> Files()
        {
            var taken = new HashSet<string>(StringComparer.Ordinal);
            var leavesByHierarchy = new List<List<string>>();
            yield return ("Common.cs", "public interface IOrder { int Id { get; } }\npublic sealed record Order(int Id) : IOrder;\n");

            for (var h = 0; h < Hierarchies.Length; h++)
            {
                var spec = Hierarchies[h];
                var outer = spec.Nesting == 0 ? null : $"Outer{h}";
                var types = new List<(string Name, string? Namespace, string Declaration)>();
                var fullNames = new List<string>();
                var bases = new List<int> { 0 };
                var leaves = new List<string>();

                var root = $"Root{h}";
                var rootFull = outer is null ? $"global::{root}" : $"global::{outer}.{root}";
                fullNames.Add(rootFull);
                types.Add((root, null, spec.RootUnsupported
                    ? $"[ClosedHierarchy] public abstract record {root} : Error {{ protected {root}(string m) : base(m) {{ }} }}"
                    : $"[ClosedHierarchy] public abstract partial record {root} : Error;"));

                for (var i = 0; i < spec.Nodes.Length; i++)
                {
                    var node = spec.Nodes[i];
                    var parent = fullNames[bases[node.Parent % bases.Count]];
                    var ns = outer is null ? Namespaces[node.Namespace] : string.Empty;
                    var name = node.Kind is NodeKind.ClosedBase or NodeKind.PlainBase ? $"Mid{h}_{i}" : Names[node.Name];
                    var key = (outer ?? ns) + "." + name;
                    if (!taken.Add(key))
                    {
                        name += $"{h}_{i}";
                        taken.Add((outer ?? ns) + "." + name);
                    }

                    var full = outer is not null ? $"global::{outer}.{name}" : ns.Length == 0 ? $"global::{name}" : $"global::{ns}.{name}";
                    var declaration = node.Kind switch
                    {
                        NodeKind.SealedLeaf => $"public sealed record {name}() : {parent}(\"m\");",
                        NodeKind.ClosedBase => $"[ClosedHierarchy] public abstract partial record {name} : {parent};",
                        NodeKind.PlainBase => $"public abstract record {name}(string M) : {parent}(M);",
                        NodeKind.OpenLeaf => $"public record {name}(string M) : {parent}(M);",
                        NodeKind.GenericLeaf => $"public sealed record {name}<T>() : {parent}(\"m\");",
                        _ => $"internal sealed record {name}() : {parent}(\"m\");",
                    };

                    fullNames.Add(full);
                    types.Add((name, ns.Length == 0 ? null : ns, declaration));
                    if (node.Kind is NodeKind.ClosedBase or NodeKind.PlainBase or NodeKind.OpenLeaf)
                    {
                        bases.Add(fullNames.Count - 1);
                    }

                    if (node.Kind is NodeKind.SealedLeaf or NodeKind.OpenLeaf)
                    {
                        leaves.Add(full);
                    }
                }

                leavesByHierarchy.Add(leaves);
                if (outer is not null)
                {
                    var partial = spec.Nesting == 1 ? "partial " : string.Empty;
                    var body = string.Join("\n", types.Select(t => "    " + t.Declaration));
                    yield return ($"Hierarchy{h}.cs", $"{Usings}public {partial}class {outer}\n{{\n{body}\n}}\n");
                }
                else
                {
                    for (var i = 0; i < types.Count; i++)
                    {
                        var (_, ns, declaration) = types[i];
                        yield return ($"Hierarchy{h}_{i}.cs", Usings + (ns is null ? declaration : $"namespace {ns} {{ {declaration} }}") + "\n");
                    }
                }
            }

            var shapes = new StringBuilder(Usings);
            for (var s = 0; s < Shapes.Length; s++)
            {
                var spec = Shapes[s];
                var h = spec.Hierarchy % Hierarchies.Length;
                var outer = Hierarchies[h].Nesting == 0 ? null : $"Outer{h}";
                var error = outer is null ? $"global::Root{h}" : $"global::{outer}.Root{h}";
                var leaf = leavesByHierarchy[h].FirstOrDefault();
                var success = spec.Success switch
                {
                    0 => null,
                    1 => "int",
                    2 => "string",
                    3 => "global::Order",
                    4 => "global::IOrder",
                    5 => leaf ?? "int",
                    6 => "object",
                    7 => "global::Resultful.Error",
                    8 => "int[]",
                    _ => "global::System.Guid",
                };

                var args = new List<string> { $"typeof({error})" };
                if (success is not null)
                {
                    args.Add($"Success = typeof({success})");
                }

                if (spec.Many)
                {
                    args.Add("ManyErrors = true");
                }

                var access = spec.Public ? "public" : "internal";
                var readOnly = spec.Readonly ? "readonly " : string.Empty;
                shapes.Append($"[ResultShape({string.Join(", ", args)})] {access} {readOnly}partial struct Shape{s};\n");
            }

            yield return ("Shapes.cs", shapes.ToString());
        }
    }
}
