using Xunit;
using static Resultful.Analyzers.Tests.AnalyzerHarness;

namespace Resultful.Analyzers.Tests;

/// <summary>`resultful.must_observe_types` (editorconfig-keys.tsv): third-party types you can't put the attribute on.</summary>
public sealed class EditorConfigTests
{
    const string Vendor = """
        namespace Vendor
        {
            public readonly struct ApiResponse<T> { public T Body { get; init; } }
            public static class Client
            {
                public static ApiResponse<string> Post() => default;
                public static int Ping() => 1;
            }
        }

        """;

    [Fact]
    public void A_listed_type_is_must_observe() => Verify(Vendor + """
        public static class Cases
        {
            static void Forgets() { Vendor.Client.Post(); } // expect: RF0001
        }
        """, "T:Vendor.ApiResponse`1");

    [Fact]
    public void An_unlisted_type_is_not() => Verify(Vendor + """
        public static class Cases
        {
            static void Forgets() { Vendor.Client.Post(); }
        }
        """, "T:Vendor.Other");

    [Fact]
    public void A_listed_method_is_must_observe_and_entries_without_a_prefix_are_types() => Verify(Vendor + """
        public static class Cases
        {
            static void Forgets()
            {
                Vendor.Client.Ping(); // expect: RF0001
                Vendor.Client.Post(); // expect: RF0001
            }
        }
        """, " M:Vendor.Client.Ping , Vendor.ApiResponse`1 ");

    [Fact]
    public void The_target_core_carries_no_attribute_so_its_results_are_checked_only_when_listed()
    {
        // Spec finding: the target API's Result<T> has no [MustObserve] (PKG-CODEANALYSIS says the core's does).
        const string source = """
            using Resultful;
            public static class Cases
            {
                static void Forgets() { Result.Ok(1); } // expect: RF0001
            }
            """;
        Verify(source, "T:Resultful.Result`1");
        Verify(source.Replace(" // expect: RF0001", "", StringComparison.Ordinal));
    }

    [Fact]
    public void Ids_parse_with_commas_semicolons_and_spaces() =>
        Assert.Equal(["M:A.B", "T:C", "T:D`1"], Symbols.ParseIds(" T:C, M:A.B;D`1 ,, ").Order(StringComparer.Ordinal));
}
