using Resultful;
using Resultful.CodeAnalysis;

[ClosedHierarchy] public abstract partial record OrderError : Error;
public sealed record OrderNotFound(int Id) : OrderError("nf");
public sealed record OrderLocked(int Id) : OrderError("locked");
public sealed record Order(int Id);
[ResultShape(typeof(OrderError), Success = typeof(Order))] public readonly partial struct PlaceOrderResult;

public static class Library
{
    public static PlaceOrderResult Place(int id) => new OrderNotFound(id);
}
