// HIER-TRYMAP (13b): TryMap walks Causes() from the outer error inwards and returns the first match.
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;
using Resultful.Spec.Support;

[ClosedHierarchy]
public abstract partial record OrderError : Error;
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
public sealed record OrderLocked(int Id, string By) : OrderError($"order {Id} is locked by {By}");
public sealed record Order(int Id);

[ResultShape(typeof(OrderError), Success = typeof(Order))]
public readonly partial struct PlaceOrderResult;

public sealed record SaveFailed(int Id) : Error($"order {Id} was not saved");

public static class Cases
{
    [SpecCase]
    public static void The_outermost_mapped_error_wins()
    {
        Error wrapped = new SaveFailed(1) { Cause = new OrderLocked(1, "ann") { Cause = new OrderNotFound(1) } };
        Check.True(OrderError.TryMap(wrapped, (OrderLocked l) => 409, (OrderNotFound n) => 404, out var status), "found");
        Check.Equal(409, status, "outer before inner");
        Check.False(OrderError.TryMap(Error.From("x"), (OrderLocked l) => 409, (OrderNotFound n) => 404, out _), "nothing to map");
    }
}
