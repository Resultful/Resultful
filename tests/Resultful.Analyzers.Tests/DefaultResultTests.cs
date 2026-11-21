using Xunit;
using static Resultful.Analyzers.Tests.AnalyzerHarness;

namespace Resultful.Analyzers.Tests;

/// <summary>RF0002 (decision 10 layer 3, decision 17 point 5) against the core, generated shapes and a user's own types.</summary>
public sealed class DefaultResultTests
{
    [Fact]
    public void Default_of_the_core_results_is_reported() => Verify("""
        using Resultful;

        public static class Cases
        {
            static Result<int> A() => default(Result<int>); // expect: RF0002
            static Result<int, string> B() => default; // expect: RF0002
            static Validation<int, string> C() => default; // expect: RF0002
            static Validation<int> D() => default; // expect: RF0002
            static void E(Result<int> r = default) { } // expect: RF0002
            static void F() { Result<int> r = default; _ = r; } // expect: RF0002
            static Result<int>[] G() => [default]; // expect: RF0002
        }
        """);

    [Fact]
    public void Comparing_with_default_is_not_reported() => Verify("""
        using Resultful;

        public static class Cases
        {
            static bool A(Result<int> r) => r == default;
            static bool B(Result<int> r) => r != default(Result<int>);
            static bool C(Result<int> r) => r.Equals(default(Result<int>));
            static bool D(Result<int> r) => Equals(r, (object)default(Result<int>));
            static bool E(Result<int> r) => (default) == r;
        }
        """);

    [Fact]
    public void An_out_parameter_and_an_error_obsolete_member_are_reported() => Verify("""
        using System;
        using Resultful;

        public static class Cases
        {
            static void Make(out Result<int> r) { r = default; } // expect: RF0002
            static bool TryGet(out Result<int> r) { r = (Result<int>)default; return false; } // expect: RF0002
            static void Reset(ref Result<int> r) { r = default; } // expect: RF0002
            [Obsolete("Never call this.", error: true)] static Result<int> Never() => default; // expect: RF0002
        }
        """);

    [Fact]
    public void Default_in_a_pattern_is_left_to_the_compiler() => Verify("""
        using Resultful;

        public static class Cases
        {
            static bool A(Result<int> r) => r is default(Result<int>); // expect: CS9135
        }
        """);

    [Fact]
    public void Option_a_nullable_result_and_a_type_parameter_are_not() => Verify("""
        using Resultful;

        public static class Cases
        {
            static Option<int> A() => default;
            static Result<int>? B() => default;
            static T C<T>() => default!;
            static int D() => default;
            static Result<int> E() => Result.Ok(1);
        }
        """);

    [Fact]
    public void A_users_struct_that_forbids_new_is_a_result() => Verify("""
        using System;

        public readonly struct Outcome
        {
            [Obsolete("Use Outcome.Ok or Outcome.Fail.", error: true)]
            public Outcome() => throw new InvalidOperationException();
        }

        public readonly struct Warned
        {
            [Obsolete("Only a warning.")]
            public Warned() { }
        }

        public readonly struct Plain { }

        public static class Cases
        {
            static Outcome A() => default; // expect: RF0002
            static Warned B() => default;
            static Plain C() => default;
        }
        """, withCore: false);

    [Fact]
    public void A_result_shape_is_a_result_before_the_generator_runs() => Verify("""
        using Resultful;
        using Resultful.CodeAnalysis;

        [ResultShape(typeof(Error))]
        public readonly partial struct Saved { }

        public static class Cases
        {
            static Saved A() => default; // expect: RF0002
            static Saved B() => new Saved(); // expect: RF0002
        }
        """);

    [Fact]
    public void An_internal_copy_of_ResultShape_counts() => Verify("""
        namespace Resultful.CodeAnalysis
        {
            [System.AttributeUsage(System.AttributeTargets.Struct)]
            internal sealed class ResultShapeAttribute(System.Type errorType) : System.Attribute;
        }

        [Resultful.CodeAnalysis.ResultShape(typeof(string))]
        public readonly partial struct Saved { }

        public static class Cases
        {
            static Saved A() => default(Saved); // expect: RF0002
        }
        """, withCore: false);

    [Fact]
    public void Default_and_new_of_an_error_union_are_reported_and_other_unions_are_not()
    {
        Assert.SkipUnless(IsNet11, "C# 15 unions need net11.0 (the fixture RF-0002-UNION is net11.0 only)");
        Verify("""
            using Resultful;

            public sealed record OrderNotFound(int Id) : Error("nf");
            public sealed record OrderLocked(int Id) : Error("locked");
            public sealed record CardDeclined(string Card) : Error("declined");
            public union OrderError(OrderNotFound, OrderLocked);
            public union PaymentError(CardDeclined);
            public union CheckoutError(OrderError, PaymentError);
            public union Pet(int, string);

            public static class Cases
            {
                static OrderError A() => default(OrderError); // expect: RF0002
                static OrderError B() => default; // expect: RF0002
                static OrderError C() => new OrderNotFound(1);
                static CheckoutError D() => default; // expect: RF0002
                static Pet E() => default;
                static Result<int, OrderError> F() => (OrderError)new OrderLocked(1);
            }
            """);
    }

    [Fact]
    public void Default_of_a_generated_shape_is_reported() => Verify("""
        using Resultful;
        using Resultful.CodeAnalysis;

        public sealed record Order(int Id);
        public sealed record Declined(string Reason) : Error(Reason);

        [ResultShape(typeof(Declined), Success = typeof(Order))]
        public readonly partial struct PayResult;

        public static class Cases
        {
            static PayResult A() => default; // expect: RF0002
            static PayResult B() => new Order(1);
            static bool C(PayResult r) => r.IsDefault;
        }
        """, generate: true);

    [Fact]
    public void The_generated_code_of_an_error_union_shape_passes_both_rules()
    {
        Assert.SkipUnless(IsNet11, "C# 15 unions need net11.0");
        Verify("""
            using Resultful;
            using Resultful.CodeAnalysis;

            public sealed record Order(int Id);
            public sealed record OrderNotFound(int Id) : Error("nf");
            public sealed record CardDeclined(string Card) : Error("declined");
            public sealed record CardExpired(string Card) : Error("expired");
            public partial union PaymentError(CardDeclined, CardExpired);
            public partial union CheckoutError(OrderNotFound, PaymentError);

            [ResultShape(typeof(CheckoutError), Success = typeof(Order))]
            public readonly partial struct CheckoutResult;

            public static class Cases
            {
                static CheckoutResult A() => default; // expect: RF0002
                static CheckoutResult B() => new CardDeclined("x");
                static void C() { B(); } // expect: RF0001
            }
            """, generate: true);
    }

    [Fact]
    public void The_generated_peer_conversion_passes() => Verify("""
        using Resultful;
        using Resultful.CodeAnalysis;

        public sealed record Receipt(string Code);
        [ClosedHierarchy] public abstract partial record AppError : Error;
        [ClosedHierarchy] public abstract partial record PaymentError : AppError;
        public sealed record CardExpired() : PaymentError("expired");
        public sealed record RateLimited() : AppError("rate limited");

        [ResultShape(typeof(AppError), Success = typeof(Receipt))]
        public readonly partial struct CheckoutResult;

        [ResultShape(typeof(PaymentError), Success = typeof(Receipt))]
        public readonly partial struct PayResult;

        public static class Cases
        {
            static PayResult Pay(int n) => n > 0 ? new Receipt("r" + n) : new CardExpired();
            static CheckoutResult Checkout(int n) => Pay(n);
            static CheckoutResult Zero() => default(PayResult); // expect: RF0002
        }
        """, generate: true);
}
