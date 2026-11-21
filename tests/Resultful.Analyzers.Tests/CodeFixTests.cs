using Xunit;
using static Resultful.Analyzers.Tests.AnalyzerHarness;

namespace Resultful.Analyzers.Tests;

/// <summary>The fixes of code-fixes.tsv, one at a time and fix-all: RF0001's `_ =`, RF0004's wrap, RF0008's types, RF0009's Bind.</summary>
public sealed class CodeFixTests
{
    const string Prelude = """
        using System;
        using System.Threading.Tasks;
        using Resultful.CodeAnalysis;

        [MustObserve] public readonly struct Reply { public void Switch(Action<int> ok) { } }

        public static class Api
        {
            public static Reply Make() => default;
            public static Task<Reply> MakeAsync() => Task.FromResult(default(Reply));
        }

        """;

    [Fact]
    public void A_statement_becomes_a_discard() => Assert.Equal(Prelude + """
        public static class Cases
        {
            static void M()
            {
                // keep this comment
                _ = Api.Make(); // and this one
            }
        }
        """, ApplyFix(Prelude + """
        public static class Cases
        {
            static void M()
            {
                // keep this comment
                Api.Make(); // and this one
            }
        }
        """));

    [Fact]
    public void An_await_becomes_a_discarded_await() => Assert.Equal(Prelude + """
        public static class Cases
        {
            static async Task M() { _ = await Api.MakeAsync(); }
        }
        """, ApplyFix(Prelude + """
        public static class Cases
        {
            static async Task M() { await Api.MakeAsync(); }
        }
        """));

    [Fact]
    public void An_unawaited_task_becomes_a_discard() => Assert.Equal(Prelude + """
        public static class Cases
        {
            static void M() { _ = Api.MakeAsync(); }
        }
        """, ApplyFix(Prelude + """
        public static class Cases
        {
            static void M() { Api.MakeAsync(); }
        }
        """));

    [Fact]
    public void A_void_lambda_body_becomes_a_discard() => Assert.Equal(Prelude + """
        public static class Cases
        {
            static readonly Action A = () => _ = Api.Make();
            static void M() => _ = Api.Make();
        }
        """, ApplyFix(Prelude + """
        public static class Cases
        {
            static readonly Action A = () => _ = Api.Make();
            static void M() => Api.Make();
        }
        """));

    [Fact]
    public void Fix_all_discards_every_one_in_the_document()
    {
        var fixedText = ApplyFix(Prelude + """
            public static class Cases
            {
                static readonly Action A = () => Api.Make();

                static async Task M(Reply? r)
                {
                    Api.Make();
                    Api.Make();
                    await Api.MakeAsync();
                    Api.MakeAsync().ConfigureAwait(false);
                    _ = Api.Make();
                }
            }
            """, fixAll: true);
        Assert.Equal(Prelude + """
            public static class Cases
            {
                static readonly Action A = () => _ = Api.Make();

                static async Task M(Reply? r)
                {
                    _ = Api.Make();
                    _ = Api.Make();
                    _ = await Api.MakeAsync();
                    _ = Api.MakeAsync().ConfigureAwait(false);
                    _ = Api.Make();
                }
            }
            """, fixedText);
        Verify(fixedText);
    }

    [Fact]
    public void No_fix_is_offered_for_an_async_void_handler() => Assert.Equal(0, CountFixes(Prelude + """
        public static class Cases
        {
            static void M(Reply r) { r.Switch(async v => await Task.Yield()); }
        }
        """));

    // ---- RF0004: wrap in Result.Ok(…) or Result.Fail<string>(…) ----

    const string BareString = """
        using Resultful;

        public static class Cases
        {
            static Result<string> Find(int id) => /* why */ $"order {id} was not found"; // keep
        }
        """;

    [Fact]
    public void A_bare_string_is_wrapped_in_the_meaning_chosen()
    {
        Assert.Equal([BareStringCodeFixProvider.OkTitle, BareStringCodeFixProvider.FailTitle], FixTitles(BareString, new BareStringCodeFixProvider()));
        Assert.Equal(BareString.Replace("$\"order {id} was not found\"", "Result.Ok($\"order {id} was not found\")", StringComparison.Ordinal),
            ApplyFix(BareString, fix: new BareStringCodeFixProvider()));
        var failed = ApplyFix(BareString, fix: new BareStringCodeFixProvider(), action: 1);
        Assert.Equal(BareString.Replace("$\"order {id} was not found\"", "Result.Fail<string>($\"order {id} was not found\")", StringComparison.Ordinal), failed);
        Verify(failed);
    }

    [Fact]
    public void No_wrap_is_offered_for_another_result_type() => Assert.Empty(FixTitles("""
        using Resultful;

        public static class Cases
        {
            static Result<string, Error> Find() => "x";
        }
        """, new BareStringCodeFixProvider()));

    // ---- RF0008: add the parameter types ----

    const string Handlers = HandlerRulesTests.Domain + """
        public static class Cases
        {
            static string M(OrderError e, PlaceOrderResult r)
            {
                var a = e.Match(
                    l => "locked", // locked
                    (n) => "missing");
                e.Switch(l => { }, static n => { });
                return a + r.Match(o => "order", (OrderLocked l) => "locked", _ => "missing");
            }
        }
        """;

    [Fact]
    public void Fix_all_adds_the_parameter_types()
    {
        var fixedText = ApplyFix(Handlers, fixAll: true, fix: new UntypedHandlerCodeFixProvider(), generate: true);
        Assert.Equal(Handlers
            .Replace("    l => \"locked\", // locked", "    (OrderLocked l) => \"locked\", // locked", StringComparison.Ordinal)
            .Replace("(n) => \"missing\");", "(OrderNotFound n) => \"missing\");", StringComparison.Ordinal)
            .Replace("e.Switch(l => { }, static n => { });", "e.Switch((OrderLocked l) => { }, static (OrderNotFound n) => { });", StringComparison.Ordinal)
            .Replace("r.Match(o => \"order\", (OrderLocked l) => \"locked\", _ => \"missing\")",
                "r.Match((Order o) => \"order\", (OrderLocked l) => \"locked\", (OrderNotFound _) => \"missing\")", StringComparison.Ordinal),
            fixedText);
        Verify(fixedText.Replace("var a = e.Match(", "var a = e.Match( // expect: RF0005", StringComparison.Ordinal), generate: true);
    }

    // ---- RF0009: replace Map with Bind ----

    [Fact]
    public void Fix_all_replaces_Map_with_Bind_where_it_binds()
    {
        const string Source = """
            using System.Threading.Tasks;
            using Resultful;

            public static class Cases
            {
                static Result<int> Parse(string s) => Result.Ok(s.Length);
                static Result<int, Error> Other(string s) => Result.Ok<int, Error>(1);
                static Task<Result<int>> ParseAsync(string s) => Task.FromResult(Parse(s));

                static async Task M(Result<string> r, ValueTask<Result<string>> vt)
                {
                    _ = await vt.Map(Parse);
                    _ = r.Map(Parse);
                    _ = r /* receiver */ .Map(s => Parse(s));
                    _ = await r.MapAsync(ParseAsync);
                    _ = r.Map(Other);
                }
            }
            """;
        var fixedText = ApplyFix(Source, fixAll: true, fix: new MapToBindCodeFixProvider());
        Assert.Equal(Source
            .Replace("r.Map(Parse)", "r.Bind(Parse)", StringComparison.Ordinal)
            .Replace("vt.Map(Parse)", "vt.Bind(Parse)", StringComparison.Ordinal)
            .Replace(".Map(s => Parse(s))", ".Bind(s => Parse(s))", StringComparison.Ordinal)
            .Replace("r.MapAsync(ParseAsync)", "r.BindAsync(ParseAsync)", StringComparison.Ordinal), fixedText);
        Verify(fixedText.Replace("_ = r.Map(Other);", "_ = r.Map(Other); // expect: RF0009", StringComparison.Ordinal));
    }
}
