// Stand-in for Resultful.Generators. Deleted in 13b. A nested union unwraps to its leaf; WithContext keeps each type.
#nullable enable
namespace Resultful.Shop.Net11;

public partial union PaymentError : IErrorUnion
{
    public static IReadOnlyList<Type> Cases { get; } = [typeof(CardDeclined), typeof(ProviderUnavailable)];

    public Error Error => Value switch
    {
        Error e => e,
        _ => throw new InvalidOperationException("a default PaymentError holds no error"),
    };

    public PaymentError WithContext(string message) => Value switch
    {
        CardDeclined e => e.WithContext(message),
        ProviderUnavailable e => e.WithContext(message),
        _ => throw new InvalidOperationException("a default PaymentError holds no error"),
    };
}

public partial union CheckoutError : IErrorUnion
{
    public static IReadOnlyList<Type> Cases { get; } = [typeof(OrderNotFound), typeof(CardDeclined), typeof(ProviderUnavailable)];

    public Error Error => Value switch
    {
        Error e => e,
        IErrorUnion u => u.Error,
        _ => throw new InvalidOperationException("a default CheckoutError holds no error"),
    };

    public CheckoutError WithContext(string message) => Value switch
    {
        OrderNotFound e => e.WithContext(message),
        PaymentError p => p.WithContext(message),
        _ => throw new InvalidOperationException("a default CheckoutError holds no error"),
    };
}
