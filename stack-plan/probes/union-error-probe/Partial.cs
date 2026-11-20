public partial union PayError(OrderNotFound, OrderLocked);
public partial union PayError : IErrorUnion
{
    public Error Error => Value switch { Error e => e, _ => throw new System.InvalidOperationException() };
}
public static class PartialCheck { public static string M(PayError p) => p.Error.Message; }
