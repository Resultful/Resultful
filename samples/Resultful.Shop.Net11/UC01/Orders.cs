namespace Resultful.Shop.Net11;

public sealed record Order(int Id, string? LockedBy = null);
public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");
public sealed record OrderLocked(int Id, string By) : Error($"order {Id} is locked by {By}");

// The union is the closed set; the generator adds IErrorUnion (Error, Message, Cause, WithContext, TryFind).
public partial union OrderError(OrderNotFound, OrderLocked);

public static class Orders
{
    private static readonly Dictionary<int, Order> Store = new() { [1] = new Order(1), [2] = new Order(2, "alice") };

    // A leaf doesn't chain leaf → union → Result (CS0029), so the core needs the union named. A shape over the union
    // gets a conversion per leaf instead (UERR-NESTED-LEAF-CONVERSION).
    public static Result<Order, OrderError> Get(int id) =>
        Store.GetValueOrDefault(id) switch
        {
            null => (OrderError)new OrderNotFound(id),
            { LockedBy: { } who } => (OrderError)new OrderLocked(id, who),
            var order => order,
        };

    // A flat switch over the union is exhaustive with no `_` arm and no suppressor; dropping an arm is CS8509.
    public static int Status(Result<Order, OrderError> result) =>
        result.Match(
            _ => 200,
            error => error switch
            {
                OrderNotFound => 404,
                OrderLocked => 409,
            });

    // Widening: the union's leaf becomes Result<T>'s Error (IErrorUnion.Error), so helpers written for Error work.
    public static Result<Order> Widen(Result<Order, OrderError> result) => result.AsResult();
}
