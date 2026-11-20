// HIER-CASES (13b): the generator lists a hierarchy's leaves (Cases), the case list Resultful.Text.Json reads through.
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
    public static void Cases_lists_every_leaf()
    {
        Check.SequenceEqual(new[] { "OrderLocked", "OrderNotFound" }, System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Select(OrderError.Cases, t => t.Name), n => n, System.StringComparer.Ordinal), "leaves");
    }
}
