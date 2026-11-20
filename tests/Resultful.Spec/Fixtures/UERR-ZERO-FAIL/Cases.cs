// Decision 17 point 4 (08b): Fail and the implicit conversion reject a union with no value (decision 10).
// spec: tfm net11.0
using Resultful;
using Resultful.Spec.Support;

public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");
public sealed record OrderLocked(int Id, string By) : Error($"order {Id} is locked by {By}");
public sealed record Order(int Id);
public union OrderError(OrderNotFound, OrderLocked);

public static class Cases
{
    [SpecCase]
    public static void A_default_union_is_rejected()
    {
        Check.Throws<System.ArgumentException>(() => Result.Fail<Order, OrderError>(default), "Fail");
        Check.Throws<System.ArgumentException>(() => { Result<Order, OrderError> r = default(OrderError); }, "implicit conversion");
        Result<Order, OrderError> ok = (OrderError)new OrderNotFound(1);
        Check.True(ok.IsFailure, "a union with a value is accepted");
    }
}
