using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Resultful.Analyzers;

namespace Resultful.AwaitProbe.Tests;

/// <summary>
/// What the compiler and the RF rules report on await-style code: Probe.cs compiled in process with a consumer
/// snippet, every warning and error compared both ways with `// expect:` markers.
/// </summary>
public sealed partial class DiagnosticTests
{
    static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers =
    [
        new MustObserveAnalyzer(), new DefaultResultAnalyzer(), new UncheckedValueAnalyzer(), new BareStringAnalyzer(), new MatchToSwitchAnalyzer(),
        new UnassignedFieldAnalyzer(), new ClosedHierarchyAnalyzer(), new UntypedHandlerAnalyzer(), new MapToBindAnalyzer(),
    ];

    static readonly string ProbeSource = ReadProbe();

    static string ReadProbe()
    {
        using var stream = typeof(DiagnosticTests).Assembly.GetManifestResourceStream("Probe.cs")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    const string Usings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Runtime.CompilerServices;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Resultful;
        global using Resultful.AwaitProbe;
        global using Resultful.AwaitProbe.ShortCircuit;
        """;

    static ImmutableArray<MetadataReference> References()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return [.. ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => string.Equals(Path.GetDirectoryName(p), runtimeDir, StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(Unit).Assembly.Location))];
    }

    static IReadOnlyList<string> Diagnose(string snippet)
    {
        var parse = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var trees = new[] { snippet, ProbeSource, Usings }.Select((s, i) => CSharpSyntaxTree.ParseText(s, parse, path: $"Test{i}.cs", encoding: Encoding.UTF8));
        var compilation = CSharpCompilation.Create("Consumer", trees, References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        // Some async-builder errors (CS8940) come only from lowering, so emit too.
        var diagnostics = compilation.WithAnalyzers(Analyzers).GetAllDiagnosticsAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult()
            .Concat(compilation.Emit(Stream.Null, cancellationToken: TestContext.Current.CancellationToken).Diagnostics);
        return [.. diagnostics
            .Where(d => d.Severity >= DiagnosticSeverity.Warning && d.Location.IsInSource)
            .Select(d => $"{d.Location.GetMappedLineSpan().Path}:{d.Location.GetMappedLineSpan().StartLinePosition.Line + 1} {d.Id}")
            .Distinct()
            .Order(StringComparer.Ordinal)];
    }

    [GeneratedRegex(@"//\s*expect:\s*(?<ids>[A-Z]+[0-9]+(\s*,\s*[A-Z]+[0-9]+)*)")]
    private static partial Regex ExpectMarker();

    static void Verify(string snippet)
    {
        var lines = snippet.Split('\n');
        var expected = lines
            .SelectMany((line, i) => ExpectMarker().Match(line) is { Success: true } m
                ? m.Groups["ids"].Value.Split(',', StringSplitOptions.TrimEntries).Select(id => $"Test0.cs:{i + 1} {id}")
                : [])
            .Order(StringComparer.Ordinal);
        Assert.Equal(expected, Diagnose(snippet).Where(d => d.StartsWith("Test0.cs:", StringComparison.Ordinal)));
    }

    const string Domain = """
        public sealed record Order(string Sku, int Quantity);
        public sealed record Receipt(string Sku);
        public sealed record Invalid() : Error("invalid");

        public static class Steps
        {
            public static Result<Order> Parse(string? sku) => sku is null ? new Invalid() : new Order(sku, 1);
            public static Result<Unit> Validate(Order order) => order.Quantity > 0 ? Unit.Value : new Invalid();
            public static Result<string?> Note(Order order) => order.Sku == "x" ? null : "note";
        }

        """;

    [Fact]
    public void The_RF_rules_report_only_the_naive_awaiter_in_the_probe()
    {
        // RF0003 is right there: NaiveAwaiter.GetResult reads Value with no check. The real awaiters use TryGetValue.
        var line = ProbeSource.Split('\n').ToList().FindIndex(l => l.Contains("public T GetResult() => result.Value;", StringComparison.Ordinal)) + 1;
        Assert.Equal([$"Test1.cs:{line} RF0003"], Diagnose("").Where(d => !d.StartsWith("Test0.cs:", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_typical_handler_reports_nothing()
    {
        Verify(Domain + """
            public static class Handler
            {
                [AsyncMethodBuilder(typeof(ResultBuilder<>))]
                public static async Result<Receipt> PlaceOrder(string? sku)
                {
                    var order = await Steps.Parse(sku);
                    _ = await Steps.Validate(order);
                    return new Receipt(order.Sku.ToUpperInvariant());
                }
            }
            """);
    }

    [Fact]
    public void A_dropped_Unit_await_is_not_RF0001_but_a_dropped_result_is_RF0001_and_CS4014()
    {
        // Once a result is awaitable, the compiler's CS4014 ("not awaited") joins RF0001 on a dropped call of an async
        // method, and on any dropped result inside an async method; its advice (await it) is wrong in a plain async method.
        Verify(Domain + """
            public static class Handler
            {
                [AsyncMethodBuilder(typeof(ResultBuilder<>))]
                public static async Result<Receipt> PlaceOrder(string? sku)
                {
                    var order = await Steps.Parse(sku);
                    await Steps.Validate(order);
                    return new Receipt(order.Sku);
                }

                public static void Caller() => PlaceOrder("a"); // expect: RF0001, CS4014

                public static void NotAsync() => Steps.Parse("a"); // expect: RF0001

                public static async Task InAsync()
                {
                    Steps.Parse("a"); // expect: RF0001, CS4014
                    await Task.Yield();
                }
            }
            """);
    }

    [Fact]
    public void Nullable_flow_loses_a_maybe_null_T_through_an_extension_GetAwaiter()
    {
        // Result<string?> awaited gives string? by type, but the compiler's nullable state after `await` with an
        // extension GetAwaiter is not-null: no CS8602 where Value, or a Task, would warn.
        Verify(Domain + """
            public static class Handler
            {
                public static async Task<string?> NoteAsync() { await Task.Yield(); return null; }

                [AsyncMethodBuilder(typeof(ResultBuilder<>))]
                public static async Result<int> Lengths(string? sku)
                {
                    var order = await Steps.Parse(sku);
                    var note = await Steps.Note(order);
                    string? declared = await Steps.Note(order);
                    var fromTask = await NoteAsync();
                    return order.Sku.Length + note.Length + declared.Length + fromTask.Length; // expect: CS8602
                }
            }
            """);
    }

    [Fact]
    public void Only_an_instance_GetAwaiter_keeps_the_maybe_null_state()
    {
        // The same awaiter reached three ways: an instance method (x), an extension method (y) and a C# 14 extension
        // member (z). Calling GetResult directly (w) tracks it; awaiting through either extension doesn't.
        Verify("""
            public readonly struct Instance<T>(T v) { public Awaiter<T> GetAwaiter() => new(v); }
            public readonly struct Classic<T>(T v) { public T V => v; }
            public readonly struct Member<T>(T v) { public T V => v; }
            public static class ClassicExtensions { public static Awaiter<T> GetAwaiter<T>(this Classic<T> b) => new(b.V); }
            public static class MemberExtensions { extension<T>(Member<T> b) { public Awaiter<T> GetAwaiter() => new(b.V); } }
            public readonly struct Awaiter<T>(T v) : INotifyCompletion
            {
                public bool IsCompleted => true;
                public T GetResult() => v;
                public void OnCompleted(Action c) => c();
            }

            public static class Use
            {
                public static async Task<int> M(Instance<string?> a, Classic<string?> b, Member<string?> c)
                {
                    var x = await a;
                    var y = await b;
                    var z = await c;
                    var w = b.GetAwaiter().GetResult();
                    return x.Length // expect: CS8602
                        + y.Length + z.Length
                        + w.Length; // expect: CS8602
                }
            }
            """);
    }

    [Fact]
    public void The_compiler_reports_the_limits_of_a_method_level_builder()
    {
        Verify(Domain + """
            public static class Handler
            {
                // No builder: Result<T> is not task-like.
                public static async Result<int> NoBuilder() => await Steps.Parse("a") is { } o ? 1 : 0; // expect: CS1983

                // Two type parameters: a method-level builder can only serve a return type of arity 0 or 1.
                [AsyncMethodBuilder(typeof(ResultBuilder<>))]
                public static async Result<int, string> TwoTypeParameters() => await Steps.Parse("a") is { } o ? 1 : 0; // expect: CS1983

                // A `return` takes T, not an error: a failure has to be awaited.
                [AsyncMethodBuilder(typeof(ResultBuilder<>))]
                public static async Result<int> ReturnError()
                {
                    await Steps.Parse("a");
                    return new Invalid(); // expect: CS0029
                }

                [AsyncMethodBuilder(typeof(ResultBuilder<>))]
                public static async Result<int> FailByAwait(bool bad) => bad ? await Result.Fail<int>(new Invalid()) : 1;

                // No await: no CS1998 (neither SDK 10.0.4xx nor 11.0.1xx reports it, for any async method).
                [AsyncMethodBuilder(typeof(ResultBuilder<>))]
                public static async Result<int> NoAwait() => 1;
            }
            """);
    }

    [Fact]
    public void A_builder_nested_in_a_generic_type_cannot_carry_the_error_type()
    {
        Verify("""
            public static class Typed<TError>
            {
                public readonly struct Of<T> { }

                public struct Builder<T>
                {
                    public static Builder<T> Create() => default;
                    public Of<T> Task => default;
                    public void Start<TSM>(ref TSM sm) where TSM : IAsyncStateMachine => sm.MoveNext();
                    public void SetStateMachine(IAsyncStateMachine sm) { }
                    public void SetResult(T value) { }
                    public void SetException(Exception e) { }
                    public void AwaitOnCompleted<TA, TSM>(ref TA a, ref TSM sm) where TA : INotifyCompletion where TSM : IAsyncStateMachine { }
                    public void AwaitUnsafeOnCompleted<TA, TSM>(ref TA a, ref TSM sm) where TA : ICriticalNotifyCompletion where TSM : IAsyncStateMachine { }
                }
            }

            public static class Handler
            {
                [AsyncMethodBuilder(typeof(Typed<>.Builder<>))]
                public static async Typed<string>.Of<int> M() { await Task.CompletedTask; return 1; } // expect: CS8940
            }
            """);
    }

    [Fact]
    public void Awaiting_a_result_in_a_plain_async_method_compiles_silently()
    {
        Verify(Domain + """
            public static class Handler
            {
                public static async Task<int> Plain(string? sku) => (await Steps.Parse(sku)).Quantity;
            }
            """);
    }
}
