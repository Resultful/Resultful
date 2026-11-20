// SHAPE-SWITCH (13b, net11.0): a switch over the shape is exhaustive, flat or grouped, with no `_` arm; a missing arm is CS8509.
// spec: tfm net11.0
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;
using Resultful.Spec.Support;

[ClosedHierarchy]
public abstract partial record OrderError : Error;
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
public sealed record OrderLocked(int Id) : OrderError($"order {Id} is locked");
[ClosedHierarchy]
public abstract partial record PaymentFault : OrderError;
public sealed record CardDeclined(string Reason) : PaymentFault($"card declined: {Reason}");
public sealed record Order(int Id);

[ResultShape(typeof(OrderError), Success = typeof(Order))]
public readonly partial struct PlaceOrderResult;

public static class Cases
{
    static int Flat(PlaceOrderResult r) => r switch { Order => 201, OrderNotFound => 404, OrderLocked => 409, CardDeclined => 402 };
    static int Grouped(PlaceOrderResult r) => r switch { Order => 201, OrderNotFound => 404, OrderLocked => 409, PaymentFault => 402 };
    static int Missing(PlaceOrderResult r) => r switch { Order => 201, OrderNotFound => 404, OrderLocked => 409 }; // expect: CS8509

    [SpecCase]
    public static void Both_forms_dispatch()
    {
        Check.Equal(402, Flat(new CardDeclined("x")), "flat");
        Check.Equal(402, Grouped(new CardDeclined("x")), "grouped");
        Check.Equal(201, Flat(new Order(1)), "success");
    }
}
