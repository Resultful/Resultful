// Decision 17 (09): an error union in Result<T, TError>. The union isn't an Error, so it implements IErrorUnion
// (written by hand here; the generator writes it from 13b). A bare leaf needs a cast to the union: C# doesn't chain
// leaf -> union -> result (CS0029, documented, not a bug).
// spec: tfm net11.0
using Resultful;
using Resultful.Spec.Support;

public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");
public sealed record OrderLocked(int Id, string By) : Error($"order {Id} is locked by {By}");
public sealed record Order(int Id);

public union OrderError(OrderNotFound, OrderLocked) : IErrorUnion
{
    public Error Error => Value switch { Error e => e, _ => throw new System.InvalidOperationException() };
}

public static class UnionSide
{
    public static Result<Order, OrderError> Get(int id) => id switch
    {
        1 => new Order(1),
        2 => (OrderError)new OrderLocked(2, "ann"),
        _ => (OrderError)new OrderNotFound(id),
    };


    public static string Describe(int id) => Get(id).Match(o => "ok " + o.Id, e => e switch
    {
        OrderNotFound nf => "404 " + nf.Id,
        OrderLocked l => "409 " + l.By,
    });

    public static Result<Order> Widened(int id) => Get(id).AsResult();
}
