// Stand-in for Resultful.Generators: what it adds to a partial union whose cases are all errors. Deleted in 13b.
#nullable enable
namespace Resultful.Shop.Net11;

public partial union OrderError : IErrorUnion
{
    public static IReadOnlyList<Type> Cases { get; } = [typeof(OrderNotFound), typeof(OrderLocked)];

    public Error Error => Value switch
    {
        Error e => e,
        _ => throw new InvalidOperationException("a default OrderError holds no error"),
    };

    public string Message => Error.Message;
    public Error? Cause => Error.Cause;

    public OrderError WithContext(string message) => Value switch
    {
        OrderNotFound e => e.WithContext(message),
        OrderLocked e => e.WithContext(message),
        _ => throw new InvalidOperationException("a default OrderError holds no error"),
    };

    public bool TryFind<TFound>([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TFound? found) where TFound : Error
        => Error.TryFind(out found);
}
