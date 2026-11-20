using Resultful.CodeAnalysis;

namespace Resultful.Shop.Domain;

// Use case 1: an endpoint that returns the right status (docs/errors-in-practice.md).
#region snippet: uc01-errors
[ClosedHierarchy]
public abstract partial record OrderError : Error;   // Resultful.Generators adds a private protected constructor
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
public sealed record OrderLocked(int Id, string By) : OrderError($"order {Id} is locked by {By}");
#endregion

#region snippet: uc01-shape
[ResultShape(typeof(OrderError), Success = typeof(Order))]
public readonly partial struct GetOrderResult;
#endregion
