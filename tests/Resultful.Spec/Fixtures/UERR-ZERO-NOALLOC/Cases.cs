// Decision 17 (08b): the zero check costs nothing: Fail with a union error allocates 0 bytes (probe union-probe-2).
// spec: tfm net11.0
using Resultful;
using Resultful.Spec.Support;

public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");
public sealed record OrderLocked(int Id, string By) : Error($"order {Id} is locked by {By}");
public sealed record Order(int Id);
public union OrderError(OrderNotFound, OrderLocked);

public static class Cases
{
    static readonly OrderError Locked = new OrderLocked(1, "ann");

    [SpecCase]
    public static void Fail_with_a_union_allocates_nothing()
    {
        Check.Equal(0L, Check.Allocated(() => _ = Result.Fail<Order, OrderError>(Locked).IsFailure), "Fail");
    }
}
