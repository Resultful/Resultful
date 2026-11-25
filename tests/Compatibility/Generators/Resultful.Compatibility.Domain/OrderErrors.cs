using Resultful;
using Resultful.CodeAnalysis;

namespace Compatibility.Domain
{
    [ClosedHierarchy]
    public abstract partial record OrderError(string Message) : Error(Message);

    public sealed record OrderNotFound(int Id) : OrderError($"Order {Id} not found");

    public sealed record OrderLocked(int Id) : OrderError($"Order {Id} locked");

    public sealed record Order(int Id);

    // No [ClosedHierarchy]: closed for the shapes project by resultful.closed_hierarchy_types, as a third-party type would be.
    public abstract record Shipment;

    public sealed record Shipped : Shipment;

    public sealed record Pending : Shipment;

#if NET11_0_OR_GREATER
    public sealed record CardDeclined(string Reason) : Error($"Card declined: {Reason}");

    public sealed record CardExpired() : Error("Card expired");

    public partial union PaymentError(CardDeclined, CardExpired);

    public partial union CheckoutError(OrderNotFound, PaymentError);
#endif
}
