// Stand-in for Resultful.Generators: a [ResultShape] over an error union. Deleted in 13b.
#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Resultful.CodeAnalysis;

namespace Resultful.Shop.Net11;

[MustObserve]
[Union]
public readonly partial struct CheckoutResult : IResultShape<Receipt, CheckoutError>, IUnion, IEquatable<CheckoutResult>
{
    private readonly Result<Receipt, CheckoutError> _inner;

    private CheckoutResult(Result<Receipt, CheckoutError> inner) => _inner = inner;

    [Obsolete("A default CheckoutResult is neither a success nor a failure.", error: true)]
    public CheckoutResult() => throw new InvalidOperationException();

    // One public constructor per direct case: the success, OrderNotFound, and the nested union as one case.
    public CheckoutResult(Receipt value) => _inner = value;
    public CheckoutResult(OrderNotFound error) => _inner = (CheckoutError)error;
    public CheckoutResult(PaymentError error) => _inner = (CheckoutError)error;

    public static CheckoutResult Ok(Receipt value) => new(value);
    public static CheckoutResult Fail(CheckoutError error) => new(Result.Fail<Receipt, CheckoutError>(error));

    public bool IsDefault => _inner.IsDefault;
    public bool HasValue => !_inner.IsDefault;
    public object? Value => _inner.IsDefault ? null : _inner.Match<object?>(r => r, e => e.Value);

    public bool TryGetValue([MaybeNullWhen(false)] out Receipt value) => _inner.TryGetValue(out value);
    public bool TryGetError(out CheckoutError error) => _inner.TryGetError(out error);
    public bool TryGetValue([MaybeNullWhen(false)] out OrderNotFound value) => (value = Value as OrderNotFound) is not null;
    public bool TryGetValue(out PaymentError value)
    {
        var found = Value is PaymentError;
        value = found ? (PaymentError)Value! : default;
        return found;
    }

    // Cases: the success, then the direct cases by fully qualified name.
    public TResult Match<TResult>(Func<Receipt, TResult> onSuccess, Func<OrderNotFound, TResult> onOrderNotFound, Func<PaymentError, TResult> onPaymentError)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onOrderNotFound);
        ArgumentNullException.ThrowIfNull(onPaymentError);
        return Value switch
        {
            Receipt r => onSuccess(r),
            OrderNotFound n => onOrderNotFound(n),
            PaymentError p => onPaymentError(p),
            _ => throw new InvalidOperationException("a default CheckoutResult holds nothing"),
        };
    }

    public CheckoutResult WithContext(string message) => new(_inner.MapError(e => e.WithContext(message)));
    public Result<Receipt, CheckoutError> AsResult() => _inner;

    public static implicit operator CheckoutResult(Receipt value) => new(value);
    public static implicit operator CheckoutResult(OrderNotFound error) => new(error);
    public static implicit operator CheckoutResult(PaymentError error) => new(error);
    // A nested union's leaves convert too: C# won't chain leaf → union → shape on its own (CS0029).
    public static implicit operator CheckoutResult(CardDeclined error) => new((PaymentError)error);
    public static implicit operator CheckoutResult(ProviderUnavailable error) => new((PaymentError)error);
    public static implicit operator CheckoutResult(Result<Receipt, CheckoutError> result) => new(result);
    public static implicit operator Result<Receipt, CheckoutError>(CheckoutResult shape) => shape._inner;
    // Widens through IErrorUnion.Error: the innermost leaf becomes Result<T>'s error.
    public static implicit operator Result<Receipt>(CheckoutResult shape) => shape._inner.AsResult();

    public bool Equals(CheckoutResult other) => _inner.Equals(other._inner);
    public override bool Equals(object? obj) => obj is CheckoutResult other && Equals(other);
    public override int GetHashCode() => _inner.GetHashCode();
    public static bool operator ==(CheckoutResult left, CheckoutResult right) => left.Equals(right);
    public static bool operator !=(CheckoutResult left, CheckoutResult right) => !left.Equals(right);
}
