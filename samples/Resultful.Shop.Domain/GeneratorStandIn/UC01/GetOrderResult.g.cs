// Stand-in for Resultful.Generators: the members of a [ResultShape] struct. Deleted in 13b.
#nullable enable
using System.Diagnostics.CodeAnalysis;
using Resultful.CodeAnalysis;

namespace Resultful.Shop.Domain;

[MustObserve]
#if NET11_0_OR_GREATER
[System.Runtime.CompilerServices.Union]
public readonly partial struct GetOrderResult : IResultShape<Order, OrderError>, IEquatable<GetOrderResult>, System.Runtime.CompilerServices.IUnion
#else
public readonly partial struct GetOrderResult : IResultShape<Order, OrderError>, IEquatable<GetOrderResult>
#endif
{
    private readonly Result<Order, OrderError> _inner;

    private GetOrderResult(Result<Order, OrderError> inner) => _inner = inner;

    [Obsolete("A default GetOrderResult is neither a success nor a failure.", error: true)]
    public GetOrderResult() => throw new InvalidOperationException();

    public static GetOrderResult Ok(Order value) => new(value);
    public static GetOrderResult Fail(OrderError error) => new(error);

    public bool IsDefault => _inner.IsDefault;
    public bool TryGetValue([MaybeNullWhen(false)] out Order value) => _inner.TryGetValue(out value);
    public bool TryGetError([MaybeNullWhen(false)] out OrderError error) => _inner.TryGetError(out error);

    public TResult Match<TResult>(Func<Order, TResult> onSuccess, Func<OrderLocked, TResult> onOrderLocked, Func<OrderNotFound, TResult> onOrderNotFound)
        => _inner.Match(onSuccess, error => error.Match(onOrderLocked, onOrderNotFound));

    public Result<TOut, OrderError> Map<TOut>(Func<Order, TOut> map) => _inner.Map(map);
    public Result<TOut, OrderError> Bind<TOut>(Func<Order, Result<TOut, OrderError>> bind) => _inner.Bind(bind);
    public GetOrderResult WithContext(string message) => new(_inner.WithContext(message));
    public Result<Order, OrderError> AsResult() => _inner;

    public static implicit operator GetOrderResult(Order value) => new(value);
    public static implicit operator GetOrderResult(OrderLocked error) => new(error);
    public static implicit operator GetOrderResult(OrderNotFound error) => new(error);
    public static implicit operator GetOrderResult(Result<Order, OrderError> result) => new(result);
    public static implicit operator Result<Order, OrderError>(GetOrderResult shape) => shape._inner;
    public static implicit operator Result<Order>(GetOrderResult shape) => shape._inner.AsResult();

#if NET11_0_OR_GREATER
    public GetOrderResult(Order value) => _inner = value;
    public GetOrderResult(OrderLocked error) => _inner = error;
    public GetOrderResult(OrderNotFound error) => _inner = error;
    public bool HasValue => !_inner.IsDefault;
    public object? Value => _inner.IsDefault ? null : _inner.Match<object>(order => order, error => error);
    public bool TryGetValue([MaybeNullWhen(false)] out OrderLocked value) => (value = _inner.TryGetError(out var e) ? e as OrderLocked : null) is not null;
    public bool TryGetValue([MaybeNullWhen(false)] out OrderNotFound value) => (value = _inner.TryGetError(out var e) ? e as OrderNotFound : null) is not null;
#endif

    public bool Equals(GetOrderResult other) => _inner.Equals(other._inner);
    public override bool Equals(object? obj) => obj is GetOrderResult other && Equals(other);
    public override int GetHashCode() => _inner.GetHashCode();
    public static bool operator ==(GetOrderResult left, GetOrderResult right) => left.Equals(right);
    public static bool operator !=(GetOrderResult left, GetOrderResult right) => !left.Equals(right);
}
