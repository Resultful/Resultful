using Resultful.CodeAnalysis;

// Use case 6's leaves reuse the name OrderNotFound, as the docs do, so they live in their own namespace.
namespace Resultful.Shop.Domain.Fulfilment;

[ClosedHierarchy]
public abstract partial record FulfilmentError : Error;
public sealed record StockUnavailable(string Sku) : FulfilmentError($"{Sku} is out of stock");
public sealed record WarehouseOffline() : FulfilmentError("the warehouse is offline");
public sealed record OrderNotFound(int OrderId) : FulfilmentError($"order {OrderId} was not found");
public sealed record AddressInvalid(int OrderId) : FulfilmentError($"order {OrderId} has an invalid address");

public sealed record OrderPlaced(int OrderId);
