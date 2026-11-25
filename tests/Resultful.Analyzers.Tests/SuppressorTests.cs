using Xunit;
using static Resultful.Analyzers.Tests.AnalyzerHarness;

namespace Resultful.Analyzers.Tests;

/// <summary>
/// RFSP0001: CS8509 on a switch over a closed base, read as Roslyn's suppressor tests read it (the compiler's
/// diagnostic is still reported, marked suppressed by RFSP0001). `// suppressed: CS8509` is a suppression,
/// `// expect: CS8509` a CS8509 that is kept.
/// </summary>
public sealed class SuppressorTests
{
    const string Domain = """
        using Resultful;
        using Resultful.CodeAnalysis;

        [ClosedHierarchy]
        public abstract record OrderError : Error { private protected OrderError(string m) : base(m) { } }
        public sealed record OrderNotFound() : OrderError("nf");
        public sealed record OrderLocked(int Id) : OrderError("locked");
        public abstract record PaymentError : OrderError { private protected PaymentError(string m) : base(m) { } }
        public sealed record CardDeclined() : PaymentError("declined");
        public sealed record CardExpired() : PaymentError("expired");
        public abstract record OpenError(string M) : Error(M);
        public sealed record Only() : OpenError("only");

        """;

    [Fact]
    public void A_switch_that_matches_every_case_is_suppressed_and_one_that_does_not_is_kept() => VerifySuppressions(Domain + """
        public static class Cases
        {
            static int Every(OrderError e) => e switch { OrderNotFound => 404, OrderLocked => 409, CardDeclined => 402, CardExpired => 402 }; // suppressed: CS8509
            static int Declarations(OrderError e) => e switch { OrderNotFound n => 404, OrderLocked l => l.Id, PaymentError p => 402 }; // suppressed: CS8509
            static int Or(OrderError e) => e switch { OrderNotFound or OrderLocked => 400, CardDeclined { } or CardExpired => 402 }; // suppressed: CS8509
            static int Intermediate(PaymentError e) => e switch { CardDeclined => 1, CardExpired => 2 }; // suppressed: CS8509
            static int Nested(OrderError e) => e switch // suppressed: CS8509
            {
                OrderNotFound => 404,
                OrderLocked => 409,
                PaymentError p => p switch { CardDeclined => 1, CardExpired => 2 }, // suppressed: CS8509
            };
            static int Missing(OrderError e) => e switch { OrderNotFound => 404, PaymentError => 402 }; // expect: CS8509
            static int MissingInner(PaymentError e) => e switch { CardDeclined => 1 }; // expect: CS8509
            static int Guarded(OrderError e) => e switch { OrderNotFound => 404, OrderLocked l when l.Id > 0 => 409, PaymentError => 402 }; // expect: CS8509
            static int Property(OrderError e) => e switch { OrderNotFound => 404, OrderLocked { Id: 1 } => 409, PaymentError => 402 }; // expect: CS8509
            static int Negated(OrderError e) => e switch { not OrderLocked => 1, OrderLocked { Id: > 0 } => 2 }; // expect: CS8509
            static int Open(OpenError e) => e switch { Only => 1 }; // expect: CS8509
            static int Interface(System.IComparable c) => c switch { string => 1 }; // expect: CS8509
            static int Tuple(OrderError a, OrderError b) => (a, b) switch { (OrderNotFound, _) => 1 }; // expect: CS8509
        }
        """);

    // Decision 15's null-arm rule: where the compiler's nullable analysis says the input may be null (where it would
    // report CS8655 for an otherwise exhaustive switch), a null arm is required; elsewhere it isn't.
    [Fact]
    public void A_nullable_input_needs_a_null_arm() => VerifySuppressions(Domain + """
        public static class Cases
        {
            static int NoNullArm(OrderError? e) => e switch { OrderNotFound => 1, OrderLocked => 2, PaymentError => 3 }; // expect: CS8509
            static int NullArm(OrderError? e) => e switch { OrderNotFound => 1, OrderLocked => 2, PaymentError => 3, null => 0 }; // suppressed: CS8509
            static int NullOr(OrderError? e) => e switch { null or OrderNotFound => 1, OrderLocked => 2, PaymentError => 3 }; // suppressed: CS8509
            static int GuardedNull(OrderError? e, bool b) => e switch { OrderNotFound => 1, OrderLocked => 2, PaymentError => 3, null when b => 0 }; // expect: CS8509
            static int Checked(OrderError? e)
            {
                if (e is null) return 0;
                return e switch { OrderNotFound => 1, OrderLocked => 2, PaymentError => 3 }; // suppressed: CS8509
            }
            static int NotNull(OrderError e) => e switch { OrderNotFound => 1, OrderLocked => 2, PaymentError => 3 }; // suppressed: CS8509
        #nullable disable
            static int Oblivious(OrderError e) => e switch { OrderNotFound => 1, OrderLocked => 2, PaymentError => 3 }; // suppressed: CS8509
        #nullable restore
        }
        """);

    [Fact]
    public void A_base_that_is_not_abstract_is_a_case_itself() => VerifySuppressions("""
        using Resultful.CodeAnalysis;

        [ClosedHierarchy] public class Shape { private protected Shape() { } }
        public sealed class Circle : Shape;

        public static class Cases
        {
            static int Leaves(Shape s) => s switch { Circle => 1 }; // expect: CS8509
        }
        """);

    [Fact]
    public void A_generic_base_or_leaf_keeps_the_warning() => VerifySuppressions("""
        using Resultful.CodeAnalysis;

        [ClosedHierarchy] public abstract record Outcome<T> { private protected Outcome() { } }
        public sealed record Done<T>(T Value) : Outcome<T>;
        public sealed record Failed<T> : Outcome<T>;

        [ClosedHierarchy] public abstract record Shape { private protected Shape() { } }
        public sealed record Circle : Shape;
        public sealed record Boxed<T>(T Value) : Shape;
        public abstract record Group : Shape { private protected Group() { } }
        public sealed record Pair<T> : Group;

        public static class Cases
        {
            static int Generic(Outcome<int> o) => o switch { Done<int> => 1, Failed<int> => 2 }; // expect: CS8509
            static int GenericLeaf(Shape s) => s switch { Circle => 1, Boxed<int> => 2, Group => 3 }; // expect: CS8509
            static int ThroughABase(Shape s) => s switch { Circle => 1, Boxed<int> or Boxed<string> => 2, Group => 3 }; // expect: CS8509
            static int GroupArm(Group g) => g switch { Pair<int> => 1 }; // expect: CS8509
        }
        """);

    const string Library = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Lib
        {
            [ClosedHierarchy] public abstract record OrderError(string Message) : Error(Message);
            public sealed record NotFound() : OrderError("missing");
            public sealed record Locked() : OrderError("locked");

            [ClosedHierarchy] public abstract record Hidden(string Message) : Error(Message);
            public sealed record Shown() : Hidden("shown");
            internal sealed record Secret() : Hidden("secret");

            // A compilation sees a reference's internal top-level types, but not its non-public nested ones.
            [ClosedHierarchy] public abstract record Nesting(string Message) : Error(Message)
            {
                private sealed record Inner() : Nesting("inner");
            }
            public sealed record Outer() : Nesting("outer");

            [ClosedHierarchy] public abstract record Extended(string Message) : Error(Message);
            public sealed record Inside() : Extended("inside");

            public abstract record Vendor;
            public sealed record VendorA : Vendor;
            public sealed record VendorB : Vendor;
        }
        """;

    [Fact]
    public void A_hierarchy_in_another_assembly_is_read_with_its_internal_types() => VerifySuppressions("""
        using Lib;

        public static class Cases
        {
            static int Every(OrderError e) => e switch { NotFound => 404, Locked => 409 }; // suppressed: CS8509
            static int Missing(OrderError e) => e switch { NotFound => 404 }; // expect: CS8509
            static int Internal(Hidden e) => e switch { Shown => 1 }; // expect: CS8509
            static int PrivateNested(Nesting e) => e switch { Outer => 1 }; // expect: CS8509
        }
        """, library: Library);

    // Arms are matched by symbol: a type with a leaf's full name from another assembly, or from source, covers nothing.
    [Fact]
    public void A_type_with_a_leaf_name_from_an_extern_alias_matches_nothing() => VerifySuppressions("""
        extern alias O;
        using Lib;

        public static class Cases
        {
            static int Alias(OrderError e) => e switch { NotFound => 404, O::Lib.Locked => 409 }; // expect: CS8509
            static int Both(OrderError e) => e switch { NotFound => 404, O::Lib.Locked => 409, Locked => 423 }; // suppressed: CS8509
        }
        """, library: Library, aliased: """
        namespace Lib { public interface Locked { } }
        """);

    [Fact]
    public void A_source_type_that_shadows_a_leaf_matches_nothing() => VerifySuppressions("""
        using Lib;

        namespace Lib { public interface Locked { } }

        public static class Cases
        {
            static int Shadowed(OrderError e) => e switch { NotFound => 404, Locked => 409 }; // expect: CS8509
        }
        """, library: Library);

    [Fact]
    public void A_type_outside_the_base_assembly_opens_the_hierarchy() => VerifySuppressions("""
        using Lib;

        public sealed record Evil : OrderError // expect: RF0010
        {
            public Evil(OrderError copy) : base(copy) { }
        }

        public static class Cases
        {
            static int Here(OrderError e) => e switch { NotFound => 404, Locked => 409 }; // expect: CS8509
            static int Matched(OrderError e) => e switch { NotFound => 404, Locked => 409, Evil => 500 }; // expect: CS8509
            static int Referenced(Extended e) => e switch { Inside => 1, Dep.Outside => 2 }; // expect: CS8509
        }
        """, library: Library, dependent: """
        namespace Dep
        {
            public sealed record Outside : Lib.Extended
            {
                public Outside(Lib.Extended copy) : base(copy) { }
            }
        }
        """);

    [Fact]
    public void A_third_party_base_listed_in_editorconfig_is_closed() => VerifySuppressions("""
        using Lib;

        public static class Cases
        {
            static int Listed(Vendor v) => v switch { VendorA => 1, VendorB => 2 }; // suppressed: CS8509
            static int Missing(Vendor v) => v switch { VendorA => 1 }; // expect: CS8509
        }
        """, library: Library, closedHierarchyTypes: "T:Lib.Other, Lib.Vendor");

    [Fact]
    public void An_unlisted_third_party_base_is_open() => VerifySuppressions("""
        using Lib;

        public static class Cases
        {
            static int Unlisted(Vendor v) => v switch { VendorA => 1, VendorB => 2 }; // expect: CS8509
        }
        """, library: Library, closedHierarchyTypes: "T:Lib.Other");

    // On net10.0 a shape is no union, so its error is what gets switched: a closed base whose constructor the generator wrote.
    [Fact]
    public void The_error_of_a_generated_shape_is_switched_as_a_closed_base() => VerifySuppressions(HandlerRulesTests.Domain + """
        public static class Cases
        {
            static int Status(PlaceOrderResult r) =>
                r.AsResult().TryGetError(out var e) ? e switch { OrderNotFound => 404, OrderLocked => 409 } : 200; // suppressed: CS8509
            static int Each(ManyResult r) =>
                r.AsValidation().TryGetErrors(out var errors) ? errors[0] switch { OrderNotFound => 404, OrderLocked => 409 } : 200; // suppressed: CS8509
            static int Missing(PlaceOrderResult r) =>
                r.AsResult().TryGetError(out var e) ? e switch { OrderNotFound => 404 } : 200; // expect: CS8509
        }
        """, generate: true);
}
