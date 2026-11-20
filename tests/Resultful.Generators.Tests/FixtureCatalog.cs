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
}
