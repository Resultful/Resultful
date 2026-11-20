// Stand-in for Resultful.Generators (tests/Resultful.TargetApi/Resultful.Generators/generated-members.tsv), written
// by hand so the Shop compiles against the target API before the generator exists. Deleted in 13b, when the real
// generator runs on the Shop (ShopGeneratorStandIn=false).
#nullable enable
namespace Resultful.Shop.Domain;

public abstract partial record OrderError
{
    private protected OrderError(string message) : base(message) { }

    public static IReadOnlyList<Type> Cases { get; } = [typeof(OrderLocked), typeof(OrderNotFound)];

    // Cases most-derived first, ties by fully qualified name.
    public TResult Match<TResult>(Func<OrderLocked, TResult> onOrderLocked, Func<OrderNotFound, TResult> onOrderNotFound)
    {
        ArgumentNullException.ThrowIfNull(onOrderLocked);
        ArgumentNullException.ThrowIfNull(onOrderNotFound);
        return this switch
        {
            OrderLocked l => onOrderLocked(l),
            OrderNotFound n => onOrderNotFound(n),
            _ => throw new InvalidOperationException("not a case of OrderError"),
        };
    }
}
