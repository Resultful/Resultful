namespace Resultful.Generators.Tests;

internal static class FixtureCatalog
{
    public const string FlatHierarchy = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Fixture.Flat
        {
            [ClosedHierarchy]
            public abstract partial record OrderError(string Message) : Error(Message);
            public sealed record OrderNotFound(int Id) : OrderError($"Order {Id} not found");
            public sealed record OrderLocked(int Id) : OrderError($"Order {Id} locked");

            public sealed record Order(int Id);

            [ResultShape(typeof(OrderError), Success = typeof(Order))]
            public readonly partial struct PlaceOrderResult;
        }
        """;

    public const string DeepFourLevelHierarchy = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Fixture.Deep
        {
            [ClosedHierarchy]
            public abstract partial record AppError(string Message) : Error(Message);

            [ClosedHierarchy]
            public abstract partial record DomainError(string Message) : AppError(Message);

            [ClosedHierarchy]
            public abstract partial record PaymentError(string Message) : DomainError(Message);

            public sealed record CardExpired() : PaymentError("expired");
            public sealed record InsufficientFunds() : PaymentError("funds");
            public sealed record OrderMissing(int Id) : DomainError($"missing {Id}");
            public sealed record RateLimited() : AppError("rate limited");

            public sealed record Receipt(string Code);

            [ResultShape(typeof(AppError), Success = typeof(Receipt))]
            public readonly partial struct CheckoutResult;
        }
        """;

    public const string ConcreteIntermediate = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Fixture.ConcreteMid
        {
            [ClosedHierarchy]
            public abstract partial record NetError(string Message) : Error(Message);

            public record HttpError(int Code) : NetError($"HTTP {Code}");
            public sealed record RateLimitHttpError(int RetryAfter) : HttpError(429);
            public sealed record TimeoutError() : NetError("timeout");

            public sealed record Payload(string Body);

            [ResultShape(typeof(NetError), Success = typeof(Payload))]
            public readonly partial struct FetchResult;
        }
        """;

    public const string CollidingLeafNames = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Fixture.Colliding
        {
            [ClosedHierarchy]
            public abstract partial record RootError(string Message) : Error(Message);

            public sealed record UniqueError() : RootError("unique");

            namespace Orders
            {
                public sealed record NotFound(int Id) : RootError($"order {Id}");
            }

            namespace Payments
            {
                public sealed record NotFound(string Tx) : RootError($"payment {Tx}");
            }

            public sealed record Summary(string Text);

            [ResultShape(typeof(RootError), Success = typeof(Summary))]
            public readonly partial struct CombinedResult;
        }
        """;

    public const string NestedShape = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Fixture.Nested
        {
            public partial class OuterService
            {
                [ClosedHierarchy]
                public abstract partial record ServiceError(string Message) : Error(Message);
                public sealed record Missing() : ServiceError("missing");

                public sealed record Item(int Id);

                [ResultShape(typeof(ServiceError), Success = typeof(Item))]
                public readonly partial struct GetItemResult;
            }
        }
        """;

    public const string InterfaceSuccess = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Fixture.InterfaceOk
        {
            public interface IOrder { int Id { get; } }

            [ClosedHierarchy]
            public abstract partial record LookupError(string Message) : Error(Message);
            public sealed record NotFound() : LookupError("not found");

            [ResultShape(typeof(LookupError), Success = typeof(IOrder))]
            public readonly partial struct FindOrderResult;
        }
        """;

    public const string ValuelessShape = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Fixture.Void
        {
            [ClosedHierarchy]
            public abstract partial record CancelError(string Message) : Error(Message);
            public sealed record AlreadyShipped() : CancelError("shipped");
            public sealed record UnknownOrder() : CancelError("unknown");

            [ResultShape(typeof(CancelError))]
            public readonly partial struct CancelOrderResult;
        }
        """;

    public const string ManyErrorsShape = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Fixture.Many
        {
            [ClosedHierarchy]
            public abstract partial record InputError(string Message) : Error(Message);
            public sealed record Required(string Field) : InputError($"{Field} required");
            public sealed record TooLong(string Field) : InputError($"{Field} too long");

            public sealed record Registration(string Email);

            [ResultShape(typeof(InputError), Success = typeof(Registration), ManyErrors = true)]
            public readonly partial struct RegisterResult;
        }
        """;

    public const string CrossAssemblyBase = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Fixture.ExternalDomain
        {
            [ClosedHierarchy]
            public abstract partial record DomainError(string Message) : Error(Message);
            public sealed record AccountClosed() : DomainError("closed");
            public sealed record AccountFrozen() : DomainError("frozen");
            public sealed record Account(int Id);
        }
        """;

    public const string CrossAssemblyConsumer = """
        using Resultful.CodeAnalysis;
        using Fixture.ExternalDomain;

        namespace Fixture.Api
        {
            [ResultShape(typeof(DomainError), Success = typeof(Account))]
            public readonly partial struct LoadAccountResult;
        }
        """;

    // Decision 17: the same errors as a C# 15 union (net11.0) and as a [ClosedHierarchy], in one namespace, so one probe
    // runs against either. The union lists its cases in the hierarchy's leaf order, so Match takes the same parameters.
    public const string TwinUnion = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Fixture.Twin
        {
            public sealed record OrderNotFound(int Id) : Error($"Order {Id} not found");
            public sealed record OrderLocked(int Id) : Error($"Order {Id} locked");
            public partial union OrderError(OrderLocked, OrderNotFound);

            public sealed record Order(int Id);

            [ResultShape(typeof(OrderError), Success = typeof(Order))]
            public readonly partial struct PlaceOrderResult;
        }
        """;

    public const string TwinHierarchy = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Fixture.Twin
        {
            [ClosedHierarchy]
            public abstract partial record OrderError(string Message) : Error(Message);
            public sealed record OrderNotFound(int Id) : OrderError($"Order {Id} not found");
            public sealed record OrderLocked(int Id) : OrderError($"Order {Id} locked");

            public sealed record Order(int Id);

            [ResultShape(typeof(OrderError), Success = typeof(Order))]
            public readonly partial struct PlaceOrderResult;
        }
        """;

    // A nested union stays one case: the shape's error cases are OrderNotFound and PaymentError.
    public const string NestedUnion = """
        using Resultful;
        using Resultful.CodeAnalysis;

        namespace Fixture.Nested
        {
            public sealed record OrderNotFound(int Id) : Error($"Order {Id} not found");
            public sealed record CardDeclined(string Reason) : Error($"Card declined: {Reason}");
            public sealed record CardExpired() : Error("Card expired");
            public partial union PaymentError(CardDeclined, CardExpired);
            public partial union CheckoutError(OrderNotFound, PaymentError);

            public sealed record Receipt(string Code);

            [ResultShape(typeof(CheckoutError), Success = typeof(Receipt))]
            public readonly partial struct CheckoutResult;
        }
        """;

    // Decision 16 item 5: next to FlatHierarchy, ManyErrorsShape and ValuelessShape, a shape with the same types as
    // PlaceOrderResult and as RegisterResult, and one with PlaceOrderResult's success type but another error type. None
    // of them converts into another: a shape with the same error type goes through AsResult().
    public const string ShapePeers = """
        using Resultful.CodeAnalysis;

        namespace Fixture.Flat
        {
            [ResultShape(typeof(OrderError), Success = typeof(Order))]
            public readonly partial struct LoadOrderResult;

            [ResultShape(typeof(Fixture.Void.CancelError), Success = typeof(Order))]
            public readonly partial struct CancelForOrderResult;
        }

        namespace Fixture.Many
        {
            [ResultShape(typeof(InputError), Success = typeof(Registration), ManyErrors = true)]
            public readonly partial struct ReviewResult;
        }
        """;

    // Appended to DeepFourLevelHierarchy (no using directives): shapes whose errors are closed bases nested under
    // AppError. CheckoutResult converts from PayResult and DomainResult, DomainResult from PayResult, AppChecks from
    // DomainChecks.
    public const string NestedPeers = """

        namespace Fixture.Deep
        {
            [Resultful.CodeAnalysis.ResultShape(typeof(PaymentError), Success = typeof(Receipt))]
            public readonly partial struct PayResult;

            [Resultful.CodeAnalysis.ResultShape(typeof(DomainError), Success = typeof(Receipt))]
            public readonly partial struct DomainResult;

            [Resultful.CodeAnalysis.ResultShape(typeof(DomainError), Success = typeof(Receipt), ManyErrors = true)]
            public readonly partial struct DomainChecks;

            [Resultful.CodeAnalysis.ResultShape(typeof(AppError), Success = typeof(Receipt), ManyErrors = true)]
            public readonly partial struct AppChecks;
        }
        """;

    // Appended to NestedUnion: a shape over the inner union, a direct case of CheckoutError, so CheckoutResult converts
    // from it; and one over CardDeclined, a leaf of the inner union, which doesn't (decision 17's direct-case rule).
    public const string NestedUnionPeers = """

        namespace Fixture.Nested
        {
            [Resultful.CodeAnalysis.ResultShape(typeof(PaymentError), Success = typeof(Receipt))]
            public readonly partial struct ChargeResult;

            [Resultful.CodeAnalysis.ResultShape(typeof(CardDeclined), Success = typeof(Receipt))]
            public readonly partial struct DeclineResult;
        }
        """;
}
