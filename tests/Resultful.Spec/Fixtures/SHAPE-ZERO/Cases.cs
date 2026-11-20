// SHAPE-ZERO (13b): default of a shape throws on every read; IsDefault (and HasValue on .NET 11) don't.
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

public static class Cases
{
    [SpecCase]
    public static void The_zero_shape_throws_on_every_read()
    {
        var zero = default(PlaceOrderResult);
        Check.True(zero.IsDefault, "IsDefault");
        Check.Throws<System.InvalidOperationException>(() => zero.TryGetValue(out Order? _), "TryGetValue");
        Check.Throws<System.InvalidOperationException>(() => zero.TryGetError(out _), "TryGetError");
        Check.Throws<System.InvalidOperationException>(() => zero.AsResult(), "AsResult");
        Check.Throws<System.InvalidOperationException>(() => { Result<Order> widened = zero; }, "widening");
        Check.Throws<System.InvalidOperationException>(() => zero.Match((Order o) => 1, (OrderLocked l) => 3, (OrderNotFound n) => 2), "Match");
#if NET11_0_OR_GREATER
        Check.False(zero.HasValue, "HasValue is false and doesn't throw");
        Check.Throws<System.Runtime.CompilerServices.SwitchExpressionException>(() => _ = zero switch { Order => 1, OrderNotFound => 2, OrderLocked => 3 }, "switch");
#endif
    }
}
