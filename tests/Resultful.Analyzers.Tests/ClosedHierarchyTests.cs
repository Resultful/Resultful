using Xunit;
using static Resultful.Analyzers.Tests.AnalyzerHarness;

namespace Resultful.Analyzers.Tests;

/// <summary>
/// RF0005 (a Match that could be a switch), RF0007 (a private protected constructor for a closed base) and RF0010 (a
/// type in another assembly that derives from a closed base), with RF0005's and RF0007's fixes. RFSP0001 is in SuppressorTests.
/// </summary>
public sealed class ClosedHierarchyTests
{
    [Fact]
    // Without Resultful.Generators, which requires a partial base (RFG0006) and writes the constructor itself.
    public void A_closed_base_another_assembly_can_derive_from_gets_a_suggestion() => Verify("""
        using Resultful;
        using Resultful.CodeAnalysis;

        [ClosedHierarchy] public abstract record Implicit; // expect: RF0007
        [ClosedHierarchy] public abstract record Positional(string Name); // expect: RF0007
        [ClosedHierarchy] internal abstract record Hidden;
        internal static class Outer { [ClosedHierarchy] public abstract class Nested { } }
        public static class Visible { [ClosedHierarchy] public abstract class Nested { } } // expect: RF0007
        [ClosedHierarchy] public abstract class Declared { protected Declared() { } public Declared(int x) { } } // expect: RF0007
        [ClosedHierarchy] public abstract class ProtectedInternal { protected internal ProtectedInternal() { } } // expect: RF0007
        [ClosedHierarchy] public abstract record Closed { private protected Closed() { } }
        [ClosedHierarchy] public abstract class Internal { internal Internal() { } }
        public abstract record Open;

        public sealed record A() : Implicit;
        public sealed record B() : Positional("b");
        public sealed record C() : Closed;
        """);

    const string Library = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Lib
        {
            [ClosedHierarchy] public abstract record OrderError(string Message) : Error(Message);
            public sealed record NotFound() : OrderError("missing");
            public abstract record Middle(string Message) : OrderError(Message);
            public abstract record Vendor;
            public abstract record Plain;
        }
        """;

    [Fact]
    public void A_type_in_another_assembly_that_derives_from_a_closed_base_is_an_error() => Verify("""
        using Lib;

        public sealed record Evil : OrderError // expect: RF0010
        {
            public Evil(OrderError copy) : base(copy) { }
        }

        public sealed record Sneaky : Middle // expect: RF0010
        {
            public Sneaky(Middle copy) : base(copy) { }
        }

        public sealed record Listed : Vendor; // expect: RF0010
        public sealed record Fine : Plain;
        """, library: Library, closedHierarchyTypes: "T:Lib.Vendor");

    // ---- RF0007's fix ----

    [Fact]
    public void Fix_all_makes_constructors_private_protected_or_adds_one()
    {
        const string Source = """
            using Resultful.CodeAnalysis;

            [ClosedHierarchy]
            public abstract class Declared
            {
                /// <summary>Kept.</summary>
                public Declared() { }
                protected internal Declared(int x) { }
                private Declared(string s) { }
            }

            [ClosedHierarchy]
            public abstract class Implicit
            {
                public int X => 1;
            }

            [ClosedHierarchy] public abstract record Positional(string Name);
            """;
        var fixedText = ApplyFix(Source, fixAll: true, fix: new PrivateProtectedCodeFixProvider());
        Assert.Equal("""
            using Resultful.CodeAnalysis;

            [ClosedHierarchy]
            public abstract class Declared
            {
                /// <summary>Kept.</summary>
                private protected Declared() { }
                private protected Declared(int x) { }
                private Declared(string s) { }
            }

            [ClosedHierarchy]
            public abstract class Implicit
            {
                private protected Implicit()
                {
                }

                public int X => 1;
            }

            [ClosedHierarchy] public abstract record Positional(string Name);
            """, fixedText);
        Verify(fixedText.Replace("Positional(string Name);", "Positional(string Name); // expect: RF0007", StringComparison.Ordinal));
    }

    // ---- RF0005 and its fix ----

    [Fact]
    public void Fix_all_rewrites_a_match_as_a_switch_expression()
    {
        var source = HandlerRulesTests.Domain + """
            public static class Cases
            {
                static string Name(OrderNotFound n) => "missing " + n.Id;

                static string M(OrderError e, OrderError? maybe)
                {
                    var a = e.Match((OrderLocked l) => "locked " + l.Id /* why */, Name);
                    var b = (maybe ?? e).Match((OrderLocked _) => 1, (OrderNotFound n) => n.Id);
                    var c = e.Match((OrderLocked l) => { return 1; }, (OrderNotFound n) => 2);
                    var orderNotFound = 1;
                    var d = e.Match((OrderLocked orderLocked) => orderLocked.Id, (OrderNotFound n) => Name(n).Length) + orderNotFound;
                    e.Match((OrderLocked l) => 1, (OrderNotFound n) => 2);
                    return a + b + c + d;
                }
            }
            """;
        Assert.Equal([MatchToSwitchCodeFixProvider.Title, MatchToSwitchCodeFixProvider.Title, MatchToSwitchCodeFixProvider.Title],
            FixTitles(source, new MatchToSwitchCodeFixProvider(), generate: true));
        var fixedText = ApplyFix(source, fixAll: true, fix: new MatchToSwitchCodeFixProvider(), generate: true);
        Assert.Equal(source
            .Replace("""e.Match((OrderLocked l) => "locked " + l.Id /* why */, Name);""", """
                e switch
                        {
                            OrderLocked l => "locked " + l.Id /* why */,
                            OrderNotFound orderNotFound2 => Name(orderNotFound2),
                        };
                """, StringComparison.Ordinal)
            .Replace("(maybe ?? e).Match((OrderLocked _) => 1, (OrderNotFound n) => n.Id);", """
                (maybe ?? e) switch
                        {
                            OrderLocked => 1,
                            OrderNotFound n => n.Id,
                        };
                """, StringComparison.Ordinal)
            .Replace("e.Match((OrderLocked orderLocked) => orderLocked.Id, (OrderNotFound n) => Name(n).Length)", """
                e switch
                        {
                            OrderLocked orderLocked => orderLocked.Id,
                            OrderNotFound n => Name(n).Length,
                        }
                """, StringComparison.Ordinal), fixedText);

        // Each switch the fix wrote gets CS8509, which RFSP0001 suppresses.
        VerifySuppressions(fixedText
            .Replace("var a = e switch", "var a = e switch // suppressed: CS8509", StringComparison.Ordinal)
            .Replace("var b = (maybe ?? e) switch", "var b = (maybe ?? e) switch // suppressed: CS8509", StringComparison.Ordinal)
            .Replace("var d = e switch", "var d = e switch // suppressed: CS8509", StringComparison.Ordinal), generate: true);
    }
}
