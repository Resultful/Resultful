// Stand-in for Resultful.Generators. Deleted in 13b.
#nullable enable
namespace Resultful.Shop.Domain;

public abstract partial record PaymentError
{
    private protected PaymentError(string message) : base(message) { }

    public static IReadOnlyList<Type> Cases { get; } = [typeof(CardDeclined), typeof(FraudSuspected), typeof(ProviderUnavailable)];
}
