using Xunit;
using static Resultful.Analyzers.Tests.AnalyzerHarness;

namespace Resultful.Analyzers.Tests;

/// <summary>RF0001's fix, "discard with `_ =`", one at a time and fix-all (code-fixes.tsv).</summary>
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
}
